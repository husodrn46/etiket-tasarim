using System.Globalization;
using AkelEtiket.Core.Model;

namespace AkelEtiket.Editor.Templates;

/// <summary>
/// Yazıcı test etiketi: kenar çerçevesi (ölçü/kayma kontrolü), 10 mm cetvel çentikleri, merkez artısı,
/// "ÜST" yön oku, Türkçe karakterler ve örnek barkodlar. Etiket ölçüsüne göre ölçeklenir.
/// </summary>
public static class TestLabelFactory
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static LabelDocument Create(LabelSetup setup, string printerName, string methodText, double dpi, DateTime now)
    {
        var doc = new LabelDocument { Name = "Test Baskısı" };
        doc.Label.WidthMm = setup.WidthMm;
        doc.Label.HeightMm = setup.HeightMm;
        doc.Label.GapMm = setup.GapMm;
        doc.Label.GapOffsetMm = setup.GapOffsetMm;
        doc.Label.Media = setup.Media;

        double w = setup.WidthMm, h = setup.HeightMm;
        double s = Math.Clamp(Math.Min(w, h) / 100.0, 0.3, 2.0);
        var e = doc.Elements;

        // Kenarlardan 1 mm içeride 0,25 mm çerçeve: dört kenar da görünmeli (kayma/kırpma kontrolü).
        e.Add(new ShapeElement { Name = "Çerçeve 1 mm", X = 1, Y = 1, Width = w - 2, Height = h - 2, StrokeWidth = 0.25 });

        // 10 mm cetvel çentikleri (üst ve sol kenar).
        for (double x = 10; x < w - 1; x += 10)
            e.Add(new ShapeElement { Kind = ShapeKind.Line, LineOrientation = LineOrientation.Vertical, X = x - 0.25, Y = 1, Width = 0.5, Height = x % 50 == 0 ? 4 : 2.5, StrokeWidth = 0.25 });
        for (double y = 10; y < h - 1; y += 10)
            e.Add(new ShapeElement { Kind = ShapeKind.Line, LineOrientation = LineOrientation.Horizontal, X = 1, Y = y - 0.25, Width = y % 50 == 0 ? 4 : 2.5, Height = 0.5, StrokeWidth = 0.25 });

        // Merkez artısı + daire.
        double cx = w / 2, cy = h / 2, arm = 6 * s;
        e.Add(new ShapeElement { Kind = ShapeKind.Line, LineOrientation = LineOrientation.Horizontal, X = cx - arm, Y = cy - 0.25, Width = 2 * arm, Height = 0.5, StrokeWidth = 0.25 });
        e.Add(new ShapeElement { Kind = ShapeKind.Line, LineOrientation = LineOrientation.Vertical, X = cx - 0.25, Y = cy - arm, Width = 0.5, Height = 2 * arm, StrokeWidth = 0.25 });
        e.Add(new ShapeElement { Kind = ShapeKind.Ellipse, X = cx - arm / 2, Y = cy - arm / 2, Width = arm, Height = arm, StrokeWidth = 0.25 });

        // Yön göstergesi.
        e.Add(new TextElement
        {
            Name = "Üst", Text = "▲ ÜST", X = cx - 12 * s, Y = 3, Width = 24 * s, Height = 6 * s,
            FontSize = Math.Max(6, 12 * s), Bold = true, HorizontalAlignment = TextHorizontalAlignment.Center,
        });

        e.Add(new TextElement
        {
            Name = "Başlık", Text = "LabelHarbor • Test Baskısı", X = 5, Y = 3 + 7 * s, Width = w - 10, Height = 8 * s,
            FontSize = Math.Max(6, 15 * s), Bold = true, HorizontalAlignment = TextHorizontalAlignment.Center, AutoShrink = true, WordWrap = false,
        });
        string info = string.Create(Turkish,
            $"{w:0.#} × {h:0.#} mm • {dpi:0.#} DPI • {methodText}\nYazıcı: {printerName}\n{now:dd.MM.yyyy HH:mm}\nTürkçe: ĞÜŞİÖÇ ğüşıöç");
        e.Add(new TextElement
        {
            Name = "Bilgi", Text = info, X = 5, Y = 3 + 16 * s, Width = w - 10, Height = 22 * s,
            FontSize = Math.Max(5, 9 * s), HorizontalAlignment = TextHorizontalAlignment.Center, AutoShrink = true,
        });

        if (w >= 60 && h >= 60)
        {
            e.Add(new BarcodeElement { Name = "EAN-13", Symbology = BarcodeType.Ean13, Content = "8691234567890", X = 5, Y = h * 0.68, Width = w * 0.5, Height = h * 0.28 });
            e.Add(new BarcodeElement { Name = "QR", Symbology = BarcodeType.QrCode, Content = "Etiket test baskısı – ölçü ve okunabilirlik", ShowText = false, X = w * 0.64, Y = h * 0.68, Width = Math.Min(w * 0.28, h * 0.28), Height = Math.Min(w * 0.28, h * 0.28) });
            e.Add(new BarcodeElement { Name = "Code 128", Symbology = BarcodeType.Code128, Content = "DEMO-TEST-123", X = w * 0.12, Y = cy + arm + 1.5, Width = w * 0.76, Height = Math.Max(8, h * 0.085) });
        }
        else
        {
            e.Add(new BarcodeElement { Name = "Code 128", Symbology = BarcodeType.Code128, Content = "TEST123", X = 3, Y = h * 0.62, Width = w - 6, Height = h * 0.33 });
        }
        return doc;
    }
}
