using System.Globalization;
using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.Core.Serialization;
using AkelEtiket.Editor.Production.Quality;
using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;
using SkiaSharp;

namespace AkelEtiket.Tests;

public sealed class ProductionPreflightTests
{
    [Fact]
    public async Task ValidCompositedBarcode_ChecksEveryRowAndCountsCopiesWithoutReadingPerCopy()
    {
        var fields = new CountingFields("DEMO-001");
        var project = Project(new BarcodeElement { X = 5, Y = 5, Width = 80, Height = 30, Content = "{Code}", ShowText = false });
        var report = await new PreflightService().AnalyzeAsync(new(project,
            [new PrintItem(1, 2, 3, fields), new PrintItem(2, 3, 2, fields)], 8));
        Assert.True(report.IsComplete);
        Assert.True(report.CanExport, Describe(report));
        Assert.Equal(2, report.RowsChecked);
        Assert.Equal(5, report.LabelsChecked);
        Assert.Equal(5, report.TotalLabels);
        Assert.Equal(2, fields.Calls);
        Assert.Contains(report.Issues, i => i.Code == "software_check_only");
    }

    [Fact]
    public async Task ErrorInLastDataRow_IsNotHiddenBySampling()
    {
        var project = Project(new BarcodeElement { X = 5, Y = 5, Width = 80, Height = 30, Symbology = BarcodeType.Ean13, Content = "{Code}", ShowText = false });
        var rows = Enumerable.Range(1, 8).Select(i => new PrintItem(i, 100 + i, 1,
            new RecordFieldSource(["Code"], [i == 8 ? "INVALID" : "0000000000000"]))).ToArray();
        var report = await new PreflightService().AnalyzeAsync(new(project, rows, 8));
        Assert.True(report.IsComplete);
        Assert.Equal(8, report.RowsChecked);
        Assert.Equal(8, report.LabelsChecked);
        Assert.False(report.CanExport);
        Assert.Contains(report.Issues, i => i.Code == "barcode_invalid" && i.RowNumber == 108);
    }

    [Fact]
    public async Task OpaqueOverlay_IsDetectedFromActualPrintImageAndIdentifiesBarcode()
    {
        var barcode = new BarcodeElement { X = 5, Y = 5, Width = 80, Height = 30, Content = "DEMO001", ShowText = false };
        var project = Project(barcode, new ShapeElement { X = 5, Y = 5, Width = 80, Height = 30, Fill = FillStyle.White, StrokeWidth = 0 });
        var report = await Analyze(project);
        var error = Assert.Single(report.Issues, i => i.Code == "barcode_unreadable");
        Assert.Equal(barcode.Id, error.ElementId);
        Assert.Equal(2, error.RowNumber);
        Assert.False(report.CanExport);
        Assert.True(report.IsComplete);
    }

    [Fact]
    public async Task ClippedBarcodeAndOverflowingText_KeepDifferentStableCodes()
    {
        var barcode = new BarcodeElement { X = 80, Y = 10, Width = 40, Height = 20, Content = "DEMO", ShowText = false };
        var text = new TextElement { X = 5, Y = 5, Width = 2, Height = 1, Text = "This cannot fit", FontSize = 20, WordWrap = false };
        var report = await Analyze(Project(barcode, text));
        Assert.Contains(report.Issues, i => i.Code == "barcode_clipped" && i.Severity == PreflightSeverity.Error && i.ElementId == barcode.Id);
        Assert.Contains(report.Issues, i => i.Code == "text_overflow" && i.Severity == PreflightSeverity.Warning && i.ElementId == text.Id);
        Assert.False(report.CanExport);
    }

    [Fact]
    public async Task MissingFieldAndMissingImage_AreErrorsWithElementAndRow()
    {
        var text = new TextElement { X = 5, Y = 5, Width = 80, Height = 15, Text = "{Missing}" };
        var image = new ImageElement { X = 5, Y = 25, Width = 10, Height = 10, AssetId = "not-present.png" };
        var report = await Analyze(Project(text, image));
        Assert.Contains(report.Issues, i => i.Code == "missing_field" && i.ElementId == text.Id && i.RowNumber == 2);
        Assert.Contains(report.Issues, i => i.Code == "image_missing_or_invalid" && i.ElementId == image.Id && i.RowNumber == 2);
        Assert.True(report.IsComplete);
        Assert.False(report.CanExport);
    }

    [Fact]
    public async Task RowImage_RequiresItsResolverEvenWhenAnEmbeddedFallbackExists()
    {
        var project = Project(new ImageElement { X = 5, Y = 5, Width = 10, Height = 10, SourceField = "Image" });
        var report = await new PreflightService().AnalyzeAsync(new(project,
            [new PrintItem(1, 21, 1, new RecordFieldSource(["Image"], ["sample.png"]))], 8));
        Assert.Contains(report.Issues, i => i.Code == "image_missing_or_invalid" && i.RowNumber == 21);
        Assert.False(report.CanExport);
    }

    [Fact]
    public async Task FalseCondition_DoesNotRequireInvisibleImageOrItsFields()
    {
        var image = new ImageElement { X = 5, Y = 5, Width = 10, Height = 10, SourceField = "MissingImage", VisibleWhen = new("Show") };
        var report = await new PreflightService().AnalyzeAsync(new(Project(image),
            [new PrintItem(1, 2, 1, new RecordFieldSource(["Show"], [""]))], 8));
        Assert.True(report.CanExport, Describe(report));
        Assert.Contains(report.Issues, i => i.Code == "condition_hidden" && i.ElementId == image.Id);
        Assert.DoesNotContain(report.Issues, i => i.Code == "image_missing_or_invalid");
    }

    [Fact]
    public async Task MissingConditionField_IsAnErrorInsteadOfPretendingConditionIsFalse()
    {
        var text = new TextElement { X = 5, Y = 5, Width = 80, Height = 15, Text = "Conditional", VisibleWhen = new("Missing") };
        var report = await Analyze(Project(text));
        Assert.Contains(report.Issues, i => i.Code == "condition_field_missing" && i.Severity == PreflightSeverity.Error);
        Assert.False(report.CanExport);
    }

    [Fact]
    public async Task EmptyOptionalField_EdgeAndFontSubstitution_AreActionableWarnings()
    {
        var text = new TextElement { X = 0, Y = 5, Width = 80, Height = 15, Text = "Name: {Name}", FontFamily = "LabelHarbor Missing Test Font 7294" };
        var report = await new PreflightService().AnalyzeAsync(new(Project(text),
            [new PrintItem(1, 2, 1, new RecordFieldSource(["Name"], [" "]))], 8));
        Assert.True(report.CanExport, Describe(report));
        Assert.Contains(report.Issues, i => i.Code == "empty_field" && i.Recommendation is not null);
        Assert.Contains(report.Issues, i => i.Code == "font_substituted" && i.Recommendation is not null);
        Assert.Contains(report.Issues, i => i.Code == "near_label_edge" && i.Recommendation is not null);
    }

    [Fact]
    public async Task ProjectAndEmbeddedAssets_AreNeverChanged()
    {
        var project = Project(new TextElement { X = 5, Y = 5, Width = 80, Height = 15, Text = "Snapshot" });
        byte[] bytes = Png();
        var asset = project.Assets.Add(bytes, "generic-logo.png");
        project.Document.Elements.Add(new ImageElement { X = 5, Y = 25, Width = 10, Height = 10, AssetId = asset.Id });
        string before = ModelJson.Serialize(project.Document);
        var report = await Analyze(project);
        Assert.True(report.CanExport, Describe(report));
        Assert.Equal(before, ModelJson.Serialize(project.Document));
        Assert.Single(project.Assets.Assets);
        Assert.Equal(bytes, project.Assets.Assets[0].Data);
    }

    [Fact]
    public async Task SelectedRowsRejectedByPlanner_AreErrorsAndBlockExport()
    {
        var report = await new PreflightService().AnalyzeAsync(new(Project(), [Item()], 8, SkippedRowCount: 7));
        Assert.False(report.IsComplete);
        Assert.False(report.CanExport);
        Assert.Equal(1, report.RowsChecked);
        Assert.Equal(1, report.LabelsChecked);
        Assert.Contains(report.Issues, i => i.Code == "rows_skipped" && i.Severity == PreflightSeverity.Error);
    }

    [Fact]
    public async Task EmptyJob_AndInvalidCopies_CannotExport()
    {
        var empty = await new PreflightService().AnalyzeAsync(new(Project(), [], 8));
        Assert.False(empty.CanExport);
        Assert.Contains(empty.Issues, i => i.Code == "no_items");
        var invalid = await new PreflightService().AnalyzeAsync(new(Project(), [Item() with { Copies = 0 }], 8));
        Assert.False(invalid.CanExport);
        Assert.Contains(invalid.Issues, i => i.Code == "invalid_print_item");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(49)]
    public async Task InvalidResolution_IsIncompleteAndDoesNotRender(double dotsPerMm)
    {
        var report = await new PreflightService().AnalyzeAsync(new(Project(), [Item()], dotsPerMm));
        Assert.False(report.IsComplete);
        Assert.False(report.CanExport);
        Assert.Equal(0, report.RowsChecked);
        Assert.Contains(report.Issues, i => i.Code == "invalid_resolution");
    }

    [Fact]
    public async Task RowLimit_ReportsPartialCountsAndBlocksExport()
    {
        var service = new PreflightService(new() { MaxRows = 2 });
        var report = await service.AnalyzeAsync(new(Project(), [Item(), Item(), Item()], 8));
        Assert.False(report.IsComplete);
        Assert.False(report.CanExport);
        Assert.Equal(2, report.RowsChecked);
        Assert.Equal(2, report.LabelsChecked);
        Assert.Equal(3, report.TotalRows);
        Assert.Contains(report.Issues, i => i.Code == "row_limit");
    }

    [Fact]
    public async Task CopyLimit_DoesNotPretendEveryCopyWasChecked()
    {
        var report = await new PreflightService(new() { MaxLabels = 3 }).AnalyzeAsync(new(Project(),
            [Item() with { Copies = 2 }, Item() with { Copies = 2 }], 8));
        Assert.False(report.IsComplete);
        Assert.Equal(1, report.RowsChecked);
        Assert.Equal(2, report.LabelsChecked);
        Assert.Equal(4, report.TotalLabels);
        Assert.Contains(report.Issues, i => i.Code == "label_limit");
    }

    [Fact]
    public async Task PixelAndElementLimits_ProtectNativeRenderingBeforeAllocation()
    {
        var pixels = await new PreflightService(new() { MaxPixelsPerLabel = 100 }).AnalyzeAsync(new(Project(), [Item()], 8));
        Assert.False(pixels.IsComplete);
        Assert.Equal(0, pixels.RowsChecked);
        Assert.Contains(pixels.Issues, i => i.Code == "label_pixel_limit");
        var elements = await new PreflightService(new() { MaxElements = 1 }).AnalyzeAsync(new(
            Project(new ShapeElement(), new ShapeElement()), [Item()], 8));
        Assert.False(elements.IsComplete);
        Assert.Contains(elements.Issues, i => i.Code == "element_limit");
    }

    [Fact]
    public async Task PixelWorkLimit_IsExplicitInsteadOfSilentlySamplingRows()
    {
        var report = await new PreflightService(new() { MaxTotalPixelWork = 1 }).AnalyzeAsync(new(Project(), [Item(), Item()], 8));
        Assert.False(report.IsComplete);
        Assert.Equal(0, report.RowsChecked);
        Assert.Contains(report.Issues, i => i.Code == "pixel_work_limit");
    }

    [Fact]
    public async Task FindingLimit_ReturnsExplicitIncompleteReport()
    {
        var report = await new PreflightService(new() { MaxIssues = 1 }).AnalyzeAsync(new(Project(), [Item(), Item()], 8));
        Assert.False(report.IsComplete);
        Assert.False(report.CanExport);
        Assert.Contains(report.Issues, i => i.Code == "issue_limit");
    }

    [Fact]
    public async Task OversizedFieldValue_IsIncompleteEvenWhenRendererCatchesTheException()
    {
        var project = Project(new TextElement { X = 5, Y = 5, Width = 80, Height = 15, Text = "{Value}" });
        var report = await new PreflightService(new() { MaxTextLength = 10 }).AnalyzeAsync(new(project,
            [new PrintItem(1, 2, 1, new RecordFieldSource(["Value"], [new string('x', 11)]))], 8));
        Assert.False(report.IsComplete);
        Assert.False(report.CanExport);
        Assert.Equal(0, report.RowsChecked);
        Assert.Contains(report.Issues, i => i.Code is "row_content_limit" or "field_value_limit");
    }

    [Fact]
    public async Task CancelledBeforeStart_ReturnsAnIncompleteReport()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var report = await new PreflightService().AnalyzeAsync(new(Project(), [Item()], 8), cancellation.Token);
        Assert.True(report.IsCancelled);
        Assert.False(report.IsComplete);
        Assert.False(report.CanExport);
        Assert.Equal(0, report.RowsChecked);
        Assert.Contains(report.Issues, i => i.Code == "analysis_cancelled");
    }

    [Fact]
    public async Task LazySource_IsReadOffTheCallingThread_AndCancellationDoesNotCountHalfARow()
    {
        using var source = new BlockingFields();
        using var cancellation = new CancellationTokenSource();
        var project = Project(new TextElement { X = 5, Y = 5, Width = 80, Height = 15, Text = "{Value}" });
        Task<PreflightReport> work = new PreflightService().AnalyzeAsync(new(project,
            [new PrintItem(1, 2, 1, source)], 8), cancellation.Token);
        try
        {
            Assert.True(source.Entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.False(work.IsCompleted);
            cancellation.Cancel();
        }
        finally { source.Release.Set(); }
        var report = await work;
        Assert.True(report.IsCancelled);
        Assert.Equal(0, report.RowsChecked);
        Assert.Equal(0, report.LabelsChecked);
    }

    [Theory]
    [InlineData("{SSCC}")]
    [InlineData("{SAYAC}")]
    [InlineData("{SAYAC:0000}")]
    public async Task ReservedNumbers_AreBlockedWithoutReadingOrAllocatingThem(string template)
    {
        var fields = new CountingFields("000000000000000000");
        var report = await new PreflightService().AnalyzeAsync(new(
            Project(new TextElement { X = 5, Y = 5, Width = 80, Height = 15, Text = template }),
            [new PrintItem(1, 2, 1, fields)], 8));
        Assert.False(report.CanExport);
        Assert.Equal(0, fields.Calls);
        Assert.Contains(report.Issues, i => i.Code == "reserve_numbering_requires_print");
    }

    [Fact]
    public async Task LocalSequenceField_IsAllowedAndNeverRewrittenAsAnIdentifier()
    {
        var report = await new PreflightService().AnalyzeAsync(new(
            Project(new TextElement { X = 5, Y = 5, Width = 80, Height = 15, Text = "{SIRA:0000}" }),
            [new PrintItem(1, 2, 1, new BuiltInFieldSource(123, new DateTime(2026, 1, 1)))], 8));
        Assert.True(report.CanExport, Describe(report));
        Assert.DoesNotContain(report.Issues, i => i.Code == "reserve_numbering_requires_print");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegionalDateFormatting_PreservesBuiltInDatesAndCompositeSourceStructure(bool composite)
    {
        var project = Project(new TextElement { X = 5, Y = 5, Width = 80, Height = 15, Text = "{TARIH:DATE}" });
        project.Document.Formatting = new LabelFormatting { InputCultureName = "en-US", CultureName = "en-US", DateFormat = "yyyy-MM-dd" };
        IFieldValueSource fields = new BuiltInFieldSource(1, new DateTime(2026, 10, 16));
        if (composite) fields = new CompositeFieldSource(new RecordFieldSource(["Name"], ["Demo"]), fields);
        var report = await new PreflightService().AnalyzeAsync(new(project, [new PrintItem(1, 2, 1, fields)], 8));
        Assert.True(report.CanExport, Describe(report));
        Assert.Equal(0, report.ErrorCount);
        Assert.True(report.IsComplete);
    }

    [Fact]
    public async Task MachineCodes_AreIndependentOfUiLanguage()
    {
        var old = CultureInfo.CurrentUICulture;
        try
        {
            var project = Project(new TextElement { X = 5, Y = 5, Width = 2, Height = 1, Text = "Does not fit", FontSize = 20, WordWrap = false });
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr");
            var turkish = await Analyze(project);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            var english = await Analyze(project);
            Assert.Equal(turkish.Issues.Select(i => i.Code), english.Issues.Select(i => i.Code));
            var trOverflow = Assert.Single(turkish.Issues, i => i.Code == "text_overflow");
            var enOverflow = Assert.Single(english.Issues, i => i.Code == "text_overflow");
            Assert.NotEqual(trOverflow.Message, enOverflow.Message);
        }
        finally { CultureInfo.CurrentUICulture = old; }
    }

    private static Task<PreflightReport> Analyze(LabelProject project) => new PreflightService().AnalyzeAsync(new(project, [Item()], 8));
    private static LabelProject Project(params LabelElement[] elements)
    {
        var project = LabelProject.CreateBlank(100, 60);
        foreach (var element in elements) project.Document.Elements.Add(element);
        return project;
    }
    private static PrintItem Item() => new(1, 2, 1, new RecordFieldSource([], []));
    private static string Describe(PreflightReport report) => string.Join(" | ", report.Issues.Select(i => i.Code + ": " + i.Message));
    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(4, 4);
        bitmap.Erase(SKColors.Black);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private sealed class CountingFields(string value) : IFieldValueSource
    {
        public int Calls { get; private set; }
        public bool TryGetValue(string name, string? format, out string result)
        {
            Calls++;
            result = value;
            return true;
        }
    }

    private sealed class BlockingFields : IFieldValueSource, IDisposable
    {
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public bool TryGetValue(string name, string? format, out string value)
        {
            Entered.Set();
            Release.Wait(TimeSpan.FromSeconds(15));
            value = "Ready";
            return true;
        }
        public void Dispose() { Entered.Dispose(); Release.Dispose(); }
    }
}
