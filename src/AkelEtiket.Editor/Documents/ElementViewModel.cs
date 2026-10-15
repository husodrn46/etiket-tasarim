using System.ComponentModel;
using AkelEtiket.Core;
using AkelEtiket.Core.Model;
using AkelEtiket.Rendering;
using CommunityToolkit.Mvvm.ComponentModel;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.Documents;

/// <summary>
/// Tuvaldeki bir öğe: model + seçim durumu + yazıcı çözünürlüğünde render edilmiş katman görüntüsü.
/// Katman, öğe kutusunun sol üst köşesine göre konumlanır; taşımada yeniden çizilmez.
/// </summary>
public sealed partial class ElementViewModel : ObservableObject
{
    private readonly DocumentSession _session;

    internal ElementViewModel(LabelElement model, DocumentSession session)
    {
        Model = model;
        _session = session;
    }

    public LabelElement Model { get; }

    public string Id => Model.Id;

    public string DisplayName => string.IsNullOrWhiteSpace(Model.Name) ? AkelEtiket.Core.Localization.UiText.T(Model.KindName) : Model.Name;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Ekran görüntüsü (WPF BitmapSource).</summary>
    [ObservableProperty]
    public partial object? LayerImage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors), nameof(HasWarnings), nameof(StatusText), nameof(IsPlaceholder),
        nameof(ImageSourceText), nameof(BarcodeModuleText), nameof(Issues))]
    public partial ElementRenderResult? RenderResult { get; private set; }

    /// <summary>Barkod okunabilirlik kontrolü sonucu (null = yapılmadı).</summary>
    [ObservableProperty]
    public partial bool? VerificationSuccess { get; internal set; }

    [ObservableProperty]
    public partial string? VerificationMessage { get; internal set; }

    public bool HasErrors => RenderResult?.HasErrors == true;

    public bool HasWarnings => RenderResult?.Issues.Any(i => i.Severity == RenderIssueSeverity.Warning) == true;

    public bool IsPlaceholder => RenderResult?.IsPlaceholder == true;

    /// <summary>Görsel öğesinin kaynak dosya bilgisi (ad, boyut, piksel).</summary>
    public string? ImageSourceText
    {
        get
        {
            if (Model is not ImageElement image || !_session.Assets.TryGet(image.AssetId, out var asset)) return null;
            string size = asset.Size >= 1024 * 1024 ? $"{asset.Size / 1024.0 / 1024.0:0.0} MB" : $"{asset.Size / 1024.0:0} KB";
            string pixels = RenderResult?.Image is { SourceWidth: > 0 } info ? $" • {info.SourceWidth}×{info.SourceHeight} px • {info.FormatName}" : "";
            return $"{asset.FileName} ({size}){pixels}";
        }
    }

    /// <summary>Barkod modül bilgisi (ör. "3 nokta = 0,375 mm").</summary>
    public string? BarcodeModuleText => RenderResult?.Barcode is { ModuleDots: > 0 } b
        ? L10n.T($"{b.ModuleDots} nokta = {b.ModuleMm.ToString("0.###", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))} mm")
        : null;

    /// <summary>Barkod içerik doğrulama mesajları (hata, uyarı, not) tek liste olarak.</summary>
    public IReadOnlyList<RenderIssue> Issues => RenderResult?.Issues ?? [];

    /// <summary>İlk hata/uyarı (katmanlar panelinde ipucu olarak).</summary>
    public string? StatusText => RenderResult?.Issues
        .OrderByDescending(i => i.Severity)
        .Select(i => i.Message)
        .FirstOrDefault();

    // Katman geometrisi (mm) — ekranda görüntünün yerleştirileceği yer.
    public double LayerLeftMm => (Units.MmToDotsRounded(Model.X, Dpmm) + (RenderResult?.LocalOffsetX ?? 0)) / Dpmm;
    public double LayerTopMm => (Units.MmToDotsRounded(Model.Y, Dpmm) + (RenderResult?.LocalOffsetY ?? 0)) / Dpmm;
    public double LayerWidthMm => (RenderResult?.Layer?.Width ?? 0) / Dpmm;
    public double LayerHeightMm => (RenderResult?.Layer?.Height ?? 0) / Dpmm;

    private double Dpmm => _session.DotsPerMm;

    /// <summary>Katmanlar paneli simge anahtarı (görünüm katmanı simgeye çevirir).</summary>
    public string KindKey => Model switch
    {
        TextElement => "Text",
        BarcodeElement b when b.Is2D => "QrCode",
        BarcodeElement => "Barcode",
        ImageElement => "Image",
        ShapeElement { Kind: ShapeKind.Ellipse } => "Ellipse",
        ShapeElement { Kind: ShapeKind.Line } => "Line",
        _ => "Rectangle",
    };

    // Render takip alanları (yalnızca arayüz iş parçacığında kullanılır).
    internal string? RenderedKey;
    internal int RequestedVersion;
    internal bool IsRendering;
    internal bool PendingRender;

    internal void ApplyRender(RenderedElementSnapshot snapshot)
    {
        RenderResult = snapshot.Result;
        LayerImage = snapshot.Image;
        RenderedKey = snapshot.Key;
        VerificationSuccess = null;
        VerificationMessage = null;
        RaiseGeometry();
    }

    internal void RaiseGeometry()
    {
        OnPropertyChanged(nameof(LayerLeftMm));
        OnPropertyChanged(nameof(LayerTopMm));
        OnPropertyChanged(nameof(LayerWidthMm));
        OnPropertyChanged(nameof(LayerHeightMm));
    }

    internal void RaiseModelDisplay(PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LabelElement.VisibleWhen)) OnConditionChangedInModel();
        if (e.PropertyName is nameof(LabelElement.Name) or nameof(LabelElement.DisplayName) or nameof(BarcodeElement.Symbology) or nameof(ShapeElement.Kind))
        {
            OnPropertyChanged(nameof(KindKey));
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public override string ToString() => Model.DisplayName;
}

/// <summary>Render önbelleğinden uygulanacak sonuç.</summary>
internal sealed record RenderedElementSnapshot(string Key, ElementRenderResult Result, object? Image);
