using System.Text;
using AkelEtiket.DataImport.Csv;
using AkelEtiket.DataImport.Internal;

namespace AkelEtiket.DataImport.Tests;

public class DetectionTests
{
    [Theory]
    [InlineData("a,b,c\n1,2,3\n4,5,6", ',')]
    [InlineData("a;b;c\n1,5;2;3\n4;5,25;6", ';')]
    [InlineData("a\tb\n1\t2", '\t')]
    [InlineData("a|b|c\n1|2|3", '|')]
    [InlineData("\"x;y\",b\n\"1;2\",3", ',')]
    [InlineData("tek sütun\ndeğer", ',')]
    [InlineData("Kod;\"Ad, uzun\"\nA1;\"Tepsi, büyük\"", ';')]
    public void Detect_PicksMostConsistentDelimiter(string text, char expected) =>
        Assert.Equal(expected, DelimiterDetector.Detect(text));

    [Fact]
    public void Detect_QuotedNewlinesDoNotSplitRecords()
    {
        // Tırnak içindeki satır sonu yeni kayıt sayılırsa noktalı virgül tutarsız görünürdü.
        string text = "Kod;Ad;Not\n1;\"çok\nsatırlı, virgüllü\";x\n2;b;c";
        Assert.Equal(';', DelimiterDetector.Detect(text));
    }

    [Theory]
    [InlineData("sep=;\r\nA;B", ';', "A;B")]
    [InlineData("SEP=,\nA,B", ',', "A,B")]
    [InlineData("sep=|", '|', "")]
    public void SepDirective_IsStripped(string text, char expected, string remaining)
    {
        int offset = 0;
        Assert.Equal(expected, DelimiterDetector.TryStripSepDirective(ref text, ref offset));
        Assert.Equal(remaining, text);
        Assert.Equal(1, offset);
    }

    [Fact]
    public void SepDirective_AbsentLeavesTextUntouched()
    {
        string text = "separator;x\nA;B";
        int offset = 0;
        Assert.Null(DelimiterDetector.TryStripSepDirective(ref text, ref offset));
        Assert.Equal("separator;x\nA;B", text);
        Assert.Equal(0, offset);
    }

    [Fact]
    public void Decode_DetectsUtf16WithoutBom()
    {
        var bytes = Encoding.Unicode.GetBytes("Kod;Ad\r\nA1;Şekerlik\r\n");
        var (text, name) = TextDecoding.Decode(bytes, null);
        Assert.Equal("UTF-16 LE", name);
        Assert.Contains("Şekerlik", text);
    }

    [Theory]
    [InlineData("8,69123E+12", true)]
    [InlineData("8.69123E+12", true)]
    [InlineData("1E5", true)]
    [InlineData("-2.5e-3", true)]
    [InlineData("8691234567890", false)]
    [InlineData("TST-E12", false)]
    [InlineData("E12", false)]
    [InlineData("", false)]
    public void ScientificNotation_IsRecognized(string value, bool expected) =>
        Assert.Equal(expected, IntegrityChecks.IsScientificNotation(value));

    [Theory]
    [InlineData("Barkod", "Barkod")]
    [InlineData("  Ürün   Adı ", "Ürün Adı")]
    [InlineData("Fiyat:TL", "Fiyat TL")]
    [InlineData("{Kod}", "(Kod)")]
    [InlineData("Satır\r\nSonu", "Satır Sonu")]
    public void HeaderSanitize(string raw, string expected) => Assert.Equal(expected, HeaderBuilder.Sanitize(raw));
}

public class SampleFileTests
{
    private static string Sample(string name) => Path.Combine(Fixtures.RepoRoot(), "samples", name);

    [Fact]
    public void SampleExcel_Has120Products_WithSyntheticRowsFirst()
    {
        var table = DataImporter.Import(Sample("Ornek-Urunler.xlsx"));

        Assert.Equal("Ürünler", table.SheetName);
        Assert.Equal(["UrunKodu", "UrunAdi", "Barkod", "Fiyat", "Adet"], table.Columns);
        Assert.Equal(120, table.Rows.Count);
        Assert.Equal(2, table.Rows[0].RowNumber);
        Assert.Equal(121, table.Rows[^1].RowNumber);
        Assert.Equal(["DEMO001", "Örnek Ürün A", "8691234567890", "120", "1"], table.Rows[0].Values);
        Assert.Equal("8691234567891", table.Rows[1].GetValue("Barkod")); // bilerek hatalı kontrol basamağı
        Assert.All(table.Rows, r => Assert.Equal(13, r.GetValue("Barkod").Length));
        Assert.Equal(120, table.Rows.Select(r => r.GetValue("UrunKodu")).Distinct().Count());
        Assert.Equal(120, table.Rows.Select(r => r.GetValue("UrunAdi")).Distinct().Count());
        Assert.All(table.Rows, r => Assert.InRange(int.Parse(r.GetValue("Adet")), 1, 3));
        Assert.Contains(table.Rows, r => r.GetValue("UrunAdi").Contains('İ') && r.GetValue("UrunAdi").Contains('ı'));
        Assert.Equal(0, table.ErrorCount);
        Assert.Equal(0, table.WarningCount);

        // Kullanıcı örneği dışındaki tüm barkodlar geçerli EAN-13.
        foreach (var row in table.Rows.Skip(2))
        {
            string barcode = row.GetValue("Barkod");
            Assert.Equal(Fixtures.Ean13(barcode[..12]), barcode);
        }
        Assert.NotEqual(Fixtures.Ean13("869123456789"), table.Rows[1].GetValue("Barkod"));
        Assert.Equal(Fixtures.Ean13("869123456789"), table.Rows[0].GetValue("Barkod"));
    }

    [Fact]
    public void SampleCsvFiles_MatchExcelData()
    {
        var excel = DataImporter.Import(Sample("Ornek-Urunler.xlsx"));
        var utf8 = DataImporter.Import(Sample("ornek-urunler-utf8.csv"));
        var ansi = DataImporter.Import(Sample("ornek-urunler-1254.csv"));

        Assert.Equal("UTF-8", utf8.EncodingName);
        Assert.Equal(',', utf8.Delimiter);
        Assert.Equal("Windows-1254 (Türkçe)", ansi.EncodingName);
        Assert.Equal(';', ansi.Delimiter);

        foreach (var table in new[] { utf8, ansi })
        {
            Assert.Equal(excel.Columns, table.Columns);
            Assert.Equal(120, table.Rows.Count);
            for (int i = 0; i < 120; i++)
            {
                Assert.Equal(excel.Rows[i].GetValue("UrunKodu"), table.Rows[i].GetValue("UrunKodu"));
                Assert.Equal(excel.Rows[i].GetValue("UrunAdi"), table.Rows[i].GetValue("UrunAdi"));
                Assert.Equal(excel.Rows[i].GetValue("Barkod"), table.Rows[i].GetValue("Barkod"));
                Assert.Equal(excel.Rows[i].RowNumber, table.Rows[i].RowNumber);
            }
        }
        Assert.Equal("120,00", ansi.Rows[0].GetValue("Fiyat"));
        Assert.Equal("120.00", utf8.Rows[0].GetValue("Fiyat"));
    }

    [Fact]
    public void SampleFaultyCsv_ReportsProblemsByRow_WithoutBlockingOtherRows()
    {
        var table = DataImporter.Import(Sample("ornek-hatali-veri.csv"));

        Assert.Equal(16, table.Rows.Count);
        int RowOf(string code) => table.Rows.Single(r => r.GetValue("UrunKodu") == code).RowNumber;
        Assert.Contains(table.Issues, i => i.RowNumber == RowOf("HTL01") && i.Message.Contains("bilimsel"));
        Assert.Contains(table.Issues, i => i.RowNumber == RowOf("HTL02") && i.Message.Contains("baştaki sıfırlar"));
        Assert.Contains(table.Issues, i => i.RowNumber == RowOf("HTL04") && i.Message.Contains("baştaki sıfırlar"));
        Assert.Equal("", table.Rows.Single(r => r.GetValue("UrunKodu") == "HTL05").GetValue("Barkod"));
        Assert.Equal("86912345ABC90", table.Rows.Single(r => r.GetValue("UrunKodu") == "HTL03").GetValue("Barkod"));
    }
}
