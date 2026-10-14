using System.Text;
using ClosedXML.Excel;

namespace AkelEtiket.DataImport.Tests;

/// <summary>
/// Eski Excel (.xls) okuma: .xlsx ile aynı güvenceler. Test dosyası gerçek bir BIFF8 çalışma kitabıdır:
/// Fixtures/eski-excel.xls (üretimi: Fixtures/eski-excel.py, Fixtures/BENIOKU.md).
/// </summary>
public class XlsImportTests
{
    private static string FixturePath => Path.Combine(Fixtures.RepoRoot(), "tests", "AkelEtiket.DataImport.Tests", "Fixtures", "eski-excel.xls");

    [Fact]
    public void SheetNames_AreListedInFileOrder()
    {
        Assert.Equal(["Boş Sayfa", "Gizli", "Ürünler", "Fiyatlar"], DataImporter.GetSheetNames(FixturePath));
        Assert.True(DataImporter.IsExcelFile("ESKI.XLS"));
        Assert.True(DataImporter.IsSupportedFile("veri.xls"));
    }

    [Fact]
    public void DefaultSheet_SkipsEmptyAndHiddenSheets_AndKeepsExcelRowNumbers()
    {
        var table = DataImporter.Import(FixturePath);

        Assert.Equal(DataSourceTable.KindExcel, table.SourceKind);
        Assert.Equal("Ürünler", table.SheetName);
        Assert.Equal(2, table.HeaderRowNumber); // 1. satır boş
        Assert.Equal(
            ["Kod", "Ürün Adı", "Barkod", "Barkod Sayı", "Barkod Biçimli", "Fiyat", "Adet", "Tarih", "Tarih Saat", "Aktif", "İndirim", "Uzun", "Bilimsel", "Hata"],
            table.Columns);
        Assert.Equal([3, 4, 6], table.Rows.Select(r => r.RowNumber));
        Assert.Equal(1, table.SkippedEmptyRows); // 5. satır boş
    }

    [Fact]
    public void CellValues_FollowBarcodeSafeRules()
    {
        var table = DataImporter.Import(FixturePath);
        var first = table.Rows[0];
        Assert.Equal("TST001", first.GetValue("Kod"));
        Assert.Equal("0012345678905", first.GetValue("Barkod"));          // metin: baştaki sıfırlar korunur
        Assert.Equal("8691234567890", first.GetValue("Barkod Sayı"));     // sayı: bilimsel gösterim yok
        Assert.Equal("0012345678905", first.GetValue("Barkod Biçimli"));  // "0000000000000" biçimi
        Assert.Equal("12,50", first.GetValue("Fiyat"));
        Assert.Equal("3", first.GetValue("Adet"));
        Assert.Equal("13.10.2026", first.GetValue("Tarih"));
        Assert.Equal("13.10.2026 14:30", first.GetValue("Tarih Saat"));
        Assert.Equal("DOĞRU", first.GetValue("Aktif"));
        Assert.Contains("20", first.GetValue("İndirim"));
        Assert.Contains("%", first.GetValue("İndirim"));
        Assert.Equal("", first.GetValue("Hata"));

        var second = table.Rows[1];
        Assert.Equal("Çay Bardağı IŞIKLI", second.GetValue("Ürün Adı"));
        Assert.Equal("0000000001234", second.GetValue("Barkod Biçimli"));
        Assert.Equal("1.234,50", second.GetValue("Fiyat"));
        Assert.Equal("0", second.GetValue("Adet"));
        Assert.Equal("02.01.2026", second.GetValue("Tarih"));
        Assert.Equal("02.01.2026", second.GetValue("Tarih Saat")); // gece yarısı: saat yazılmaz
        Assert.Equal("YANLIŞ", second.GetValue("Aktif"));
        Assert.Contains("15,5", second.GetValue("İndirim"));
        Assert.Equal("42", second.GetValue("Uzun"));

        var third = table.Rows[2];
        Assert.Equal("TST003", third.GetValue("Kod")); // boşluklar kırpılır
        Assert.Equal("0,1", third.GetValue("Fiyat"));   // Genel biçimli ondalık
        Assert.Equal("", third.GetValue("Barkod"));
    }

    [Fact]
    public void Issues_HaveExcelRowNumbersAndColumns()
    {
        var table = DataImporter.Import(FixturePath);

        var tooLong = Assert.Single(table.Issues, i => i.Severity == ImportIssueSeverity.Error);
        Assert.Equal(3, tooLong.RowNumber);
        Assert.Equal("Uzun", tooLong.Column);
        Assert.Contains("15 haneden uzun", tooLong.Message);
        Assert.StartsWith("123456789012346", table.Rows[0].GetValue("Uzun")); // Excel 15 anlamlı basamak saklar

        var scientific = Assert.Single(table.Issues, i => i.Column == "Bilimsel");
        Assert.Equal((3, ImportIssueSeverity.Warning), (scientific.RowNumber, scientific.Severity));
        Assert.Contains("bilimsel gösterimde", scientific.Message);

        var error = Assert.Single(table.Issues, i => i.Column == "Hata");
        Assert.Equal((3, ImportIssueSeverity.Warning), (error.RowNumber, error.Severity));
        Assert.Contains("#N/A", error.Message);
    }

    [Fact]
    public void SameSheetAsXlsx_GivesIdenticalTable()
    {
        using var dir = new TempDirectory();
        string xlsx = Fixtures.WriteWorkbook(dir, "ayni.xlsx", wb =>
        {
            var ws = wb.AddWorksheet("Ürünler");
            string[] headers = ["Kod", "Ürün Adı", "Barkod", "Barkod Sayı", "Barkod Biçimli", "Fiyat", "Adet", "Tarih", "Tarih Saat", "Aktif", "İndirim", "Uzun", "Bilimsel", "Hata"];
            for (int c = 0; c < headers.Length; c++) ws.Cell(2, c + 1).Value = headers[c];
            void Row(int r, XLCellValue[] values, string?[] formats)
            {
                for (int c = 0; c < values.Length; c++)
                {
                    if (values[c].IsBlank) continue;
                    ws.Cell(r, c + 1).Value = values[c];
                    if (formats[c] is { } f) ws.Cell(r, c + 1).Style.NumberFormat.Format = f;
                }
            }
            Row(3,
                ["TST001", "Melamin Kase", "0012345678905", 8691234567890d, 12345678905d, 12.5, 3, new DateTime(2026, 10, 13), new DateTime(2026, 10, 13, 14, 30, 0), true, 0.2, 12345678901234567d, "8,69123E+12", XLError.NoValueAvailable],
                [null, null, null, null, "0000000000000", "0.00", null, "dd.mm.yyyy", "dd.mm.yyyy hh:mm", null, "0%", null, null, null]);
            Row(4,
                ["TST002", "Çay Bardağı IŞIKLI", "0012345678912", 8690000000013d, 1234, 1234.5, 0, new DateTime(2026, 1, 2), new DateTime(2026, 1, 2), false, 0.155, 42, Blank.Value, Blank.Value],
                [null, null, null, null, "0000000000000", "#,##0.00", null, "dd.mm.yyyy", "dd.mm.yyyy hh:mm", null, "0.0%", null, null, null]);
            Row(6, ["  TST003  ", "Servis Tabağı", Blank.Value, Blank.Value, Blank.Value, 0.1, 2], [null, null, null, null, null, null, null]);
        });

        var expected = DataImporter.Import(xlsx);
        var actual = DataImporter.Import(FixturePath);

        Assert.Equal(expected.Columns, actual.Columns);
        Assert.Equal(expected.HeaderRowNumber, actual.HeaderRowNumber);
        Assert.Equal(expected.SkippedEmptyRows, actual.SkippedEmptyRows);
        Assert.Equal(expected.Rows.Select(r => r.RowNumber), actual.Rows.Select(r => r.RowNumber));
        for (int i = 0; i < expected.Rows.Count; i++)
            Assert.Equal(expected.Rows[i].Values, actual.Rows[i].Values);
        Assert.Equal(
            expected.Issues.Select(i => (i.RowNumber, i.Column, i.Severity, i.Message)),
            actual.Issues.Select(i => (i.RowNumber, i.Column, i.Severity, i.Message)));
    }

    [Fact]
    public void SheetSelection_ByName_IsCaseInsensitiveWithTurkishRules()
    {
        var prices = DataImporter.Import(FixturePath, new ImportOptions { SheetName = "FİYATLAR" });
        Assert.Equal("Fiyatlar", prices.SheetName);
        Assert.Equal(["Kod", "Fiyat"], prices.Columns);
        Assert.Equal(["19,9", "7"], prices.Rows.Select(r => r.GetValue("Fiyat")));
        Assert.Equal([2, 3], prices.Rows.Select(r => r.RowNumber));

        // Gizli sayfa adıyla istenirse okunur.
        var hidden = DataImporter.Import(FixturePath, new ImportOptions { SheetName = "gizli" });
        Assert.Equal(["Gizli veri"], hidden.Columns);
        Assert.Equal("x", Assert.Single(hidden.Rows).GetValue(0));
    }

    [Fact]
    public void SheetSelection_Errors_AreTurkish()
    {
        var missing = Assert.Throws<DataImportException>(() => DataImporter.Import(FixturePath, new ImportOptions { SheetName = "Stok" }));
        Assert.Contains("\"Stok\" adlı sayfa bulunamadı", missing.Message);
        Assert.Contains("Boş Sayfa, Gizli, Ürünler, Fiyatlar", missing.Message);

        var empty = Assert.Throws<DataImportException>(() => DataImporter.Import(FixturePath, new ImportOptions { SheetName = "Boş Sayfa" }));
        Assert.Contains("\"Boş Sayfa\" sayfasında veri bulunamadı", empty.Message);
    }

    [Fact]
    public void HeaderRowOption_IsHonored()
    {
        var table = DataImporter.Import(FixturePath, new ImportOptions { HeaderRow = 3 });
        Assert.Equal(3, table.HeaderRowNumber);
        Assert.Equal("TST001", table.Columns[0]);
        Assert.Equal([4, 6], table.Rows.Select(r => r.RowNumber));

        var tooFar = Assert.Throws<DataImportException>(() => DataImporter.Import(FixturePath, new ImportOptions { HeaderRow = 50 }));
        Assert.Contains("son dolu satırdan (6)", tooFar.Message);
    }

    [Fact]
    public void CorruptOrTruncatedXls_GivesTurkishMessageWithSaveAsGuidance()
    {
        using var dir = new TempDirectory();
        string header = dir.File("baslik.xls");
        File.WriteAllBytes(header, [0xD0, 0xCF, 0x11, 0xE0]);
        string truncated = dir.File("yarim.xls");
        File.WriteAllBytes(truncated, File.ReadAllBytes(FixturePath)[..1024]);

        foreach (string path in new[] { header, truncated })
        {
            var ex = Assert.Throws<DataImportException>(() => DataImporter.Import(path));
            Assert.Contains("Eski Excel (.xls) dosyası açılamadı", ex.Message);
            Assert.Contains(".xlsx", ex.Message);
            Assert.Equal(path, ex.FilePath);
        }
        Assert.Throws<DataImportException>(() => DataImporter.GetSheetNames(header));
    }

    [Fact]
    public void XlsExtension_WithOtherContent_IsHandled()
    {
        using var dir = new TempDirectory();

        // Yeni biçim (.xlsx) .xls uzantısıyla kaydedilmiş.
        string xlsx = Fixtures.WriteWorkbook(dir, "yeni.xlsx", wb =>
        {
            var ws = wb.AddWorksheet("Veri");
            ws.Cell(1, 1).Value = "Barkod";
            ws.Cell(2, 1).Value = "0012345678905";
        });
        string renamed = dir.File("yeni-ama-xls.xls");
        File.Move(xlsx, renamed);
        Assert.Equal(["Veri"], DataImporter.GetSheetNames(renamed));
        Assert.Equal("0012345678905", Assert.Single(DataImporter.Import(renamed).Rows).GetValue(0));

        // Sekmeli metin dışa aktarımı .xls uzantısıyla.
        string tsv = Fixtures.WriteText(dir, "disa-aktarim.xls", "Kod\tAd\r\nAKL001\tKase\r\n", Fixtures.Windows1254);
        var text = DataImporter.Import(tsv);
        Assert.Equal(DataSourceTable.KindCsv, text.SourceKind);
        Assert.Equal("Kase", Assert.Single(text.Rows).GetValue("Ad"));
        Assert.Contains(text.Issues, i => i.Severity == ImportIssueSeverity.Info && i.Message.Contains("metin dosyası olarak okundu"));

        // Web sayfası (HTML) .xls uzantısıyla.
        string html = Fixtures.WriteText(dir, "web.xls", "<html><table><tr><td>Kod</td></tr></table></html>", Fixtures.Utf8Bom);
        var ex = Assert.Throws<DataImportException>(() => DataImporter.Import(html));
        Assert.Contains("HTML", ex.Message);
    }

    [Fact]
    public void OpenFileFilter_IncludesXls()
    {
        string filter = DataImporter.OpenFileFilter;
        Assert.Equal(0, filter.Split('|').Length % 2);
        Assert.Contains("*.xls;", filter);
        Assert.Contains("|*.xlsx;*.xlsm;*.xls|", filter);
    }

    [Fact]
    public void Fixture_IsRealBiff8CompoundFile()
    {
        byte[] bytes = File.ReadAllBytes(FixturePath);
        Assert.True(bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]));
        // Çalışma kitabı akışının adı ("Workbook", UTF-16) dizinde bulunur.
        Assert.True(bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes("Workbook")) > 0);
    }
}
