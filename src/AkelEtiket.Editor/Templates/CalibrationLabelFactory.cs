using System.Globalization;
using AkelEtiket.Core.Model;

namespace AkelEtiket.Editor.Templates;

/// <summary>
/// Kalibrasyon (cetvel) etiketi: kenarlardan tam <see cref="FrameInsetMm"/> mm içeride çerçeve, dört kenarda milimetre
/// cetveli (1 mm kısa, 5 mm orta, 10 mm uzun çentik ve sayılar), merkez artısı, "▲ ÜST" işareti ve ölçü bilgisi.
/// Kullanıcı basılan etikette kağıt kenarından çerçevenin dış çizgisine olan uzaklığı ölçer; beklenen değerden farkı
/// ofset düzeltmesidir.
/// </summary>
public static class CalibrationLabelFactory
{
    /// <summary>Çerçevenin etiket kenarından beklenen uzaklığı (mm).</summary>
    public const double FrameInsetMm = 2.0;

    private const double Stroke = 0.25;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static LabelDocument Create(LabelSetup setup, string printerName, double offsetXMm, double offsetYMm, bool rotate180, DateTime now)
    {
        var doc = new LabelDocument { Name = "Kalibrasyon Etiketi" };
        doc.Label.WidthMm = setup.WidthMm;
        doc.Label.HeightMm = setup.HeightMm;
        doc.Label.GapMm = setup.GapMm;
        doc.Label.GapOffsetMm = setup.GapOffsetMm;
        doc.Label.Media = setup.Media;

        double w = setup.WidthMm, h = setup.HeightMm, inset = FrameInsetMm;
        var e = doc.Elements;

        // Çerçevenin DIŞ kenarı etiket kenarından tam 2 mm içeride (çizgi kutunun içine çizilir).
        e.Add(new ShapeElement { Name = "Çerçeve (dış kenar 2 mm)", X = inset, Y = inset, Width = w - 2 * inset, Height = h - 2 * inset, StrokeWidth = Stroke });

        // Cetveller: çerçevenin iç tarafında, etiket kenarından ölçülen milimetreler.
        double fontPt = Math.Clamp(Math.Min(w, h) / 22.0, 4.5, 7);
        for (int mm = (int)Math.Ceiling(inset + 1); mm < w - inset - 0.5; mm++)
        {
            double len = mm % 10 == 0 ? 4 : mm % 5 == 0 ? 2.5 : 1.2;
            e.Add(Tick(mm, inset + Stroke, len, vertical: true));
            e.Add(Tick(mm, h - inset - Stroke - len, len, vertical: true));
            if (mm % 10 == 0 && mm < w - inset - 4)
                e.Add(Number(mm, mm - 3, inset + Stroke + 4.2, fontPt));
        }
        for (int mm = (int)Math.Ceiling(inset + 1); mm < h - inset - 0.5; mm++)
        {
            double len = mm % 10 == 0 ? 4 : mm % 5 == 0 ? 2.5 : 1.2;
            e.Add(Tick(inset + Stroke, mm, len, vertical: false));
            e.Add(Tick(w - inset - Stroke - len, mm, len, vertical: false));
            // 10 mm sayısı üst cetvelin sayısıyla çakışmasın diye sol cetvelde 20'den başlanır.
            if (mm % 10 == 0 && mm >= 20 && mm < h - inset - 3)
                e.Add(Number(mm, inset + Stroke + 4.5, mm - 1.4, fontPt));
        }

        // Merkez artısı.
        double cx = w / 2, cy = h / 2, arm = Math.Clamp(Math.Min(w, h) / 12, 3, 8);
        e.Add(new ShapeElement { Kind = ShapeKind.Line, LineOrientation = LineOrientation.Horizontal, X = cx - arm, Y = cy - Stroke, Width = 2 * arm, Height = 2 * Stroke, StrokeWidth = Stroke, Name = "Merkez yatay" });
        e.Add(new ShapeElement { Kind = ShapeKind.Line, LineOrientation = LineOrientation.Vertical, X = cx - Stroke, Y = cy - arm, Width = 2 * Stroke, Height = 2 * arm, StrokeWidth = Stroke, Name = "Merkez dikey" });

        // Üst işareti ve bilgiler (büyük etiketlerde daha fazla bilgi).
        double textWidth = Math.Max(10, w - 2 * inset - 14);
        double big = Math.Clamp(Math.Min(w, h) / 9.0, 6, 14);
        e.Add(new TextElement
        {
            Name = "ÜST işareti", Text = "▲ ÜST", FontSize = big, Bold = true, HorizontalAlignment = TextHorizontalAlignment.Center,
            X = (w - textWidth) / 2, Y = inset + 6, Width = textWidth, Height = big * 0.3528 * 1.4,
        });
        string info = string.Create(Turkish,
            $"{w:0.##} × {h:0.##} mm · çerçeve dış kenarı her yandan {inset:0.#} mm olmalı\n" +
            $"Ofset sağ/aşağı: {offsetXMm:0.###} / {offsetYMm:0.###} mm · {(rotate180 ? "180° (DIRECTION 0)" : "normal (DIRECTION 1)")}\n" +
            $"{printerName} · {now:dd.MM.yyyy HH:mm}");
        double small = Math.Clamp(Math.Min(w, h) / 18.0, 4.5, 8);
        double infoHeight = small * 0.3528 * 1.25 * 3 + 1;
        e.Add(new TextElement
        {
            Name = "Bilgi", Text = info, FontSize = small, HorizontalAlignment = TextHorizontalAlignment.Center, AutoShrink = true,
            X = (w - textWidth) / 2, Y = Math.Max(cy + arm + 2, h - inset - 6 - infoHeight), Width = textWidth, Height = infoHeight,
        });
        return doc;
    }

    private static ShapeElement Tick(double x, double y, double length, bool vertical) => vertical
        ? new ShapeElement { Kind = ShapeKind.Line, LineOrientation = LineOrientation.Vertical, X = x - Stroke / 2, Y = y, Width = Stroke, Height = length, StrokeWidth = Stroke, Name = "Çentik" }
        : new ShapeElement { Kind = ShapeKind.Line, LineOrientation = LineOrientation.Horizontal, X = x, Y = y - Stroke / 2, Width = length, Height = Stroke, StrokeWidth = Stroke, Name = "Çentik" };

    private static TextElement Number(int mm, double x, double y, double fontPt) => new()
    {
        Name = "Cetvel " + mm,
        Text = mm.ToString(CultureInfo.InvariantCulture),
        FontSize = fontPt,
        HorizontalAlignment = TextHorizontalAlignment.Center,
        X = x,
        Y = y,
        Width = 6,
        Height = fontPt * 0.3528 * 1.3,
        WordWrap = false,
    };

    /// <summary>
    /// Ölçümden yeni ofseti hesaplar. Ölçülen uzaklık (kağıt kenarından çerçevenin dış kenarına) beklenenden büyükse
    /// içerik o kadar sağa/aşağı kaymıştır; ofset aynı miktarda ters yöne düzeltilir.
    /// </summary>
    public static double CorrectedOffset(double currentOffsetMm, double measuredMm) =>
        Math.Round(currentOffsetMm + (FrameInsetMm - measuredMm), 3);
}
