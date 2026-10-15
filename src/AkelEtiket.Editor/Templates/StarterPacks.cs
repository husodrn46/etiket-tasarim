using AkelEtiket.Core;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.ProjectManager;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.Templates;

public sealed record StarterPack(string Id, string Name, string Description, string Instructions);

public static class StarterPacks
{
    public static IReadOnlyList<StarterPack> All =>
    [
        new("retail",L10n.T("Mağaza"),L10n.T("Ürün ve raf etiketleri; fiyat ve barkod alanları."),L10n.T("Şablonları ekleyin. Örnek CSV'yi Veri panelinden açın, sütun eşleştirmesini kontrol edin. Fiyat ham metindir; para birimini kendiniz seçin.")),
        new("warehouse",L10n.T("Depo"),L10n.T("Stok ve raf konumu; şirket içi SKU kodları."),L10n.T("SKU, ProductName, Location ve Quantity sütunlarını bağlayın. SKU barkodu iç kodunuzdur; GS1 numarası değildir.")),
        new("shipping",L10n.T("Kargo"),L10n.T("Alıcı, adres ve taşıyıcıdan alınan takip numarası."),L10n.T("Recipient, Address ve TrackingNumber sütunlarını bağlayın. Gerçek takip numarasını taşıyıcınızdan alın; örnek kod gönderi oluşturmaz.")),
        new("small-business",L10n.T("Küçük işletme"),L10n.T("Ürün partisi ve şirket içi demirbaş etiketleri."),L10n.T("Ürün, parti ve tarih alanlarını kendi verinizle değiştirin. Bu paket mevzuata uygunluk veya ürün sertifikası sağlamaz.")),
    ];
    public static IReadOnlyList<(string Name,LabelProject Project)> Create(string id,CompanyProfile company)
    {
        bool en=L10n.Language=="en";
        var result=new List<(string,LabelProject)>();
        LabelProject New(double width,double height,string name)
        {
            var project=LabelProject.CreateBlank(width,height);project.Document.Name=name;
            project.Document.Elements.Add(new TextElement { Name="Company",Text=string.IsNullOrWhiteSpace(company.Name)?(en?"Your Company":"Firma Adınız"):company.Name,X=3,Y=2,Width=width-6,Height=6,FontSize=11,Bold=true,AutoShrink=true });
            return project;
        }
        void Text(LabelProject project,string text,double y,double height=8,double size=12) => project.Document.Elements.Add(new TextElement { Text=text,X=3,Y=y,Width=project.Document.Label.WidthMm-6,Height=height,FontSize=size,AutoShrink=true });
        void Code(LabelProject project,string field,double y,double height) => project.Document.Elements.Add(new BarcodeElement { Symbology=BarcodeType.Code128,Content="{"+field+"}",X=5,Y=y,Width=project.Document.Label.WidthMm-10,Height=height });
        if(id=="retail")
        {
            var product=New(70,50,en?"Retail Product 70x50":"Mağaza Ürün 70x50");Text(product,"{ProductName}",10);Text(product,"{Price}",20,9,18);Code(product,"Barcode",31,16);result.Add((product.Document.Name,product));
            var shelf=New(58,40,en?"Retail Shelf 58x40":"Mağaza Raf 58x40");Text(shelf,"{ProductName}",9,7,10);Text(shelf,"{Price}",17,8,17);Code(shelf,"Barcode",26,11);result.Add((shelf.Document.Name,shelf));
        }
        else if(id=="warehouse")
        {
            var stock=New(100,50,en?"Warehouse Stock 100x50":"Depo Stok 100x50");Text(stock,"{ProductName}",10);Text(stock,(en?"Location: ":"Konum: ")+"{Location}  ·  "+(en?"Quantity: ":"Adet: ")+"{Quantity}",20,6,10);Code(stock,"SKU",28,19);result.Add((stock.Document.Name,stock));
            var location=New(70,35,en?"Warehouse Location 70x35":"Depo Konum 70x35");Text(location,"{Location}",10,7,15);Code(location,"Location",19,13);result.Add((location.Document.Name,location));
        }
        else if(id=="shipping")
        {
            var shipping=InternationalSamples.CreateAll(company.LogoPng,company,L10n.Language)[6];result.Add(shipping);
            var compact=New(100,75,en?"Shipping Compact 100x75":"Kargo Kısa 100x75");Text(compact,"{Recipient}",10);Text(compact,"{Address}",20,24,11);Code(compact,"TrackingNumber",48,23);result.Add((compact.Document.Name,compact));
            // Both package variants use the supplied English CSV schema even in Turkish UI.
            foreach(var (_,project) in result)foreach(var element in project.Document.Elements)
            {
                if(element is TextElement t)t.Text=t.Text.Replace("{Alici}","{Recipient}").Replace("{Adres}","{Address}");
                if(element is BarcodeElement b)b.Content=b.Content.Replace("{TakipNo}","{TrackingNumber}");
            }
        }
        else if(id=="small-business")
        {
            var batch=New(70,50,en?"Business Batch 70x50":"İşletme Parti 70x50");Text(batch,"{ProductName}",10);Text(batch,(en?"Batch: ":"Parti: ")+"{Batch}  {Date}",20,6,10);Code(batch,"SKU",28,19);result.Add((batch.Document.Name,batch));
            result.Add(InternationalSamples.CreateAll(company.LogoPng,company,L10n.Language)[7]);
        }
        else throw new ArgumentException("Unknown starter pack",nameof(id));
        return result;
    }
    public static IFieldValueSource ExampleFields => new RecordFieldSource(
        ["ProductName","Price","Barcode","SKU","Location","Quantity","Recipient","Address","TrackingNumber","Batch","Date"],
        ["Sample Product","12.50","DEMO-001","SKU-001","A-01-02","2","Sample Recipient","Example Street 10\nExample City","DEMO-TRACKING-001","LOT-001","2026-10-15"]);
}
