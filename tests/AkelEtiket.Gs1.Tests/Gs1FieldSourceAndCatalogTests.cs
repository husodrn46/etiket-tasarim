using AkelEtiket.BarcodeEngine;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;

namespace AkelEtiket.Gs1.Tests;

public class Gs1FieldSourceTests
{
    [Fact]
    public void Preview_ReturnsPlaceholder_WithoutTouchingDisk()
    {
        using var temp = new TempFolder();
        var context = new Gs1Context(temp.Path);
        var fields = context.PreviewFields;
        Assert.True(fields.TryGetValue("SSCC", null, out var sscc));
        Assert.Equal(Gs1FieldSource.PreviewSscc, sscc);
        Assert.True(fields.TryGetValue("sscc", null, out _));
        Assert.Empty(Directory.GetFiles(temp.Path));
        Assert.False(File.Exists(context.Registry.LogPath));
    }

    [Fact]
    public void ProfileIsLoaded_OnlyWhenGlnOrGcpRequested()
    {
        int calls = 0;
        var profile = TestData.VerifiedProfile();
        var source = Gs1FieldSource.Preview(() =>
        {
            calls++;
            return profile;
        });
        source.TryGetValue("SSCC", null, out _);
        Assert.Equal(0, calls);
        Assert.True(source.TryGetValue("GLN", null, out var gln));
        Assert.Equal("1234567890128", gln);
        Assert.True(source.TryGetValue("GCP", null, out var gcp));
        Assert.Equal(TestData.TestGcp, gcp);
        Assert.True(source.TryGetValue("FirmaOneki", null, out _));
        Assert.Equal(3, calls);
        Assert.False(source.TryGetValue("UrunAdi", null, out _));
    }

    [Fact]
    public void ForAllocated_ValidatesNumber()
    {
        var sscc = Gs1Identifiers.BuildSscc(0, TestData.TestGcp, 9);
        var source = Gs1FieldSource.ForAllocated(sscc);
        Assert.False(source.IsPreview);
        Assert.True(source.TryGetValue("SSCC", null, out var value));
        Assert.Equal(sscc, value);
        Assert.Throws<ArgumentException>(() => Gs1FieldSource.ForAllocated("123456789012345678"));
    }

    [Fact]
    public void PreviewSscc_MakesGs1_128ContentValid()
    {
        var resolved = FieldTemplate.Resolve("(00){SSCC}", Gs1FieldSource.Preview());
        Assert.True(resolved.IsComplete);
        var validation = BarcodeValidator.Validate(BarcodeType.Gs1_128, resolved.Text);
        Assert.True(validation.IsValid, validation.FirstError);
    }

    [Fact]
    public void DocumentUsage_IsDetectedInTextAndBarcodes()
    {
        var doc = LabelDocument.CreateBlank();
        Assert.False(Gs1FieldSource.DocumentUsesSscc(doc));
        doc.Elements.Add(new TextElement { Text = "GLN: {GLN}" });
        Assert.False(Gs1FieldSource.DocumentUsesSscc(doc));
        Assert.True(Gs1FieldSource.DocumentUsesGs1Fields(doc));
        doc.Elements.Add(new BarcodeElement { Symbology = BarcodeType.Gs1_128, Content = "(00){sscc}" });
        Assert.True(Gs1FieldSource.DocumentUsesSscc(doc));
    }
}

public class Gs1AiCatalogTests
{
    [Theory]
    [InlineData("00", "N2+N18", false, true)]
    [InlineData("01", "N2+N14", false, true)]
    [InlineData("02", "N2+N14", false, true)]
    [InlineData("10", "N2+X..20", true, false)]
    [InlineData("37", "N2+N..8", true, false)]
    [InlineData("410", "N3+N13", false, true)]
    public void CommonAis_HaveFormatsAndTurkishDescriptions(string ai, string format, bool separator, bool checkDigit)
    {
        var info = Gs1AiCatalog.Find(ai);
        Assert.NotNull(info);
        Assert.Equal(format, info!.Format);
        Assert.Equal(separator, info.NeedsSeparator);
        Assert.Equal(checkDigit, info.HasCheckDigit);
        Assert.False(string.IsNullOrWhiteSpace(info.Description));
        Assert.Equal($"({ai})", info.Display);
    }

    [Fact]
    public void Catalog_MirrorsBarcodeEngineTable()
    {
        Assert.Equal(BarcodeEngine.Gs1.Definitions.Count, Gs1AiCatalog.All.Count);
        foreach (var ai in new[] { "00", "01", "02", "10", "11", "13", "15", "17", "21", "30", "37", "3103", "3302", "400", "410", "411", "412", "413", "414", "420" })
            Assert.NotNull(Gs1AiCatalog.Find(ai));
        Assert.All(Gs1AiCatalog.Common, a => Assert.True(a.IsCommon));
        Assert.Contains("ondalık", Gs1AiCatalog.Find("3103")!.Description);
    }
}

public class GtinImportTests
{
    [Fact]
    public void Import_MapsColumns_ReportsBadRowsByNumber_AndSkipsDuplicates()
    {
        IReadOnlyList<string> columns = ["Ürün Kodu", "Ürün Adı", "Barkod"];
        var rows = new List<(int, IReadOnlyList<string>)>
        {
            (2, ["TST-1", "Tabak", "4006381333931"]),
            (3, ["TST-2", "Kase", "4006381333932"]),
            (4, ["TST-3", "Tepsi", "73513537"]),
            (5, ["TST-4", "Tekrar", "04006381333931"]),
            (6, ["TST-5", "Boş", ""]),
            (7, ["TST-6", "Mevcut", "036000291452"]),
        };
        var existing = new[] { new RegisteredGtin { Gtin = "036000291452" } };
        var result = GtinImport.FromRows(columns, rows, existing);

        Assert.Equal(["4006381333931", "73513537"], result.Added.Select(g => g.Gtin));
        Assert.Equal("TST-1", result.Added[0].ProductCode);
        Assert.Equal("Tabak", result.Added[0].Description);
        Assert.Contains(result.Skipped, s => s.StartsWith("Satır 3:", StringComparison.Ordinal) && s.Contains("kontrol basamağı"));
        Assert.Contains(result.Skipped, s => s.StartsWith("Satır 5:", StringComparison.Ordinal) && s.Contains("zaten listede"));
        Assert.Contains(result.Skipped, s => s.StartsWith("Satır 7:", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Skipped, s => s.StartsWith("Satır 6:", StringComparison.Ordinal));
        Assert.Contains("Barkod", result.ColumnSummary);
    }

    [Fact]
    public void Import_WithoutGtinColumn_ExplainsWhy()
    {
        var result = GtinImport.FromRows(["Ad", "Fiyat"], [(2, ["x", "1"])], []);
        Assert.Empty(result.Added);
        Assert.Contains("GTIN sütunu bulunamadı", Assert.Single(result.Skipped));
    }
}
