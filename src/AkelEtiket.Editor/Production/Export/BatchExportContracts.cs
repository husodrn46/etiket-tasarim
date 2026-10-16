using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;

namespace AkelEtiket.Editor.Production.Export;

public enum BatchExportFormat
{
    Pdf,
    PngZip,
}

/// <summary>
/// A caller-owned immutable project/field snapshot. Export does not allocate counters, assign SSCCs,
/// advance sequences or send anything to a printer. Existing destinations require explicit opt-in.
/// </summary>
public sealed record BatchExportRequest(
    LabelProject Project,
    IReadOnlyList<PrintItem> Items,
    double DotsPerMm,
    IImageSourceResolver? Images = null,
    bool OverwriteExisting = false,
    int MaxLabels = BatchExportService.MaximumLabels);

public sealed record BatchExportProgress(int CompletedLabels, int TotalLabels);

/// <summary>UniqueDesignCount counts distinct final monochrome images, including variable data.</summary>
public sealed record BatchExportResult(
    string OutputPath,
    int LabelCount,
    int UniqueDesignCount,
    IReadOnlyList<string> Warnings);
