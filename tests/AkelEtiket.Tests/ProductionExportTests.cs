using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.Core.Serialization;
using AkelEtiket.Editor.Production.Export;
using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;

namespace AkelEtiket.Tests;

public sealed class ProductionExportTests : IDisposable
{
    private readonly string _folder = Path.Combine(TestDataRoot.Root, "production-export-" + Guid.NewGuid().ToString("N"));
    private readonly BatchExportService _service = new();

    public ProductionExportTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Destination(string name = "output.zip") => Path.Combine(_folder, name);

    private static LabelProject Project(bool barcode = false)
    {
        var project = LabelProject.CreateBlank(40, 25);
        if (barcode)
            project.Document.Elements.Add(new BarcodeElement
            {
                Symbology = BarcodeType.QrCode,
                Content = "{SKU}",
                X = 2, Y = 2, Width = 20, Height = 20, ShowText = false,
            });
        return project;
    }

    private static PrintItem Item(int sequence = 1, int? row = 2, int copies = 1, string sku = "DEMO-001") =>
        new(sequence, row, copies, new RecordFieldSource(["SKU", "UnusedSecret"], [sku, "private-unused-field"]));

    [Fact]
    public async Task Pdf_ExpandsEverySelectedCopy_AndPreservesExactMillimetrePageSize()
    {
        var updates = new List<BatchExportProgress>();
        string path = Destination("labels.pdf");
        var result = await _service.ExportAsync(new(Project(barcode: true), [Item(copies: 2), Item(2, 8, 3, "DEMO-002")], 8),
            BatchExportFormat.Pdf, path, new InlineProgress(updates.Add));

        string pdf = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(path));
        Assert.StartsWith("%PDF-", pdf);
        Assert.Equal(5, Regex.Matches(pdf, @"/Type\s*/Page(?![a-zA-Z])").Count);
        var boxes = Regex.Matches(pdf, @"/MediaBox\s*\[\s*([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)\s*\]");
        Assert.Equal(5, boxes.Count);
        foreach (Match box in boxes)
        {
            Assert.Equal(40 / 25.4 * 72, double.Parse(box.Groups[3].Value, CultureInfo.InvariantCulture), 1);
            Assert.Equal(25 / 25.4 * 72, double.Parse(box.Groups[4].Value, CultureInfo.InvariantCulture), 1);
        }
        Assert.Equal(Path.GetFullPath(path), result.OutputPath);
        Assert.Equal(5, result.LabelCount);
        Assert.Equal(2, result.UniqueDesignCount);
        Assert.Equal(Enumerable.Range(0, 6), updates.Select(p => p.CompletedLabels));
        Assert.All(updates, p => Assert.Equal(5, p.TotalLabels));
        Assert.Equal([path], Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task PngZip_DeduplicatesImagesWithoutLosingRowsOrderOrCopies_AndIncludesPhysicalResolution()
    {
        string path = Destination();
        var result = await _service.ExportAsync(new(Project(barcode: true),
            [Item(11, 4, 2), Item(12, 19, 3), Item(13, 27, 4, "DEMO-002")], 8), BatchExportFormat.PngZip, path);

        using var archive = ZipFile.OpenRead(path);
        Assert.Equal(3, archive.Entries.Count);
        using var manifest = JsonDocument.Parse(archive.GetEntry("manifest.json")!.Open());
        var root = manifest.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(9, root.GetProperty("labelCount").GetInt32());
        Assert.Equal(2, root.GetProperty("uniqueDesignCount").GetInt32());
        Assert.Contains("copies times", root.GetProperty("copyRepresentation").GetString());
        var rows = root.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal([11, 12, 13], rows.Select(r => r.GetProperty("sequence").GetInt32()));
        Assert.Equal([4, 19, 27], rows.Select(r => r.GetProperty("rowNumber").GetInt32()));
        Assert.Equal([2, 3, 4], rows.Select(r => r.GetProperty("copies").GetInt32()));
        Assert.Equal([1, 3, 6], rows.Select(r => r.GetProperty("firstLabel").GetInt32()));
        Assert.Equal([2, 5, 9], rows.Select(r => r.GetProperty("lastLabel").GetInt32()));
        Assert.Equal(rows[0].GetProperty("image").GetString(), rows[1].GetProperty("image").GetString());
        Assert.NotEqual(rows[1].GetProperty("image").GetString(), rows[2].GetProperty("image").GetString());
        foreach (var row in rows)
        {
            using var stream = archive.GetEntry(row.GetProperty("image").GetString()!)!.Open();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            byte[] png = buffer.ToArray();
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(png)), row.GetProperty("sha256").GetString());
            Assert.Equal(320u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16)));
            Assert.Equal(200u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20)));
            Assert.Equal("pHYs", Encoding.ASCII.GetString(png, 37, 4));
            Assert.Equal(8000u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(41)));
        }
        Assert.Equal(9, result.LabelCount);
        Assert.Equal(2, result.UniqueDesignCount);
    }

    [Fact]
    public async Task ManifestAndEntryNames_DoNotExposeUserFieldsPathsOrDocumentMetadata()
    {
        var project = Project();
        project.Document.Name = "../../private-name";
        project.Document.Description = "private-description";
        project.Document.DataSource = new DataSourceLink { FilePath = "/private/customers/private-data.csv", SheetName = "private-sheet" };
        project.FilePath = "/private/templates/private-template.akelabel";
        await _service.ExportAsync(new(project, [Item(sku: "../../private-product")], 8), BatchExportFormat.PngZip, Destination());

        using var archive = ZipFile.OpenRead(Destination());
        Assert.Equal(["labels/label-00001.png", "manifest.json"], archive.Entries.Select(e => e.FullName));
        using var reader = new StreamReader(archive.GetEntry("manifest.json")!.Open());
        string json = await reader.ReadToEndAsync();
        Assert.DoesNotContain("private", json);
        Assert.DoesNotContain("UnusedSecret", json);
        Assert.DoesNotContain("SKU", json);
        Assert.DoesNotContain("filePath", json);
    }

    [Theory]
    [InlineData(BatchExportFormat.Pdf)]
    [InlineData(BatchExportFormat.PngZip)]
    public async Task ExistingDestination_RequiresExplicitOverwrite_AndIsOtherwiseUntouched(BatchExportFormat format)
    {
        string path = Destination();
        await File.WriteAllTextAsync(path, "previous complete output");
        await Assert.ThrowsAsync<IOException>(() => _service.ExportAsync(new(Project(), [Item()], 8), format, path));
        Assert.Equal("previous complete output", await File.ReadAllTextAsync(path));
        Assert.Equal([path], Directory.GetFiles(_folder));

        await _service.ExportAsync(new(Project(), [Item()], 8, OverwriteExisting: true), format, path);
        Assert.NotEqual("previous complete output", await File.ReadAllTextAsync(path));
        Assert.Equal([path], Directory.GetFiles(_folder));
    }

    [Theory]
    [InlineData(BatchExportFormat.Pdf)]
    [InlineData(BatchExportFormat.PngZip)]
    public async Task MidJobBarcodeFailure_PreservesOldDestination_AndRemovesTemporaryOutput(BatchExportFormat format)
    {
        string path = Destination();
        await File.WriteAllTextAsync(path, "previous complete output");
        var missing = new PrintItem(2, 9, 1, new RecordFieldSource([], []));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ExportAsync(
            new(Project(barcode: true), [Item(), missing], 8, OverwriteExisting: true), format, path));
        Assert.Equal("previous complete output", await File.ReadAllTextAsync(path));
        Assert.Equal([path], Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task CompositeBarcodeVerification_RejectsAValidQrCoveredByAnotherObject()
    {
        var project = Project(barcode: true);
        project.Document.Elements.Add(new ShapeElement
        {
            X = 2, Y = 2, Width = 20, Height = 20, Fill = FillStyle.White, StrokeWidth = 0,
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ExportAsync(new(project, [Item()], 8),
            BatchExportFormat.Pdf, Destination()));
        Assert.Empty(Directory.GetFileSystemEntries(_folder));
    }

    [Theory]
    [InlineData(BatchExportFormat.Pdf, 1)]
    [InlineData(BatchExportFormat.Pdf, 3)]
    [InlineData(BatchExportFormat.PngZip, 1)]
    [InlineData(BatchExportFormat.PngZip, 3)]
    public async Task CancelDuringWorkOrAfterLastLabel_PreservesOldDestination_AndRemovesTemp(BatchExportFormat format, int cancelAt)
    {
        string path = Destination();
        await File.WriteAllTextAsync(path, "previous complete output");
        using var cancel = new CancellationTokenSource();
        var progress = new InlineProgress(p => { if (p.CompletedLabels >= cancelAt) cancel.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.ExportAsync(
            new(Project(), [Item(), Item(2), Item(3)], 8, OverwriteExisting: true), format, path, progress, cancel.Token));
        Assert.Equal("previous complete output", await File.ReadAllTextAsync(path));
        Assert.Equal([path], Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task CancellationBeforeStarting_CreatesNothing()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.ExportAsync(new(Project(), [Item()], 8),
            BatchExportFormat.Pdf, Destination(), token: cancel.Token));
        Assert.Empty(Directory.GetFileSystemEntries(_folder));
    }

    [Fact]
    public async Task DestinationCreatedDuringRender_IsPreserved_WhenOverwriteWasNotAuthorized()
    {
        string path = Destination();
        var progress = new InlineProgress(p => { if (p.CompletedLabels == 0) File.WriteAllText(path, "other export won"); });
        await Assert.ThrowsAsync<IOException>(() => _service.ExportAsync(new(Project(), [Item()], 8),
            BatchExportFormat.PngZip, path, progress));
        Assert.Equal("other export won", await File.ReadAllTextAsync(path));
        Assert.Equal([path], Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task FailedFinalMove_CleansTemp_AndKeepsDestinationDirectoryContents()
    {
        string path = Destination("existing-directory");
        Directory.CreateDirectory(path);
        await File.WriteAllTextAsync(Path.Combine(path, "keep.txt"), "keep");
        await Assert.ThrowsAnyAsync<IOException>(() => _service.ExportAsync(new(Project(), [Item()], 8, OverwriteExisting: true),
            BatchExportFormat.PngZip, path));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(path, "keep.txt")));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task RenderDoesNotMutateProjectDocumentAssetsOrInputItems()
    {
        var project = Project(barcode: true);
        project.FilePath = "/example/template.akelabel";
        var items = new[] { Item(copies: 2), Item(2, 4, 3, "DEMO-002") };
        string before = ModelJson.Serialize(project.Document);
        await _service.ExportAsync(new(project, items, 8), BatchExportFormat.PngZip, Destination());
        Assert.Equal(before, ModelJson.Serialize(project.Document));
        Assert.Equal("/example/template.akelabel", project.FilePath);
        Assert.Equal(0, project.Assets.Count);
        Assert.Equal([2, 3], items.Select(i => i.Copies));
    }

    [Fact]
    public async Task ExactMaximumCopies_AreRepresentedWithoutCreatingThousandsOfImages()
    {
        var result = await _service.ExportAsync(new(Project(), [Item(copies: BatchExportService.MaximumLabels)], 1),
            BatchExportFormat.PngZip, Destination());
        using var archive = ZipFile.OpenRead(Destination());
        Assert.Equal(2, archive.Entries.Count);
        Assert.Equal(BatchExportService.MaximumLabels, result.LabelCount);
        using var manifest = JsonDocument.Parse(archive.GetEntry("manifest.json")!.Open());
        Assert.Equal(10_000, manifest.RootElement.GetProperty("items")[0].GetProperty("lastLabel").GetInt32());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task InvalidCopyCounts_AreRejectedBeforeCreatingOutput(int copies)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ExportAsync(new(Project(), [Item(copies: copies)], 8),
            BatchExportFormat.PngZip, Destination()));
        Assert.Empty(Directory.GetFileSystemEntries(_folder));
    }

    [Fact]
    public async Task TotalAndCallerLimits_AreEnforcedBeforeCreatingOutput()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ExportAsync(new(Project(), [Item(copies: 6000), Item(copies: 6000)], 8),
            BatchExportFormat.Pdf, Destination()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ExportAsync(new(Project(), [Item(copies: 11)], 8, MaxLabels: 10),
            BatchExportFormat.Pdf, Destination()));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.ExportAsync(new(Project(), [Item()], 8, MaxLabels: 10001),
            BatchExportFormat.Pdf, Destination()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ExportAsync(new(Project(), [], 8),
            BatchExportFormat.Pdf, Destination()));
        Assert.Empty(Directory.GetFileSystemEntries(_folder));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(49)]
    public async Task InvalidResolutions_AreRejected(double resolution)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.ExportAsync(new(Project(), [Item()], resolution),
            BatchExportFormat.Pdf, Destination()));
        Assert.Empty(Directory.GetFileSystemEntries(_folder));
    }

    [Fact]
    public async Task OversizedLabelAndOffCanvasElement_AreRejectedBeforeRasterAllocation()
    {
        var project = LabelProject.CreateBlank(1000, 1000);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ExportAsync(new(project, [Item()], 48),
            BatchExportFormat.Pdf, Destination()));
        project = Project();
        project.Document.Elements.Add(new ShapeElement { Width = 2000, Height = 2000, X = 1000 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ExportAsync(new(project, [Item()], 8),
            BatchExportFormat.Pdf, Destination()));
        Assert.Empty(Directory.GetFileSystemEntries(_folder));
    }

    [Fact]
    public async Task ManyIndividuallyValidLayers_AreRejectedWhenTheirCombinedMemoryBudgetIsExceeded()
    {
        var project = Project();
        for (int i = 0; i < 20; i++)
            project.Document.Elements.Add(new ShapeElement { Width = 500, Height = 500 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ExportAsync(new(project, [Item()], 4),
            BatchExportFormat.Pdf, Destination()));
        Assert.Empty(Directory.GetFileSystemEntries(_folder));
    }

    [Theory]
    [InlineData("{SAYAC}")]
    [InlineData("{SAYAC.Inventory:0000}")]
    [InlineData("{SSCC}")]
    public async Task AllocatedIdentifiers_AreRejectedWithoutAllocatingOrExportingPreviewValues(string content)
    {
        var project = Project();
        project.Document.Elements.Add(new TextElement { Text = content });
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ExportAsync(new(project, [Item()], 8),
            BatchExportFormat.PngZip, Destination()));
        Assert.Empty(Directory.GetFileSystemEntries(_folder));
    }

    [Fact]
    public async Task RenderWarnings_AreReturnedOnce_WithoutBeingWrittenIntoManifest()
    {
        var project = Project();
        project.Document.Elements.Add(new ShapeElement { Name = "private-warning-name", X = 39, Y = 1, Width = 5, Height = 5 });
        var result = await _service.ExportAsync(new(project, [Item(), Item(2)], 8), BatchExportFormat.PngZip, Destination());
        Assert.Single(result.Warnings);
        Assert.Contains("private-warning-name", result.Warnings[0]);
        using var archive = ZipFile.OpenRead(Destination());
        using var reader = new StreamReader(archive.GetEntry("manifest.json")!.Open());
        Assert.DoesNotContain("private-warning-name", await reader.ReadToEndAsync());
    }

    private sealed class InlineProgress(Action<BatchExportProgress> callback) : IProgress<BatchExportProgress>
    {
        public void Report(BatchExportProgress value) => callback(value);
    }
}
