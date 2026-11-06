# BarTender etiketlerini içe aktarma (.btw)

[English](BARTENDER.md)

LabelHarbor, **BarTender 10.1 ve 2016** ile hazırlanmış etiket dosyalarını (`.btw`) açar ve düzenlenebilir
LabelHarbor etiketine çevirir. Özgün dosya yalnızca okunur, değiştirilmez; içindeki betikler, veritabanı
bağlantıları ve yazıcı komutları çalıştırılmaz.

## Tek etiket açma

- **Aç** penceresi `.akelabel` dosyalarının yanında `.btw` dosyalarını da gösterir. Dosyayı pencereye
  sürükleyebilir ya da **Kaydet'in yanındaki ▾ → BarTender etiketi aç…** komutunu kullanabilirsiniz.
- Etiket yeni ve kaydedilmemiş bir sekmede açılır. **Kaydet**, normal bir `.akelabel` projesi olarak kaydeder.
- Birebir aktarılamayan bir şey varsa kısa bir liste bir kez gösterilir. Basmadan önce kontrol edin.

## Klasörü toplu dönüştürme

**▾ → BarTender klasörünü dönüştür…** bir klasör sorar ve alt klasörler dahil içindeki tüm `.btw` dosyalarını çevirir.

- Çıktı: `Belgeler\Etiket Tasarım\BarTender'dan aktarılanlar\<klasör adı>`, aynı alt klasörlerle.
- Var olan dosyanın üzerine yazılmaz; bir dosyayı yeniden çevirmek için çıktıyı silip tekrar çalıştırın.
- Çıktı klasöründeki `Aktarım raporu.txt`, bakılması gereken dosyaları listeler.
- Çevrilen etiketler önizlemeyle kaydedilir.

## Neler aktarılır

| BarTender | LabelHarbor |
|---|---|
| Şablon ölçüsü | Etiket genişliği ve yüksekliği; rulo üzerinde yan yana basılan etiketler bu düzeni korur (aşağıya bakın) |
| Yazı | Aynı yazı tipi, punto, kalın/italik/altı çizili, döndürme, hizalama ve konum. Karakter genişliği (ör. %94,8) korunur. Sabit genişlikli yazı BarTender'ın kırdığı yerlerden satırlara bölünür. |
| Yazı çerçevesi | Aynı kalınlıkta, yuvarlak köşeli dikdörtgen |
| EAN-13 | Aynı veri, çubuk yüksekliği ve konum. Modül genişliği, BarTender'daki gibi dosyada kayıtlı yazıcı çözünürlüğünün tam noktasına yuvarlanır; aynı yazıcıda çubuklar aynı yere düşer. |
| Resim | Dosyanın içine gömülü görsel (PNG, JPEG, GIF, BMP), aynı ölçü ve döndürmeyle |
| Kutu | Dikdörtgen; kesik (pah) köşeler yuvarlak köşe olur |

Konumlar BarTender'ın bağlantı noktası (nesnenin dokuz noktasından biri) ve döndürmesine göre hesaplanır.

## Sınırlar

- **Çizgiler** aktarılmaz. Denenen dosyalarda BarTender bu çizgileri basmıyordu. Gerekirse Şekil → Çizgi ile ekleyin.
- Yazı ve barkodların yalnızca **o anki değeri** aktarılır. Veri kaynakları, seri numaraları, sorular ve
  veritabanı bağlantıları aktarılmaz.
- EAN-13 dışındaki barkod türleri adından tanınır ama ölçüleri doğrulanmadı; kontrol edin.
- Rulo üzerinde **yan yana** basılan etiketler (ör. 104 mm ruloda 5 × 20 mm) aynı sayı, kenar payı ve arayla
  aktarılır; değerler BarTender'ın sayfa ayarından alınır. Denenen dosyalarda iki yanda kenar payı var, etiketler
  arasında boşluk yok; ilk baskıda hizayı kontrol edip gerekirse Etiket ölçüsü penceresinden düzeltin.
- Yazı tipi yüklü değilse Arial kullanılır ve bu söylenir.
- LabelHarbor ile BarTender farklı yazı tipi ipuçlama kullandığı için yazı uzunlukları yüzde birkaç farklı olabilir.
- `.btw` biçimi belgelenmemiştir. Aktarıcı etiket için gereken bölümleri okur ve atladığı her şeyi bildirir.
  Toplu baskıdan önce mutlaka bir deneme etiketi basın.

Karakter genişliği %100'den farklı olan etiketler biçim 4 olarak kaydedilir. Eski LabelHarbor sürümleri bu
dosyaları "daha yeni sürüm" notuyla açar ve o yazıyı normal genişlikte çizer.

## Nasıl denendi

Aktarıcı, BarTender 10.1 SR4 ve 2016 R7 ile yapılmış 610 gerçek etiketle geliştirildi (20 × 40 mm,
12,5 × 35 mm ve 100 × 150 mm; yazı, EAN-13, yuvarlak çerçeve, logo). Her dosya çevrildi, LabelHarbor'da 300 dpi'da
çizildi ve BarTender'ın dosyanın içine koyduğu önizleme görüntüsüyle karşılaştırıldı. Ortanca mürekkep örtüşmesi
%97,5 oldu ve çevrilen etiketlerin hiçbirinde yazı kutudan taşmadı. Bu müşteri dosyaları depoda yoktur; otomatik
testler aynı yapıdaki küçük sentetik `.btw` dosyalarını kullanır.
