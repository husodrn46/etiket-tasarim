using AkelEtiket.Core.Model;
using AkelEtiket.Core.Serialization;

namespace AkelEtiket.Core.Tests.Core;

/// <summary>Yan yana rulo ayarı (v4): yalnızca kullanıldığında dosyaya yazılır.</summary>
public sealed class MultiAcrossSetupTests
{
    [Fact]
    public void SingleRowLabelsKeepTheirJsonAndFormat()
    {
        var doc = LabelDocument.CreateBlank(100, 50);
        string json = ModelJson.Serialize(doc);
        Assert.DoesNotContain("columns", json);
        Assert.DoesNotContain("sideMarginMm", json);
        Assert.Equal(LabelDocument.Version1Format, doc.GetRequiredFormatVersion());
        Assert.Equal(100, doc.Label.RowWidthMm);
    }

    [Fact]
    public void MultiAcrossRoundTripsClonesAndRequiresFormat4()
    {
        var doc = LabelDocument.CreateBlank(20, 40);
        doc.Label.Columns = 5;
        doc.Label.SideMarginMm = 2;
        doc.Label.ColumnGapMm = 0.555;

        Assert.Equal(0.56, doc.Label.ColumnGapMm);
        Assert.Equal(4 + 100 + 4 * 0.56, doc.Label.RowWidthMm, 6);
        Assert.Equal(LabelDocument.CurrentFormatVersion, doc.GetRequiredFormatVersion());

        var loaded = ModelJson.DeserializeDocument(ModelJson.Serialize(doc));
        Assert.Equal((5, 2.0, 0.56), (loaded.Label.Columns, loaded.Label.SideMarginMm, loaded.Label.ColumnGapMm));
        var clone = loaded.Label.Clone();
        Assert.Equal((5, 2.0, 0.56), (clone.Columns, clone.SideMarginMm, clone.ColumnGapMm));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(40, LabelSetup.MaxColumns)]
    public void ColumnsAreClamped(int input, int expected) =>
        Assert.Equal(expected, new LabelSetup { Columns = input }.Columns);
}
