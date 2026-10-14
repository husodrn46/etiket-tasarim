# İlk kurulum ve günlük kullanım geliştirmeleri

Bu değişiklikler `codex/onboarding-and-usability` dalındadır; mevcut `v2.0.0-preview.1` kurulum paketi bunları içermez.

## Yapılanlar

- İlk açılışta üç adımlı kurulum: firma/logo, isteğe bağlı yazıcı, hazır veya özel etiket ölçüsü ve önizleme.
  Kaydetmeden mevcut ayarlar değişmez. Önceki kurulumunu tamamlayan kullanıcıya zorla açılmaz;
  Yardım menüsünden yeniden açılabilir. Baskı göndermez ve GS1 numarası oluşturmaz.
- Şablon galerisi ad/ölçü araması ve kategori filtresi. Firma şablonlarında gömülü görsel önizleme.
  Kargo 100×150 ve demirbaş 70×35 tasarımları eklendi; eski şablonlar korunur.
  Kargo örneği `samples/ornek-kargo.csv` ile denenebilir. Takip numarası kullanıcıdan gelir;
  taşıyıcı veya GS1 numarası uydurulmaz.
- Excel/CSV sütunları tasarım alanlarına eşleştirilir; tarih kaydırmaları, biçimler, görsel ve görünürlük alanları
  korunur. Kaynak tabloya yazılmaz. İşlem tek adımda geri alınabilir; eşleştirme tasarımla birlikte kaydedilir.
- Hızlı baskıda yeni okutma bulunamazsa veya birden çok ürüne uyarsa önceki ürün temizlenir. Kısmi kod tek sonuç
  verse bile otomatik seçilmez. Elle listeden seçim yapılabilir. Son şablon hatırlanır; adet başlangıçta 1'dir.
  Satır başına baskı sınırıyla tutarlı olarak 1–9999 adet kabul edilir; sessiz adet azaltımı önlenir.
- Tek `.etiketyedek` dosyasında firma/arayüz ayarları, şablonlar ve ürün listesi. Boyut, dosya listesi,
  sürüm ve SHA-256 doğrulanır; dizin dışına çıkan dosya yolları reddedilir. Geri yüklenen şablonlar yeni klasöre
  çıkarılır, eski ürün listesi ayrıca saklanır. Geri yükleme öncesinde içerik ve değişecek veriler gösterilir.
- Baskı geçmişi yazımı dosya yoluna bağlı isimli mutex ile süreçler arasında sıralanır; aynı tasarımın eşzamanlı
  kopyaları ortak geçici dosya kullanmaz.

## Yedeğin sınırları

Harici Excel/CSV ve görseller, GS1/SSCC tahsisleri, sayaçlar, baskı geçmişi ve bağlantı anahtarları dahil değildir.
Kopyada eski bilgisayarın dosya yolları ve yazıcı kuyruğu temizlenir. Canlı kurulumun GS1 öneki ve ürün sıra sayacı
bu paketle geri alınmaz. Aynı GS1 önekiyle başka bilgisayara taşınırken tahsis geçmişi ayrıca korunmalıdır.
Yedekler işletme verisi içerir ve yereldir; paylaşılmadan önce içeriği değerlendirilmelidir.

## Yerel doğrulama

- Çözüm Mac üzerinde Windows hedeflemesiyle derlendi. Bu, WPF ekranlarının çalıştırıldığı anlamına gelmez.
- Seçilmiş **906 test geçti, 0 başarısız**: editör 204, Core 203, DataImport 83, Rendering 49, Design 35,
  GS1 78, PrintEngine 58, ImageProcessor 107, Assistant 81 ve LocalApi 8.
- Dört bağımsız .NET süreci aynı geçmiş dosyasına 80'er kayıt yazdı: **320/320 geçerli ve benzersiz kayıt**,
  tüm süreçler başarılı çıkış yaptı. Süreç içi 80 kayıtlık eşzamanlı ekleme regresyon testi de geçiyor.
- DPAPI kullanan üç editör testi, Windows spooler testleri, yerel EXE/host devralma entegrasyonları ve
  Mac'te farklı hata metni veren dosya kilidi mesajı testi bu gruba dahil edilmedi. Tam Windows test takımı
  başarıyla çalışmış gibi sunulmaz.
- Windows kurulum/arayüz ve gerçek yazıcı testleri kullanıcının isteğiyle ertelendi.
  `windows_acceptance.ps1`, ilk kurulum sihirbazını erteleyerek önceki ayar ekranı akışını sınayacak biçimde
  güncellendi; bu betik de Windows'ta çalıştırılmadı. Sihirbazın görsel/klavye kabulü ayrıca yapılmalıdır.
