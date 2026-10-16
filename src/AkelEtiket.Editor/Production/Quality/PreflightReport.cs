using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;

namespace AkelEtiket.Editor.Production.Quality;

public enum PreflightSeverity { Info, Warning, Error }

/// <summary>A stable machine code and a localized explanation of one print-quality finding.</summary>
public sealed record PreflightIssue(
    PreflightSeverity Severity,
    string Code,
    string Message,
    int? RowNumber = null,
    string? ElementId = null,
    string? ElementName = null,
    string? Recommendation = null);

/// <summary>
/// Read-only input. Fields must already be preview/snapshot fields: this service never allocates counters
/// or SSCCs. Copies reuse the same rendered image; distinct PrintItems are checked individually.
/// </summary>
public sealed record PreflightRequest(
    LabelProject Project,
    IReadOnlyList<PrintItem> Items,
    double DotsPerMm,
    IImageSourceResolver? Images = null,
    int SkippedRowCount = 0);

public sealed class PreflightReport
{
    public required IReadOnlyList<PreflightIssue> Issues { get; init; }
    public required int RowsChecked { get; init; }
    public required long LabelsChecked { get; init; }
    public required int TotalRows { get; init; }
    public required long TotalLabels { get; init; }
    public required bool IsComplete { get; init; }
    public bool IsCancelled { get; init; }
    public int ErrorCount => Issues.Count(i => i.Severity == PreflightSeverity.Error);
    public int WarningCount => Issues.Count(i => i.Severity == PreflightSeverity.Warning);
    public int InfoCount => Issues.Count(i => i.Severity == PreflightSeverity.Info);
    public bool CanExport => IsComplete && ErrorCount == 0;
}

/// <summary>
/// Explicit safety limits, not sampling limits. Hitting any limit returns an incomplete, non-exportable
/// report. Pixel work includes a second production render when barcode decoding is needed.
/// </summary>
public sealed record PreflightLimits
{
    public int MaxRows { get; init; } = 10_000;
    public long MaxLabels { get; init; } = 10_000;
    public int MaxElements { get; init; } = 1_000;
    public long MaxPixelsPerLabel { get; init; } = 16_000_000;
    public long MaxPixelsPerElement { get; init; } = 8_000_000;
    public long MaxPixelsPerRow { get; init; } = 64_000_000;
    public long MaxTotalPixelWork { get; init; } = 4_000_000_000;
    public long MaxAssetBytes { get; init; } = 64L * 1024 * 1024;
    public int MaxTextLength { get; init; } = 100_000;
    public int MaxIssues { get; init; } = 20_000;
    public double EdgeWarningMm { get; init; } = 0.5;

    internal void Validate()
    {
        if (MaxRows < 1 || MaxLabels < 1 || MaxElements < 1 || MaxPixelsPerLabel < 1 || MaxPixelsPerElement < 1 ||
            MaxPixelsPerRow < 1 || MaxTotalPixelWork < 1 || MaxAssetBytes < 1 || MaxTextLength < 1 ||
            MaxIssues < 1 || !double.IsFinite(EdgeWarningMm) || EdgeWarningMm < 0)
            throw new ArgumentOutOfRangeException(nameof(PreflightLimits));
    }
}
