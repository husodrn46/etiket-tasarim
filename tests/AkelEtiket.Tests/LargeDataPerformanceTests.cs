using System.Diagnostics;
using System.Text;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.DataImport;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.Rendering;
using Xunit.Abstractions;

namespace AkelEtiket.Tests;

/// <summary>Büyük veri: 10 000 satırlık CSV'nin içe aktarılması, tabloya uygulanması, doğrulanması ve etiket üretimi süresi.</summary>
public sealed class LargeDataPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void TenThousandRows_ImportApplyValidate_WithinSeconds()
    {
        string folder = Path.Combine(Path.GetTempPath(), "akel-perf-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        try
        {
            string csv = Path.Combine(folder, "buyuk.csv");
            var sb = new StringBuilder("StokKodu;UrunAdi;Barkod;Fiyat\n");
            for (int i = 1; i <= 10_000; i++)
            {
                string body = "8691" + i.ToString("D8");
                string ean = body + AkelEtiket.BarcodeEngine.CheckDigits.Gs1Mod10(body);
                sb.Append($"TST{i:D5};Melamin Ürün {i};{ean};{i % 500},90\n");
            }
            File.WriteAllText(csv, sb.ToString(), new UTF8Encoding(true));

            var watch = Stopwatch.StartNew();
            var table = DataImporter.Import(csv);
            long import = watch.ElapsedMilliseconds;
            Assert.Equal(10_000, table.Rows.Count);

            var data = new DataSourceViewModel();
            data.ApplyTable(table);
            long apply = watch.ElapsedMilliseconds - import;

            var doc = LabelDocument.CreateBlank(100, 50);
            doc.Elements.Add(new TextElement { Text = "{UrunAdi}", X = 2, Y = 2, Width = 96, Height = 8 });
            doc.Elements.Add(new BarcodeElement { Symbology = BarcodeType.Ean13, Content = "{Barkod}", X = 20, Y = 14, Width = 50, Height = 30 });
            data.ValidateAgainst(doc);
            long validate = watch.ElapsedMilliseconds - import - apply;
            Assert.Equal(10_000, data.Rows.Count(r => r.IsSelected));

            // 100 etiketin baskı kipinde üretimi (gerçek yazıcı hızı ~1 etiket/sn olduğundan darboğaz değildir).
            var producer = new LabelProducer(doc, new Core.Assets.AssetStore(), verifyBarcodes: false);
            var produceWatch = Stopwatch.StartNew();
            for (int i = 0; i < 100; i++)
            {
                var fields = new CompositeFieldSource(table.Rows[i].ToFieldSource(), new BuiltInFieldSource(i + 1, DateTime.Now));
                var (bitmap, problems, _) = producer.Produce(new PrintItem(i + 1, table.Rows[i].RowNumber, 1, fields), 8);
                Assert.NotNull(bitmap);
                Assert.Empty(problems);
            }
            long produce = produceWatch.ElapsedMilliseconds;
            producer.Dispose();

            output.WriteLine($"İçe aktarma {import} ms, tabloya uygulama {apply} ms, doğrulama {validate} ms, 100 etiket üretimi {produce} ms");
            Assert.True(import + apply + validate < 30_000, "10 000 satır 30 saniyeden uzun sürdü");
            Assert.True(produce < 30_000, "100 etiketin üretimi 30 saniyeden uzun sürdü");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
