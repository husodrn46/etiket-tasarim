using System.ComponentModel;
using System.Text.Json.Serialization;

namespace AkelEtiket.Design.Tools;

// Araç girdileri. Adlar JSON'da snake_case olur (ör. font_size_pt). Şemalar bu sınıflardan üretilir; açıklamalar
// modelin aracı doğru kullanması içindir. Bütün ölçüler milimetre (mm), yazı boyutu punto (pt) cinsindendir.

/// <summary>Etiket üzerinde hazır konum.</summary>
public enum Placement
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    Center,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

public enum TextAlign
{
    Left,
    Center,
    Right,
}

public enum TextVAlign
{
    Top,
    Middle,
    Bottom,
}

public enum AlignTo
{
    Auto,
    Selection,
    Label,
}

public enum AlignEdge
{
    Left,
    CenterHorizontal,
    Right,
    Top,
    CenterVertical,
    Bottom,
}

public enum Direction
{
    Horizontal,
    Vertical,
}

public enum LayerAction
{
    BringToFront,
    BringForward,
    SendBackward,
    SendToBack,
}

public enum ResizeAnchor
{
    TopLeft,
    Center,
    TopRight,
    BottomLeft,
    BottomRight,
}

public enum ProductBindMode
{
    /// <summary>Alanlar tasarımda kalır; önizleme bu ürünün değerleriyle gösterilir.</summary>
    Preview,

    /// <summary>{Alan} yer tutucuları bu ürünün değerleriyle sabit metne çevrilir.</summary>
    Fill,
}

public enum FileListKind
{
    Images,
    Data,
    Designs,
}

/// <summary>Değiştiren araçların ortak alanları.</summary>
public abstract class DesignMutationArgs
{
    [Description("Tasarım kimliği (list_designs/get_design çıktısındaki design_id).")]
    public required string DesignId { get; init; }

    [Description("İsteğe bağlı: get_design ile okunan sürüm. Verilirse ve tasarım bu arada değiştiyse VERSION_CONFLICT döner.")]
    public string? ExpectedVersion { get; init; }
}

/// <summary>Konum: ya x/y ya da placement verilir.</summary>
public abstract class PlacedElementArgs : DesignMutationArgs
{
    [Description("Sol üst köşenin soldan uzaklığı (mm). placement verilmezse gerekli.")]
    public double? X { get; init; }

    [Description("Sol üst köşenin üstten uzaklığı (mm). placement verilmezse gerekli.")]
    public double? Y { get; init; }

    [Description("Genişlik (mm). Verilmezse içeriğe göre hesaplanır.")]
    public double? Width { get; init; }

    [Description("Yükseklik (mm). Verilmezse içeriğe göre hesaplanır.")]
    public double? Height { get; init; }

    [Description("Hazır konum (x/y yerine): top_left, top_center, top_right, middle_left, center, middle_right, bottom_left, bottom_center, bottom_right.")]
    public Placement? Placement { get; init; }

    [Description("placement kullanılırken etiket kenarına bırakılacak boşluk (mm). Varsayılan 3.")]
    public double? MarginMm { get; init; }

    [Description("Saat yönünde döndürme açısı (derece). Barkodlar 90°'nin katlarına yuvarlanır.")]
    public double? Rotation { get; init; }

    [Description("Katmanlar panelinde görünen ad (ör. \"Ürün adı\"). Öğeleri sonra bulmayı kolaylaştırır.")]
    public string? Name { get; init; }
}

public sealed class AddTextArgs : PlacedElementArgs
{
    [Description("Metin. Veri alanı için {SütunAdı} yazılabilir (ör. \"{UrunAdi}\"). Satır sonu için \\n. " +
                 "Biçimler: {Fiyat:0.00}; {TARIH} (bugün), {SAAT}, {SIRA:0000}; tarih hesabı {TARIH+30}, {TARIH-7:dd.MM.yyyy}, " +
                 "{TARIH+2 hafta}, {TARIH+6ay}, {TARIH+1yıl} (tarih içeren sütunlarda da: {UretimTarihi+365}); Türkçe yazı dönüşümü " +
                 "{UrunAdi:BUYUK}, {Renk:KUCUK}, {UrunAdi:BASHARF}; birlikte: {TARIH:dd MMMM yyyy|BUYUK}. Kalıcı seri numarası " +
                 "{SAYAC:000000} (her basılan etikette artar; adlı sayaç {SAYAC.Koli}); GS1 firma alanları {GLN}, {GCP}, {SSCC}.")]
    public required string Text { get; init; }

    [Description("Yazı boyutu (punto). Varsayılan 12. 203 dpi baskıda 6 pt altı zor okunur.")]
    public double? FontSizePt { get; init; }

    [Description("Yazı tipi ailesi (ör. Arial, Segoe UI). Yüklü değilse Arial kullanılır.")]
    public string? FontFamily { get; init; }

    public bool? Bold { get; init; }

    public bool? Italic { get; init; }

    public bool? Underline { get; init; }

    [Description("Yatay hizalama: left, center, right.")]
    public TextAlign? Align { get; init; }

    [Description("Dikey hizalama: top, middle, bottom.")]
    public TextVAlign? VerticalAlign { get; init; }

    [Description("Kutu genişliğinde satır kaydır (varsayılan true).")]
    public bool? WordWrap { get; init; }

    [Description("Sığmazsa yazıyı küçült. Veri alanı içeren metinlerde varsayılan true.")]
    public bool? AutoShrink { get; init; }

    [Description("Beyaz yazı (siyah zemin üzerinde kullanmak için).")]
    public bool? WhiteText { get; init; }

    [Description("Kutu zemini: none, black, white.")]
    public FillStyle? Background { get; init; }
}

public sealed class AddBarcodeArgs : PlacedElementArgs
{
    [Description("Barkod türü: ean13, ean8, upc_a, code128, gs1_128, code39, itf, itf14, qr_code, data_matrix, pdf417, " +
                 "gs1_data_matrix, gs1_qr_code. GS1 türleri (gs1_128, gs1_data_matrix, gs1_qr_code) düz Code 128 / Data Matrix / QR " +
                 "ile aynı değildir: içerik (01)… gibi uygulama tanımlayıcılarıyla yazılır, sembolde başta FNC1 bulunur.")]
    public required BarcodeType Symbology { get; init; }

    [Description("Barkod içeriği veya {SütunAdı}. EAN-13 için 12 hane verilirse kontrol basamağı hesaplanır. GS1 türleri örneği " +
                 "(GS1-128, GS1 DataMatrix, GS1 QR): (01)08691234567890(17)281231(10)LOT1; son kullanma tarihi hesapla " +
                 "(17){TARIH+730:yyMMdd}; seri numarası (21){SAYAC:000000}; koli SSCC (00){SSCC}.")]
    public required string Content { get; init; }

    [Description("1D barkodun altında okunabilir metin (varsayılan true).")]
    public bool? ShowText { get; init; }

    [Description("QR / GS1 QR hata düzeltme: l, m, q, h (varsayılan m).")]
    public QrErrorCorrectionLevel? QrErrorCorrection { get; init; }

    [Description("Standart beyaz kenar boşluğunu (sessiz bölge) kutuya dahil et (varsayılan true; okunabilirlik için önerilir).")]
    public bool? QuietZone { get; init; }
}

public sealed class AddImageArgs : PlacedElementArgs
{
    [Description("Görsel dosyası: izinli klasörlerden biri (list_files images ile bulunur), ör. \"Şablonlar/logo.png\" veya tam yol. builtin ya da asset_id verilmezse gerekli.")]
    public string? Path { get; init; }

    [Description("Yerleşik görsel: \"firma-logo\" (ayarlarda seçilen firma logosu) ya da hazır simge \"sembol:<anahtar>\" (ör. \"sembol:gida-uygun\"; liste: list_symbols). Simgeler varsayılan olarak logo yöntemiyle (keskin) basılır.")]
    public string? Builtin { get; init; }

    [Description("Tasarımda zaten bulunan bir görselin asset_id değeri (get_design çıktısındaki assetId).")]
    public string? AssetId { get; init; }

    [Description("Yerleşim: contain (oranı koru, tamamı görünsün), cover (doldur, taşanı kırp), stretch.")]
    public ImageFitMode? FitMode { get; init; }

    [Description("1-bit dönüşüm yöntemi: auto, threshold, otsu, floyd_steinberg, ordered, photo, logo.")]
    public ImageConversionMethod? Method { get; init; }

    [Description("Satıra göre değişen görsel: dosya adını içeren veri sütunu (ör. \"Gorsel\").")]
    public string? SourceField { get; init; }
}

public sealed class AddShapeArgs : PlacedElementArgs
{
    [Description("Şekil: rectangle, ellipse, line.")]
    public required ShapeKind Kind { get; init; }

    [Description("Çizgi/kenar kalınlığı (mm). Varsayılan 0,5.")]
    public double? StrokeWidthMm { get; init; }

    [Description("Dolgu: none, black, white.")]
    public FillStyle? Fill { get; init; }

    [Description("Dikdörtgen köşe yarıçapı (mm).")]
    public double? CornerRadiusMm { get; init; }

    [Description("Çizgi yönü: horizontal, vertical, diagonal_down, diagonal_up.")]
    public LineOrientation? LineOrientation { get; init; }

    [Description("Beyaz kenar çizgisi (siyah zemin üzerinde).")]
    public bool? WhiteStroke { get; init; }
}

public sealed class UpdateElementArgs : DesignMutationArgs
{
    [Description("Öğe kimliği (get_design → elements[].id).")]
    public required string ElementId { get; init; }

    [Description("Değişecek özellikler, get_design çıktısındaki adlarla (camelCase): ör. {\"text\":\"Yeni\",\"fontSize\":16,\"bold\":true}, " +
                 "{\"content\":\"8691234567890\"}, {\"x\":10,\"y\":20}, {\"visible\":false}, {\"method\":\"Logo\",\"threshold\":140}. " +
                 "Koşullu gösterme: {\"visibleWhen\":{\"field\":\"Indirim\",\"operator\":\"NotEmpty\"}} (işleçler: NotEmpty, Empty, " +
                 "IsEqual, NotEqual, Contains; son üçü için \"value\"); kaldırmak için {\"visibleWhen\":null}. " +
                 "id ve type değiştirilemez; kilitli öğeler değiştirilemez.")]
    public required JsonObject Changes { get; init; }
}

public sealed class MoveElementArgs : DesignMutationArgs
{
    [Description("Taşınacak öğe kimlikleri (gruplu öğe verilirse bütün grup birlikte taşınır).")]
    public required List<string> ElementIds { get; init; }

    [Description("Göreli taşıma: sağa (+) / sola (-) mm.")]
    public double? Dx { get; init; }

    [Description("Göreli taşıma: aşağı (+) / yukarı (-) mm.")]
    public double? Dy { get; init; }

    [Description("Mutlak konum: ilk öğenin (veya grubun) sol kenarı (mm). Diğer öğeler aynı miktarda kayar.")]
    public double? X { get; init; }

    [Description("Mutlak konum: ilk öğenin (veya grubun) üst kenarı (mm).")]
    public double? Y { get; init; }
}

public sealed class ResizeElementArgs : DesignMutationArgs
{
    [Description("Öğe kimliği. group_id verilirse gerek yok.")]
    public string? ElementId { get; init; }

    [Description("Bütün grubu orantılı boyutlandır (grup kimliği).")]
    public string? GroupId { get; init; }

    [Description("Yeni genişlik (mm).")]
    public double? Width { get; init; }

    [Description("Yeni yükseklik (mm).")]
    public double? Height { get; init; }

    [Description("Ölçek katsayısı (ör. 1.2 = %20 büyüt). width/height yerine.")]
    public double? Scale { get; init; }

    [Description("Sabit kalacak nokta: top_left (varsayılan), center, top_right, bottom_left, bottom_right.")]
    public ResizeAnchor? Anchor { get; init; }

    [Description("Yalnız width veya height verildiğinde oranı koru (varsayılan: görsel ve 2D barkodlarda true).")]
    public bool? KeepAspect { get; init; }

    [Description("Metin öğelerinde yazı boyutunu da aynı oranda değiştir (scale ile; varsayılan true).")]
    public bool? ScaleFont { get; init; }
}

public sealed class ElementIdsArgs : DesignMutationArgs
{
    [Description("Öğe kimlikleri.")]
    public required List<string> ElementIds { get; init; }
}

public sealed class GroupElementsArgs : DesignMutationArgs
{
    [Description("Gruplanacak öğe kimlikleri (en az 2).")]
    public required List<string> ElementIds { get; init; }

    [Description("Grup adı (isteğe bağlı, öğe adlarına ön ek olmaz).")]
    public string? Name { get; init; }
}

public sealed class UngroupElementsArgs : DesignMutationArgs
{
    [Description("Grubu çözülecek öğe kimlikleri (bir üye yeterli).")]
    public List<string>? ElementIds { get; init; }

    [Description("Çözülecek grup kimliği.")]
    public string? GroupId { get; init; }
}

public sealed class AlignElementsArgs : DesignMutationArgs
{
    [Description("Hizalanacak öğeler (gruplar birim olarak hizalanır).")]
    public required List<string> ElementIds { get; init; }

    [Description("Kenar: left, center_horizontal, right, top, center_vertical, bottom.")]
    public required AlignEdge Edge { get; init; }

    [Description("Neye göre: auto (tek öğe → etiket, çok öğe → seçim), selection, label.")]
    public AlignTo? RelativeTo { get; init; }
}

public sealed class DistributeElementsArgs : DesignMutationArgs
{
    [Description("Dağıtılacak öğeler (sabit aralık için en az 2, eşit dağıtım için en az 3).")]
    public required List<string> ElementIds { get; init; }

    [Description("Yön: horizontal, vertical.")]
    public required Direction Direction { get; init; }

    [Description("Öğeler arası sabit boşluk (mm). Verilmezse ilk ve son öğe sabit kalır, aradakiler eşit aralıklı olur.")]
    public double? SpacingMm { get; init; }
}

public sealed class SetLayerOrderArgs : DesignMutationArgs
{
    [Description("Öğe kimliği.")]
    public required string ElementId { get; init; }

    [Description("bring_to_front, bring_forward, send_backward, send_to_back. index verilirse yok sayılır.")]
    public LayerAction? Action { get; init; }

    [Description("Hedef katman sırası (0 = en alt).")]
    public int? Index { get; init; }
}

public sealed class ProcessImageArgs : DesignMutationArgs
{
    [Description("Görsel öğesinin kimliği.")]
    public required string ElementId { get; init; }

    [Description("Yöntem: auto, threshold, otsu, floyd_steinberg, ordered, photo, logo. Logo/çizim için logo, fotoğraf için photo.")]
    public ImageConversionMethod? Method { get; init; }

    [Description("Eşik 1–254 (threshold yönteminde mutlak; diğerlerinde 128 üstü koyulaştırır).")]
    public int? Threshold { get; init; }

    [Description("Parlaklık -100…100.")]
    public int? Brightness { get; init; }

    [Description("Kontrast -100…100.")]
    public int? Contrast { get; init; }

    [Description("Kenar keskinleştirme 0…100 (0 = kapalı).")]
    public int? Sharpen { get; init; }

    [Description("Siyah/beyazı ters çevir.")]
    public bool? Invert { get; init; }

    [Description("Yerleşim: contain, cover, stretch.")]
    public ImageFitMode? FitMode { get; init; }

    [Description("Kırpma oranları 0–0,9 (ör. {\"left\":0.1}).")]
    public CropArgs? Crop { get; init; }

    [Description("Görseli etikete ortala (yatay ve dikey).")]
    public bool? CenterOnLabel { get; init; }

    [Description("Görsel kutusunu etikete sığacak en büyük boyuta getir (kenar boşluğu margin_mm, oran korunur).")]
    public bool? FitToLabel { get; init; }

    [Description("fit_to_label için kenar boşluğu (mm), varsayılan 3.")]
    public double? MarginMm { get; init; }
}

public sealed class CropArgs
{
    public double? Left { get; init; }
    public double? Top { get; init; }
    public double? Right { get; init; }
    public double? Bottom { get; init; }
}

public sealed class DesignRefArgs
{
    [Description("Tasarım kimliği.")]
    public required string DesignId { get; init; }
}

public sealed class ListDesignsArgs
{
    [Description("Ada göre süzme (isteğe bağlı, büyük/küçük harf ve Türkçe karakter duyarsız).")]
    public string? Query { get; init; }
}

public sealed class GetDesignArgs
{
    [Description("Tasarım kimliği.")]
    public required string DesignId { get; init; }

    [Description("Baskı çözünürlüğünde önizleme görüntüsü de döndür (varsayılan false).")]
    public bool? IncludePreview { get; init; }
}

public sealed class CreateDesignArgs
{
    [Description("Tasarım adı.")]
    public required string Name { get; init; }

    [Description("Etiket genişliği (mm), varsayılan 100.")]
    public double? WidthMm { get; init; }

    [Description("Etiket yüksekliği (mm), varsayılan 100.")]
    public double? HeightMm { get; init; }

    [Description("Açıklama (isteğe bağlı).")]
    public string? Description { get; init; }
}

public sealed class DuplicateDesignArgs
{
    [Description("Kopyalanacak tasarım.")]
    public required string DesignId { get; init; }

    [Description("Yeni tasarımın adı.")]
    public required string Name { get; init; }
}

public sealed class SaveDesignArgs
{
    [Description("Kaydedilecek tasarım (yeni: veya doğrudan modda açık/dosya tasarımı).")]
    public required string DesignId { get; init; }

    [Description("Dosya adı (yalnızca yeni tasarımın ilk kaydında; Şablonlar klasörüne kaydedilir, var olan dosyanın üzerine yazılmaz).")]
    public string? FileName { get; init; }
}

public sealed class BeginDraftArgs
{
    [Description("Değiştirilecek var olan tasarım (dosya: veya acik:).")]
    public required string DesignId { get; init; }
}

public sealed class SubmitDraftArgs
{
    [Description("Taslak kimliği (taslak:…).")]
    public required string DraftId { get; init; }

    [Description("Kullanıcıya gösterilecek kısa Türkçe açıklama: ne değişti ve neden.")]
    public required string Summary { get; init; }
}

public sealed class DraftRefArgs
{
    [Description("Taslak kimliği (taslak:…).")]
    public required string DraftId { get; init; }
}

public sealed class RenderPreviewArgs
{
    [Description("Tasarım kimliği.")]
    public required string DesignId { get; init; }

    [Description("Öğe sınırlarını ve kimliklerini renkli çerçeveyle göster (yerleşimi anlamak için).")]
    public bool? Annotate { get; init; }

    [Description("Veri satırı numarası (1'den başlar) ile doldur. Veri kaynağı bağlıysa.")]
    public int? Row { get; init; }

    [Description("Ürün anahtarı ile doldur (search_products sonucu).")]
    public string? ProductKey { get; init; }

    [Description("Büyütme 1–3 (varsayılan 1 = baskı çözünürlüğü, 8 nokta/mm).")]
    public int? Scale { get; init; }
}

public sealed class RenderBatchPreviewArgs
{
    [Description("Tasarım kimliği (veri kaynağı bağlı olmalı).")]
    public required string DesignId { get; init; }

    [Description("İlk satır (1'den başlar), varsayılan 1.")]
    public int? FromRow { get; init; }

    [Description("Satır sayısı (en fazla 24), varsayılan 12.")]
    public int? Count { get; init; }
}

public sealed class InspectLayoutArgs
{
    [Description("Tasarım kimliği.")]
    public required string DesignId { get; init; }

    [Description("Kenar boşluğu sınırı (mm), varsayılan 1,5.")]
    public double? MarginMm { get; init; }
}

public sealed class ValidateDesignArgs
{
    [Description("Tasarım kimliği.")]
    public required string DesignId { get; init; }

    [Description("Barkodları baskı görüntüsünden okuyarak doğrula (varsayılan true).")]
    public bool? VerifyBarcodes { get; init; }

    [Description("Veri kaynağı bağlıysa denetlenecek en fazla satır (varsayılan 50).")]
    public int? MaxRows { get; init; }
}

public sealed class ListProductsArgs
{
    [Description("Atlanacak kayıt sayısı.")]
    public int? Offset { get; init; }

    [Description("En fazla kayıt (varsayılan 50, en çok 200).")]
    public int? Limit { get; init; }
}

public sealed class SearchProductsArgs
{
    [Description("Aranacak metin: ürün adı, kodu veya barkodu (Türkçe karakter duyarsız).")]
    public required string Query { get; init; }

    [Description("En fazla sonuç (varsayılan 20).")]
    public int? Limit { get; init; }
}

public sealed class ImportProductsArgs
{
    [Description("Excel/CSV dosyası (izinli klasörlerde).")]
    public required string Path { get; init; }

    [Description("Excel sayfası (isteğe bağlı).")]
    public string? Sheet { get; init; }

    [Description("Ürünü benzersiz tanımlayan sütun (ör. StokKodu). Verilmezse tahmin edilir.")]
    public string? KeyColumn { get; init; }
}

public sealed class BindProductArgs : DesignMutationArgs
{
    [Description("Ürün anahtarı (search_products → key).")]
    public required string ProductKey { get; init; }

    [Description("preview: alanlar kalır, önizleme bu ürünle gösterilir. fill: {Alan} yer tutucuları ürün değerleriyle sabit metne çevrilir.")]
    public ProductBindMode? Mode { get; init; }
}

public sealed class SetDataSourceArgs : DesignMutationArgs
{
    [Description("Excel/CSV dosyası (izinli klasörlerde).")]
    public required string Path { get; init; }

    [Description("Excel sayfası (isteğe bağlı).")]
    public string? Sheet { get; init; }
}

public sealed class DataPreviewArgs
{
    [Description("Tasarım kimliği (veri kaynağı bağlı) veya path ile dosya.")]
    public string? DesignId { get; init; }

    [Description("Doğrudan bir Excel/CSV dosyası (izinli klasörlerde).")]
    public string? Path { get; init; }

    [Description("Excel sayfası.")]
    public string? Sheet { get; init; }

    [Description("Gösterilecek satır sayısı (varsayılan 10, en çok 50).")]
    public int? Rows { get; init; }
}

public sealed class BindFieldArgs : DesignMutationArgs
{
    [Description("Bağlanacak öğe.")]
    public required string ElementId { get; init; }

    [Description("Veri sütunu adı (ör. UrunAdi). Metinde {UrunAdi}, barkodda içerik, görselde satıra göre değişen görsel olur.")]
    public required string Column { get; init; }

    [Description("Metin öğelerinde: replace (metni tamamen alan yap, varsayılan), append (sonuna ekle), prefix (\"Ürün: \" gibi ön ekle birlikte).")]
    public string? Mode { get; init; }

    [Description("mode=prefix için sabit ön ek metni.")]
    public string? Prefix { get; init; }
}

public sealed class ListFilesArgs
{
    [Description("Tür: images, data, designs.")]
    public required FileListKind Kind { get; init; }

    [Description("Ada göre süzme (isteğe bağlı).")]
    public string? Query { get; init; }

    [Description("En fazla sonuç (varsayılan 100).")]
    public int? Limit { get; init; }
}

public sealed class NoArgs
{
}
