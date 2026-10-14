using AkelEtiket.Core.Data;
using ClosedXML.Excel;

namespace AkelEtiket.DataImport.Tests;

public class ExcelImportTests
{
    [Fact]
    public void Reads150Rows_WithColumnsValuesAndRowNumbers()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteWorkbook(dir, "urunler.xlsx", wb =>
        {
            var ws = wb.AddWorksheet("Ürünler");
            ws.Cell(1, 1).Value = "UrunKodu";
            ws.Cell(1, 2).Value = "UrunAdi";
            ws.Cell(1, 3).Value = "Barkod";
            ws.Cell(1, 4).Value = "Fiyat";
            for (int i = 1; i <= 150; i++)
            {
                ws.Cell(i + 1, 1).Value = $"TST{i:000}";
                ws.Cell(i + 1, 2).Value = $"Çorba Kasesi Şekerli İnci {i}";
                ws.Cell(i + 1, 3).Value = Fixtures.Ean13($"869123{i:000000}");
                ws.Cell(i + 1, 4).Value = 100 + i + 0.5;
            }
        });

        var table = DataImporter.Import(path);

        Assert.Equal(DataSourceTable.KindExcel, table.SourceKind);
        Assert.Equal("Ürünler", table.SheetName);
        Assert.Equal(1, table.HeaderRowNumber);
        Assert.Equal(["UrunKodu", "UrunAdi", "Barkod", "Fiyat"], table.Columns);
        Assert.Equal(150, table.Rows.Count);
        Assert.Equal(2, table.Rows[0].RowNumber);
        Assert.Equal(151, table.Rows[^1].RowNumber);
        Assert.Equal("TST150", table.Rows[^1].GetValue(0));
        Assert.Equal("Çorba Kasesi Şekerli İnci 7", table.Rows[6].GetValue("UrunAdi"));
        Assert.Equal(Fixtures.Ean13("869123000001"), table.Rows[0].GetValue("Barkod"));
        Assert.Equal("101,5", table.Rows[0].GetValue("Fiyat"));
        Assert.Equal(0, table.ErrorCount);
        Assert.Equal(0, table.WarningCount);
        Assert.Null(table.Delimiter);
        Assert.Null(table.EncodingName);
    }

    [Fact]
    public void NumericBarcodeCell_ReturnsFullDigits_WithoutExponentOrGrouping()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteWorkbook(dir, "sayi.xlsx", wb =>
        {
            var ws = wb.AddWorksheet("Veri");
            ws.Cell(1, 1).Value = "Barkod";
            ws.Cell(2, 1).Value = 8691234567890d;                // Genel biçim (Excel'de 8,69123E+12 görünür)
            ws.Cell(3, 1).Value = 8691234567890d;
            ws.Cell(3, 1).Style.NumberFormat.Format = "#,##0"; // binlik ayraçlı biçim
            ws.Cell(4, 1).Value = 123456789012345d;              // 15 hane: sınırın hemen altı
        });

        var table = DataImporter.Import(path);

        Assert.Equal("8691234567890", table.Rows[0].GetValue(0));
        Assert.Equal("8691234567890", table.Rows[1].GetValue(0));
        Assert.Equal("123456789012345", table.Rows[2].GetValue(0));
        Assert.DoesNotContain(table.Rows, r => r.GetValue(0).Contains('E') || r.GetValue(0).Contains('.') || r.GetValue(0).Contains(','));
        Assert.Equal(0, table.ErrorCount);
    }

    [Fact]
    public void TextCell_PreservesLeadingZeros_AndZeroPaddedNumberFormatIsApplied()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteWorkbook(dir, "sifir.xlsx", wb =>
        {
            var ws = wb.AddWorksheet("Veri");
            ws.Cell(1, 1).Value = "Barkod";
            ws.Cell(2, 1).Style.NumberFormat.Format = "@";
            ws.Cell(2, 1).Value = "0012345678905";
            ws.Cell(3, 1).Value = 12345678905d;
            ws.Cell(3, 1).Style.NumberFormat.Format = "0000000000000";
            ws.Cell(4, 1).Value = "0012345678905";
        });

        var table = DataImporter.Import(path);

        Assert.Equal(["0012345678905", "0012345678905", "0012345678905"], table.Rows.Select(r => r.GetValue(0)));
        Assert.DoesNotContain(table.Issues, i => i.Severity != ImportIssueSeverity.Info);
    }

    [Fact]
    public void ScientificNotationText_ProducesWarningWithRowNumber_AndRowIsKept()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteWorkbook(dir, "bilimsel.xlsx", wb =>
        {
            var ws = wb.AddWorksheet("Veri");
            ws.Cell(1, 1).Value = "UrunAdi";
            ws.Cell(1, 2).Value = "Barkod";
            for (int r = 2; r <= 6; r++)
            {
                ws.Cell(r, 1).Value = $"Ürün {r}";
                ws.Cell(r, 2).Value = Fixtures.Ean13($"869123400{r:000}");
            }
            ws.Cell(5, 2).Value = "8,69123E+12";
        });

        var table = DataImporter.Import(path);

        Assert.Equal(5, table.Rows.Count);
        var issue = Assert.Single(table.Issues, i => i.Severity == ImportIssueSeverity.Warning);
        Assert.Equal(5, issue.RowNumber);
        Assert.Equal("Barkod", issue.Column);
        Assert.Contains("bilimsel gösterim", issue.Message);
        Assert.Contains("Metin", issue.Message);
        Assert.Equal("8,69123E+12", table.Rows[3].GetValue("Barkod"));
        Assert.StartsWith("Satır 5, \"Barkod\":", issue.FullText);
    }

    [Fact]
    public void NumberOver15Digits_ProducesError_WithRowNumber()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteWorkbook(dir, "uzun.xlsx", wb =>
        {
            var ws = wb.AddWorksheet("Veri");
            ws.Cell(1, 1).Value = "SSCC";
            ws.Cell(2, 1).Value = 12345678901234567890d;
            ws.Cell(3, 1).Value = 1234567d;
        });

        var table = DataImporter.Import(path);

        var error = Assert.Single(table.Issues, i => i.Severity == ImportIssueSeverity.Error);
        Assert.Equal(2, error.RowNumber);
        Assert.Equal("SSCC", error.Column);
        Assert.Contains("15", error.Message);
        string value = table.Rows[0].GetValue(0);
        Assert.Equal(20, value.Length);
        Assert.StartsWith("123456789012346", value);
        Assert.All(value, c => Assert.True(char.IsAsciiDigit(c)));
        Assert.Equal("1234567", table.Rows[1].GetValue(0));
    }

    [Fact]
    public void DecimalBooleanDateTimeAndErrorCells_AreConvertedToTurkishText()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteWorkbook(dir, "turler.xlsx", wb =>
        {
            var ws = wb.AddWorksheet("Veri");
            ws.Cell(1, 1).Value = "Deger";
            ws.Cell(2, 1).Value = 1234.5;
            ws.Cell(2, 1).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(3, 1).Value = 0.1;
            ws.Cell(4, 1).Value = true;
            ws.Cell(5, 1).Value = new DateTime(2026, 10, 13);
            ws.Cell(6, 1).Value = new DateTime(2026, 10, 13, 9, 30, 0);
            ws.Cell(7, 1).Value = XLError.NoValueAvailable;
            ws.Cell(7, 2).Value = "satır boş sayılmasın";
            ws.Cell(8, 1).Value = 0.25;
            ws.Cell(8, 1).Style.NumberFormat.NumberFormatId = 9; // 0%
        });

        var table = DataImporter.Import(path);
        var values = table.Rows.Select(r => r.GetValue(0)).ToList();

        Assert.Equal("1.234,50", values[0]);
        Assert.Equal("0,1", values[1]);
        Assert.Equal("DOĞRU", values[2]);
        Assert.Equal("13.10.2026", values[3]);
        Assert.Equal("13.10.2026 09:30", values[4]);
        Assert.Equal("", values[5]);
        Assert.Contains("25", values[6]);
        Assert.Contains("%", values[6]);
        var warning = Assert.Single(table.Issues, i => i.Severity == ImportIssueSeverity.Warning);
        Assert.Equal(7, warning.RowNumber);
        Assert.Contains("#N/A", warning.Message);
    }

    [Fact]
    public void FormulaCells_UseComputedValue()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteWorkbook(dir, "formul.xlsx", wb =>
        {
            var ws = wb.AddWorksheet("Veri");
            ws.Cell(1, 1).Value = "Adet";
            ws.Cell(1, 2).Value = "Toplam";
            ws.Cell(1, 3).Value = "Kod";
            ws.Cell(2, 1).Value = 21;
            ws.Cell(2, 2).FormulaA1 = "A2*2";
            ws.Cell(2, 3).FormulaA1 = "\"TST\"&A2";
        });

        var table = DataImporter.Import(path);

        Assert.Equal("42", table.Rows[0].GetValue("Toplam"));
        Assert.Equal("TST21", table.Rows[0].GetValue("Kod"));
    }

    [Fact]
    public void Headers_EmptyDuplicateSanitized_AndDataBeyondHeaderGetsSutunN()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteWorkbook(dir, "baslik.xlsx", wb =>
        {
            var ws = wb.AddWorksheet("Veri");
            ws.Cell(1, 1).Value = "Barkod";
            // B1 boş
            ws.Cell(1, 3).Value = "barkod";
            ws.Cell(1, 4).Value = "Fiyat:TL {KDV}\nDahil";
            ws.Cell(2, 1).Value = "1";
            ws.Cell(2, 2).Value = "2";
            ws.Cell(2, 3).Value = "3";
            ws.Cell(2, 4).Value = "4";
            ws.Cell(2, 5).Value = "5"; // başlığın ötesinde veri
        });

        var table = DataImporter.Import(path);

        Assert.Equal(["Barkod", "Sütun 2", "barkod (2)", "Fiyat TL (KDV) Dahil", "Sütun 5"], table.Columns);
        Assert.Equal(["1", "2", "3", "4", "5"], table.Rows[0].Values);
        Assert.All(table.Issues, i => Assert.Equal(ImportIssueSeverity.Info, i.Severity));
        Assert.Equal("4", FieldTemplate.Resolve("{Fiyat TL (KDV) Dahil}", table.Rows[0].ToFieldSource()).Text);
    }

    [Fact]
    public void EmptyRowsAreSkipped_ButRowNumbersStayReal()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteWorkbook(dir, "bosluk.xlsx", wb =>
        {
            var ws = wb.AddWorksheet("Veri");
            ws.Cell(3, 2).Value = "Kod"; // başlık 3. satır, B sütunu
            ws.Cell(3, 3).Value = "Ad";
            ws.Cell(4, 2).Value = "A1";
            ws.Cell(4, 3).Value = "Tepsi";
            ws.Cell(6, 2).Value = "A2";   // 5. satır boş
            ws.Cell(6, 3).Value = "Kase";
            ws.Cell(7, 3).Value = "   ";  // yalnızca boşluk → boş
            ws.Cell(9, 2).Value = "A3";
        });

        var table = DataImporter.Import(path);

        Assert.Equal(3, table.HeaderRowNumber);
        Assert.Equal(["Kod", "Ad"], table.Columns);
        Assert.Equal([4, 6, 9], table.Rows.Select(r => r.RowNumber));
        Assert.Equal(3, table.SkippedEmptyRows); // 5, 7 (yalnızca boşluk) ve 8. satırlar
        Assert.Equal("", table.Rows[2].GetValue("Ad"));
    }

    [Fact]
    public void SheetSelection_FirstNonEmptySheet_NamedSheet_AndMissingSheet()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteWorkbook(dir, "sayfalar.xlsx", wb =>
        {
            wb.AddWorksheet("Boş");
            var data = wb.AddWorksheet("Veri");
            data.Cell(1, 1).Value = "Ad";
            data.Cell(2, 1).Value = "Birinci";
            var other = wb.AddWorksheet("Diğer Sayfa");
            other.Cell(1, 1).Value = "Ad";
            other.Cell(2, 1).Value = "İkinci";
        });

        Assert.Equal(["Boş", "Veri", "Diğer Sayfa"], DataImporter.GetSheetNames(path));
        Assert.Equal("Veri", DataImporter.Import(path).SheetName);
        var other = DataImporter.Import(path, new ImportOptions { SheetName = "diğer sayfa" });
        Assert.Equal("Diğer Sayfa", other.SheetName);
        Assert.Equal("İkinci", other.Rows[0].GetValue(0));

        var ex = Assert.Throws<DataImportException>(() => DataImporter.Import(path, new ImportOptions { SheetName = "Yok" }));
        Assert.Contains("bulunamadı", ex.Message);
        Assert.Contains("Diğer Sayfa", ex.Message);
    }

    [Fact]
    public void HeaderRowOption_SkipsTitleRows()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteWorkbook(dir, "baslikli.xlsx", wb =>
        {
            var ws = wb.AddWorksheet("Veri");
            ws.Cell(1, 1).Value = "Örnek Firma Ürün Listesi";
            ws.Cell(3, 1).Value = "Kod";
            ws.Cell(3, 2).Value = "Ad";
            ws.Cell(4, 1).Value = "A1";
            ws.Cell(4, 2).Value = "Tepsi";
        });

        var auto = DataImporter.Import(path);
        Assert.Equal(1, auto.HeaderRowNumber);

        var table = DataImporter.Import(path, new ImportOptions { HeaderRow = 3 });
        Assert.Equal(["Kod", "Ad"], table.Columns);
        Assert.Equal(4, Assert.Single(table.Rows).RowNumber);

        Assert.Throws<DataImportException>(() => DataImporter.Import(path, new ImportOptions { HeaderRow = 50 }));
    }

    [Fact]
    public void MaxRows_TruncatesWithWarning()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteWorkbook(dir, "cok.xlsx", wb =>
        {
            var ws = wb.AddWorksheet("Veri");
            ws.Cell(1, 1).Value = "No";
            for (int i = 1; i <= 30; i++) ws.Cell(i + 1, 1).Value = i;
        });

        var table = DataImporter.Import(path, new ImportOptions { MaxRows = 10 });

        Assert.Equal(10, table.Rows.Count);
        Assert.True(table.WasTruncated);
        Assert.Contains(table.Issues, i => i.RowNumber == 0 && i.Severity == ImportIssueSeverity.Warning && i.Message.Contains("10"));
    }

    [Fact]
    public void FileOpenForWritingByAnotherProgram_CanStillBeRead()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteWorkbook(dir, "acik.xlsx", wb =>
        {
            var ws = wb.AddWorksheet("Veri");
            ws.Cell(1, 1).Value = "Ad";
            ws.Cell(2, 1).Value = "Tepsi";
        });

        // Excel'in yaptığı gibi: yazma erişimiyle açık, okumaya izin veriyor.
        using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        var table = DataImporter.Import(path);
        Assert.Equal("Tepsi", table.Rows[0].GetValue(0));
    }

    [Fact]
    public void ExclusivelyLockedFile_GivesTurkishLockMessage()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteWorkbook(dir, "kilitli.xlsx", wb => wb.AddWorksheet("Veri").Cell(1, 1).Value = "Ad");

        using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var ex = Assert.Throws<DataImportException>(() => DataImporter.Import(path));
        Assert.Contains("kilitlenmiş", ex.Message);
        Assert.Equal(path, ex.FilePath);
    }

    [Fact]
    public void CorruptOldXls_ThrowsTurkishMessageWithSaveAsGuidance()
    {
        // .xls artık okunur (XlsImportTests); bozuk dosya açıklayıcı Türkçe hata verir.
        using var dir = new TempDirectory();
        string path = dir.File("eski.xls");
        File.WriteAllBytes(path, [0xD0, 0xCF, 0x11, 0xE0]);
        var ex = Assert.Throws<DataImportException>(() => DataImporter.Import(path));
        Assert.Contains(".xlsx", ex.Message);
        Assert.Contains("açılamadı", ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void MissingFile_Throws()
    {
        var ex = Assert.Throws<DataImportException>(() => DataImporter.Import(@"C:\yok\olmayan-dosya.xlsx"));
        Assert.Contains("bulunamadı", ex.Message);
    }

    [Fact]
    public void CorruptXlsx_ThrowsTurkishMessage()
    {
        using var dir = new TempDirectory();
        string path = dir.File("bozuk.xlsx");
        File.WriteAllText(path, "bu bir excel dosyası değil");
        var ex = Assert.Throws<DataImportException>(() => DataImporter.Import(path));
        Assert.Contains("Excel dosyası açılamadı", ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void UnsupportedExtension_Throws()
    {
        using var dir = new TempDirectory();
        string path = dir.File("veri.json");
        File.WriteAllText(path, "{}");
        Assert.Throws<DataImportException>(() => DataImporter.Import(path));
        Assert.False(DataImporter.IsSupportedFile(path));
        Assert.True(DataImporter.IsSupportedFile("a.XLSX"));
        Assert.True(DataImporter.IsSupportedFile("a.csv"));
    }

    [Fact]
    public async Task ImportAsync_ReturnsSameResult()
    {
        using var dir = new TempDirectory();
        string path = Fixtures.WriteWorkbook(dir, "async.xlsx", wb =>
        {
            var ws = wb.AddWorksheet("Veri");
            ws.Cell(1, 1).Value = "Ad";
            ws.Cell(2, 1).Value = "Kase";
        });
        var table = await DataImporter.ImportAsync(path);
        Assert.Equal("Kase", table.Rows[0].GetValue(0));
    }
}
