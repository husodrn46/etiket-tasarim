using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Model;

namespace AkelEtiket.ProjectManager;

/// <summary>Açık bir etiket projesi: tasarım belgesi + gömülü görseller + dosya yolu.</summary>
public sealed class LabelProject
{
    public LabelProject(LabelDocument document, AssetStore? assets = null, string? filePath = null)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Assets = assets ?? new AssetStore();
        FilePath = filePath;
    }

    public LabelDocument Document { get; }
    public AssetStore Assets { get; }

    /// <summary>Kaydedildiği .akelabel dosyası (henüz kaydedilmediyse null).</summary>
    public string? FilePath { get; set; }

    public string DisplayName => FilePath is null ? Document.Name : Path.GetFileNameWithoutExtension(FilePath);

    /// <summary>Belgede başvurulup varlık deposunda bulunmayan görseller.</summary>
    public IReadOnlyList<string> GetMissingAssetIds() =>
        Document.GetReferencedAssetIds().Where(id => !Assets.Contains(id)).ToList();

    public static LabelProject CreateBlank(double widthMm = 100, double heightMm = 100) =>
        new(LabelDocument.CreateBlank(widthMm, heightMm));
}

/// <summary>Proje dosyası okunamadığında/yazılamadığında Türkçe açıklamalı hata.</summary>
public sealed class ProjectFileException : Exception
{
    public ProjectFileException(string message, Exception? inner = null) : base(message, inner) { }
}
