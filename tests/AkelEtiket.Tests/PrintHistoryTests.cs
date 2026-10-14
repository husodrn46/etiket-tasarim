using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Printing;
using AkelEtiket.PrintEngine;
using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;

namespace AkelEtiket.Tests;

/// <summary>Baskı geçmişi: kalıcı kayıt, en yeni önce, bozuk satıra dayanıklılık, eşzamanlı ekleme, tasarım kopyası.</summary>
public sealed class PrintHistoryTests : IDisposable
{
    private readonly string _folder = Path.Combine(TestDataRoot.Root, "gecmis-" + Guid.NewGuid().ToString("N")[..8]);

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

    private static PrintHistoryEntry Entry(string title, DateTimeOffset time, params PrintHistoryItem[] items) => new()
    {
        Title = title,
        Time = time,
        DocumentName = "Koli Etiketi",
        PrinterName = "Sahte",
        Method = PrintMethod.TsplRaw,
        TotalLabels = items.Sum(i => i.Copies),
        Items = [.. items],
    };

    private static PrintHistoryItem Item(long sequence, int? row, int copies, PrintItemState state) =>
        new() { Sequence = sequence, Row = row, Copies = copies, State = state };

    [Fact]
    public void Append_PersistsAcrossInstances_NewestFirst()
    {
        var store = new PrintHistoryStore(_folder);
        var now = DateTimeOffset.Now;
        store.Append(Entry("Eski", now.AddHours(-2), Item(1, null, 5, PrintItemState.Sent)));
        store.Append(Entry("Yeni", now, Item(1, 2, 1, PrintItemState.Sent), Item(2, 3, 2, PrintItemState.NotSent)));
        store.Append(Entry("Orta", now.AddHours(-1), Item(1, null, 1, PrintItemState.Failed)));

        var all = new PrintHistoryStore(_folder).ReadAll();
        Assert.Equal(["Yeni", "Orta", "Eski"], all.Select(e => e.Title));
        var newest = all[0];
        Assert.Equal(1, newest.LabelsSent);
        Assert.Equal(2, newest.LabelsRemaining);
        Assert.True(newest.CanResume);
        Assert.Equal(PrintMethod.TsplRaw, newest.Method);
        Assert.Equal(PrintItemState.NotSent, newest.Items[1].State);
        Assert.Equal(3, newest.Items[1].Row);
        Assert.Contains("gönderilmedi", newest.StatusText);
        Assert.Equal(1, all[1].LabelsFailed);
        Assert.Equal(newest.Id, store.Find(newest.Id)!.Id);
        // Satır başına bir JSON: dosya yalnızca eklenir.
        Assert.Equal(3, File.ReadAllLines(store.LogPath).Length);
    }

    [Fact]
    public void ReadAll_SkipsCorruptOrHalfWrittenLines()
    {
        var store = new PrintHistoryStore(_folder);
        store.Append(Entry("Birinci", DateTimeOffset.Now.AddMinutes(-1)));
        File.AppendAllText(store.LogPath, "{\"title\":\"yarım kal\n");
        File.AppendAllText(store.LogPath, "çöp satır\n");
        store.Append(Entry("İkinci", DateTimeOffset.Now));
        Assert.Equal(["İkinci", "Birinci"], store.ReadAll().Select(e => e.Title));
    }

    [Fact]
    public void Append_IsSafeUnderConcurrentWriters()
    {
        var stores = Enumerable.Range(0, 4).Select(_ => new PrintHistoryStore(_folder)).ToArray();
        Parallel.For(0, 80, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
            stores[i % stores.Length].Append(Entry("İş " + i, DateTimeOffset.Now.AddSeconds(i), Item(i, null, 1, PrintItemState.Sent))));
        var all = new PrintHistoryStore(_folder).ReadAll();
        Assert.Equal(80, all.Count);
        Assert.Equal(80, all.Select(e => e.Id).Distinct().Count());
        Assert.Equal("İş 79", all[0].Title);
    }

    [Fact]
    public void Snapshot_KeepsPrintedDesign_AndHashDetectsTemplateChanges()
    {
        var store = new PrintHistoryStore(_folder);
        var project = LabelProject.CreateBlank(100, 50);
        project.Document.Name = "Kopya testi";
        project.Document.Elements.Add(new TextElement { Text = "Basılan hâl" });
        string? file = store.SaveSnapshot(project);
        Assert.NotNull(file);
        Assert.EndsWith(ProjectFile.Extension, file);
        // Aynı tasarım tekrar basılınca yeni kopya yazılmaz.
        Assert.Equal(file, store.SaveSnapshot(project));
        Assert.Single(Directory.GetFiles(store.SnapshotFolder, "*" + ProjectFile.Extension));

        var entry = new PrintHistoryEntry { SnapshotFile = file };
        string? path = store.SnapshotPath(entry);
        Assert.NotNull(path);
        var loaded = ProjectFile.Load(path!).Project;
        Assert.Equal("Kopya testi", loaded.Document.Name);
        Assert.Contains(loaded.Document.Elements, e => e is TextElement { Text: "Basılan hâl" });
        Assert.Null(store.SnapshotPath(new PrintHistoryEntry { SnapshotFile = "yok.akelabel" }));

        string template = Path.Combine(_folder, "sablon.akelabel");
        ProjectFile.Save(project, template);
        string? hash = PrintHistoryStore.HashFile(template);
        Assert.NotNull(hash);
        Assert.Equal(hash, PrintHistoryStore.HashFile(template));
        project.Document.Elements.Add(new TextElement { Text = "Sonradan eklendi" });
        ProjectFile.Save(project, template);
        Assert.NotEqual(hash, PrintHistoryStore.HashFile(template));
        // Değişen tasarım ayrı kopya olarak saklanır; eski kopya yerinde kalır.
        string? changed = store.SaveSnapshot(project);
        Assert.NotEqual(file, changed);
        Assert.Equal(2, Directory.GetFiles(store.SnapshotFolder, "*" + ProjectFile.Extension).Length);
        Assert.Null(PrintHistoryStore.HashFile(Path.Combine(_folder, "olmayan.akelabel")));
    }

    [Fact]
    public void Recorder_FirstSpooledProducedItemsAreSent_FailedAndRestAreTracked()
    {
        var fields = new BuiltInFieldSource(1, DateTime.Now);
        var items = Enumerable.Range(0, 6).Select(i => new PrintItem(10 + i, 2 + i / 2, 1, fields)).ToList();
        var recorder = new PrintJobRecorder(items);
        recorder.Produced(0);
        recorder.Produced(1);
        recorder.Failed(2, "barkod sığmadı");
        recorder.Produced(3);
        recorder.Produced(4);
        // 5 hiç üretilmedi (iptal).

        var states = recorder.States(spooledPages: 3);
        Assert.Equal([PrintItemState.Sent, PrintItemState.Sent, PrintItemState.Failed, PrintItemState.Sent, PrintItemState.NotSent, PrintItemState.NotSent], states);
        var remaining = recorder.Remaining(spooledPages: 3);
        Assert.Equal([14, 15], remaining.Select(i => i.Sequence));

        var history = recorder.ToHistory(spooledPages: 3);
        Assert.Equal("barkod sığmadı", history[2].Problem);
        Assert.Equal(4, history[4].Row);

        // Hiçbir şey kuyruğa gitmediyse üretilenler de devam planında kalır.
        Assert.Equal(5, recorder.Remaining(0).Count);
    }

    [Fact]
    public void ResumePlanner_RebuildsRemainingRowsAndSequence()
    {
        var entry = Entry("Baskı", DateTimeOffset.Now,
            Item(1, 2, 1, PrintItemState.Sent),
            Item(1, 2, 1, PrintItemState.Sent),
            Item(1, 2, 1, PrintItemState.NotSent),
            Item(2, 3, 1, PrintItemState.Failed),
            Item(3, 4, 1, PrintItemState.NotSent),
            Item(3, 4, 1, PrintItemState.NotSent));
        Assert.Equal([(2, 1), (4, 2)], ResumePlanner.RemainingRows(entry));
        Assert.Equal([(2, 3), (3, 1), (4, 2)], ResumePlanner.AllRows(entry));
        Assert.Equal<long?>(1, ResumePlanner.FirstRemainingSequence(entry));
        Assert.Null(ResumePlanner.RemainingSequence(entry));
        Assert.Equal(3, entry.LabelsRemaining);

        var noData = Entry("Baskı", DateTimeOffset.Now,
            Item(1, null, 1, PrintItemState.Sent),
            Item(2, null, 1, PrintItemState.Sent),
            Item(3, null, 1, PrintItemState.NotSent),
            Item(4, null, 1, PrintItemState.NotSent));
        Assert.Equal<(long, int)?>((3L, 2), ResumePlanner.RemainingSequence(noData));
        Assert.Empty(ResumePlanner.RemainingRows(noData));

        var done = Entry("Baskı", DateTimeOffset.Now, Item(1, null, 5, PrintItemState.Sent));
        Assert.False(done.CanResume);
        Assert.Null(ResumePlanner.RemainingSequence(done));
        Assert.Null(ResumePlanner.FirstRemainingSequence(done));
    }

    [Fact]
    public void HistoryFilter_ByDateTemplateAndIncomplete()
    {
        var today = DateTimeOffset.Now;
        var entries = new[]
        {
            new PrintHistoryEntry { Title = "a", Time = today, DocumentName = "Koli Etiketi", Items = [Item(1, null, 1, PrintItemState.NotSent)] },
            new PrintHistoryEntry { Title = "b", Time = today.AddDays(-3), DocumentName = "Ürün Etiketi", TemplatePath = @"C:\Şablonlar\urun.akelabel" },
            new PrintHistoryEntry { Title = "c", Time = today.AddDays(-10), DocumentName = "KOLİ etiketi" },
        };
        string Titles(DateTime? from, DateTime? to, string? template, bool incomplete) =>
            string.Concat(Editor.ViewModels.PrintHistoryViewModel.Filter(entries, from, to, template, incomplete).Select(e => e.Title));

        Assert.Equal("abc", Titles(null, null, null, false));
        Assert.Equal("a", Titles(DateTime.Today, DateTime.Today, null, false));
        Assert.Equal("ab", Titles(DateTime.Today.AddDays(-5), null, "", false));
        Assert.Equal("ac", Titles(null, null, "koli", false));
        Assert.Equal("b", Titles(null, null, "urun.akel", false));
        Assert.Equal("a", Titles(null, null, null, true));
    }
}
