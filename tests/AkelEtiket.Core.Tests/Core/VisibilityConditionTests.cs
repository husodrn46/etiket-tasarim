using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.Core.Serialization;

namespace AkelEtiket.Core.Tests.Core;

/// <summary>Koşullu gösterme: işleçler, Türkçe büyük/küçük harf, eksik alan, model ve JSON.</summary>
public sealed class VisibilityConditionTests
{
    private static RecordFieldSource Row(string value) => new(["Indirim"], [value]);

    [Theory]
    [InlineData(ConditionOperator.NotEmpty, "%20", null, true)]
    [InlineData(ConditionOperator.NotEmpty, "   ", null, false)]
    [InlineData(ConditionOperator.NotEmpty, "", null, false)]
    [InlineData(ConditionOperator.Empty, "", null, true)]
    [InlineData(ConditionOperator.Empty, " x ", null, false)]
    [InlineData(ConditionOperator.IsEqual, "İSTANBUL", "istanbul", true)]
    [InlineData(ConditionOperator.IsEqual, " Evet ", "evet", true)]
    [InlineData(ConditionOperator.IsEqual, "IŞIK", "ışık", true)]
    [InlineData(ConditionOperator.IsEqual, "Evet", "Hayır", false)]
    [InlineData(ConditionOperator.NotEqual, "Evet", "Hayır", true)]
    [InlineData(ConditionOperator.NotEqual, "EVET", "evet", false)]
    [InlineData(ConditionOperator.Contains, "Melamin Servis Tabağı", "TABAĞI", true)]
    [InlineData(ConditionOperator.Contains, "Melamin Kase", "tabak", false)]
    [InlineData(ConditionOperator.Contains, "her şey", "", true)]
    public void Operators_AreTurkishCaseInsensitive(ConditionOperator op, string value, string? expected, bool show)
    {
        var condition = new VisibilityCondition("Indirim", op, expected);
        Assert.Equal(show ? ConditionResult.Show : ConditionResult.Hide, VisibilityEvaluator.Evaluate(condition, Row(value)));
    }

    [Fact]
    public void MissingFieldOrNoData_CannotBeEvaluated()
    {
        var condition = new VisibilityCondition("Indirim");
        Assert.Equal(ConditionResult.FieldMissing, VisibilityEvaluator.Evaluate(condition, null));
        Assert.Equal(ConditionResult.FieldMissing, VisibilityEvaluator.Evaluate(condition, new RecordFieldSource(["Baska"], ["1"])));
        Assert.Equal(ConditionResult.Show, VisibilityEvaluator.Evaluate(null, null));
        Assert.Equal(ConditionResult.Show, VisibilityEvaluator.Evaluate(new VisibilityCondition("  "), Row("")));
    }

    [Fact]
    public void FieldNamesMatchLikePlaceholders()
    {
        var source = new RecordFieldSource(["İndirim Oranı"], ["%10"]);
        Assert.Equal(ConditionResult.Show, VisibilityEvaluator.Evaluate(new VisibilityCondition("{IndirimOrani}"), source));
        Assert.Equal(ConditionResult.Show, VisibilityEvaluator.Evaluate(new VisibilityCondition("TARIH"), new BuiltInFieldSource(1, DateTime.Now)));
    }

    [Fact]
    public void ElementProperty_CleansFieldAndRaisesPlaceholderFlag()
    {
        var shape = new ShapeElement();
        Assert.False(shape.HasFieldPlaceholders);
        var changed = new List<string?>();
        shape.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        shape.VisibleWhen = new VisibilityCondition(" {Indirim} ");
        Assert.Equal("Indirim", shape.VisibleWhen!.Field);
        Assert.True(shape.HasFieldPlaceholders);
        Assert.Contains(nameof(LabelElement.VisibleWhen), changed);
        Assert.Contains(nameof(LabelElement.HasFieldPlaceholders), changed);

        shape.VisibleWhen = new VisibilityCondition("   ");
        Assert.Null(shape.VisibleWhen);
        Assert.False(shape.HasFieldPlaceholders);

        var text = new TextElement { Text = "Sabit", VisibleWhen = new VisibilityCondition("Indirim") };
        Assert.True(text.HasFieldPlaceholders);
    }

    [Fact]
    public void FormatVersion2_OnlyWhenConditionUsed_AndJsonRoundTrips()
    {
        var doc = LabelDocument.CreateBlank();
        var text = new TextElement { Text = "İNDİRİM" };
        doc.Elements.Add(text);
        Assert.Equal(1, doc.GetRequiredFormatVersion());
        Assert.DoesNotContain("visibleWhen", ModelJson.Serialize(doc));

        text.VisibleWhen = new VisibilityCondition("Indirim", ConditionOperator.IsEqual, "Evet");
        Assert.Equal(2, doc.GetRequiredFormatVersion());
        string json = ModelJson.Serialize(doc);
        Assert.Contains("\"visibleWhen\"", json);
        Assert.Contains("\"IsEqual\"", json);

        var loaded = ModelJson.DeserializeDocument(json);
        Assert.Equal(new VisibilityCondition("Indirim", ConditionOperator.IsEqual, "Evet"), loaded.Elements[0].VisibleWhen);
    }

    [Fact]
    public void Json_MissingOperatorDefaultsToNotEmpty()
    {
        const string json = """[{"type":"text","id":"aaaaaaaaaaaa","text":"x","visibleWhen":{"field":" {Indirim} "}}]""";
        var element = Assert.Single(ModelJson.DeserializeElements(json));
        Assert.Equal(new VisibilityCondition("Indirim"), element.VisibleWhen);
        Assert.Equal(ConditionOperator.NotEmpty, element.VisibleWhen!.Operator);
    }

    [Theory]
    [InlineData(ConditionOperator.NotEmpty, null, "{Indirim} doluysa")]
    [InlineData(ConditionOperator.Empty, null, "{Indirim} boşsa")]
    [InlineData(ConditionOperator.IsEqual, "Evet", "{Indirim} = \"Evet\" ise")]
    [InlineData(ConditionOperator.Contains, "kase", "{Indirim} \"kase\" içeriyorsa")]
    public void Describe_IsTurkish(ConditionOperator op, string? value, string expected) =>
        Assert.Equal(expected, new VisibilityCondition("Indirim", op, value).Describe());
}
