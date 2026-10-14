using AkelEtiket.Rendering.Symbols;

namespace AkelEtiket.Design.Tools;

public sealed partial class DesignToolset
{
    private void RegisterSymbolTools()
    {
        Registry.Add<NoArgs>("list_symbols", "Hazır simgeler",
            "Uygulamayla gelen hazır simgeleri (piktogram) salt okunur listeler: gıdaya uygun, bulaşık makinesi, mikrodalga/fırın " +
            "yasağı, dondurucu, geri dönüşüm ve koli işaretleri (bu taraf yukarı, kırılacak, nemden/güneşten koru, istif sınırı). " +
            "Eklemek için add_image(builtin=\"sembol:<anahtar>\") kullanın; simgeler keskin 1-bit (logo yöntemi) basılır ve " +
            "editörde normal görsel gibi düzenlenir. Tescilli işaretler (Yeşil Nokta, CE vb.) yoktur.",
            (_, _, _) =>
            {
                var data = new JsonObject
                {
                    ["symbols"] = new JsonArray(SymbolLibrary.All.Select(s => (JsonNode)new JsonObject
                    {
                        ["key"] = s.Key,
                        ["builtin"] = SymbolLibrary.BuiltinPrefix + s.Key,
                        ["name"] = s.Name,
                        ["category"] = s.Category,
                        ["description"] = s.Description,
                    }).ToArray()),
                    ["usage"] = "add_image(builtin=\"sembol:gida-uygun\", width=12) — kare simge; yalnız genişlik ya da yükseklik verin.",
                };
                return Task.FromResult(ToolRegistry.Ok($"{SymbolLibrary.All.Count} hazır simge.", data));
            }, readOnly: true);
    }
}
