# Genel ürün: ilk uygulama dilimi

Bu çalışma kaynak uygulamadan ayrı bir yerel kopyada geliştirilir. Geçici görünen ürün adı **Etiket Tasarım**;
C# namespace/proje adları ve `.akelabel` paket kimliği dosya/API uyumluluğu için korunur.

## Firma bilgileri

Ayarlar'da ad, adres, web sitesi ve logo düzenlenir. Logo en fazla 10 MB kaynaktan okunup oranı korunarak
PNG'ye dönüştürülür, ayarlara gömülür. Vazgeç kaydedilmiş profili değiştirmez. Şablonlara gömülen logolar
başka dosyalara bağımlı değildir; şirkete özel kırpma uygulanmaz.

Kaydederken isteğe bağlı olarak firma bilgileriyle yeni şablon kopyaları eklenir. Mevcut tasarımlar,
özelleştirilmiş şablonlar ve şablon içindeki barkodlar değiştirilmez. Firma profili değişiklikleri eski
etiketlere geriye dönük uygulanmaz. Şablon ve veri dosyası klasörleri mevcut seçim ekranlarından ayarlanır;
ürün/veri bağlantısı yerel Excel/XLS/CSV üzerinden çalışır. Yeni sunucu veya ücretli servis eklenmez.

## Ayrı kurulum ve geçiş

- Genel sürümün veri klasörleri, EXE adı ve Inno Setup AppId'si ayrıdır. Kaynak uygulamanın kaldırma kimliğini kullanmaz.
- `.akelabel` ilişkilendirmesi varsayılan olarak kapalıdır; çalışan kurulumun dosya ilişkilendirmesini otomatik devralmaz.
- Önceki ayar JSON'ları yeni `company` alanı olmadan okunabilir; kayıtlı yazıcı, tercihler ve sayaç sırası korunur.
- Otomatik v1 ayar/şablon devralma genel sürümde kapalıdır. Eski içeriği kullanmak isteyen kişi yalnız kendi
  dosyalarının kopyasını açmalıdır. Tam veri taşıma sihirbazı bu dilimde yoktur.
- GS1 ve SSCC kayıtları otomatik kopyalanmaz. SSCC üretiminin yeni kurulumda yeniden başlatılması numara tekrarına
  yol açabileceğinden, aynı GCP ile geçişte bütün tahsis geçmişi ve seri aralıkları ayrıca doğrulanmalıdır.

## Yapay zekâ

Mevcut asistan, MCP ve yerel API altyapısı korunur; varsayılan kapalıdır. Yeni hesap/anahtar alınmaz.
`firma-logo` aracı masaüstünde yapılandırılan logoyu kullanır. Eski `akel-logo` araç takma adı yalnız uyumluluk
amacıyla korunur; gömülü marka görseline düşmez. Arka plan Worker'da sağlayıcı verilmemişse logo kullanılamaz;
kayıtlı tasarımların kendi görselleri kullanılabilir. AI araçları fiziksel baskı başlatamaz.

## Public kaynak hazırlığı

`python tools/publication/prepare_source.py <boş-hedef-klasör>` temiz bir kaynak anlık görüntüsü üretir.
Özel deponun `.git` geçmişi, eski firma teslim raporları/planı, eski logo dosyaları, yerel ayarlar,
derleme çıktıları ve test günlükleri dışarıda kalır. Betik örnek ZIP/XLSX/etiket içeriklerini de tarar.
Tarama bilinen firma kimlikleri ve tipik sır kalıpları için denetimdir; insanın içerik incelemesinin yerine geçmez.

Yeni public depo, push veya release bu betiğin işi değildir. Genel kaynak kopyası `husodrn46/etiket-tasarim`
adresinde geçici ürün adıyla paylaşılır. Kaynak kodun açık kaynak lisansı henüz seçilmedi. Windows/yazıcı kabulü
tamamlanana kadar indirmeler ön sürüm olarak sunulur. Eski teslim raporlarının başarılı Windows testleri
bu genel sürümün kabulü olarak sunulmaz.

## Son kullanıcı yayın adayı

Yerelde NSIS kurulum EXE'si ve taşınabilir Windows x64 ZIP üretilebilir. Paket, sabitlenmiş upstream commitlerden
ve NuGet paketlerinden toplanan tam lisans metinlerini içerir. Derleme aracı eksik lisans metniyle paketi tamamlanmış
saymaz. Paket kaynak lisansının seçilmesi yerine geçmez; görünen ürün adı geçicidir.

Baskı penceresindeki Windows yazıcı keşfi varsayılan olarak korunur; kabul testleri keşif ve spooler adaptörlerini
sahte yazıcıyla değiştirir. Firma ayarı → şablon → barkod doğrulama → kullanıcı Yazdır komutu → iki kopyalık TSPL
verisi → baskı geçmişi akışı donanımsız denetlenir. Bu, etiketin kağıtta çıktığını kanıtlamaz.

Kalan gerçek kabul: Windows kurulum/açılış, ayar ekranı, kaldırma ve bir yazıcıda ölçü/yön/barkod okuyucu.
`tools/distribution/windows_acceptance.ps1` ilk bölüm için hazırlanmıştır; henüz gerçek Windows'ta çalıştırılmamıştır.
