using AkelEtiket.BarcodeEngine;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.DataImport;
using AkelEtiket.Editor.Printing;
using AkelEtiket.Editor.Templates;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.PrintEngine;
using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;

namespace AkelEtiket.Tests;

/// <summary>
/// Kabul testi (toplu veri): örnek Excel'den 120 ürün okunur, şablon sütunlara bağlanır, satır bazında
/// barkodlar ayrı ayrı üretilip çözücüyle doğrulanır, adetler uygulanır, hatalı satır açıklanır ve
/// RAW TSPL iş verisi oluşturulup komut yapısı denetlenir. (Fiziksel yazıcıya gönderim YOK.)
/// </summary>
public class BatchPrintAcceptanceTests
{
    [Fact]
    public void ExcelBatch_EndToEnd_ProducesVerifiedLabelsAndTspl()
    {
        string xlsx = EditorTestHelpers.FindRepoFile("samples", "Ornek-Urunler.xlsx");
        var table = DataImporter.Import(xlsx);
        Assert.Equal(120, table.Rows.Count);
        Assert.Contains("Barkod", table.Columns);

        var project = SampleTemplates.CreateBatchLabel(logoPng: null);
        var data = new DataSourceViewModel();
        data.ApplyTable(table);
        // Satır bazlı adet: Adet sütunu otomatik bulunur.
        Assert.Equal("Adet", data.CopiesColumn);
        data.ValidateAgainst(project.Document);

        // TST002 (Excel satır 3) kullanıcı örneğindeki hatalı kontrol basamağı → hata, diğerleri sağlam.
        var errorRows = data.Rows.Where(r => r.Status == RowStatus.Error).ToList();
        var bad = Assert.Single(errorRows);
        Assert.Equal(3, bad.RowNumber);
        Assert.Contains("kontrol basamağı", bad.Problem);

        var plan = PrintPlanner.Plan(project.Document, data, useData: true, copies: 1, startNumber: 1, DateTime.Now);
        Assert.Equal(119, plan.Items.Count);
        Assert.Single(plan.Skipped);
        Assert.Equal(data.Rows.Where(r => r.Status != RowStatus.Error).Sum(r => r.Copies), plan.TotalLabels);
        Assert.Contains(plan.Items, i => i.Copies > 1); // örnek veride adet 1–3

        int barcodeColumn = table.IndexOfColumn("Barkod");
        var pages = new List<LabelPage>();
        using (var producer = new LabelProducer(project.Document, project.Assets, verifyBarcodes: true))
        {
            foreach (var item in plan.Items)
            {
                var (bitmap, problems, _) = producer.Produce(item, 8.0);
                Assert.True(bitmap is not null, $"{item.Describe()}: {string.Join("; ", problems)}");
                Assert.Equal(800, bitmap!.Width);
                pages.Add(new LabelPage(bitmap, item.Copies));
            }
        }

        // Her etiketin barkodu kendi satırının değeriyle ayrı ayrı üretildi mi? (ilk 10 satırda bağımsız kontrol)
        using var renderer = new LabelRenderer(project.Assets);
        var barcodeElement = project.Document.Elements.OfType<BarcodeElement>().Single(b => b.Content == "{Barkod}");
        foreach (var item in plan.Items.Take(10))
        {
            var row = data.Rows.Single(r => r.RowNumber == item.RowNumber);
            string expected = row.Record.GetValue(barcodeColumn);
            var result = renderer.RenderElement(barcodeElement, new RenderOptions { DotsPerMm = 8, Fields = item.Fields, Mode = RenderMode.Print });
            var verification = BarcodeVerifier.Verify(result.Layer!.Ink, BarcodeType.Ean13, expected);
            Assert.True(verification.Success, $"Satır {row.RowNumber}: {verification.Message}");
        }

        // RAW TSPL iş verisi ve komut yapısı.
        var media = LabelMedia.FromSetup(project.Document.Label);
        var settings = new LabelPrinterSettings { PrinterName = "test" };
        byte[] job = TsplCommandBuilder.BuildJob(media, settings, pages);
        var commands = TsplInspector.Parse(job);
        Assert.Equal("SIZE", commands[0].Name);
        Assert.Equal(119, commands.Count(c => c.Name == "BITMAP"));
        Assert.All(commands.Where(c => c.Name == "BITMAP"), c => Assert.Equal(100 * 800, c.PayloadLength));
        var prints = commands.Where(c => c.Name == "PRINT").ToList();
        Assert.Equal(plan.TotalLabels, prints.Sum(p => int.Parse(p.Arguments.Split(',')[1])));
    }

    [Fact]
    public void NoData_WithSequenceField_ProducesUniqueNumbers()
    {
        var doc = LabelDocument.CreateBlank(50, 30);
        doc.Elements.Add(new BarcodeElement { Symbology = BarcodeType.Code128, Content = "TST-{SIRA:00000}", Width = 45, Height = 20, X = 2, Y = 2 });
        var plan = PrintPlanner.Plan(doc, new DataSourceViewModel(), useData: false, copies: 5, startNumber: 98, DateTime.Now);
        Assert.Equal(5, plan.Items.Count);
        Assert.All(plan.Items, i => Assert.Equal(1, i.Copies));
        var values = plan.Items.Select(i => FieldTemplate.Resolve("{SIRA:00000}", i.Fields).Text).ToList();
        Assert.Equal(["00098", "00099", "00100", "00101", "00102"], values);
    }

    [Fact]
    public void Producer_RejectsBarcodeCoveredByAnotherElement()
    {
        var project = LabelProject.CreateBlank(60, 40);
        project.Document.Elements.Add(new BarcodeElement { Symbology = BarcodeType.Code128, Content = "AKEL001", X = 2, Y = 2, Width = 56, Height = 20 });
        project.Document.Elements.Add(new ShapeElement { X = 10, Y = 0, Width = 40, Height = 30, Fill = FillStyle.Black });
        using var producer = new LabelProducer(project.Document, project.Assets, verifyBarcodes: true);
        var (bitmap, problems, _) = producer.Produce(new PrintItem(1, null, 1, new BuiltInFieldSource(1, DateTime.Now)), 8);
        Assert.Null(bitmap);
        Assert.Contains(problems, p => p.Contains("okunabilirlik"));
    }

    [Fact]
    public void TestLabel_RendersWithoutErrors_ForCommonSizes()
    {
        foreach (var (w, h) in new[] { (100.0, 100.0), (58.0, 40.0), (100.0, 150.0), (40.0, 30.0) })
        {
            var doc = TestLabelFactory.Create(new LabelSetup { WidthMm = w, HeightMm = h }, "barkod", "TSPL", 203.2, DateTime.Now);
            using var renderer = new LabelRenderer(new Core.Assets.AssetStore());
            var result = renderer.RenderLabel(doc, new RenderOptions { DotsPerMm = 8, Mode = RenderMode.Print });
            Assert.False(result.HasErrors, $"{w}×{h}: " + string.Join("; ", result.Errors));
            // Çerçeve dört kenarda da bulunmalı (1 mm içeride).
            Assert.True(result.Bitmap[8, result.Bitmap.Height / 2]);
            Assert.True(result.Bitmap[result.Bitmap.Width - 9, result.Bitmap.Height / 2]);
        }
    }

    [Fact]
    public void SampleTemplates_RenderCleanly_WithLogo()
    {
        string logo = EditorTestHelpers.FindRepoFile("src", "AkelEtiket.App", "Assets", "AkelEtiket-256.png");
        foreach (var (name, project) in SampleTemplates.CreateAll(File.ReadAllBytes(logo)))
        {
            using var renderer = new LabelRenderer(project.Assets);
            var result = renderer.RenderLabel(project.Document, new RenderOptions { DotsPerMm = 8 });
            Assert.False(result.HasErrors, $"{name}: " + string.Join("; ", result.Errors));
            Assert.DoesNotContain(result.Issues, i => i.Severity == RenderIssueSeverity.Warning && !i.Message.Contains("%80"));
        }
    }
}
