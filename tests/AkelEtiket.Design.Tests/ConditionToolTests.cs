using System.Text.Json.Nodes;
using AkelEtiket.Design.Tools;

namespace AkelEtiket.Design.Tests;

/// <summary>Yapay zekâ araçlarıyla koşullu gösterme ve alan ifadeleri.</summary>
public sealed class ConditionToolTests : IDisposable
{
    private readonly DesignTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task UpdateElement_SetsValidatesAndRemovesVisibilityCondition()
    {
        string id = (await _host.Call("create_design", new { name = "Koşul " + Guid.NewGuid().ToString("N")[..6] }))["design_id"]!.GetValue<string>();
        string text = (await _host.Call("add_text", new { design_id = id, text = "İNDİRİM {Indirim}", x = 5, y = 5 }))["element"]!["id"]!.GetValue<string>();

        await _host.Call("update_element", new
        {
            design_id = id,
            element_id = text,
            changes = new JsonObject { ["visible_when"] = new JsonObject { ["field"] = "{Indirim}", ["operator"] = "not_empty" } },
        });
        var design = await _host.Call("get_design", new { design_id = id });
        var condition = design["elements"]![0]!["visibleWhen"]!;
        Assert.Equal("Indirim", condition["field"]!.GetValue<string>());
        Assert.Equal("NotEmpty", condition["operator"]!.GetValue<string>());

        await _host.Call("update_element", new
        {
            design_id = id,
            element_id = text,
            changes = new JsonObject { ["visibleWhen"] = new JsonObject { ["field"] = "Durum", ["operator"] = "is_equal", ["value"] = "Kampanya" } },
        });
        design = await _host.Call("get_design", new { design_id = id });
        Assert.Equal("IsEqual", design["elements"]![0]!["visibleWhen"]!["operator"]!.GetValue<string>());
        Assert.Contains("Durum", design["fields_used"]!.AsArray().Select(n => n!.GetValue<string>()));

        string noField = await _host.ExpectError("update_element", new
        {
            design_id = id,
            element_id = text,
            changes = new JsonObject { ["visibleWhen"] = new JsonObject { ["operator"] = "empty" } },
        }, ToolErrorCodes.InvalidArgument);
        Assert.Contains("field", noField);
        string badOperator = await _host.ExpectError("update_element", new
        {
            design_id = id,
            element_id = text,
            changes = new JsonObject { ["visibleWhen"] = new JsonObject { ["field"] = "Durum", ["operator"] = "büyüktür" } },
        }, ToolErrorCodes.InvalidArgument);
        Assert.Contains("Contains", badOperator);

        await _host.Call("update_element", new { design_id = id, element_id = text, changes = new JsonObject { ["visibleWhen"] = null } });
        design = await _host.Call("get_design", new { design_id = id });
        Assert.Null(design["elements"]![0]!["visibleWhen"]);
    }

    [Fact]
    public void AddTextDescription_DocumentsExpressionSyntax()
    {
        var addText = _host.Tools.Registry.Tools.Single(t => t.Name == "add_text").InputSchema;
        string description = addText["properties"]!["text"]!["description"]!.GetValue<string>();
        Assert.Contains("{TARIH+30}", description);
        Assert.Contains("BUYUK", description);
        Assert.Contains("BASHARF", description);
        var update = _host.Tools.Registry.Tools.Single(t => t.Name == "update_element").InputSchema;
        Assert.Contains("visibleWhen", update["properties"]!["changes"]!["description"]!.GetValue<string>());
    }
}
