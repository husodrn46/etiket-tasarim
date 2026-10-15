using AkelEtiket.Core;
using AkelEtiket.Core.Model;
using AkelEtiket.ProjectManager;

namespace AkelEtiket.Editor.Templates;

/// <summary>Localizes newly generated bundled examples before company data is applied. Never used on saved designs.</summary>
public static class InternationalSamples
{
    private static readonly string[] Names =
    ["Product Label 100x100", "Excel Batch Product Label 100x100", "Shelf Label 58x40", "Carton Label GS1-128 100x150",
     "SSCC Carton Label 100x100", "GS1 DataMatrix Product Label 100x100", "Shipping Label 100x150", "Asset Label 70x35"];
    private static readonly string[] Descriptions =
    [
        "Fictional demonstration data. Replace product identifiers with your own assigned identifiers before production.",
        "Use samples/sample-products.csv: ProductCode, ProductName, Barcode, Price, Quantity. Price is raw text; add your own currency and format.",
        "Fictional shelf label. Replace price, currency and barcode before production.",
        "GS1 demonstration only. Replace GTIN, batch, expiry date and item code with your own assigned values.",
        "Columns: ProductName, GTIN (14 digits, identifies the contained product), Quantity, Batch. {SSCC} is allocated at print time from a verified company prefix; preview shows 18 zeros.",
        "GS1 DataMatrix example: (01) GTIN, (11) production date, (10) batch. Edit with the GS1 content builder. Demonstration identifiers are not assigned to your company.",
        "Use Recipient, Address and TrackingNumber columns. Obtain the tracking number from your carrier; the app does not create carrier identifiers.",
        "Internal asset label, not a GS1 identifier. Replace DEMIRBAS-001 with your own inventory code.",
    ];
    private static readonly Dictionary<string,string> Texts = new(StringComparer.Ordinal)
    {
        ["Firma Adınız"] = "Your Company", ["Örnek Ürün"] = "Sample Product", ["Ürün Kodu: DEMO-001"] = "Product Code: DEMO-001",
        ["Renk: Beyaz\nÖlçü: Ø 26 cm\nYalnızca örnek veridir"] = "Color: White\nSize: Ø 26 cm\nDemonstration data only",
        ["160,00 TL"] = "160.00", ["45,90 TL"] = "45.90", ["Örnek Raf Ürünü"] = "Sample Shelf Product",
        ["Ürün Kodu: {UrunKodu}"] = "Product Code: {ProductCode}",
        ["Tarih: {TARIH}\nSıra: {SIRA:0000}"] = "Date: {TARIH:yyyy-MM-dd}\nSequence: {SIRA:0000}",
        ["{Fiyat:#,##0.00} TL"] = "{Price}", ["KOLİ ETİKETİ"] = "CARTON LABEL", ["SSCC KOLİ ETİKETİ"] = "SSCC CARTON LABEL",
        ["Ürün: Örnek Ürün Ø 26 cm\nAdet: 24 adet / koli\nParti: ABC123   Son Kul.: 31.12.2026\nGTIN: 08691234567890"] = "Product: Sample Product Ø 26 cm\nQuantity: 24 / carton\nBatch: ABC123   Expiry: 2026-12-31\nGTIN: 08691234567890",
        ["Ürün: {UrunAdi}\nİçerik GTIN: {GTIN}   Adet: {KoliIciAdet}\nParti: {Parti}"] = "Product: {ProductName}\nContent GTIN: {GTIN}   Quantity: {Quantity}\nBatch: {Batch}",
        ["Renk: Beyaz   Ölçü: Ø 26 cm\nÜretim: 13.10.2026   Parti: DEMO2610"] = "Color: White   Size: Ø 26 cm\nProduced: 2026-10-13   Batch: DEMO2610",
        ["Gönderici adresi"] = "Sender address", ["Örnek demirbaş"] = "Sample asset",
    };
    private static readonly Dictionary<string,string> ElementNames = new(StringComparer.Ordinal)
    {
        ["Firma"] = "Company", ["Alt bilgi"] = "Footer", ["Ürün adı"] = "Product name", ["Ürün kodu"] = "Product code",
        ["Çerçeve"] = "Border", ["Ayırıcı"] = "Divider", ["Ayırıcı 1"] = "Divider 1", ["Ayırıcı 2"] = "Divider 2",
        ["Açıklama"] = "Description", ["Fiyat"] = "Price", ["Başlık"] = "Title", ["İçerik"] = "Contents",
        ["GS1-128 içerik"] = "GS1-128 contents", ["SSCC başlığı"] = "SSCC heading", ["Bilgiler"] = "Details",
        ["Gönderici"] = "Sender", ["Alıcı"] = "Recipient", ["Teslimat adresi"] = "Delivery address", ["Takip numarası"] = "Tracking number",
        ["Demirbaş"] = "Asset", ["Envanter kodu"] = "Inventory code",
    };
    private static string Fields(string value) => value.Replace("{UrunAdi}", "{ProductName}").Replace("{UrunKodu}", "{ProductCode}")
        .Replace("{Barkod}", "{Barcode}").Replace("{KoliIciAdet}", "{Quantity}").Replace("{Parti}", "{Batch}")
        .Replace("{Alici}", "{Recipient}").Replace("{Adres}", "{Address}").Replace("{TakipNo}", "{TrackingNumber}")
        .Replace("/urun/", "/product/");

    public static IReadOnlyList<(string Name, LabelProject Project)> CreateAll(byte[]? logoPng, CompanyProfile? company, string language)
    {
        if (language != "en") return SampleTemplates.CreateAll(logoPng, company);
        var examples = SampleTemplates.CreateAll(logoPng, null);
        var result = new List<(string, LabelProject)>();
        for (int i = 0; i < examples.Count; i++)
        {
            var project = examples[i].Project;
            project.Document.Name = Names[i]; project.Document.Description = Descriptions[i];
            foreach (var element in project.Document.Elements)
            {
                if (ElementNames.TryGetValue(element.Name, out var localizedName)) element.Name = localizedName;
                if (element is TextElement text) text.Text = Fields(Texts.GetValueOrDefault(text.Text, text.Text));
                if (element is BarcodeElement barcode) barcode.Content = Fields(barcode.Content);
            }
            SampleTemplates.ApplyCompany(project, company, english: true);
            result.Add((Names[i], project));
        }
        return result;
    }

    public static LabelProject CreateForSize(double width, double height, CompanyProfile company, string language)
    {
        if (language != "en") return SampleTemplates.CreateForSize(width, height, company);
        int index = (width, height) switch { (100,100) => 0, (58,40) => 2, (70,35) => 7, (100,150) => 6, _ => -1 };
        if (index >= 0) return CreateAll(company.LogoPng, company, language)[index].Project;
        var project = LabelProject.CreateBlank(width, height); project.Document.Name = "My first label"; return project;
    }
}
