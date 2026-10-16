using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AkelEtiket.Core;
using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Geometry;
using AkelEtiket.Core.Imaging;
using AkelEtiket.Core.Model;
using AkelEtiket.Core.Serialization;
using AkelEtiket.Editor.Printing;
using AkelEtiket.Gs1;
using AkelEtiket.Rendering;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.Production.Export;

/// <summary>
/// Verified local batch output. Only a complete, closed file is moved into the selected destination.
/// Rendering is sequential, retaining one rendered label at a time rather than a batch of bitmaps.
/// </summary>
public sealed class BatchExportService
{
    public const int MaximumLabels = 10_000;
    public const long MaximumBitmapPixels = 16_777_216;
    public const long MaximumEstimatedRenderBytes = 256L * 1024 * 1024;
    private const int MaximumWarnings = 100;

    public Task<BatchExportResult> ExportAsync(
        BatchExportRequest request,
        BatchExportFormat format,
        string destinationPath,
        IProgress<BatchExportProgress>? progress = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Project);
        ArgumentNullException.ThrowIfNull(request.Items);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        // Copy the list before yielding to the background worker. Sources themselves are snapshots,
        // by contract; trying to enumerate arbitrary field sources would lose dates/format semantics.
        var items = request.Items.ToArray();
        string outputPath = Path.GetFullPath(destinationPath);
        return Task.Run(() => Export(request, items, format, outputPath, progress, token), token);
    }

    private static BatchExportResult Export(
        BatchExportRequest request,
        PrintItem[] items,
        BatchExportFormat format,
        string outputPath,
        IProgress<BatchExportProgress>? progress,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        int totalLabels = Validate(request, items, format);
        if (!request.OverwriteExisting && File.Exists(outputPath))
            throw new IOException(L10n.T("Çıktı dosyası zaten var. Üzerine yazmayı açıkça onaylayın."));

        // Do not use a predictable .tmp beside the destination: another concurrent export may exist.
        string folder = Path.GetDirectoryName(outputPath)!;
        string temp = Path.Combine(folder, ".labelharbor-export-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            // LabelProducer may read model values on a worker. Its document cannot change mid-job.
            var document = ModelJson.Clone(request.Project.Document);
            var warnings = new WarningCollector();
            var designs = new HashSet<string>(StringComparer.Ordinal);
            progress?.Report(new BatchExportProgress(0, totalLabels));
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024))
            {
                if (format == BatchExportFormat.Pdf)
                {
                    PdfExport.Write(output, Pages(), AppIdentity.ProductName);
                }
                else
                {
                    WriteZip(output, document, request.Project.Assets, request.Images, items, request.DotsPerMm, totalLabels, designs, warnings, progress, token);
                }
                token.ThrowIfCancellationRequested();
                output.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            // Same-directory rename is the commit point. With overwrite=false, a destination created
            // by another process since our initial check is preserved and Move fails safely.
            File.Move(temp, outputPath, overwrite: request.OverwriteExisting);
            return new BatchExportResult(outputPath, totalLabels, designs.Count, warnings.ToArray());

            IEnumerable<PdfLabelPage> Pages()
            {
                int completed = 0;
                foreach (var item in items)
                {
                    token.ThrowIfCancellationRequested();
                    var bitmap = Produce(document, request.Project.Assets, request.Images, item, request.DotsPerMm, warnings, token);
                    designs.Add(BitmapHash(bitmap));
                    for (int copy = 0; copy < item.Copies; copy++)
                    {
                        token.ThrowIfCancellationRequested();
                        yield return new PdfLabelPage(bitmap, document.Label.WidthMm, document.Label.HeightMm);
                        progress?.Report(new BatchExportProgress(++completed, totalLabels));
                    }
                }
            }
        }
        finally
        {
            // Never delete the selected destination, including when cleanup itself encounters an error.
            try { File.Delete(temp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static int Validate(BatchExportRequest request, PrintItem[] items, BatchExportFormat format)
    {
        if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
        if (request.MaxLabels is < 1 or > MaximumLabels)
            throw new ArgumentOutOfRangeException(nameof(request.MaxLabels));
        if (!double.IsFinite(request.DotsPerMm) || request.DotsPerMm <= 0 || request.DotsPerMm > 48)
            throw new ArgumentOutOfRangeException(nameof(request.DotsPerMm), L10n.T("Çıktı çözünürlüğü 0 ile 48 nokta/mm arasında olmalıdır."));
        if (items.Length == 0)
            throw new InvalidOperationException(L10n.T("Dışa aktarılacak etiket seçilmedi."));

        long total = 0;
        foreach (var item in items)
        {
            if (item is null || item.Fields is null || item.Copies <= 0)
                throw new InvalidOperationException(L10n.T("Her çıktı satırının geçerli verisi ve pozitif adedi olmalıdır."));
            total += item.Copies;
            if (total > request.MaxLabels)
                throw new InvalidOperationException(L10n.T($"Bir çıktı işi en fazla {request.MaxLabels} etiket içerebilir."));
        }

        var document = request.Project.Document;
        if (CounterFields.DocumentUsesCounters(document) || Gs1FieldSource.DocumentUsesSscc(document))
            throw new InvalidOperationException(L10n.T("Sayaç veya SSCC kullanan tasarımlar toplu dosya olarak dışa aktarılamaz. Bu numaralar için doğrulanmış baskı akışını kullanın."));
        long labelPixels = PixelArea(document.Label.WidthMm, document.Label.HeightMm, request.DotsPerMm);
        // Gray8 encoding, Skia surfaces, monochrome buffers and barcode verification overlap in time.
        // Include rotated element layers: even a small label can contain a huge off-canvas element.
        long peakBytes = checked(labelPixels * 12);
        foreach (var element in document.Elements.Where(e => e.Visible))
        {
            var bounds = ElementGeometry.GetRotatedBoundsMm(element);
            long elementPixels = PixelArea(bounds.Width, bounds.Height, request.DotsPerMm);
            peakBytes = checked(peakBytes + elementPixels * 4);
            if (peakBytes > MaximumEstimatedRenderBytes) throw RenderMemoryLimit();
        }
        foreach (var asset in request.Project.Assets.Assets)
        {
            peakBytes = checked(peakBytes + asset.Size);
            if (peakBytes > MaximumEstimatedRenderBytes) throw RenderMemoryLimit();
        }
        if (peakBytes > MaximumEstimatedRenderBytes) throw RenderMemoryLimit();
        return (int)total;
    }

    private static long PixelArea(double widthMm, double heightMm, double dotsPerMm)
    {
        double width = Math.Max(1, Math.Ceiling(widthMm * dotsPerMm));
        double height = Math.Max(1, Math.Ceiling(heightMm * dotsPerMm));
        if (!double.IsFinite(width) || !double.IsFinite(height) || width * height > MaximumBitmapPixels)
            throw RenderMemoryLimit();
        return checked((long)width * (long)height);
    }

    private static InvalidOperationException RenderMemoryLimit() =>
        new(L10n.T("Bu tasarım çıktı belleği sınırını aşıyor. Çözünürlüğü veya büyük öğeleri küçültün."));

    private static MonoBitmap Produce(LabelDocument document, AssetStore assets, IImageSourceResolver? images,
        PrintItem item, double dotsPerMm, WarningCollector warnings, CancellationToken token)
    {
        // The renderer caches decoded external images. A per-item producer releases that native cache
        // before the next row, so thousands of distinct image fields cannot accumulate decoded images.
        using var producer = new LabelProducer(document, assets, verifyBarcodes: true, images);
        var result = producer.Produce(item, dotsPerMm);
        token.ThrowIfCancellationRequested();
        if (result.Bitmap is null || result.Problems.Count > 0)
            throw new InvalidOperationException(L10n.T($"{item.Describe()}: çıktı doğrulanamadı.") + "\n" + string.Join("\n", result.Problems.Take(4)));
        warnings.Add(result.Warnings);
        return result.Bitmap;
    }

    private static string BitmapHash(MonoBitmap bitmap) => Convert.ToHexStringLower(SHA256.HashData(bitmap.Data));

    private static void WriteZip(
        Stream output, LabelDocument document, AssetStore assets, IImageSourceResolver? resolver, PrintItem[] items,
        double dotsPerMm, int totalLabels, HashSet<string> designs, WarningCollector warnings,
        IProgress<BatchExportProgress>? progress, CancellationToken token)
    {
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var images = new Dictionary<string, (string Path, string Sha256)>(StringComparer.Ordinal);
        var manifestItems = new List<ManifestItem>(items.Length);
        int completed = 0;
        for (int index = 0; index < items.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var item = items[index];
            var bitmap = Produce(document, assets, resolver, item, dotsPerMm, warnings, token);
            string designHash = BitmapHash(bitmap);
            designs.Add(designHash);
            if (!images.TryGetValue(designHash, out var image))
            {
                // Entry names contain generated ASCII numbers only; never user fields or file names.
                string entryPath = $"labels/label-{images.Count + 1:00000}.png";
                byte[] png = MonoImageExport.EncodePng(bitmap, dotsPerMm * Units.MmPerInch);
                token.ThrowIfCancellationRequested();
                image = (entryPath, Convert.ToHexStringLower(SHA256.HashData(png)));
                using (var entryStream = archive.CreateEntry(entryPath, CompressionLevel.NoCompression).Open())
                    entryStream.Write(png);
                images.Add(designHash, image);
            }
            manifestItems.Add(new ManifestItem(index + 1, item.Sequence, item.RowNumber, item.Copies,
                completed + 1, completed + item.Copies, image.Path, image.Sha256));
            completed += item.Copies;
            progress?.Report(new BatchExportProgress(completed, totalLabels));
        }
        token.ThrowIfCancellationRequested();
        using var manifestStream = archive.CreateEntry("manifest.json", CompressionLevel.Optimal).Open();
        // The manifest intentionally excludes document metadata, fields, paths, assets and warnings.
        // Rendered images necessarily contain the label data the user explicitly selected to export.
        JsonSerializer.Serialize(manifestStream, new
        {
            schemaVersion = 1,
            application = AppIdentity.ProductName,
            copyRepresentation = "Each item references one image. Repeat it copies times in item order. firstLabel and lastLabel are inclusive, one-based output positions.",
            labelCount = totalLabels,
            uniqueDesignCount = images.Count,
            widthMm = document.Label.WidthMm,
            heightMm = document.Label.HeightMm,
            dpi = dotsPerMm * Units.MmPerInch,
            items = manifestItems,
        }, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private sealed record ManifestItem(int ItemIndex, int Sequence, int? RowNumber, int Copies,
        int FirstLabel, int LastLabel, string Image, string Sha256);

    private sealed class WarningCollector
    {
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private readonly List<string> _warnings = [];
        private bool _truncated;

        public void Add(IReadOnlyList<string> warnings)
        {
            foreach (string warning in warnings)
            {
                if (_seen.Contains(warning)) continue;
                if (_warnings.Count == MaximumWarnings) { _truncated = true; continue; }
                _seen.Add(warning);
                _warnings.Add(warning);
            }
        }

        public string[] ToArray() => _truncated
            ? [.. _warnings, L10n.T("Diğer çıktı uyarıları listede gösterilmiyor.")]
            : [.. _warnings];
    }
}
