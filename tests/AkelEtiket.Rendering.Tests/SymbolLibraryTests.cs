using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Model;
using AkelEtiket.ImageProcessor;
using AkelEtiket.Rendering;
using AkelEtiket.Rendering.Symbols;

namespace AkelEtiket.Rendering.Tests;

/// <summary>Hazır simgeler: her SVG yüklenir ve baskı çözünürlüğünde boş olmayan, kenarlara taşmayan 1-bit katman verir.</summary>
public class SymbolLibraryTests
{
    public static TheoryData<string> Keys()
    {
        var data = new TheoryData<string>();
        foreach (var s in SymbolLibrary.All) data.Add(s.Key);
        return data;
    }

    [Fact]
    public void Library_HasExpectedSymbols_WithUniqueKeysAndTurkishNames()
    {
        Assert.Equal(11, SymbolLibrary.All.Count);
        Assert.Equal(SymbolLibrary.All.Count, SymbolLibrary.All.Select(s => s.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(SymbolLibrary.All, s =>
        {
            Assert.Matches("^[a-z0-9-]+$", s.Key);
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
            // Paneldeki küçük kutuya iki satırda sığsın.
            Assert.InRange(s.ShortName.Length, 3, 16);
            Assert.Contains(s.Category, new[] { "Ürün kullanımı", "Koli işaretleri" });
        });
        Assert.True(SymbolLibrary.TryGet("sembol:GIDA-UYGUN", out var info));
        Assert.Equal("gida-uygun", info.Key);
        Assert.False(SymbolLibrary.TryGet("yesil-nokta", out _));
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void EverySymbol_LoadsAsSvg_AndRendersCrisp1Bit(string key)
    {
        byte[] svg = SymbolLibrary.GetSvg(key);
        Assert.StartsWith("<svg", System.Text.Encoding.UTF8.GetString(svg).TrimStart());
        using (var loaded = ImageLoader.Load(svg, key + ".svg"))
        {
            Assert.True(loaded.Width > 0 && loaded.Height > 0);
        }

        // 20 mm simge, 8 nokta/mm: mürekkep hem var hem de kutuyu doldurmuyor (gerçek bir çizim).
        var asset = SymbolLibrary.CreateAsset(key);
        var assets = new AssetStore();
        assets.AddExisting(asset);
        Assert.True(SymbolLibrary.TryGet(key, out var info));
        var doc = LabelDocument.CreateBlank(30, 30);
        var element = SymbolLibrary.CreateElement(info, asset, 20);
        element.X = 5;
        element.Y = 5;
        doc.Elements.Add(element);
        using var renderer = new LabelRenderer(assets);
        var result = renderer.RenderLabel(doc, new RenderOptions { DotsPerMm = 8 });
        Assert.DoesNotContain(result.Issues, i => i.Severity == RenderIssueSeverity.Error);
        int black = result.Bitmap.CountBlack();
        int box = 160 * 160;
        Assert.InRange(black, box / 50, box * 3 / 4);
        // Mürekkep yalnızca öğe kutusunun içinde.
        var bounds = result.Bitmap.GetInkBounds();
        Assert.NotNull(bounds);
        var (bx, by, bw, bh) = bounds!.Value;
        Assert.True(bx >= 40 - 1 && by >= 40 - 1 && bx + bw <= 200 + 1 && by + bh <= 200 + 1, $"Mürekkep kutu dışında: {bx},{by} {bw}×{bh}");
        Assert.Equal(ImageConversionMethod.Logo, element.Method);
    }

    [Fact]
    public void PreviewPng_IsProduced()
    {
        byte[] png = SymbolLibrary.RenderPreviewPng("gida-uygun");
        Assert.True(png.Length > 100);
        Assert.Equal(0x89, png[0]);
    }
}
