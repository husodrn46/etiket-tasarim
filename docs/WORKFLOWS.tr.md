# Kişiselleştirme ve günlük iş akışları

[English](WORKFLOWS.md) · [Türkçe](WORKFLOWS.tr.md)

Bu özellikler bu belgenin bulunduğu daldan yeni derleme gerektirir. Yayımlanmış `v2.0.0-preview.1` paketinde
yoktur. Windows arayüzü ve gerçek yazıcı kabul testleri ertelenmiştir.

## Kişiselleştirme

**Ayarlar → Kişiselleştirme** bölümünde kırmızı, mavi, petrol mavisi veya mor vurgu; %90, %100, %110 veya %125
arayüz ölçeği; yeni metinlerin yazı tipi ve isteğe bağlı başlangıç şablonu seçilir. Koyu/açık temaya sistem
teması eklendi. Sistem seçimi Windows uygulama temasını açılışta veya ayarlar uygulanırken okur. Görünüm
ayarları onaydan sonra uygulanır; arayüz dilini değiştirmek için yeniden başlatmak gerekir.

Yeni yazı tipi yalnız yeni eklenen metin, metin stili ve metin alanlarını etkiler. Mevcut tasarımlar değişmez.
Arayüz ölçeği etiketin mm ölçüsünü, yazıcı çözünürlüğünü veya dışa aktarılan resmi değiştirmez.
**Panel düzenini hatırla**, seçili araç panelini ve araç/AI panelinin açık durumunu saklar; AI bağlantısı kurmaz.
Pencere konumu mevcut ayrı ayarla saklanmaya devam eder.

Başlangıç şablonu kaydedilmemiş yeni kopya olarak açılır. Kurtarılan çalışma ve komut satırındaki dosyalar
önceliklidir. Şablon bulunamazsa uyarı gösterilir, editör çalışır. Galeride yeniden adlandırma favori/başlangıç
bağlantısını günceller; silme bu bağlantıları temizler. Dosyayı uygulama dışından taşımak bağlantıyı bozabilir.

## Favoriler

**Şablonlar** bölümünde seçim yapıp **Favoriye ekle** kullanılır. **Yalnız favoriler** galeriyi süzer; Hızlı
Baskı listesi de favorileri önce gösterir. Favori satırındaki **Aç** düğmesi şablonu tek tıkla açar.

**Baskı geçmişi** bölümünde bir iş favorilenebilir. **Yalnız favoriler** ile bulunup **Tekrar bas** seçildiğinde
(satır düğmesiyle de) mevcut baskı onayı açılır; favorilemek veya açmak baskı göndermez. Kaynak veri dosyası hâlâ mevcut olmalıdır;
değişmiş veri uyarısını inceleyin. Favori işlerin tasarım kopyaları süreye bağlı temizlikten korunur. Koruma
listesi atomik yazılır; okunamıyorsa temizlik silme işlemini atlar.

## Etiket bazında tarih, sayı ve para biçimi

**Etiket ölçüsü/ayarları → Etikete özel biçimleri kullan** seçilir. Çıktı bölgesi, verideki sayı/tarih bölgesi,
tarih biçimi, sayı biçimi ve isteğe bağlı para birimi etikete kaydedilir. Önizleme sabit örnek değerler kullanır.
Arayüz dili bu ayarlardan bağımsızdır.

| Metin alanı | Davranış |
| --- | --- |
| `{Fiyat}` | Hücre metni aynen korunur |
| `{Fiyat:NUMBER}` | Veri bölgesinde okunur, seçili sayı biçimiyle gösterilir |
| `{Fiyat:CURRENCY}` | Aynı sayı, seçilen para birimi kodu ve çıktı bölgesinin para düzeniyle gösterilir |
| `{Tarih:DATE}` | Veri bölgesinde okunur, seçili tarih biçimiyle gösterilir |
| `{Tarih+30:DATE}` | Tarihe 30 gün eklenir |

Örneğin `1234.56`, en-US veri ve de-DE çıktı bölgesiyle N2 biçiminde `1.234,56` olur. EUR seçmek kur dönüşümü
yapmaz; yalnız para birimi kodunu basar. Para birimi boşsa CURRENCY seçili sayı biçimini kullanır. Barkodlar,
GS1 kimlikleri, ham alanlar, koşullar ve sayaç tahsisi mevcut kaynaklarını kullanır; bölgesel biçim yalnız metin
çiziminde uygulanır. Önizleme, dışa aktarma ve baskı aynı uygulamayı kullanır.

Excel aktarımı mevcut sabit metne çevirme davranışını korur; sayı/tarih hücreleri genellikle tr-TR metnidir.
Veri bölgesini Excel arayüz diline göre değil aktarılan metne göre seçin. CSV kendi kaynak düzenini kullanır.
`01/02/2026` gibi belirsiz tarihler seçili veri bölgesine göre okunur; bozuk sayı gruplaması/tarih hata verir.

Eski dosyalarda bu profil yoktur ve eski davranış korunur. Özellik açık etiketler **biçim 3** ile kaydedilir;
kapalı etiketler gerekli en düşük biçimi (1 veya 2) kullanır. Eski uygulamalar yeni biçimleri güvenilir biçimde
üretemez; eski sürümde açmadan önce kopya saklayın. Değişiklik geri alınabilir; kapatmak eski davranışı getirir.

## Hazır paketler

**Şablonlar → Hazır kullanım paketleri** önizleme, kısa tarif ve örnek klasörü sunar. **Şablonları ekle** iki
yeni dosya ekler; var olanın üzerine yazmaz. Tekrar ekleme numaralı kopya üretir. Firma bilgileri yerel
ayarlarınızdan gelir. Her tasarımı baskı öncesi kontrol edin.

| Paket | Tasarımlar |
| --- | --- |
| Mağaza | Ürün 70×50, raf 58×40 mm |
| Depo | Stok 100×50, konum 70×35 mm |
| Kargo | Tam 100×150, kısa 100×75 mm |
| Küçük işletme | Parti 70×50, demirbaş 70×35 mm |

Örnek CSV ve iki dilli tarifler kaynakta `samples/packs/`, kurulumda `Örnekler/packs/` altındadır. Sütun adları
her iki dilde İngilizce kalır. Tüm satırlar kurgusaldır. Şirket içi Code 128 kodları GS1 tahsisi değildir.
Gerçek takip numarası taşıyıcıdan alınır; tasarım gönderi veya kargo satın alımı oluşturmaz. Ürün/gıda etiketi
mevzuata uygunluk belgesi sağlamaz.

## Aktarım önerileri ve profilleri

**Veri → Sütunları eşleştir** önce birebir başlık, sonra tekil normalize eşleşme ve sınırlı Türkçe/İngilizce
alan karşılıkları önerir: `UrunAdi` → `ProductName`, `Fiyat` → `Price`, `UrunKodu` → `SKU`. Belirsiz öneri boş
kalır. Uygulamadan önce sütunu ve örnek hücreyi inceleyin. Eşleştirme tasarımın alan adlarını tek geri alma
adımında değiştirir; Excel/CSV'ye yazmaz.

**Profili kaydet…** eşleştirmeyi, ayırıcıyı, kodlamayı, sayfayı ve başlık satırını saklar. Aynı adla güncelleme
onay ister. Veri panelinde dosyayı açmadan önce profili seçin; ayrıştırma seçenekleri kullanılır, eşleştirme
incelemesi açılır. Eksik sütun boş bırakılır ve elle seçilmelidir. Alan adları daha önce eşleştirilmiş tasarımda
da profil kullanılabilir. Tedarikçinin değiştirdiği dosya için eski profilin hâlâ doğru olduğunu varsaymayın.

Profiller ürün satırı, kaynak dosya yolu, parola veya API anahtarı saklamaz. Verdiğiniz profil/tedarikçi adını
ve sütun adlarını içerebilir. Tercihler, favoriler ve profiller yereldir; yeni makineye bağlı iş akışı tercihleri
taşınabilir firma yedeğine eklenmez. Kaynak dosyalara, numara kayıtlarına veya diğer kurulumlara dokunulmaz.
