using System.Globalization;
using AkelEtiket.Core.Geometry;

namespace AkelEtiket.BarcodeEngine;

/// <summary>Barkod yerleşim ayarları.</summary>
public sealed record BarcodeLayoutOptions
{
    /// <summary>Okunabilir metni göster.</summary>
    public bool ShowText { get; init; } = true;

    /// <summary>Sessiz bölgeleri kutuya dahil et (EAN/UPC'de her zaman dahildir).</summary>
    public bool IncludeQuietZone { get; init; } = true;

    /// <summary>Metin satırı yüksekliği (nokta); null = modüle göre otomatik.</summary>
    public int? TextHeightDots { get; init; }

    /// <summary>Uyarı mesajlarındaki mm hesapları için yazıcı çözünürlüğü.</summary>
    public double DotsPerMm { get; init; } = 8.0;
}

/// <summary>Okunabilir metin kutusu (öğe kutusuna göre, nokta).</summary>
public sealed record BarcodeTextPlacement(string Text, DotRect Box, bool Small);

/// <summary>Kutuya yerleştirilmiş barkod: siyah dikdörtgenler + metin kutuları (hepsi tam sayı nokta).</summary>
public sealed class BarcodeLayout
{
    public bool Fits { get; init; }

    /// <summary>Sığmama/yükseklik sorunu açıklaması (Türkçe).</summary>
    public string? Problem { get; init; }

    /// <summary>Dar modül (X boyutu) genişliği, nokta.</summary>
    public int ModuleDots { get; init; }

    /// <summary>Geniş eleman genişliği (Code 39/ITF), nokta.</summary>
    public int WideDots { get; init; }

    public IReadOnlyList<DotRect> Bars { get; init; } = [];
    public IReadOnlyList<BarcodeTextPlacement> Texts { get; init; } = [];
    public int TextHeightDots { get; init; }

    /// <summary>Barkodun (sessiz bölgeler dahil) kapladığı alan.</summary>
    public DotRect SymbolArea { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>1 noktalık modülle gereken asgari kutu genişliği.</summary>
    public int MinimumWidthDots { get; init; }
}

/// <summary>
/// Barkodu yazıcı noktası ızgarasına hizalar: modül genişliği her zaman TAM SAYI noktadır,
/// böylece baskıda çubuklar keskin olur ve dithering/ölçekleme uygulanmaz.
/// Kutuya sığan en büyük modül genişliği seçilir.
/// </summary>
public static class BarcodeLayoutEngine
{
    private const double EanNominalModuleMm = 0.33;

    public static BarcodeLayout Layout(BarcodeSymbol symbol, int boxWidthDots, int boxHeightDots, BarcodeLayoutOptions options) =>
        symbol switch
        {
            LinearSymbol linear => LayoutLinear(linear, boxWidthDots, boxHeightDots, options),
            MatrixSymbol matrix => LayoutMatrix(matrix, boxWidthDots, boxHeightDots, options),
            _ => throw new NotSupportedException(),
        };

    /// <summary>Belirli bir modül genişliği için gereken kutu genişliği (nokta).</summary>
    public static int GetRequiredWidthDots(BarcodeSymbol symbol, int moduleDots, BarcodeLayoutOptions options)
    {
        moduleDots = Math.Max(1, moduleDots);
        return symbol switch
        {
            LinearSymbol l => l.GetSymbolWidthDots(moduleDots) + 2 * QuietZone(l, options) * moduleDots,
            MatrixSymbol m => (m.Width + 2 * (options.IncludeQuietZone ? m.QuietZoneModules : 0)) * moduleDots,
            _ => throw new NotSupportedException(),
        };
    }

    /// <summary>2B barkod için belirli modülle gereken kutu yüksekliği (metin dahil).</summary>
    public static int GetRequiredHeightDots(MatrixSymbol symbol, int moduleDots, BarcodeLayoutOptions options)
    {
        int qz = options.IncludeQuietZone ? symbol.QuietZoneModules : 0;
        int symbolHeight = (symbol.Height + 2 * qz) * Math.Max(1, moduleDots);
        if (!options.ShowText) return symbolHeight;
        int textHeight = options.TextHeightDots ?? Math.Clamp((int)Math.Round(symbolHeight * 0.14), 12, 60);
        return symbolHeight + textHeight + 4;
    }

    private static int QuietZone(LinearSymbol symbol, BarcodeLayoutOptions options) =>
        options.IncludeQuietZone || symbol.IsUpcEanFamily ? symbol.QuietZoneModules : 0;

    private static BarcodeLayout LayoutLinear(LinearSymbol symbol, int boxW, int boxH, BarcodeLayoutOptions options)
    {
        int qz = QuietZone(symbol, options);
        int Total(int n) => symbol.GetSymbolWidthDots(n) + 2 * qz * n;
        int minWidth = Total(1);
        var warnings = new List<string>();

        if (minWidth > boxW)
        {
            return new BarcodeLayout
            {
                Fits = false,
                MinimumWidthDots = minWidth,
                Problem = $"Barkod bu genişliğe sığmıyor: en az {Mm(minWidth, options)} mm gerekli " +
                          $"(okunabilirlik için {Mm(Total(2), options)} mm önerilir).",
            };
        }

        int n = Math.Max(1, boxW / Math.Max(1, minWidth));
        while (n > 1 && Total(n) > boxW) n--;
        while (Total(n + 1) <= boxW) n++;

        bool showText = options.ShowText && symbol.HumanReadableText.Length > 0;
        int textH = 0, gap = 0;
        if (showText)
        {
            int auto = symbol.IsUpcEanFamily ? (int)Math.Round(n * 8.5) : (int)Math.Round(n * 7.0 + 4);
            textH = Math.Clamp(options.TextHeightDots ?? auto, 8, Math.Max(8, boxH / 2));
            gap = symbol.IsUpcEanFamily ? Math.Max(1, n) : Math.Max(2, n);
        }

        int barBottom = boxH - textH - gap;
        int minBarHeight = Math.Max(8, n * 5);
        if (barBottom < minBarHeight)
        {
            return new BarcodeLayout
            {
                Fits = false,
                ModuleDots = n,
                MinimumWidthDots = minWidth,
                Problem = "Barkod yüksekliği yetersiz; kutuyu yükseltin veya okunabilir metni kapatın.",
            };
        }

        int bearer = symbol.HasBearerBars ? Math.Max(2 * n, 3) : 0;
        int barsTop = bearer;
        int barsBottom = barBottom - bearer;
        int extendedBottom = showText && symbol.IsUpcEanFamily ? boxH - textH / 2 : barsBottom;
        if (barsBottom - barsTop < minBarHeight)
        {
            return new BarcodeLayout
            {
                Fits = false,
                ModuleDots = n,
                MinimumWidthDots = minWidth,
                Problem = "Barkod yüksekliği yetersiz; kutuyu yükseltin.",
            };
        }

        int total = Total(n);
        int areaX = (boxW - total) / 2;
        int x0 = areaX + qz * n;
        int wide = symbol.GetWideDots(n);
        var bars = new List<DotRect>();
        int x = x0;
        foreach (var run in symbol.Runs)
        {
            int w = run.IsWide ? wide : run.Modules * n;
            if (run.IsBar)
            {
                int bottom = symbol.IsExtended(run.StartModule) ? extendedBottom : barsBottom;
                bars.Add(new DotRect(x, barsTop, w, bottom - barsTop));
            }
            x += w;
        }
        if (bearer > 0)
        {
            bars.Add(new DotRect(areaX, 0, total, bearer));
            bars.Add(new DotRect(areaX, barsBottom, total, bearer));
        }

        var texts = new List<BarcodeTextPlacement>();
        if (showText)
        {
            if (symbol.HriSegments.Count > 0)
            {
                int smallH = Math.Max(6, (int)Math.Round(textH * 0.75));
                foreach (var seg in symbol.HriSegments)
                {
                    int sx = x0 + (int)Math.Round(seg.StartModule * n);
                    int sw = Math.Max(1, (int)Math.Round((seg.EndModule - seg.StartModule) * n));
                    int h = seg.Small ? smallH : textH;
                    texts.Add(new BarcodeTextPlacement(seg.Text, new DotRect(sx, boxH - h, sw, h), seg.Small));
                }
            }
            else
            {
                texts.Add(new BarcodeTextPlacement(symbol.HumanReadableText, new DotRect(0, boxH - textH, boxW, textH), false));
            }
        }

        AddLinearWarnings(symbol, n, barsBottom - barsTop, options, warnings);

        return new BarcodeLayout
        {
            Fits = true,
            ModuleDots = n,
            WideDots = symbol.UsesWideNarrow ? wide : n,
            Bars = bars,
            Texts = texts,
            TextHeightDots = textH,
            SymbolArea = new DotRect(areaX, 0, total, boxH),
            Warnings = warnings,
            MinimumWidthDots = minWidth,
        };
    }

    private static void AddLinearWarnings(LinearSymbol symbol, int n, int barHeight, BarcodeLayoutOptions options, List<string> warnings)
    {
        double moduleMm = n / options.DotsPerMm;
        if (n == 1)
            warnings.Add($"Modül genişliği 1 nokta ({Mm(1, options)} mm) çok ince; okuma sorunu olabilir. Barkodu genişletin.");
        if (symbol.IsUpcEanFamily)
        {
            double percent = moduleMm / EanNominalModuleMm * 100;
            if (percent < 80)
                warnings.Add($"Büyütme %{percent:0} (X = {moduleMm.ToString("0.###", CultureInfo.GetCultureInfo("tr-TR"))} mm). " +
                             "GS1 en az %80 (0,264 mm) önerir; barkodu genişletin.");
            else if (percent > 200)
                warnings.Add($"Büyütme %{percent:0}; GS1 üst sınırı %200'dür.");
        }
        double barHeightMm = barHeight / options.DotsPerMm;
        if (barHeightMm < 5)
            warnings.Add($"Çubuk yüksekliği {barHeightMm.ToString("0.#", CultureInfo.GetCultureInfo("tr-TR"))} mm; en az 6 mm önerilir.");
    }

    private static BarcodeLayout LayoutMatrix(MatrixSymbol symbol, int boxW, int boxH, BarcodeLayoutOptions options)
    {
        int qz = options.IncludeQuietZone ? symbol.QuietZoneModules : 0;
        int unitsW = symbol.Width + 2 * qz;
        int unitsH = symbol.Height + 2 * qz;
        bool showText = options.ShowText && symbol.HumanReadableText.Length > 0;
        int textH = showText ? Math.Clamp(options.TextHeightDots ?? (int)Math.Round(boxH * 0.12), 8, Math.Max(8, boxH / 3)) : 0;
        int gap = showText ? 4 : 0;
        int availH = boxH - textH - gap;
        int n = Math.Min(boxW / unitsW, Math.Max(0, availH) / unitsH);
        int minWidth = unitsW;
        if (n < 1)
        {
            return new BarcodeLayout
            {
                Fits = false,
                MinimumWidthDots = minWidth,
                Problem = $"Barkod bu alana sığmıyor: en az {Mm(unitsW, options)} × {Mm(unitsH + textH + gap, options)} mm gerekli " +
                          $"(okunabilirlik için {Mm(unitsW * 3, options)} mm önerilir).",
            };
        }

        int symW = unitsW * n, symH = unitsH * n;
        int areaX = (boxW - symW) / 2;
        int areaY = Math.Max(0, (availH - symH) / 2);
        int x0 = areaX + qz * n, y0 = areaY + qz * n;
        var bars = new List<DotRect>();
        for (int y = 0; y < symbol.Height; y++)
        {
            int xStart = -1;
            for (int xm = 0; xm <= symbol.Width; xm++)
            {
                bool black = xm < symbol.Width && symbol[xm, y];
                if (black && xStart < 0) xStart = xm;
                else if (!black && xStart >= 0)
                {
                    bars.Add(new DotRect(x0 + xStart * n, y0 + y * n, (xm - xStart) * n, n));
                    xStart = -1;
                }
            }
        }

        var texts = new List<BarcodeTextPlacement>();
        if (showText)
            texts.Add(new BarcodeTextPlacement(symbol.HumanReadableText, new DotRect(0, boxH - textH, boxW, textH), false));

        var warnings = new List<string>();
        if (n == 1)
            warnings.Add($"Modül boyutu 1 nokta ({Mm(1, options)} mm) çok küçük; okuma sorunu olabilir. Barkodu büyütün.");
        else if (n == 2 && symbol.Type is Core.Model.BarcodeType.QrCode or Core.Model.BarcodeType.Gs1QrCode)
            warnings.Add($"QR modülü 2 nokta ({Mm(2, options)} mm); telefonla okuma için 3 nokta ve üzeri önerilir.");

        return new BarcodeLayout
        {
            Fits = true,
            ModuleDots = n,
            WideDots = n,
            Bars = bars,
            Texts = texts,
            TextHeightDots = textH,
            SymbolArea = new DotRect(areaX, areaY, symW, symH),
            Warnings = warnings,
            MinimumWidthDots = minWidth,
        };
    }

    private static string Mm(int dots, BarcodeLayoutOptions options) =>
        (dots / options.DotsPerMm).ToString("0.#", CultureInfo.GetCultureInfo("tr-TR"));
}
