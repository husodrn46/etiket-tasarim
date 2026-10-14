using AkelEtiket.BarcodeEngine;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Imaging;
using AkelEtiket.Core.Model;

namespace AkelEtiket.Rendering;

public enum RenderMode
{
    /// <summary>Tasarım: doldurulmamış alanlar yer tutucu olarak çizilir, hata sayılmaz.</summary>
    Design,
    /// <summary>Baskı: doldurulmamış alan veya geçersiz barkod hatadır.</summary>
    Print,
}

/// <summary>Render ayarları.</summary>
public sealed record RenderOptions
{
    /// <summary>Hedef çözünürlük (nokta/mm). XP-470B: 8.</summary>
    public double DotsPerMm { get; init; } = 8.0;

    /// <summary>Değişken alan değerleri (veri satırı + yerleşik alanlar). Null = tasarım görünümü.</summary>
    public IFieldValueSource? Fields { get; init; }

    public RenderMode Mode { get; init; } = RenderMode.Design;

    /// <summary>Satıra göre değişen görsellerin (ImageElement.SourceField) dosyalarını bulan çözücü.</summary>
    public IImageSourceResolver? ImageResolver { get; init; }
}

public enum RenderIssueSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>Bir öğenin çiziminde bulunan sorun (Türkçe).</summary>
public sealed record RenderIssue(string ElementId, string ElementName, RenderIssueSeverity Severity, string Message)
{
    public override string ToString() => $"{ElementName}: {Message}";
}

/// <summary>Barkod öğesinin çizim bilgisi (özellik panelinde gösterilir).</summary>
public sealed class BarcodeRenderInfo
{
    public required BarcodeValidationResult Validation { get; init; }
    public BarcodeLayout? Layout { get; init; }
    public int ModuleDots => Layout?.ModuleDots ?? 0;
    public double ModuleMm { get; init; }
    public string VerificationText => Validation.VerificationText;
    public bool IsValid => Validation.IsValid && Layout is { Fits: true };
    public BarcodeSymbol? Symbol { get; init; }
}

/// <summary>Görsel öğesinin çizim bilgisi.</summary>
public sealed class ImageRenderInfo
{
    public ImageConversionMethod AppliedMethod { get; init; }

    /// <summary>Uygulanan yöntemin Türkçe adı.</summary>
    public string AppliedMethodText => AppliedMethod switch
    {
        ImageConversionMethod.Threshold => "Eşik değeri",
        ImageConversionMethod.Otsu => "Otsu (otomatik eşik)",
        ImageConversionMethod.FloydSteinberg => "Floyd–Steinberg dithering",
        ImageConversionMethod.Ordered => "Düzenli (Bayer) dithering",
        ImageConversionMethod.Photo => "Fotoğraf (detay koruyan)",
        ImageConversionMethod.Logo => "Logo / çizim (keskin kenar)",
        _ => "Otomatik",
    };
    public string DetectedKindText { get; init; } = "";
    public int? EffectiveThreshold { get; init; }
    public int SourceWidth { get; init; }
    public int SourceHeight { get; init; }
    public string FormatName { get; init; } = "";
}

/// <summary>Metin öğesinin çizim bilgisi.</summary>
public sealed class TextRenderInfo
{
    public double EffectiveFontSizePt { get; init; }
    public bool Overflow { get; init; }
    public bool FontSubstituted { get; init; }
    public string UsedFamily { get; init; } = "";
    public int LineCount { get; init; }
}

/// <summary>Tek öğenin yazıcı çözünürlüğündeki çizim sonucu.</summary>
public sealed class ElementRenderResult
{
    public required string ElementId { get; init; }

    /// <summary>Etiket koordinatlarında (nokta) konumlanmış katman; çizilecek bir şey yoksa null.</summary>
    public MonoLayer? Layer { get; init; }

    /// <summary>Katmanın öğe kutusunun sol üst köşesine göre konumu (nokta). Sürüklemede yeniden çizim gerekmez.</summary>
    public int LocalOffsetX { get; init; }

    public int LocalOffsetY { get; init; }

    private IReadOnlyList<RenderIssue> _issues = [];

    public IReadOnlyList<RenderIssue> Issues { get => _issues; init => _issues = value; }

    /// <summary>Çizimden sonra not ekler (koşul bilgisi gibi); diğer bilgiler aynen kalır.</summary>
    internal void AppendIssue(RenderIssue issue) => _issues = [.. _issues, issue];

    /// <summary>Doldurulmamış değişken alan nedeniyle yer tutucu çizildi.</summary>
    public bool IsPlaceholder { get; init; }

    /// <summary>Koşullu gösterme sağlanmadığı için bu veride çizilmedi (katman yok).</summary>
    public bool HiddenByCondition { get; init; }

    /// <summary>Alanları doldurulmuş içerik (metin/barkod).</summary>
    public string? ResolvedContent { get; init; }

    public BarcodeRenderInfo? Barcode { get; init; }
    public ImageRenderInfo? Image { get; init; }
    public TextRenderInfo? Text { get; init; }

    public bool HasErrors => Issues.Any(i => i.Severity == RenderIssueSeverity.Error);
}

/// <summary>Tüm etiketin 1-bit çizim sonucu.</summary>
public sealed class LabelRenderResult
{
    public required MonoBitmap Bitmap { get; init; }
    public required double DotsPerMm { get; init; }
    public IReadOnlyList<RenderIssue> Issues { get; init; } = [];
    public IReadOnlyList<ElementRenderResult> Elements { get; init; } = [];
    public bool HasErrors => Issues.Any(i => i.Severity == RenderIssueSeverity.Error);
    public IEnumerable<RenderIssue> Errors => Issues.Where(i => i.Severity == RenderIssueSeverity.Error);
}
