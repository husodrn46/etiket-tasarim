using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace AkelEtiket.SampleData;

/// <summary>Örnek Excel ve CSV dosyalarını yazar.</summary>
internal static class SampleWriters
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static readonly string[] Headers = ["UrunKodu", "UrunAdi", "Barkod", "Fiyat", "Adet"];

    /// <summary>"Ürünler" sayfalı .xlsx; Barkod sütunu METİN biçiminde (baştaki sıfırlar ve 13 hane korunur).</summary>
    public static void WriteExcel(string path, IReadOnlyList<SampleProduct> products)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Ürünler");

        for (int c = 0; c < Headers.Length; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            cell.Value = Headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8E8E8");
        }

        for (int i = 0; i < products.Count; i++)
        {
            var p = products[i];
            int row = i + 2;
            sheet.Cell(row, 1).Value = p.Code;
            sheet.Cell(row, 2).Value = p.Name;
            var barcode = sheet.Cell(row, 3);
            barcode.Style.NumberFormat.Format = "@";
            barcode.Value = p.Barcode;
            var price = sheet.Cell(row, 4);
            price.Value = (double)p.Price;
            price.Style.NumberFormat.Format = "#,##0.00";
            sheet.Cell(row, 5).Value = p.Quantity;
        }

        sheet.Column(1).Width = 12;
        sheet.Column(2).Width = 42;
        sheet.Column(3).Width = 17;
        sheet.Column(4).Width = 11;
        sheet.Column(5).Width = 7;
        sheet.SheetView.FreezeRows(1);
        sheet.Range(1, 1, products.Count + 1, Headers.Length).SetAutoFilter();
        workbook.SaveAs(path);
    }

    /// <summary>Virgül ayırıcılı, UTF-8 (BOM'lu), fiyatlar nokta ondalıklı.</summary>
    public static void WriteUtf8CommaCsv(string path, IReadOnlyList<SampleProduct> products) =>
        WriteCsv(path, products, ',', new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            p => p.Price.ToString("0.00", CultureInfo.InvariantCulture));

    /// <summary>Noktalı virgül ayırıcılı, Windows-1254 (Türkçe Excel'in klasik CSV çıktısı), fiyatlar virgül ondalıklı.</summary>
    public static void Write1254SemicolonCsv(string path, IReadOnlyList<SampleProduct> products) =>
        WriteCsv(path, products, ';', Encoding.GetEncoding(1254), p => p.Price.ToString("0.00", Turkish));

    /// <summary>
    /// Tipik veri bozulmalarını gösteren dosya (noktalı virgül, UTF-8 BOM): bilimsel gösterim, kaybolmuş baştaki sıfır,
    /// harf içeren barkod, eksik hane, boş barkod, hatalı kontrol basamağı.
    /// </summary>
    public static void WriteFaultyCsv(string path, IReadOnlyList<SampleProduct> products)
    {
        var rows = new List<string[]>();
        foreach (var p in products.Skip(2).Take(10))
            rows.Add([p.Code, p.Name, p.Barcode, p.Price.ToString("0.00", Turkish), p.Quantity.ToString(CultureInfo.InvariantCulture)]);
        rows.Add(["HTL01", "Bilimsel Gösterime Dönmüş Barkod", "8,69123E+12", "45,00", "1"]);
        rows.Add(["HTL02", "Baştaki Sıfırları Kaybolmuş Barkod", "12345678905", "52,50", "1"]); // doğrusu 0012345678905
        rows.Add(["HTL03", "Harf İçeren Barkod", "86912345ABC90", "38,90", "2"]);
        rows.Add(["HTL04", "Eksik Haneli Barkod", "869123456", "61,00", "1"]);
        rows.Add(["HTL05", "Boş Barkod", "", "27,50", "3"]);
        rows.Add(["HTL06", "Kontrol Basamağı Hatalı Barkod", "8691234567891", "160,00", "1"]);

        var sb = new StringBuilder();
        AppendLine(sb, Headers, ';');
        foreach (var row in rows) AppendLine(sb, row, ';');
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static void WriteCsv(string path, IReadOnlyList<SampleProduct> products, char delimiter, Encoding encoding,
        Func<SampleProduct, string> formatPrice)
    {
        var sb = new StringBuilder();
        AppendLine(sb, Headers, delimiter);
        foreach (var p in products)
            AppendLine(sb, [p.Code, p.Name, p.Barcode, formatPrice(p), p.Quantity.ToString(CultureInfo.InvariantCulture)], delimiter);
        File.WriteAllText(path, sb.ToString(), encoding);
    }

    private static void AppendLine(StringBuilder sb, IReadOnlyList<string> fields, char delimiter)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            if (i > 0) sb.Append(delimiter);
            sb.Append(Quote(fields[i], delimiter));
        }
        sb.Append("\r\n");
    }

    private static string Quote(string value, char delimiter)
    {
        bool needsQuotes = value.IndexOf(delimiter) >= 0 || value.IndexOfAny(['"', '\r', '\n']) >= 0;
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
