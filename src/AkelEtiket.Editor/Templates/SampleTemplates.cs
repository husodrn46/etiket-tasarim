using AkelEtiket.Core.Model;
using AkelEtiket.Core;
using AkelEtiket.ProjectManager;

namespace AkelEtiket.Editor.Templates;

/// <summary>
/// İlk açılışta şablon klasörüne yazılan örnek şablonlar. Logo verilirse görsele gömülür;
/// böylece 1-bit dönüşüm de örnekte görülür.
/// </summary>
public static class SampleTemplates
{
    public const string ProductLabelName = "Ürün Etiketi 100x100";
    public const string BatchLabelName = "Excel Toplu Ürün Etiketi 100x100";
    public const string ShelfLabelName = "Raf Etiketi 58x40";
    public const string CartonLabelName = "Koli Etiketi GS1-128 100x150";
    public const string SsccCartonLabelName = "SSCC Koli Etiketi 100x100";
    public const string Gs1DataMatrixLabelName = "GS1 DataMatrix Ürün Etiketi 100x100";

    public static IReadOnlyList<(string Name, LabelProject Project)> CreateAll(byte[]? logoPng, CompanyProfile? company = null) =>
    [
        (ProductLabelName, CreateProductLabel(logoPng, company)),
        (BatchLabelName, CreateBatchLabel(logoPng, company)),
        (ShelfLabelName, CreateShelfLabel()),
        (CartonLabelName, CreateCartonLabel(logoPng, company)),
        (SsccCartonLabelName, CreateSsccCartonLabel(logoPng, company)),
        (Gs1DataMatrixLabelName, CreateGs1DataMatrixLabel(logoPng, company)),
    ];

    /// <summary>Sabit içerikli 100 × 100 mm ürün etiketi.</summary>
    public static LabelProject CreateProductLabel(byte[]? logoPng, CompanyProfile? company = null) =>
        ApplyCompany(CreateProductLayout(logoPng, ProductLabelName,
            name: "Örnek Ürün",
            code: "Ürün Kodu: DEMO-001",
            details: "Renk: Beyaz\nÖlçü: Ø 26 cm\nYalnızca örnek veridir",
            price: "160,00 TL",
            barcode: "8691234567890",
            qr: "https://example.com"), company);

    /// <summary>Örnek Excel dosyasının sütunlarına bağlı toplu baskı şablonu.</summary>
    public static LabelProject CreateBatchLabel(byte[]? logoPng, CompanyProfile? company = null)
    {
        var project = CreateProductLayout(logoPng, BatchLabelName,
            name: "{UrunAdi}",
            code: "Ürün Kodu: {UrunKodu}",
            details: "Tarih: {TARIH}\nSıra: {SIRA:0000}",
            price: "{Fiyat:#,##0.00} TL",
            barcode: "{Barkod}",
            qr: "https://example.com/urun/{UrunKodu}");
        project.Document.Description = "samples\\Ornek-Urunler.xlsx dosyasıyla kullanın (UrunKodu, UrunAdi, Barkod, Fiyat, Adet).";
        return ApplyCompany(project, company);
    }

    private static LabelProject CreateProductLayout(byte[]? logoPng, string docName, string name, string code, string details, string price, string barcode, string qr)
    {
        var project = LabelProject.CreateBlank(100, 100);
        var doc = project.Document;
        doc.Name = docName;
        doc.Label.GapMm = 3;

        doc.Elements.Add(new ShapeElement { Name = "Çerçeve", X = 2, Y = 2, Width = 96, Height = 96, StrokeWidth = 0.5, CornerRadius = 3 });
        if (logoPng is { Length: > 0 })
        {
            var asset = project.Assets.Add(logoPng, "firma-logo.png");
            doc.Elements.Add(new ImageElement
            {
                Name = "Logo",
                AssetId = asset.Id,
                X = 22, Y = 4, Width = 56, Height = 20,
                Method = ImageConversionMethod.Auto,
            });
        }
        else
        {
            doc.Elements.Add(new TextElement
            {
                Name = "Firma", Text = "Firma Adınız", X = 6, Y = 6, Width = 88, Height = 14,
                FontSize = 26, Bold = true, AutoShrink = true, HorizontalAlignment = TextHorizontalAlignment.Center, VerticalAlignment = TextVerticalAlignment.Middle,
            });
        }
        doc.Elements.Add(new TextElement
        {
            Name = "Ürün adı", Text = name, X = 5, Y = 26, Width = 90, Height = 9,
            FontSize = 17, Bold = true, HorizontalAlignment = TextHorizontalAlignment.Center, AutoShrink = true, WordWrap = false,
        });
        doc.Elements.Add(new TextElement
        {
            Name = "Ürün kodu", Text = code, X = 5, Y = 35.5, Width = 90, Height = 5.5,
            FontSize = 11, HorizontalAlignment = TextHorizontalAlignment.Center, AutoShrink = true, WordWrap = false,
        });
        doc.Elements.Add(new ShapeElement { Name = "Ayırıcı", Kind = ShapeKind.Line, X = 6, Y = 42.5, Width = 88, Height = 0.5, StrokeWidth = 0.375 });
        doc.Elements.Add(new TextElement
        {
            Name = "Açıklama", Text = details, X = 7, Y = 45, Width = 60, Height = 14,
            FontSize = 10, LineSpacing = 1.05,
        });
        doc.Elements.Add(new BarcodeElement
        {
            Name = "QR", Symbology = BarcodeType.QrCode, Content = qr, X = 71, Y = 44.5, Width = 22, Height = 22,
            ShowText = false, QrErrorCorrection = QrErrorCorrectionLevel.M,
        });
        doc.Elements.Add(new TextElement
        {
            Name = "Fiyat", Text = price, X = 7, Y = 59, Width = 60, Height = 9,
            FontSize = 20, Bold = true, AutoShrink = true, WordWrap = false,
        });
        doc.Elements.Add(new BarcodeElement
        {
            Name = "EAN-13", Symbology = BarcodeType.Ean13, Content = barcode, X = 12, Y = 70, Width = 76, Height = 26,
        });
        return project;
    }

    /// <summary>58 × 40 mm raf etiketi.</summary>
    public static LabelProject CreateShelfLabel()
    {
        var project = LabelProject.CreateBlank(58, 40);
        var doc = project.Document;
        doc.Name = ShelfLabelName;
        doc.Label.GapMm = 2;
        doc.Elements.Add(new TextElement
        {
            Name = "Ürün adı", Text = "Örnek Raf Ürünü", X = 2, Y = 1.5, Width = 54, Height = 9,
            FontSize = 11, Bold = true, AutoShrink = true, HorizontalAlignment = TextHorizontalAlignment.Center,
        });
        doc.Elements.Add(new TextElement
        {
            Name = "Fiyat", Text = "45,90 TL", X = 2, Y = 10.5, Width = 54, Height = 10,
            FontSize = 24, Bold = true, HorizontalAlignment = TextHorizontalAlignment.Center, AutoShrink = true, WordWrap = false,
        });
        doc.Elements.Add(new BarcodeElement
        {
            Name = "EAN-13", Symbology = BarcodeType.Ean13, Content = "869123456789", X = 6, Y = 21.5, Width = 46, Height = 17,
        });
        return project;
    }

    /// <summary>100 × 150 mm koli etiketi: GS1-128 (GTIN + parti + son kullanma) ve ITF-14.</summary>
    public static LabelProject CreateCartonLabel(byte[]? logoPng, CompanyProfile? company = null)
    {
        var project = LabelProject.CreateBlank(100, 150);
        var doc = project.Document;
        doc.Name = CartonLabelName;
        doc.Label.GapMm = 3;
        doc.Elements.Add(new ShapeElement { Name = "Çerçeve", X = 1.5, Y = 1.5, Width = 97, Height = 147, StrokeWidth = 0.5 });
        if (logoPng is { Length: > 0 })
        {
            var asset = project.Assets.Add(logoPng, "firma-logo.png");
            doc.Elements.Add(new ImageElement
            {
                Name = "Logo", AssetId = asset.Id, X = 5, Y = 4, Width = 40, Height = 15,
                Method = ImageConversionMethod.Auto,
            });
        }
        doc.Elements.Add(new TextElement
        {
            Name = "Başlık", Text = "KOLİ ETİKETİ", X = 50, Y = 6, Width = 45, Height = 10,
            FontSize = 16, Bold = true, HorizontalAlignment = TextHorizontalAlignment.Right, VerticalAlignment = TextVerticalAlignment.Middle,
        });
        doc.Elements.Add(new ShapeElement { Name = "Ayırıcı", Kind = ShapeKind.Line, X = 3, Y = 21, Width = 94, Height = 0.5, StrokeWidth = 0.375 });
        doc.Elements.Add(new TextElement
        {
            Name = "İçerik", X = 5, Y = 24, Width = 90, Height = 26, FontSize = 11, LineSpacing = 1.1,
            Text = "Ürün: Örnek Ürün Ø 26 cm\nAdet: 24 adet / koli\nParti: ABC123   Son Kul.: 31.12.2026\nGTIN: 08691234567890",
        });
        doc.Elements.Add(new BarcodeElement
        {
            Name = "GS1-128", Symbology = BarcodeType.Gs1_128, Content = "(01)08691234567890(10)ABC123(17)261231",
            X = 4, Y = 54, Width = 92, Height = 32,
        });
        doc.Elements.Add(new BarcodeElement
        {
            Name = "ITF-14", Symbology = BarcodeType.Itf14, Content = "1869123456789", X = 4, Y = 92, Width = 92, Height = 36,
        });
        doc.Elements.Add(new TextElement
        {
            Name = "Alt bilgi", Text = "Firma Adınız", X = 5, Y = 135, Width = 90, Height = 8,
            FontSize = 10, HorizontalAlignment = TextHorizontalAlignment.Center, VerticalAlignment = TextVerticalAlignment.Middle,
        });
        return ApplyCompany(project, company);
    }

    /// <summary>
    /// 100 × 100 mm GS1 SSCC koli etiketi: üstte içerik bilgisi ve (02)(37)(10) GS1-128, altta (00){SSCC}.
    /// {SSCC} her etikette baskı anında GS1 Firma Profili'nden yeni numara alır (önizlemede 18 sıfır görünür);
    /// {GLN} profildeki Küresel Lokasyon Numarasıdır.
    /// </summary>
    public static LabelProject CreateSsccCartonLabel(byte[]? logoPng, CompanyProfile? company = null)
    {
        var project = LabelProject.CreateBlank(100, 100);
        var doc = project.Document;
        doc.Name = SsccCartonLabelName;
        doc.Description = "GS1 SSCC koli etiketi. Veri sütunları: UrunAdi, GTIN (14 hane, koli içindeki ürünün GTIN'i), " +
                          "KoliIciAdet, Parti. {SSCC} baskı anında GS1 Firma Profili'nden ayrılır (firma öneki doğrulanmış olmalı); " +
                          "önizlemede 000000000000000000 görünür.";
        doc.Label.GapMm = 3;
        doc.Elements.Add(new ShapeElement { Name = "Çerçeve", X = 1.5, Y = 1.5, Width = 97, Height = 97, StrokeWidth = 0.5 });
        if (logoPng is { Length: > 0 })
        {
            var asset = project.Assets.Add(logoPng, "firma-logo.png");
            doc.Elements.Add(new ImageElement
            {
                Name = "Logo", AssetId = asset.Id, X = 4, Y = 3.5, Width = 34, Height = 12,
                Method = ImageConversionMethod.Auto,
            });
        }
        doc.Elements.Add(new TextElement
        {
            Name = "Başlık", Text = "SSCC KOLİ ETİKETİ", X = 40, Y = 3.5, Width = 56, Height = 6.5,
            FontSize = 12, Bold = true, HorizontalAlignment = TextHorizontalAlignment.Right, VerticalAlignment = TextVerticalAlignment.Middle,
        });
        doc.Elements.Add(new TextElement
        {
            Name = "GLN", Text = "GLN: {GLN}", X = 40, Y = 10.5, Width = 56, Height = 5,
            FontSize = 8, HorizontalAlignment = TextHorizontalAlignment.Right, VerticalAlignment = TextVerticalAlignment.Middle,
        });
        doc.Elements.Add(new ShapeElement { Name = "Ayırıcı 1", Kind = ShapeKind.Line, X = 3, Y = 17, Width = 94, Height = 0.5, StrokeWidth = 0.375 });
        doc.Elements.Add(new TextElement
        {
            Name = "İçerik", X = 4, Y = 18.5, Width = 92, Height = 17, FontSize = 9, LineSpacing = 1.1, AutoShrink = true,
            Text = "Ürün: {UrunAdi}\nİçerik GTIN: {GTIN}   Adet: {KoliIciAdet}\nParti: {Parti}",
        });
        doc.Elements.Add(new BarcodeElement
        {
            Name = "GS1-128 içerik", Symbology = BarcodeType.Gs1_128, Content = "(02){GTIN}(37){KoliIciAdet}(10){Parti}",
            X = 4, Y = 36.5, Width = 92, Height = 22,
        });
        doc.Elements.Add(new ShapeElement { Name = "Ayırıcı 2", Kind = ShapeKind.Line, X = 3, Y = 60, Width = 94, Height = 0.5, StrokeWidth = 0.375 });
        doc.Elements.Add(new TextElement
        {
            Name = "SSCC başlığı", Text = "SSCC", X = 4, Y = 61.5, Width = 30, Height = 4, FontSize = 7, Bold = true,
        });
        doc.Elements.Add(new BarcodeElement
        {
            Name = "SSCC", Symbology = BarcodeType.Gs1_128, Content = "(00){SSCC}", X = 4, Y = 66, Width = 92, Height = 30,
        });
        return ApplyCompany(project, company);
    }

    /// <summary>
    /// 100 × 100 mm GS1 DataMatrix ürün etiketi: (01) GTIN, (11) üretim tarihi ve (10) parti tek bir GS1 DataMatrix'te
    /// (başta FNC1; okuyucu "]d2" ile GS1 verisi olarak tanır). Okunur metin barkodun kendisinden gelir, içerik
    /// değişince birlikte değişir.
    /// </summary>
    public static LabelProject CreateGs1DataMatrixLabel(byte[]? logoPng, CompanyProfile? company = null)
    {
        var project = LabelProject.CreateBlank(100, 100);
        var doc = project.Document;
        doc.Name = Gs1DataMatrixLabelName;
        doc.Description = "GS1 DataMatrix ürün etiketi: (01) GTIN, (11) üretim tarihi, (10) parti. İçeriği barkodun özelliklerindeki " +
                          "\"GS1 içerik oluşturucu…\" ile düzenleyin; değerler {Parti} gibi veri alanlarına da bağlanabilir.";
        doc.Label.GapMm = 3;
        doc.Elements.Add(new ShapeElement { Name = "Çerçeve", X = 2, Y = 2, Width = 96, Height = 96, StrokeWidth = 0.5, CornerRadius = 3 });
        if (logoPng is { Length: > 0 })
        {
            var asset = project.Assets.Add(logoPng, "firma-logo.png");
            doc.Elements.Add(new ImageElement
            {
                Name = "Logo", AssetId = asset.Id, X = 22, Y = 4, Width = 56, Height = 20,
                Method = ImageConversionMethod.Auto,
            });
        }
        else
        {
            doc.Elements.Add(new TextElement
            {
                Name = "Firma", Text = "Firma Adınız", X = 6, Y = 6, Width = 88, Height = 14,
                FontSize = 26, Bold = true, AutoShrink = true, HorizontalAlignment = TextHorizontalAlignment.Center, VerticalAlignment = TextVerticalAlignment.Middle,
            });
        }
        doc.Elements.Add(new TextElement
        {
            Name = "Ürün adı", Text = "Örnek Ürün", X = 5, Y = 26, Width = 90, Height = 9,
            FontSize = 17, Bold = true, HorizontalAlignment = TextHorizontalAlignment.Center, AutoShrink = true, WordWrap = false,
        });
        doc.Elements.Add(new TextElement
        {
            Name = "Ürün kodu", Text = "Ürün Kodu: DEMO-001", X = 5, Y = 35.5, Width = 90, Height = 5.5,
            FontSize = 11, HorizontalAlignment = TextHorizontalAlignment.Center, AutoShrink = true, WordWrap = false,
        });
        doc.Elements.Add(new ShapeElement { Name = "Ayırıcı", Kind = ShapeKind.Line, X = 6, Y = 42.5, Width = 88, Height = 0.5, StrokeWidth = 0.375 });
        doc.Elements.Add(new TextElement
        {
            Name = "Bilgiler", X = 5, Y = 44.5, Width = 90, Height = 10, FontSize = 10, LineSpacing = 1.1, AutoShrink = true,
            HorizontalAlignment = TextHorizontalAlignment.Center,
            Text = "Renk: Beyaz   Ölçü: Ø 26 cm\nÜretim: 13.10.2026   Parti: DEMO2610",
        });
        doc.Elements.Add(new BarcodeElement
        {
            Name = "GS1 DataMatrix", Symbology = BarcodeType.Gs1DataMatrix, Content = "(01)08691234567890(11)261013(10)DEMO2610",
            X = 5, Y = 56, Width = 90, Height = 28, ShowText = true,
        });
        doc.Elements.Add(new TextElement
        {
            Name = "Alt bilgi", Text = "Firma Adınız", X = 5, Y = 88, Width = 90, Height = 6,
            FontSize = 9, HorizontalAlignment = TextHorizontalAlignment.Center, VerticalAlignment = TextVerticalAlignment.Middle,
        });
        return ApplyCompany(project, company);
    }
    private static LabelProject ApplyCompany(LabelProject project, CompanyProfile? company)
    {
        if (company is null) return project;
        foreach (var text in project.Document.Elements.OfType<TextElement>())
        {
            if (text.Name == "Firma") text.Text = company.DisplayName;
            if (text.Name == "Alt bilgi")
            {
                text.Text = company.Footer.Length > 0 ? company.Footer : company.DisplayName;
                text.AutoShrink = true;
                text.WordWrap = false;
            }
        }
        if (Uri.TryCreate(company.Website, UriKind.Absolute, out var site) && site.Scheme is "http" or "https")
        {
            foreach (var qr in project.Document.Elements.OfType<BarcodeElement>().Where(b => b.Symbology == BarcodeType.QrCode))
                qr.Content = site.AbsoluteUri;
        }
        // Ürün etiketi altındaki boşluk firma adresine ayrılır; ürün barkodu değişmez.
        if (company.Footer.Length > 0 && (project.Document.Name is ProductLabelName or BatchLabelName))
        {
            foreach (var barcode in project.Document.Elements.OfType<BarcodeElement>().Where(b => b.Symbology == BarcodeType.Ean13))
                barcode.Height = 24;
            project.Document.Elements.Add(new TextElement
            {
                Name = "Firma iletişim", Text = company.Footer, X = 5,
                Y = 95, Width = 90, Height = 2.5, FontSize = 6, AutoShrink = true, WordWrap = false,
                HorizontalAlignment = TextHorizontalAlignment.Center,
            });
        }
        return project;
    }

}
