using AkelEtiket.Gs1;

namespace AkelEtiket.Design.Tools;

public sealed partial class DesignToolset
{
    private void RegisterGs1Tools()
    {
        Registry.Add<NoArgs>("get_gs1_profile", "GS1 firma profili",
            "Yapılandırılan firmanın GS1 bilgilerini salt okunur döndürür: GLN, firma öneki (GCP) ve doğrulanma durumu, SSCC hazır olup olmadığı, " +
            "kayıtlı GTIN'ler ve sık kullanılan uygulama tanımlayıcıları (AI). GLN bir konum/firma numarasıdır: ürün barkodu (GTIN) ya " +
            "da firma öneki olarak kullanılmaz. SSCC için tasarıma GS1-128 barkod olarak (00){SSCC} yazın; numara yalnızca gerçek " +
            "baskıda ayrılır (önizlemede sıfırlar görünür). Yapay zekâ numara ayıramaz.",
            (_, _, _) =>
            {
                var profile = _service.Gs1.Profile;
                var blockers = profile.GetSsccBlockers();
                var data = new JsonObject
                {
                    ["company_name"] = profile.CompanyName,
                    ["gln"] = profile.Gln,
                    ["gln_note"] = "GLN (Küresel Lokasyon Numarası) ürün barkodu veya firma öneki değildir; firma öneki GLN'den türetilmez.",
                    ["gcp"] = profile.GcpPrefix.Length > 0 ? profile.GcpPrefix : null,
                    ["gcp_verified"] = profile.GcpVerified,
                    ["sscc_ready"] = blockers.Count == 0,
                    ["sscc_blockers"] = new JsonArray(blockers.Select(b => (JsonNode)b).ToArray()),
                    ["sscc_usage"] = "GS1-128 içerik: (00){SSCC} — örnek: add_barcode(symbology=gs1_128, content=\"(00){SSCC}\").",
                    ["registered_gtins"] = new JsonArray(profile.RegisteredGtins.Take(200).Select(g => (JsonNode)new JsonObject
                    {
                        ["gtin"] = g.Gtin,
                        ["product_code"] = g.ProductCode,
                        ["description"] = DesignViews.Clip(g.Description, 120),
                        ["packaging_level"] = g.PackagingLevel.ToString(),
                    }).ToArray()),
                    ["common_application_identifiers"] = new JsonArray(Gs1AiCatalog.Common.Select(a => (JsonNode)new JsonObject
                    {
                        ["ai"] = a.Display,
                        ["title"] = a.Title,
                        ["description"] = a.Description,
                        ["format"] = a.Format,
                    }).ToArray()),
                };
                string summary = blockers.Count == 0
                    ? $"GS1 profili: GLN {profile.Gln}, firma öneki {profile.GcpPrefix} (doğrulandı)."
                    : $"GS1 profili: GLN {profile.Gln}. SSCC kapalı: {blockers[0]}";
                return Task.FromResult(ToolRegistry.Ok(summary, data));
            }, readOnly: true);
    }
}
