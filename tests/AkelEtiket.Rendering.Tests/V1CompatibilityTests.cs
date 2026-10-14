using System.Security.Cryptography;
using AkelEtiket.Core.Data;
using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;

namespace AkelEtiket.Rendering.Tests;

/// <summary>
/// Nötr v1 biçimindeki dosyalar kaynak alınan 2bc1894 motoruyla aynı çıktıyı üretmeli.
/// Golden özetleri değiştirilmemiş kaynak motorundan alınmıştır. Firma verisi ve işletim sistemine
/// göre değişen fontlar bu fixture'larda bulunmaz; yeni ürün örnekleri bu sabit fixture'lardan ayrıdır.
/// </summary>
public sealed class V1CompatibilityTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AkelEtiket.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
        }
    }

    public static IEnumerable<object[]> Golden() =>
        File.ReadAllLines(Path.Combine(RepoRoot, "tests", "AkelEtiket.Rendering.Tests", "Golden", "v1-ornekler.txt"))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split('|'))
            .Select(p => new object[] { p[0], int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]), p[4] });

    [Theory]
    [MemberData(nameof(Golden))]
    public void V1Samples_RenderBitIdentical(string file, int width, int height, int black, string sha256)
    {
        var project = ProjectFile.Load(Path.Combine(RepoRoot, "tests", "AkelEtiket.Rendering.Tests", "Fixtures", "v1", file)).Project;
        using var renderer = new LabelRenderer(project.Assets);
        var result = renderer.RenderLabel(project.Document, new RenderOptions
        {
            DotsPerMm = 8,
            Fields = new BuiltInFieldSource(1, new DateTime(2026, 1, 15, 10, 30, 0)),
            Mode = RenderMode.Design,
        });
        Assert.Equal(width, result.Bitmap.Width);
        Assert.Equal(height, result.Bitmap.Height);
        Assert.Equal(black, result.Bitmap.CountBlack());
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(result.Bitmap.Data)));
    }
}
