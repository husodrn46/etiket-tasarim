using System.Globalization;
using System.Text.Json.Serialization;
using AkelEtiket.Core;
using AkelEtiket.Core.Imaging;
using AkelEtiket.Core.Model;

namespace AkelEtiket.PrintEngine;

/// <summary>Baskı yöntemi. İki yol asla karıştırılmaz (çift ölçekleme olmaz).</summary>
public enum PrintMethod
{
    /// <summary>Etiket uygulama içinde 1-bit görüntüye çevrilir ve TSPL BITMAP komutuyla RAW gönderilir (önerilen).</summary>
    TsplRaw,

    /// <summary>Windows sürücüsü (GDI) üzerinden; görüntü sürücünün gerçek çözünürlüğünde üretilir.</summary>
    WindowsDriver,
}

/// <summary>
/// Yırtma (tear) modu. TSPL'de SET komutları yazıcı belleğinde kalıcıdır;
/// <see cref="Unchanged"/> seçiliyse hiçbir SET TEAR komutu gönderilmez.
/// </summary>
public enum TearMode
{
    Unchanged,
    On,
    Off,
}

/// <summary>
/// Yazıcıya özgü baskı ayarları (Xprinter XP-470B için güvenli varsayılanlar).
/// Etiket ölçüleri burada değil, belgenin <see cref="LabelSetup"/> bilgisinde (<see cref="LabelMedia"/>) bulunur.
/// </summary>
public sealed class LabelPrinterSettings
{
    public const int MinDensity = 0;
    public const int MaxDensity = 15;

    /// <summary>
    /// Desteklenen hız aralığı. Gerçek yazıcı ve medya üzerinde kalibrasyon gerekir.
    /// </summary>
    public const int MinSpeed = 2;
    public const int MaxSpeed = 6;

    /// <summary>Ofset sınırı (mm). Daha büyük kaydırma yanlış etiket ölçüsüne işaret eder.</summary>
    public const double MaxOffsetMm = 20;

    /// <summary>Windows'taki yazıcı (kuyruk) adı, ör. "barkod".</summary>
    public string PrinterName { get; set; } = "";

    public PrintMethod Method { get; set; } = PrintMethod.TsplRaw;

    /// <summary>TSPL yolunda yazıcı çözünürlüğü: 203 DPI = 8 nokta/mm, 300 DPI = 12 nokta/mm.</summary>
    public double DotsPerMm { get; set; } = 8.0;

    /// <summary>Yazıcının en geniş baskı alanı (XP-470B: 108 mm = 864 nokta).</summary>
    public double MaxPrintWidthMm { get; set; } = 108;

    /// <summary>Baskı yoğunluğu (koyuluk) 0–15. TSPL varsayılanı 8.</summary>
    public int Density { get; set; } = 8;

    /// <summary>Baskı hızı (inç/sn). XP-470B kılavuzu: 2–5 (sipariş sistemi 6 kullanıyor). Düşük hız daha keskin barkod verir.</summary>
    public int Speed { get; set; } = 4;

    /// <summary>Baskıyı 180° döndür (TSPL DIRECTION 0). Varsayılan DIRECTION 1: etiket çıkarken düz okunur.</summary>
    public bool Rotate180 { get; set; }

    /// <summary>Yatay kaydırma (mm). Pozitif = etiket üzerinde sağa.</summary>
    public double OffsetXMm { get; set; }

    /// <summary>Dikey kaydırma (mm). Pozitif = etiket üzerinde aşağı.</summary>
    public double OffsetYMm { get; set; }

    public TearMode TearMode { get; set; } = TearMode.Unchanged;

    /// <summary>
    /// Yazıcı çevrimdışı/hata görünse bile göndermeye izin ver. Yalnızca durum algılaması hatalıysa açın;
    /// açıkken iş kuyrukta bekleyebilir.
    /// </summary>
    public bool AllowPrintWhenOffline { get; set; }

    /// <summary>
    /// Toplu baskıyı tek Windows işi olarak gönder ("tek parça"): araya başka bir iş (ör. sipariş sisteminin etiketleri)
    /// giremez. Durdurma yalnızca iş kuyruğa yazılmayı bitirene kadar etkilidir.
    /// </summary>
    public bool SingleJob { get; set; }

    public LabelPrinterSettings Clone() => (LabelPrinterSettings)MemberwiseClone();

    /// <summary>Gönderilecek yoğunluk (0–15 aralığına sıkıştırılmış).</summary>
    [JsonIgnore]
    public int EffectiveDensity => Math.Clamp(Density, MinDensity, MaxDensity);

    /// <summary>Gönderilecek hız (2–6 aralığına sıkıştırılmış).</summary>
    [JsonIgnore]
    public int EffectiveSpeed => Math.Clamp(Speed, MinSpeed, MaxSpeed);

    [JsonIgnore]
    public int OffsetXDots => Units.MmToDotsRounded(OffsetXMm, SafeDotsPerMm);

    [JsonIgnore]
    public int OffsetYDots => Units.MmToDotsRounded(OffsetYMm, SafeDotsPerMm);

    [JsonIgnore]
    public int MaxPrintWidthDots => Units.MmToDotsRounded(MaxPrintWidthMm, SafeDotsPerMm);

    internal double SafeDotsPerMm => double.IsFinite(DotsPerMm) && DotsPerMm > 0 ? DotsPerMm : 8.0;

    /// <summary>Ayar sorunlarını Türkçe olarak listeler (boş liste = geçerli).</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(PrinterName))
            problems.Add("Yazıcı seçilmedi. Ayarlar'dan etiket yazıcısını seçin.");
        if (!double.IsFinite(DotsPerMm) || DotsPerMm < 4 || DotsPerMm > 24)
            problems.Add($"Yazıcı çözünürlüğü geçersiz ({Format(DotsPerMm)} nokta/mm). 203 DPI için 8, 300 DPI için 12 olmalı.");
        if (!double.IsFinite(MaxPrintWidthMm) || MaxPrintWidthMm < 10 || MaxPrintWidthMm > 300)
            problems.Add($"Azami baskı genişliği geçersiz ({Format(MaxPrintWidthMm)} mm).");
        if (Density is < MinDensity or > MaxDensity)
            problems.Add($"Baskı yoğunluğu {MinDensity}–{MaxDensity} arasında olmalı (girilen {Density}).");
        if (Speed is < MinSpeed or > MaxSpeed)
            problems.Add($"Baskı hızı {MinSpeed}–{MaxSpeed} inç/sn arasında olmalı (girilen {Speed}).");
        if (!double.IsFinite(OffsetXMm) || Math.Abs(OffsetXMm) > MaxOffsetMm)
            problems.Add($"Yatay ofset en fazla ±{Format(MaxOffsetMm)} mm olabilir (girilen {Format(OffsetXMm)} mm).");
        if (!double.IsFinite(OffsetYMm) || Math.Abs(OffsetYMm) > MaxOffsetMm)
            problems.Add($"Dikey ofset en fazla ±{Format(MaxOffsetMm)} mm olabilir (girilen {Format(OffsetYMm)} mm).");
        return problems;
    }

    private static string Format(double value) => value.ToString("0.##", CultureInfo.GetCultureInfo("tr-TR"));
}

/// <summary>Etiket medyası (rulo) bilgisi, mm.</summary>
public sealed record LabelMedia(double WidthMm, double HeightMm, double GapMm, double GapOffsetMm, MediaType Type)
{
    public static LabelMedia FromSetup(LabelSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        return new LabelMedia(setup.WidthMm, setup.HeightMm, setup.GapMm, setup.GapOffsetMm, setup.Media);
    }

    public int WidthDots(double dotsPerMm) => Units.MmToDotsRounded(WidthMm, dotsPerMm);

    public int HeightDots(double dotsPerMm) => Units.MmToDotsRounded(HeightMm, dotsPerMm);

    /// <summary>Medya ölçüsü sorunlarını Türkçe listeler.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (!double.IsFinite(WidthMm) || WidthMm <= 0) problems.Add("Etiket genişliği geçersiz.");
        if (!double.IsFinite(HeightMm) || HeightMm <= 0) problems.Add("Etiket yüksekliği geçersiz.");
        if (!double.IsFinite(GapMm) || GapMm < 0 || GapMm > 25.4) problems.Add("Etiket boşluğu 0–25,4 mm arasında olmalı.");
        if (!double.IsFinite(GapOffsetMm) || GapOffsetMm < 0 || GapOffsetMm > 25.4) problems.Add("Boşluk ofseti 0–25,4 mm arasında olmalı.");
        if (Type == MediaType.Gap && GapMm <= 0)
            problems.Add("Boşluklu etikette boşluk 0 olamaz; sürekli rulo için medya türünü 'Sürekli' seçin.");
        return problems;
    }
}

/// <summary>Yazıcıya gidecek tek etiket görüntüsü ve kopya sayısı.</summary>
public sealed record LabelPage(MonoBitmap Bitmap, int Copies);
