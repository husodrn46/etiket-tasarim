using System.Globalization;
using System.Text;
using AkelEtiket.Core.Imaging;
using AkelEtiket.Core.Model;

namespace AkelEtiket.PrintEngine;

/// <summary>
/// TSPL/TSPL2 komut üretici (TSC TSPL/TSPL2 Programming Manual ve XP-470B kullanım kılavuzuna göre).
/// <list type="bullet">
/// <item>Her komut CR LF ile biter; sayılar her zaman nokta ondalık ayırıcıyla (InvariantCulture) yazılır.</item>
/// <item>BITMAP X,Y,genişlik(BAYT),yükseklik(nokta),0,veri — veri satır satır, MSB en soldaki noktadır.</item>
/// <item>TSPL kutupluluğu: bit 0 = SİYAH nokta, bit 1 = beyaz. <see cref="MonoBitmap"/> 1 = siyah tuttuğu için baytlar ters çevrilir;
/// satır sonu dolgu bitleri böylece 1 (beyaz) olur.</item>
/// <item>OFFSET komutu baskı kaydırması DEĞİLDİR (fazladan besleme); kaydırma BITMAP X,Y ile ve gerekirse kırparak yapılır.</item>
/// </list>
/// </summary>
public static class TsplCommandBuilder
{
    public const string LineEnding = "\r\n";

    /// <summary>PRINT komutundaki kopya sayısı üst sınırı.</summary>
    public const int MaxCopies = 999_999;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>İş başı ayarları: SIZE, GAP/BLINE, DIRECTION, REFERENCE, DENSITY, SPEED ve (seçiliyse) SET TEAR.</summary>
    public static byte[] BuildSetup(LabelMedia media, LabelPrinterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(settings);
        var sb = new StringBuilder(192);
        AppendSizeAndMedia(sb, media);
        Line(sb, $"DIRECTION {(settings.Rotate180 ? 0 : 1)},0");
        Line(sb, "REFERENCE 0,0");
        Line(sb, "DENSITY " + settings.EffectiveDensity.ToString(Inv));
        Line(sb, "SPEED " + settings.EffectiveSpeed.ToString(Inv));
        switch (settings.TearMode)
        {
            case TearMode.On:
                Line(sb, "SET TEAR ON");
                break;
            case TearMode.Off:
                Line(sb, "SET TEAR OFF");
                break;
        }
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>Tek etiket: CLS, BITMAP (ofset uygulanmış), PRINT 1,kopya.</summary>
    public static byte[] BuildLabel(MonoBitmap label, LabelPrinterSettings settings, int copies, ICollection<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(settings);
        using var output = new MemoryStream(label.Data.Length + 96);
        WriteLabel(output, label, settings, copies, warnings);
        return output.ToArray();
    }

    /// <summary>Kurulum başlığı + tüm etiketler tek veri akışında.</summary>
    public static byte[] BuildJob(LabelMedia media, LabelPrinterSettings settings, IEnumerable<LabelPage> pages, ICollection<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(pages);
        using var output = new MemoryStream();
        output.Write(BuildSetup(media, settings));
        foreach (var page in pages)
        {
            ArgumentNullException.ThrowIfNull(page);
            WriteLabel(output, page.Bitmap, settings, page.Copies, warnings);
        }
        return output.ToArray();
    }

    /// <summary>1-bit görüntüyü TSPL BITMAP verisine çevirir (satır başına ⌈genişlik/8⌉ bayt, 0 = siyah).</summary>
    public static byte[] PackBitmap(MonoBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var packed = new byte[bitmap.Data.Length];
        var source = bitmap.Data;
        for (int i = 0; i < source.Length; i++) packed[i] = (byte)~source[i];
        return packed;
    }

    /// <summary>
    /// Sensör kalibrasyonu: medya türünü bildirip otomatik algılama komutunu gönderir.
    /// Boşluklu etiket → GAPDETECT, siyah işaretli → BLINEDETECT (TSPL2, 2011'den beri kılavuzda).
    /// Parametresiz gönderilir: yazıcı etiket ve boşluk boyunu kendisi ölçer (birkaç etiket ilerletir).
    /// Sürekli rulo için kalibrasyon yoktur; yalnızca sürekli mod ayarlanır.
    /// </summary>
    public static byte[] BuildGapDetect(LabelMedia media)
    {
        ArgumentNullException.ThrowIfNull(media);
        var sb = new StringBuilder(96);
        AppendSizeAndMedia(sb, media);
        switch (media.Type)
        {
            case MediaType.Gap:
                Line(sb, "GAPDETECT");
                break;
            case MediaType.BlackMark:
                Line(sb, "BLINEDETECT");
                break;
        }
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>Bir sonraki etiketin başına ilerlet.</summary>
    public static byte[] BuildFormFeed() => Encoding.ASCII.GetBytes("FORMFEED" + LineEnding);

    /// <summary>Yazıcının kendi test/ayar sayfasını basar.</summary>
    public static byte[] BuildSelfTest() => Encoding.ASCII.GetBytes("SELFTEST" + LineEnding);

    /// <summary>Gönderim öncesi uyarılar (gönderimi engellemez).</summary>
    public static IReadOnlyList<string> CheckLabel(LabelMedia media, LabelPrinterSettings settings, int bitmapWidthDots, int bitmapHeightDots)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(settings);
        var warnings = new List<string>();
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        if (media.WidthMm > settings.MaxPrintWidthMm + 0.01)
            warnings.Add($"Etiket genişliği ({media.WidthMm.ToString("0.##", tr)} mm) yazıcının azami baskı genişliğini " +
                         $"({settings.MaxPrintWidthMm.ToString("0.##", tr)} mm) aşıyor; taşan kısım basılmaz.");
        int expectedW = media.WidthDots(settings.SafeDotsPerMm);
        int expectedH = media.HeightDots(settings.SafeDotsPerMm);
        if (Math.Abs(bitmapWidthDots - expectedW) > 2 || Math.Abs(bitmapHeightDots - expectedH) > 2)
            warnings.Add($"Etiket görüntüsü ({bitmapWidthDots}×{bitmapHeightDots} nokta) medya ölçüsüyle ({expectedW}×{expectedH} nokta) " +
                         "uyuşmuyor; yazıcı çözünürlüğü ayarını kontrol edin.");
        return warnings;
    }

    internal static void WriteLabel(Stream output, MonoBitmap label, LabelPrinterSettings settings, int copies, ICollection<string>? warnings)
    {
        ArgumentNullException.ThrowIfNull(label);
        WriteAscii(output, "CLS" + LineEnding);
        var (bitmap, x, y) = Place(label, settings, warnings);
        if (bitmap is not null)
        {
            WriteAscii(output, string.Create(Inv, $"BITMAP {x},{y},{bitmap.Stride},{bitmap.Height},0,"));
            output.Write(PackBitmap(bitmap));
            WriteAscii(output, LineEnding);
        }
        WriteAscii(output, string.Create(Inv, $"PRINT 1,{ClampCopies(copies)}") + LineEnding);
    }

    /// <summary>
    /// Ofseti uygular: pozitif ofset BITMAP koordinatı olur, negatif ofset görüntüyü soldan/üstten kırpar.
    /// Hiçbir zaman negatif koordinat gönderilmez ve etiket alanı ile azami baskı genişliği dışına taşılmaz.
    /// </summary>
    internal static (MonoBitmap? Bitmap, int X, int Y) Place(MonoBitmap label, LabelPrinterSettings settings, ICollection<string>? warnings)
    {
        int ox = settings.OffsetXDots, oy = settings.OffsetYDots;
        int x = Math.Max(0, ox), y = Math.Max(0, oy);
        int srcX = Math.Max(0, -ox), srcY = Math.Max(0, -oy);
        // Kaydırılan görüntü etiketin kendi alanında kalır (rulo dışına/merdaneye basılmaz).
        int width = label.Width - Math.Abs(ox);
        int height = label.Height - Math.Abs(oy);

        int maxWidth = settings.MaxPrintWidthDots;
        if (maxWidth > 0 && x + width > maxWidth)
        {
            int clipped = maxWidth - x;
            warnings?.Add($"Etiket görüntüsü yazıcının azami baskı genişliğini ({maxWidth} nokta) aşıyor; " +
                          $"sağdaki {width - Math.Max(0, clipped)} nokta basılmayacak.");
            width = clipped;
        }
        if (width <= 0 || height <= 0)
        {
            warnings?.Add("Ofset etiket ölçüsünden büyük; etiket boş basılacak.");
            return (null, 0, 0);
        }
        bool needsCrop = srcX != 0 || srcY != 0 || width != label.Width || height != label.Height;
        return (needsCrop ? label.Crop(srcX, srcY, width, height) : label, x, y);
    }

    internal static int ClampCopies(int copies) => Math.Clamp(copies, 1, MaxCopies);

    private static void AppendSizeAndMedia(StringBuilder sb, LabelMedia media)
    {
        Line(sb, $"SIZE {Mm(media.WidthMm)} mm,{Mm(media.HeightMm)} mm");
        switch (media.Type)
        {
            case MediaType.BlackMark:
                Line(sb, $"BLINE {Mm(media.GapMm)} mm,{Mm(media.GapOffsetMm)} mm");
                break;
            case MediaType.Continuous:
                Line(sb, "GAP 0,0");
                break;
            default:
                Line(sb, $"GAP {Mm(media.GapMm)} mm,{Mm(media.GapOffsetMm)} mm");
                break;
        }
    }

    /// <summary>mm değeri: en fazla 2 ondalık, nokta ayırıcı, gereksiz sıfırsız (101.6, 100, 2.54).</summary>
    internal static string Mm(double value)
    {
        if (!double.IsFinite(value) || value < 0) value = 0;
        return Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.##", Inv);
    }

    private static void Line(StringBuilder sb, string command) => sb.Append(command).Append(LineEnding);

    private static void WriteAscii(Stream output, string text) => output.Write(Encoding.ASCII.GetBytes(text));
}
