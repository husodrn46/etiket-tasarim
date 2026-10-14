using AkelEtiket.BarcodeEngine;
using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Imaging;
using AkelEtiket.Core.Model;

namespace AkelEtiket.Rendering;

/// <summary>Basılacak tek bir etiket görüntüsü (adet kadar tekrar edilir).</summary>
public sealed record PrintItem(int Sequence, int? RowNumber, int Copies, IFieldValueSource Fields)
{
    public string Describe() => RowNumber is { } row ? $"Satır {row}" : $"Etiket {Sequence}";
}

/// <summary>
/// Baskı için etiket üretir: belgenin anlık kopyası yazıcı çözünürlüğünde, baskı kipinde render edilir;
/// istenirse her barkod bağımsız çözücüyle okunarak doğrulanır. Hatalı etiket ASLA basılmaz.
/// İş parçacığı güvenlidir (kendi render motoru vardır).
/// </summary>
public sealed class LabelProducer : IDisposable
{
    private readonly LabelDocument _document;
    private readonly LabelRenderer _renderer;
    private readonly bool _verifyBarcodes;
    private readonly IImageSourceResolver? _images;

    public LabelProducer(LabelDocument documentSnapshot, AssetStore assets, bool verifyBarcodes, IImageSourceResolver? images = null)
    {
        _document = documentSnapshot;
        _renderer = new LabelRenderer(assets);
        _verifyBarcodes = verifyBarcodes;
        _images = images;
    }

    /// <summary>Etiketi üretir. Sorun varsa bitmap null döner ve sorunlar listelenir.</summary>
    public (MonoBitmap? Bitmap, IReadOnlyList<string> Problems, IReadOnlyList<string> Warnings) Produce(PrintItem item, double dotsPerMm)
    {
        var result = _renderer.RenderLabel(_document, new RenderOptions { DotsPerMm = dotsPerMm, Fields = item.Fields, Mode = RenderMode.Print, ImageResolver = _images });
        var problems = result.Issues.Where(i => i.Severity == RenderIssueSeverity.Error).Select(i => i.ToString()).ToList();
        var warnings = result.Issues.Where(i => i.Severity == RenderIssueSeverity.Warning).Select(i => i.ToString()).Distinct().ToList();
        if (problems.Count == 0 && _verifyBarcodes)
        {
            foreach (var element in result.Elements)
            {
                if (element.Barcode is not { IsValid: true } info || element.Layer is null) continue;
                var model = _document.FindElement(element.ElementId) as BarcodeElement;
                if (model is null) continue;
                // Birleşik etiketten barkod bölgesi kesilip okunur: üstüne binen öğeler de hesaba katılır.
                var layer = element.Layer;
                var region = result.Bitmap.Crop(layer.OffsetX, layer.OffsetY, layer.Width, layer.Height);
                var verification = BarcodeVerifier.Verify(region, model.Symbology, info.VerificationText);
                if (!verification.Success)
                    problems.Add($"{model.DisplayName}: okunabilirlik kontrolü başarısız — {verification.Message}");
            }
        }
        return (problems.Count == 0 ? result.Bitmap : null, problems, warnings);
    }

    public void Dispose() => _renderer.Dispose();
}
