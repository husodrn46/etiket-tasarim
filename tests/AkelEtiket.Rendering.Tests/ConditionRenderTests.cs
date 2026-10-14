using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.Rendering;

namespace AkelEtiket.Rendering.Tests;

/// <summary>Ortak render motoru koşullu gösterimi ve alan ifadesi sorunlarını uygular (tuval, toplu önizleme, baskı aynı).</summary>
public sealed class ConditionRenderTests : IDisposable
{
    private readonly LabelRenderer _renderer = new(new AssetStore());

    public void Dispose() => _renderer.Dispose();

    private static LabelDocument DiscountLabel()
    {
        var doc = LabelDocument.CreateBlank(60, 30);
        doc.Elements.Add(new ShapeElement
        {
            Kind = ShapeKind.Rectangle,
            Fill = FillStyle.Black,
            X = 5,
            Y = 5,
            Width = 50,
            Height = 20,
            VisibleWhen = new VisibilityCondition("Indirim"),
        });
        return doc;
    }

    private static RenderOptions Print(IFieldValueSource? fields) => new() { DotsPerMm = 8, Fields = fields, Mode = RenderMode.Print };

    [Fact]
    public void Print_ConditionFalse_ElementIsSkipped_WithoutError()
    {
        var result = _renderer.RenderLabel(DiscountLabel(), Print(new RecordFieldSource(["Indirim"], [""])));
        Assert.Equal(0, result.Bitmap.CountBlack());
        var element = Assert.Single(result.Elements);
        Assert.True(element.HiddenByCondition);
        Assert.Null(element.Layer);
        Assert.DoesNotContain(result.Issues, i => i.Severity == RenderIssueSeverity.Error);
    }

    [Fact]
    public void Print_ConditionTrue_ElementIsPrinted()
    {
        var result = _renderer.RenderLabel(DiscountLabel(), Print(new RecordFieldSource(["Indirim"], ["%20"])));
        Assert.True(result.Bitmap.CountBlack() > 1000);
        Assert.False(result.Elements[0].HiddenByCondition);
    }

    [Fact]
    public void Print_ConditionFieldMissing_IsError_SoLabelIsNotPrinted()
    {
        var result = _renderer.RenderLabel(DiscountLabel(), Print(new RecordFieldSource(["Baska"], ["x"])));
        var error = Assert.Single(result.Issues, i => i.Severity == RenderIssueSeverity.Error);
        Assert.Contains("{Indirim}", error.Message);
        Assert.Equal(0, result.Bitmap.CountBlack());
    }

    [Fact]
    public void Design_WithoutData_ElementIsShown_WithInfo()
    {
        var result = _renderer.RenderLabel(DiscountLabel(), new RenderOptions { DotsPerMm = 8 });
        Assert.True(result.Bitmap.CountBlack() > 1000);
        Assert.Contains(result.Issues, i => i.Severity == RenderIssueSeverity.Info && i.Message.Contains("Koşul"));
    }

    [Fact]
    public void RenderElement_HonorsConditionToo()
    {
        var shape = DiscountLabel().Elements[0];
        var hidden = _renderer.RenderElement(shape, new RenderOptions { DotsPerMm = 8, Fields = new RecordFieldSource(["Indirim"], [" "]) });
        Assert.True(hidden.HiddenByCondition);
        Assert.Null(hidden.Layer);
        var shown = _renderer.RenderElement(shape, new RenderOptions { DotsPerMm = 8, Fields = new RecordFieldSource(["Indirim"], ["var"]) });
        Assert.False(shown.HiddenByCondition);
        Assert.NotNull(shown.Layer);
    }

    [Fact]
    public void Print_DateArithmeticOnNonDate_IsError()
    {
        var doc = LabelDocument.CreateBlank(60, 30);
        doc.Elements.Add(new TextElement { Text = "SKT {Kod+30}", X = 2, Y = 2, Width = 50, Height = 10 });
        var result = _renderer.RenderLabel(doc, Print(new RecordFieldSource(["Kod"], ["TST001"])));
        var error = Assert.Single(result.Issues, i => i.Severity == RenderIssueSeverity.Error);
        Assert.Contains("tarih değil", error.Message);
    }
}
