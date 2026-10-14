using System.Text;
using AkelEtiket.Core.Data;

namespace AkelEtiket.DataImport.Tests;

public class CsvImportTests
{
    private const string TurkishLine = "Çorba Kasesi;ğüşıöç İĞÜŞÖÇ";

    [Fact]
    public void Utf8WithBom_TurkishCharactersCorrect()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteText(dir, "bom.csv", "UrunAdi;Not\r\n" + TurkishLine + "\r\n", Fixtures.Utf8Bom);

        var table = DataImporter.Import(path);

        Assert.Equal(DataSourceTable.KindCsv, table.SourceKind);
        Assert.Equal("UTF-8", table.EncodingName);
        Assert.Equal(';', table.Delimiter);
        Assert.Equal(["UrunAdi", "Not"], table.Columns); // BOM başlığa karışmaz
        Assert.Equal("Çorba Kasesi", table.Rows[0].GetValue(0));
        Assert.Equal("ğüşıöç İĞÜŞÖÇ", table.Rows[0].GetValue(1));
    }

    [Fact]
    public void Utf8WithoutBom_IsDetected()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteText(dir, "nobom.csv", "UrunAdi;Not\n" + TurkishLine + "\n", Fixtures.Utf8NoBom);

        var table = DataImporter.Import(path);

        Assert.Equal("UTF-8", table.EncodingName);
        Assert.Equal("ğüşıöç İĞÜŞÖÇ", table.Rows[0].GetValue(1));
    }

    [Fact]
    public void Windows1254_IsDetected_AndTurkishCharactersCorrect()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteText(dir, "ansi.csv", "UrunAdi;Not\r\n" + TurkishLine + "\r\n", Fixtures.Windows1254);

        var table = DataImporter.Import(path);

        Assert.Equal("Windows-1254 (Türkçe)", table.EncodingName);
        Assert.Equal("Çorba Kasesi", table.Rows[0].GetValue(0));
        Assert.Equal("ğüşıöç İĞÜŞÖÇ", table.Rows[0].GetValue(1));
    }

    [Fact]
    public void Utf16LittleEndianWithBom_IsDetected()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteText(dir, "utf16.csv", "UrunAdi\tNot\r\n" + TurkishLine.Replace(';', '\t') + "\r\n", Encoding.Unicode);

        var table = DataImporter.Import(path);

        Assert.Equal("UTF-16 LE", table.EncodingName);
        Assert.Equal('\t', table.Delimiter);
        Assert.Equal("ğüşıöç İĞÜŞÖÇ", table.Rows[0].GetValue(1));
    }

    [Theory]
    [InlineData(',')]
    [InlineData(';')]
    [InlineData('\t')]
    [InlineData('|')]
    public void Delimiter_IsDetected(char delimiter)
    {
        using var dir = new TempDirectory();
        string d = delimiter.ToString();
        string content = $"Kod{d}Ad{d}Barkod\r\nA1{d}Tepsi{d}8691234567890\r\nA2{d}Kase{d}8691234100035\r\n";
        string path = Fixtures.WriteText(dir, "ayirici.csv", content, Fixtures.Utf8NoBom);

        var table = DataImporter.Import(path);

        Assert.Equal(delimiter, table.Delimiter);
        Assert.Equal(["Kod", "Ad", "Barkod"], table.Columns);
        Assert.Equal("Kase", table.Rows[1].GetValue("Ad"));
    }

    [Fact]
    public void TurkishSemicolonFileWithDecimalCommas_DetectsSemicolon_AndKeepsValuesAsText()
    {
        using var dir = new TempDirectory();
        string content =
            "UrunKodu;Barkod;Fiyat;İndirim\r\n" +
            "TST001;0012345678905;120,50;1,5\r\n" +
            "TST002;8691234567890;160,00;2,25\r\n" +
            "TST003;8691234100035;1.250,90;0\r\n";
        string path = Fixtures.WriteText(dir, "tr.csv", content, Fixtures.Windows1254);

        var table = DataImporter.Import(path);

        Assert.Equal(';', table.Delimiter);
        Assert.Equal(["UrunKodu", "Barkod", "Fiyat", "İndirim"], table.Columns);
        Assert.Equal("0012345678905", table.Rows[0].GetValue("Barkod"));
        Assert.Equal("120,50", table.Rows[0].GetValue("Fiyat"));
        Assert.Equal("1.250,90", table.Rows[2].GetValue("Fiyat"));
    }

    [Fact]
    public void QuotedFieldsWithDelimitersAndNewlines_KeepCorrectRowNumbers()
    {
        using var dir = new TempDirectory();
        string content =
            "Kod,Ad,Barkod\r\n" +                     // 1
            "A1,\"Tepsi, büyük\",8691234567890\r\n" + // 2
            "A2,\"Çok\r\n" +                          // 3
            "satırlı \"\"ad\"\"\",8691234100035\r\n" + // 4
            "\r\n" +                                  // 5 boş
            "A3,Kase,8691234100042\r\n";              // 6
        string path = Fixtures.WriteText(dir, "tirnak.csv", content, Fixtures.Utf8NoBom);

        var table = DataImporter.Import(path);

        Assert.Equal(',', table.Delimiter);
        Assert.Equal([2, 3, 6], table.Rows.Select(r => r.RowNumber));
        Assert.Equal("Tepsi, büyük", table.Rows[0].GetValue("Ad"));
        Assert.Equal("Çok\r\nsatırlı \"ad\"", table.Rows[1].GetValue("Ad"));
        Assert.Equal("8691234100042", table.Rows[2].GetValue("Barkod"));
        Assert.Equal(1, table.SkippedEmptyRows);
    }

    [Fact]
    public void ExcelSepDirective_SetsDelimiter_AndRowNumbersCountTheDirectiveLine()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteText(dir, "sep.csv", "sep=;\r\nKod;Ad,Not\r\nA1;Tepsi, büyük\r\n", Fixtures.Utf8NoBom);

        var table = DataImporter.Import(path);

        Assert.Equal(';', table.Delimiter);
        Assert.Equal(2, table.HeaderRowNumber);
        Assert.Equal(["Kod", "Ad,Not"], table.Columns);
        Assert.Equal(3, table.Rows[0].RowNumber);
        Assert.Equal("Tepsi, büyük", table.Rows[0].GetValue(1));
    }

    [Fact]
    public void HeadersAreMadeUnique_AndExtraFieldsGetSutunN()
    {
        using var dir = new TempDirectory();
        string content = "Barkod;;Barkod;Ad\r\n1;2;3;4;5\r\n6;7;8;9\r\n";
        string path = Fixtures.WriteText(dir, "baslik.csv", content, Fixtures.Utf8NoBom);

        var table = DataImporter.Import(path);

        Assert.Equal(["Barkod", "Sütun 2", "Barkod (2)", "Ad", "Sütun 5"], table.Columns);
        Assert.Equal(["1", "2", "3", "4", "5"], table.Rows[0].Values);
        Assert.Equal(["6", "7", "8", "9", ""], table.Rows[1].Values);
    }

    [Fact]
    public void TrailingDelimiters_DoNotCreateEmptyColumns()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteText(dir, "sonda.csv", "Kod;Ad;;\r\nA1;Tepsi;;\r\n", Fixtures.Utf8NoBom);

        var table = DataImporter.Import(path);

        Assert.Equal(["Kod", "Ad"], table.Columns);
        Assert.Equal(["A1", "Tepsi"], table.Rows[0].Values);
    }

    [Fact]
    public void EmptyLinesAreSkipped_WithRealLineNumbers()
    {
        using var dir = new TempDirectory();
        string content = "\r\n\r\nKod,Ad\r\n\r\nA1,Tepsi\r\n   \r\nA2,Kase\r\n,\r\n";
        string path = Fixtures.WriteText(dir, "bos.csv", content, Fixtures.Utf8NoBom);

        var table = DataImporter.Import(path);

        Assert.Equal(3, table.HeaderRowNumber);
        Assert.Equal([5, 7], table.Rows.Select(r => r.RowNumber));
        Assert.Equal(3, table.SkippedEmptyRows);
    }

    [Fact]
    public void ValuesAreNeverConvertedToNumbers()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteText(dir, "metin.csv",
            "Barkod,Fiyat,Kod\r\n0012345678905,1.50,000123\r\n8691234567890,1e3,TST001\r\n", Fixtures.Utf8NoBom);

        var table = DataImporter.Import(path);

        Assert.Equal(["0012345678905", "1.50", "000123"], table.Rows[0].Values);
        Assert.Equal("1e3", table.Rows[1].GetValue("Fiyat"));
    }

    [Fact]
    public void ScientificNotationAndLeadingZeroLoss_AreReportedWithRowNumbers()
    {
        using var dir = new TempDirectory();
        var sb = new StringBuilder("UrunAdi;Barkod\r\n");
        for (int i = 0; i < 10; i++) sb.Append($"Ürün {i};{Fixtures.Ean13($"8691234100{i:00}")}\r\n"); // satır 2–11
        sb.Append("Bozuk;8,69123E+12\r\n");   // 12
        sb.Append("Sıfırsız;12345678905\r\n"); // 13
        sb.Append("Tamam;0012345678905\r\n");  // 14
        string path = Fixtures.WriteText(dir, "sorunlu.csv", sb.ToString(), Fixtures.Utf8Bom);

        var table = DataImporter.Import(path);

        Assert.Equal(13, table.Rows.Count);
        var scientific = Assert.Single(table.Issues, i => i.Message.Contains("bilimsel"));
        Assert.Equal(12, scientific.RowNumber);
        Assert.Equal("Barkod", scientific.Column);
        var zeros = Assert.Single(table.Issues, i => i.Message.Contains("baştaki sıfırlar kaybolmuş olabilir (beklenen 13 hane)"));
        Assert.Equal(13, zeros.RowNumber);
        Assert.Equal(ImportIssueSeverity.Warning, zeros.Severity);
        Assert.Equal(2, table.WarningCount);
        Assert.Equal(0, table.ErrorCount);
    }

    [Fact]
    public void LeadingZeroHeuristic_IgnoresShortNumericColumnsLikePrices()
    {
        using var dir = new TempDirectory();
        var sb = new StringBuilder("Fiyat,Adet\r\n");
        foreach (var price in new[] { "120", "160", "150", "130", "95" }) sb.Append($"{price},1\r\n");
        string path = Fixtures.WriteText(dir, "fiyat.csv", sb.ToString(), Fixtures.Utf8NoBom);

        var table = DataImporter.Import(path);

        Assert.Empty(table.Issues);
    }

    [Fact]
    public void ExplicitOptions_OverrideDetection()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteText(dir, "zorla.csv", "Kod,Ad\r\nA1,Şeker\r\n", Fixtures.Windows1254);

        var forcedDelimiter = DataImporter.Import(path, new ImportOptions { Delimiter = ';' });
        Assert.Equal(';', forcedDelimiter.Delimiter);
        Assert.Equal(["Kod,Ad"], forcedDelimiter.Columns);
        Assert.Equal("A1,Şeker", forcedDelimiter.Rows[0].GetValue(0));

        var forcedEncoding = DataImporter.Import(path, new ImportOptions { Encoding = Encoding.GetEncoding(28599) });
        Assert.Equal("ISO-8859-9 (Türkçe)", forcedEncoding.EncodingName);
        Assert.Equal("Şeker", forcedEncoding.Rows[0].GetValue("Ad"));

        var withoutTrim = Fixtures.WriteText(dir, "bosluk.csv", "Kod,Ad\r\n A1 , Tepsi \r\n", Fixtures.Utf8NoBom);
        Assert.Equal(" Tepsi ", DataImporter.Import(withoutTrim, new ImportOptions { TrimValues = false }).Rows[0].GetValue(1));
        Assert.Equal("Tepsi", DataImporter.Import(withoutTrim).Rows[0].GetValue(1));
    }

    [Fact]
    public void HeaderRowOption_UsesPhysicalLineNumber()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteText(dir, "baslik.csv", "Rapor;2026\r\n\r\nKod;Ad\r\nA1;Tepsi\r\n", Fixtures.Utf8NoBom);

        var table = DataImporter.Import(path, new ImportOptions { HeaderRow = 3 });

        Assert.Equal(3, table.HeaderRowNumber);
        Assert.Equal(["Kod", "Ad"], table.Columns);
        Assert.Equal(4, Assert.Single(table.Rows).RowNumber);
    }

    [Fact]
    public void EmptyOrBlankFile_Throws_HeaderOnlyFileReturnsNoRowsWithWarning()
    {
        using var dir = new TempDirectory();
        string empty = dir.File("bos.csv");
        File.WriteAllBytes(empty, []);
        Assert.Contains("boş", Assert.Throws<DataImportException>(() => DataImporter.Import(empty)).Message);

        string blank = Fixtures.WriteText(dir, "satirsiz.csv", "\r\n  \r\n", Fixtures.Utf8NoBom);
        Assert.Throws<DataImportException>(() => DataImporter.Import(blank));

        string headerOnly = Fixtures.WriteText(dir, "baslik.csv", "Kod;Ad\r\n", Fixtures.Utf8NoBom);
        var table = DataImporter.Import(headerOnly);
        Assert.Empty(table.Rows);
        Assert.Contains(table.Issues, i => i.RowNumber == 0 && i.Severity == ImportIssueSeverity.Warning);
    }

    [Fact]
    public void FileHeldOpenForWriting_CanBeRead()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteText(dir, "acik.csv", "Kod;Ad\r\nA1;Tepsi\r\n", Fixtures.Utf8NoBom);

        using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        Assert.Equal("Tepsi", DataImporter.Import(path).Rows[0].GetValue("Ad"));
    }

    [Fact]
    public void BadQuotes_AreReportedButRowIsRead()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteText(dir, "tirnak.csv", "Kod,Ad\r\nA1,5\" boru\r\nA2,Kase\r\n", Fixtures.Utf8NoBom);

        var table = DataImporter.Import(path);

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("5\" boru", table.Rows[0].GetValue("Ad"));
        Assert.Contains(table.Issues, i => i.RowNumber == 2 && i.Message.Contains("tırnak"));
    }

    [Fact]
    public void Record_ToFieldSource_FillsPlaceholders()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteText(dir, "alan.csv", "Ürün Adı;Barkod;Fiyat\r\nŞekerlik;0012345678905;1234,5\r\n", Fixtures.Utf8Bom);

        var table = DataImporter.Import(path);
        var record = table.Rows[0];

        Assert.Equal("Şekerlik 0012345678905", FieldTemplate.Resolve("{UrunAdi} {Barkod}", record.ToFieldSource()).Text);
        Assert.Equal("1.234,50 TL", FieldTemplate.Resolve("{Fiyat:#,##0.00} TL", record.ToFieldSource(table.Columns)).Text);
        Assert.Equal(1, table.IndexOfColumn("barkod"));
        Assert.Equal("0012345678905", record.GetValue("BARKOD"));
    }
}
