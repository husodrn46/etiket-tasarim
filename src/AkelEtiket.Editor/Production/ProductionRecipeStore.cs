using System.Text.Json;
using AkelEtiket.DataImport;
using AkelEtiket.Editor.Services;
using AkelEtiket.Core;
using AkelEtiket.Editor.Production.Export;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.Production;

public sealed record ProductionRecipe
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "";
    public string ProjectPath { get; init; } = "";
    public string? DataPath { get; init; }
    public string? ImportProfileId { get; init; }
    public ProductionImportSettings? Import { get; init; }
    public bool UseData { get; init; }
    public int Copies { get; init; } = 1;
    public int StartNumber { get; init; } = 1;
    public BatchExportFormat Format { get; init; }
    public long? DataLength { get; init; }
    public DateTime? DataWriteUtc { get; init; }
    public List<ProductionRecipeRow> Rows { get; init; } = [];
    public override string ToString() => Name;
}
public sealed record ProductionImportSettings(string? SheetName, int HeaderRow, string? EncodingName, char? Delimiter)
{
    public ImportOptions ToOptions() => new() { SheetName = SheetName, HeaderRow = HeaderRow,
        Encoding = ImportChoices.ResolveEncoding(EncodingName), Delimiter = Delimiter };
}
public sealed record ProductionRecipeRow(int RowNumber, int Copies);

/// <summary>Private local recipes, never label values or credentials. Corrupt files are preserved.</summary>
public sealed class ProductionRecipeStore(string? filePath = null)
{
    public string FilePath { get; } = filePath ?? Path.Combine(AppIdentity.RoamingFolder, "uretim", "is-tarifleri.json");
    private sealed record RecipeFile(int Version, List<ProductionRecipe> Recipes);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public IReadOnlyList<ProductionRecipe> Load()
    {
        if (!File.Exists(FilePath)) return [];
        if (new FileInfo(FilePath).Length > 8 * 1024 * 1024) throw InvalidFile();
        try
        {
            var file = JsonSerializer.Deserialize<RecipeFile>(File.ReadAllText(FilePath), Json);
            if (file is null || file.Version != 1 || file.Recipes is null || file.Recipes.Count > 200) throw InvalidFile();
            foreach (var recipe in file.Recipes) Validate(recipe);
            if (file.Recipes.Select(r => r.Id).Distinct().Count() != file.Recipes.Count) throw InvalidFile();
            return file.Recipes.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch (JsonException ex) { throw new IOException(L10n.T("İş tarifleri dosyası okunamadı; mevcut dosya korundu."), ex); }
    }

    public void Save(ProductionRecipe recipe) { Validate(recipe); Update(list => { list.RemoveAll(r => r.Id == recipe.Id); list.Add(recipe); }); }
    public void Delete(string id) => Update(list => list.RemoveAll(r => r.Id == id));
    private void Update(Action<List<ProductionRecipe>> change)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(FilePath))!;
        Directory.CreateDirectory(directory);
        using var gate = new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var list = Load().ToList(); change(list);
        if (list.Count > 200) throw new IOException(L10n.T("En fazla 200 yerel iş tarifi kaydedilebilir."));
        string temp = Path.Combine(directory, ".recipes-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new RecipeFile(1, list), Json);
                if (stream.Length > 8 * 1024 * 1024) throw new IOException(L10n.T("İş tarifleri 8 MiB sınırını aşıyor. Önce kullanılmayan tarifleri kaldırın."));
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, FilePath, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static IOException InvalidFile() => new(L10n.T("İş tarifleri dosyası geçersiz veya daha yeni bir sürüme ait; mevcut dosya korundu."));
    private static void Validate(ProductionRecipe? r)
    {
        if (r is null || !Guid.TryParseExact(r.Id, "N", out _) || string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 100 ||
            string.IsNullOrWhiteSpace(r.ProjectPath) || r.ProjectPath.Length > 4096 || !Path.IsPathFullyQualified(r.ProjectPath) ||
            r.Copies is < 1 or > ProductionSnapshot.MaximumLabels || r.StartNumber is < 1 or > int.MaxValue - ProductionSnapshot.MaximumLabels ||
            !Enum.IsDefined(r.Format) || r.Rows is null || r.Rows.Count > ProductionSnapshot.MaximumLabels ||
            r.Rows.Any(row => row is null || row.RowNumber < 1 || row.Copies is < 1 or > 9999) ||
            r.Rows.Select(row => row.RowNumber).Distinct().Count() != r.Rows.Count ||
            r.Rows.Sum(row => (long)row.Copies) > ProductionSnapshot.MaximumLabels ||
            (r.UseData && (string.IsNullOrWhiteSpace(r.DataPath) || !Path.IsPathFullyQualified(r.DataPath))) ||
            r.DataPath?.Length > 4096 || r.ImportProfileId?.Length > 100 ||
            (r.UseData && r.Import is null) ||
            (r.Import is { } import && (import.SheetName?.Length > 100 || import.EncodingName?.Length > 100 ||
             import.HeaderRow is < 1 or > 1048576 || import.Delimiter is '\r' or '\n' or '\0' or '"')))
            throw InvalidFile();
    }
}
