using AkelEtiket.Editor.Templates;
using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;

namespace AkelEtiket.SampleData;

/// <summary>samples/ klasörüne örnek .akelabel şablonlarını (isteğe bağlı firma logosuyla) ve baskı çözünürlüğünde PNG'lerini yazar.</summary>
internal static class TemplateSamples
{
    public static void Write(string samplesDirectory, string? logoPath)
    {
        byte[]? logo = logoPath is not null && File.Exists(logoPath) ? File.ReadAllBytes(logoPath) : null;
        var files = new (string File, LabelProject Project)[]
        {
            ("Ornek-100x100-Urun-Etiketi.akelabel", SampleTemplates.CreateProductLabel(logo)),
            ("Ornek-100x100-Excel-Toplu-Etiket.akelabel", SampleTemplates.CreateBatchLabel(logo)),
            ("Ornek-58x40-Raf-Etiketi.akelabel", SampleTemplates.CreateShelfLabel()),
            ("Ornek-100x150-Koli-Etiketi-GS1.akelabel", SampleTemplates.CreateCartonLabel(logo)),
        };
        foreach (var (file, project) in files)
        {
            using var renderer = new LabelRenderer(project.Assets);
            var preview = renderer.RenderLabel(project.Document, new RenderOptions { DotsPerMm = 4 });
            string path = Path.Combine(samplesDirectory, file);
            ProjectFile.Save(project, path, MonoImageExport.EncodePng(preview.Bitmap, 4 * 25.4));
            var full = renderer.RenderLabel(project.Document, new RenderOptions { DotsPerMm = 8 });
            File.WriteAllBytes(Path.ChangeExtension(path, ".png"), MonoImageExport.EncodePng(full.Bitmap, 203.2));
            string problems = full.HasErrors ? "  HATA: " + string.Join("; ", full.Errors) : "";
            Console.WriteLine($"  {file} ({new FileInfo(path).Length:N0} bayt){problems}");
        }
    }
}
