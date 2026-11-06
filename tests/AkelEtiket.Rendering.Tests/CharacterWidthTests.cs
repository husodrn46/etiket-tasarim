using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Model;
using AkelEtiket.Core.Serialization;
using AkelEtiket.Rendering;

namespace AkelEtiket.Rendering.Tests;

/// <summary>Karakter genişliği (v4): yazı yatayda daralır/genişler, yükseklik ve dosya uyumluluğu korunur.</summary>
public sealed class CharacterWidthTests
{
    private static (int Width, int Height) InkSize(double characterWidth)
    {
        var doc = LabelDocument.CreateBlank(80, 20);
        doc.Elements.Add(new TextElement
        {
            Text = "LABELHARBOR 2026", X = 2, Y = 2, Width = 76, Height = 12, FontSize = 12, WordWrap = false,
            CharacterWidth = characterWidth,
        });
        using var renderer = new LabelRenderer(new AssetStore());
        var bounds = renderer.RenderLabel(doc, new RenderOptions { DotsPerMm = 8 }).Bitmap.GetInkBounds();
        Assert.NotNull(bounds);
        return (bounds.Value.Width, bounds.Value.Height);
    }

    [Fact]
    public void NarrowsAndWidensTextWithoutChangingItsHeight()
    {
        var normal = InkSize(100);
        var narrow = InkSize(50);
        var wide = InkSize(130);
        Assert.InRange(narrow.Width / (double)normal.Width, 0.45, 0.56);
        Assert.InRange(wide.Width / (double)normal.Width, 1.24, 1.36);
        Assert.Equal(normal.Height, narrow.Height);
        Assert.Equal(normal.Height, wide.Height);
    }

    [Fact]
    public void MeasurementFollowsCharacterWidth()
    {
        var normal = TextMetrics.Measure("LABELHARBOR", "Arial", 18);
        var narrow = TextMetrics.Measure("LABELHARBOR", "Arial", 18, characterWidth: 50);
        Assert.InRange(narrow.WidthMm / normal.WidthMm, 0.45, 0.56);
        Assert.Equal(normal.HeightMm, narrow.HeightMm, 3);
    }

    [Fact]
    public void StoredOnlyWhenChanged_AndRequiresFormat4()
    {
        var doc = LabelDocument.CreateBlank();
        var text = new TextElement { Text = "A" };
        doc.Elements.Add(text);
        string json = ModelJson.Serialize(doc);
        Assert.DoesNotContain("characterWidth", json);
        Assert.Equal(LabelDocument.Version1Format, doc.GetRequiredFormatVersion());

        text.CharacterWidth = 87.46;
        json = ModelJson.Serialize(doc);
        Assert.Contains("\"characterWidth\": 87.5", json);
        Assert.Equal(4, doc.GetRequiredFormatVersion());
        var loaded = ModelJson.DeserializeDocument(json);
        Assert.Equal(87.5, Assert.IsType<TextElement>(Assert.Single(loaded.Elements)).CharacterWidth);

        text.CharacterWidth = 1000;
        Assert.Equal(TextElement.MaxCharacterWidth, text.CharacterWidth);
        text.CharacterWidth = double.NaN;
        Assert.Equal(100, text.CharacterWidth);
    }
}
