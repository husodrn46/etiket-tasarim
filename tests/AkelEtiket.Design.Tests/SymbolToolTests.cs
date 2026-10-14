using AkelEtiket.Design.Tools;
using AkelEtiket.Rendering.Symbols;

namespace AkelEtiket.Design.Tests;

/// <summary>Hazır simgeler yapay zekâ araçlarında: list_symbols (salt okunur) ve add_image(builtin="sembol:…").</summary>
public sealed class SymbolToolTests : IDisposable
{
    private readonly DesignTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task ListSymbols_IsReadOnly_AndGivesBuiltinNames()
    {
        Assert.True(_host.Tools.Registry.Tools.Single(t => t.Name == "list_symbols").ReadOnly);

        var result = await _host.Call("list_symbols", new { });
        var symbols = result["symbols"]!.AsArray();
        Assert.Equal(SymbolLibrary.All.Count, symbols.Count);
        Assert.All(symbols, s => Assert.Equal("sembol:" + s!["key"]!.GetValue<string>(), s["builtin"]!.GetValue<string>()));
        Assert.Contains(symbols, s => s!["key"]!.GetValue<string>() == "gida-uygun" && s["name"]!.GetValue<string>() == "Gıda ile temasa uygun");
        Assert.DoesNotContain(symbols, s => s!["key"]!.GetValue<string>().Contains("yesil", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddImage_WithSymbol_IsSharpSquareAndNamed()
    {
        string id = await NewDesign();
        var added = await _host.Call("add_image", new { design_id = id, builtin = "sembol:mikrodalga-yok", width = 12, x = 5, y = 5 });
        string elementId = added["element"]!["id"]!.GetValue<string>();

        var design = await _host.Call("get_design", new { design_id = id });
        var element = design["elements"]!.AsArray().Single(e => e!["id"]!.GetValue<string>() == elementId)!;
        Assert.Equal("Logo", element["method"]!.GetValue<string>());
        Assert.Equal("Mikrodalgada kullanmayınız", element["name"]!.GetValue<string>());
        Assert.Equal(12, element["width"]!.GetValue<double>(), 2);
        Assert.Equal(12, element["height"]!.GetValue<double>(), 2);

        var preview = await _host.CallRaw("render_preview", new { design_id = id });
        Assert.False(preview.IsError, preview.Text);
    }

    [Fact]
    public async Task AddImage_SymbolMethodCanBeOverridden_AndUnknownSymbolIsRejected()
    {
        string id = await NewDesign();
        var added = await _host.Call("add_image", new { design_id = id, builtin = "sembol:dondurucu", method = "threshold", x = 5, y = 5 });
        string elementId = added["element"]!["id"]!.GetValue<string>();
        var design = await _host.Call("get_design", new { design_id = id });
        var element = design["elements"]!.AsArray().Single(e => e!["id"]!.GetValue<string>() == elementId)!;
        Assert.Equal("Threshold", element["method"]!.GetValue<string>());
        Assert.Equal(15, element["width"]!.GetValue<double>(), 2);

        string text = await _host.ExpectError("add_image", new { design_id = id, builtin = "sembol:yesil-nokta", x = 5, y = 5 }, ToolErrorCodes.NotFound);
        Assert.Contains("list_symbols", text);
    }

    private async Task<string> NewDesign() =>
        (await _host.Call("create_design", new { name = "Simge " + Guid.NewGuid().ToString("N")[..6] }))["design_id"]!.GetValue<string>();
}
