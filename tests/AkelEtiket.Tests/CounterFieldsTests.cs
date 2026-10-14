using AkelEtiket.Core.Data;
using AkelEtiket.Core.Imaging;
using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Printing;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.PrintEngine;
using AkelEtiket.Rendering;

namespace AkelEtiket.Tests;

/// <summary>
/// Kalıcı sayaçlar: {SAYAC} / {SAYAC.Ad} / {SAYAC:000000}; önizleme tüketmez, yalnızca kuyruğa giden etiketler sayacı
/// ilerletir, kilit iki baskının aynı değeri almasını engeller. (Gerçek yazıcıya hiçbir şey gönderilmez.)
/// </summary>
public sealed class CounterFieldsTests : IDisposable
{
    private readonly string _folder = Path.Combine(TestDataRoot.Root, "sayac-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static LabelDocument CounterDocument(string text, string name = "Koli Etiketi")
    {
        var doc = LabelDocument.CreateBlank();
        doc.Name = name;
        doc.Elements.Add(new TextElement { Text = text, X = 2, Y = 2, Width = 60, Height = 10 });
        return doc;
    }

    private static string Resolve(IFieldValueSource fields, string field, string? format = null) =>
        fields.TryGetValue(field, format, out var value) ? value : "<yok>";

    [Theory]
    [InlineData("SAYAC", "Koli Etiketi")]
    [InlineData("sayaç", "Koli Etiketi")]
    [InlineData("Sayac.Koli", "Koli")]
    [InlineData("SAYAÇ.Palet No", "Palet No")]
    public void CounterNames_DefaultIsDocumentName_NamedAfterDot(string field, string expected)
    {
        Assert.True(CounterFields.TryGetCounterName(field, "Koli Etiketi", out var name));
        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData("SAYACI")]
    [InlineData("SIRA")]
    [InlineData("SAYAC.")]
    [InlineData("")]
    public void NonCounterFields_AreNotCounters(string field) =>
        Assert.False(CounterFields.TryGetCounterName(field, "Belge", out _));

    [Fact]
    public void DefaultCounterName_FallsBackToGeneral()
    {
        Assert.True(CounterFields.TryGetCounterName("SAYAC", null, out var name));
        Assert.Equal("Genel", name);
    }

    [Fact]
    public void Format_UsesNumericPattern_AndFallsBackForInvalid()
    {
        Assert.Equal("000123", CounterFields.Format(123, "000000"));
        Assert.Equal("123", CounterFields.Format(123, null));
        Assert.Equal("1234567", CounterFields.Format(1234567, "0000"));
    }

    [Fact]
    public void DocumentCounters_AreFoundInTextAndBarcodes()
    {
        var doc = CounterDocument("Koli {SAYAC:0000} / {SAYAC.Palet}");
        doc.Elements.Add(new BarcodeElement { Symbology = BarcodeType.Code128, Content = "K{SAYAC:000000}", X = 2, Y = 20, Width = 60, Height = 15 });
        Assert.Equal(["Koli Etiketi", "Palet"], CounterFields.CountersUsedBy(doc));
        Assert.True(CounterFields.DocumentUsesCounters(doc));
        Assert.False(CounterFields.DocumentUsesCounters(CounterDocument("Sabit {SIRA}")));
    }

    [Fact]
    public void Store_PersistsAcrossInstances_AndPreviewDoesNotConsume()
    {
        var store = new CounterStore(_folder);
        Assert.Equal(1, store.Peek("Koli"));
        store.Set("Koli", 120, step: 5);

        var other = new CounterStore(_folder);
        Assert.Equal(120, other.Peek("koli"));
        Assert.Equal(5, other.Get("Koli").Step);

        var preview = CounterFieldSource.Preview(other, () => "Belge");
        for (int i = 0; i < 3; i++) Assert.Equal("000120", Resolve(preview, "SAYAC.Koli", "000000"));
        Assert.Equal("1", Resolve(preview, "SAYAC"));
        Assert.Equal("<yok>", Resolve(preview, "UrunAdi"));
        Assert.Equal(120, new CounterStore(_folder).Peek("Koli"));
        Assert.Single(new CounterStore(_folder).List());
    }

    [Fact]
    public void Tracker_AssignsSequentialValues_AndOnlySpooledLabelsAdvanceTheCounter()
    {
        var store = new CounterStore(_folder);
        store.Set("Koli", 41, step: 1);
        var doc = CounterDocument("{SAYAC.Koli:0000}");
        var fields = new BuiltInFieldSource(1, DateTime.Now);
        string Value(PrintItem item) => Resolve(item.Fields, "SAYAC.Koli", "0000");

        using (var tracker = CounterPrintTracker.Begin(store, doc, TimeSpan.FromSeconds(1)))
        {
            Assert.True(tracker.IsActive);
            Assert.Equal("0041", Value(tracker.Prepare(new PrintItem(1, null, 1, fields))));
            var second = tracker.Prepare(new PrintItem(2, null, 1, fields));
            Assert.Equal("0042", Value(second));
            // Üretilemeyen etiketin değeri geri alınır: bir sonraki etiket aynı numarayı alır.
            tracker.ProductionFailed();
            Assert.Equal("0042", Value(tracker.Prepare(new PrintItem(3, null, 1, fields))));
            Assert.Equal("0043", Value(tracker.Prepare(new PrintItem(4, null, 1, fields))));
            Assert.Equal("0044", Value(tracker.Prepare(new PrintItem(5, null, 1, fields))));

            // 4 etikete değer verildi, yalnızca 2'si kuyruğa gitti.
            var result = tracker.Complete(spooledLabels: 2);
            Assert.NotNull(result);
            Assert.False(result!.Value.Failed);
            Assert.Contains("41–42", result.Value.Text);
            Assert.Contains("2 etiketin numaraları bir sonraki baskıda yeniden kullanılacak", result.Value.Text);
            Assert.Null(tracker.Complete(4));
        }
        var after = new CounterStore(_folder).Get("Koli");
        Assert.Equal(43, after.Next);
        Assert.Equal("41–42", after.LastRange);
        Assert.NotNull(after.LastUsedAt);
    }

    [Fact]
    public void Tracker_NothingSpooled_LeavesCounterUnchanged_AndHonoursStep()
    {
        var store = new CounterStore(_folder);
        store.Set("Palet", 100, step: 10);
        var doc = CounterDocument("{SAYAC.Palet}");
        var fields = new BuiltInFieldSource(1, DateTime.Now);
        using (var tracker = CounterPrintTracker.Begin(store, doc, TimeSpan.FromSeconds(1)))
        {
            Assert.Equal("100", Resolve(tracker.Prepare(new PrintItem(1, null, 1, fields)).Fields, "SAYAC.Palet"));
            Assert.Equal("110", Resolve(tracker.Prepare(new PrintItem(2, null, 1, fields)).Fields, "SAYAC.Palet"));
            Assert.Contains("ilerlemedi", tracker.Complete(0)!.Value.Text);
        }
        Assert.Equal(100, store.Peek("Palet"));

        using (var tracker = CounterPrintTracker.Begin(store, doc, TimeSpan.FromSeconds(1)))
        {
            tracker.Prepare(new PrintItem(1, null, 1, fields));
            tracker.Prepare(new PrintItem(2, null, 1, fields));
            tracker.Complete(2);
        }
        Assert.Equal(120, store.Peek("Palet"));
    }

    [Fact]
    public void Tracker_WithoutCounterFields_IsInactive_AndTakesNoLock()
    {
        var store = new CounterStore(_folder);
        var item = new PrintItem(1, null, 3, new BuiltInFieldSource(1, DateTime.Now));
        using var tracker = CounterPrintTracker.Begin(store, CounterDocument("Sabit metin {SIRA}"), TimeSpan.Zero);
        Assert.False(tracker.IsActive);
        Assert.Same(item, tracker.Prepare(item));
        Assert.Null(tracker.Complete(1));
        using var lease = store.Acquire(TimeSpan.Zero);
    }

    [Fact]
    public void Lock_PreventsTwoPrintsFromTakingTheSameValues()
    {
        var store = new CounterStore(_folder);
        var doc = CounterDocument("{SAYAC.Koli}");
        using (var first = CounterPrintTracker.Begin(store, doc, TimeSpan.FromSeconds(1)))
        {
            var ex = Assert.Throws<CounterException>(() => CounterPrintTracker.Begin(new CounterStore(_folder), doc, TimeSpan.FromMilliseconds(150)));
            Assert.Contains("başka bir baskıda", ex.Message);
            first.Prepare(new PrintItem(1, null, 1, new BuiltInFieldSource(1, DateTime.Now)));
            first.Complete(1);
        }
        // Kilit bırakıldı: sıradaki baskı devam eder.
        using var second = CounterPrintTracker.Begin(store, doc, TimeSpan.FromSeconds(1));
        Assert.Equal(2, second.StartOf("Koli"));
    }

    [Fact]
    public void ConcurrentPrints_NeverReuseAValue()
    {
        var doc = CounterDocument("{SAYAC.Seri}");
        var fields = new BuiltInFieldSource(1, DateTime.Now);
        var values = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 6, new ParallelOptions { MaxDegreeOfParallelism = 6 }, _ =>
        {
            // Her iş kendi depo nesnesiyle (ayrı pencere/işlem gibi) çalışır; ortak olan yalnızca dosya ve kilittir.
            using var tracker = CounterPrintTracker.Begin(new CounterStore(_folder), doc, TimeSpan.FromSeconds(30));
            for (int i = 0; i < 5; i++) values.Add(Resolve(tracker.Prepare(new PrintItem(i + 1, null, 1, fields)).Fields, "SAYAC.Seri"));
            tracker.Complete(5);
        });
        Assert.Equal(30, values.Count);
        Assert.Equal(30, values.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 30).Select(i => i.ToString()), values.Select(int.Parse).Order().Select(i => i.ToString()));
        Assert.Equal(31, new CounterStore(_folder).Peek("Seri"));
    }

    [Fact]
    public void CorruptCounterFile_IsReportedAndNotOverwritten()
    {
        var store = new CounterStore(_folder);
        store.Set("Koli", 5);
        File.WriteAllText(store.FilePath, "{ bozuk");
        var fresh = new CounterStore(_folder);
        Assert.Throws<CounterException>(() => fresh.List());
        Assert.Throws<CounterException>(() => CounterPrintTracker.Begin(fresh, CounterDocument("{SAYAC.Koli}"), TimeSpan.FromSeconds(1)));
        Assert.Equal("{ bozuk", File.ReadAllText(store.FilePath));
        // Önizleme baskıyı engellemez ama değer uydurmaz: 1 gösterir.
        Assert.Equal("1", Resolve(CounterFieldSource.Preview(fresh, () => null), "SAYAC.Koli"));
    }

    [Fact]
    public void Planner_SplitsCopiesPerLabel_WhenCountersAreUsed()
    {
        var data = new DataSourceViewModel();
        var plan = PrintPlanner.Plan(CounterDocument("No {SAYAC:000}"), data, useData: false, copies: 4, startNumber: 1, DateTime.Now);
        Assert.Equal(4, plan.Items.Count);
        Assert.All(plan.Items, i => Assert.Equal(1, i.Copies));
        Assert.Equal(4, plan.TotalLabels);
    }

    /// <summary>
    /// Baskı akışının dayandığı kural: TSPL servisi sayfaları sırayla üretir; iptal/hata sonrası kuyruğa giden etiket sayısı
    /// kadar sayaç ilerler ve "kaldığı yerden devam" tam olarak ilk gönderilmeyen etiketten başlar.
    /// </summary>
    [Fact]
    public async Task TsplBatch_CanceledMidway_CountersAndResumeMatchSpooledLabels()
    {
        var store = new CounterStore(_folder);
        store.Set("Koli", 1);
        var doc = CounterDocument("{SAYAC.Koli:0000}");
        var plan = PrintPlanner.Plan(doc, new DataSourceViewModel(), useData: false, copies: 30, startNumber: 1, DateTime.Now);
        Assert.Equal(30, plan.Items.Count);

        var backend = new FakeSpooler();
        var service = new TsplPrintService(backend) { MaxLabelsPerJob = 10 };
        var recorder = new PrintJobRecorder(plan.Items);
        var printed = new List<string>();
        using var cts = new CancellationTokenSource();
        using var tracker = CounterPrintTracker.Begin(store, doc, TimeSpan.FromSeconds(1));
        IEnumerable<LabelPage> Pages()
        {
            for (int index = 0; index < plan.Items.Count; index++)
            {
                var item = tracker.Prepare(plan.Items[index]);
                printed.Add(Resolve(item.Fields, "SAYAC.Koli", "0000"));
                recorder.Produced(index);
                if (index == 24) cts.Cancel();
                var bitmap = new MonoBitmap(80, 40);
                bitmap[index, 0] = true;
                yield return new LabelPage(bitmap, item.Copies);
            }
        }
        var report = await service.PrintAsync(new LabelMedia(10, 5, 2, 0, MediaType.Gap), new LabelPrinterSettings { PrinterName = "Sahte" }, Pages(), 30, null, cts.Token);
        Assert.True(report.Canceled);
        Assert.Equal(25, report.LabelsSpooled);
        Assert.Equal(25, printed.Count);

        tracker.Complete(report.LabelsSpooled);
        Assert.Equal(26, store.Peek("Koli"));
        var remaining = recorder.Remaining(report.LabelsSpooled);
        Assert.Equal(5, remaining.Count);
        Assert.Equal(26, remaining[0].Sequence);
        Assert.Equal("0001", printed[0]);
        Assert.Equal("0025", printed[^1]);
    }
}

/// <summary>Sahte Windows kuyruğu: işleri bellekte toplar, hiçbir yazıcıya gönderim yapmaz.</summary>
internal sealed class FakeSpooler : IPrintBackend
{
    public List<FakeSpoolJob> Jobs { get; } = [];

    /// <summary>Bu numaralı iş (1'den başlar) ilk etiket yazımında hata verir.</summary>
    public int? FailOnJob { get; set; }

    public PrinterStatusSnapshot GetStatus(string printerName) => new()
    {
        PrinterName = printerName,
        Printer = new PrinterInfo { Name = printerName, DriverName = "Xprinter XP-470B", PortName = "USB001", UsbConnected = true },
    };

    public IRawJobWriter StartJob(string printerName, string documentName)
    {
        var job = new FakeSpoolJob(500 + Jobs.Count, Jobs.Count + 1 == FailOnJob);
        Jobs.Add(job);
        return job;
    }

    public Task<SpoolWaitResult> WaitForJobAsync(string printerName, int jobId, TimeSpan timeout, CancellationToken cancellationToken) =>
        Task.FromResult(new SpoolWaitResult(SpoolOutcome.LeftQueue, "kuyruktan çıktı", null));

    public int CompletedLabels => Jobs.Where(j => j.Completed).Sum(j => j.Labels);
}

internal sealed class FakeSpoolJob(int id, bool fail) : IRawJobWriter
{
    private int _writes;

    public int JobId { get; } = id;
    public bool Completed { get; private set; }
    public bool Aborted { get; private set; }
    public int Labels { get; private set; }
    public MemoryStream Data { get; } = new();

    public string Text => System.Text.Encoding.Latin1.GetString(Data.ToArray());

    public void Write(ReadOnlySpan<byte> data)
    {
        _writes++;
        // İlk yazım iş başı ayarlarıdır (SIZE, GAP...); sonrakiler etikettir.
        if (_writes > 1 && fail) throw new PrintEngineException("Yazıcıya veri gönderilemedi: deneme hatası", 31);
        if (_writes > 1) Labels++;
        Data.Write(data);
    }

    public void Complete() => Completed = true;
    public void Abort() => Aborted = true;
    public void Dispose()
    {
    }
}
