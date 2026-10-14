# Etiket Tasarım

Windows için çevrimdışı etiket ve barkod tasarım uygulaması. **Etiket Tasarım geçici ürün adıdır.**
Firma adı, adres, web sitesi, logo ve şablon klasörü ayarlanabilir. Hesap veya abonelik gerekmez.

## İndir ve kur

[Windows x64 kurulum dosyasını indir](https://github.com/husodrn46/etiket-tasarim/releases/download/v2.0.0-preview.1/Etiket-Tasarim-Kurulum-2.0.0-aday.exe)
· [Taşınabilir ZIP](https://github.com/husodrn46/etiket-tasarim/releases/download/v2.0.0-preview.1/Etiket-Tasarim-2.0.0-win-x64-aday.zip)
· [Ön sürüm ve doğrulama bilgileri](https://github.com/husodrn46/etiket-tasarim/releases/tag/v2.0.0-preview.1)

Windows 10/11 64-bit içindir; .NET çalışma zamanı pakete dahildir. Kurulum dosyasını çalıştırın veya ZIP'i
bir klasöre çıkarıp `Etiket Tasarim.exe` dosyasını açın. Ardından aşağıdaki başlangıç adımlarını izleyin.

Bu ilk genel **ön sürümdür**. Mac üzerinde seçilmiş 885 otomatik test geçti; paketin dosya bütünlüğü ve
bilinen özel veri/sır kalıpları kontrol edildi. Gerçek Windows kurulum/arayüz ve fiziksel yazıcı kabul testleri
henüz yapılmadı. İlk baskıda kendi yazıcınızda ölçü, yön ve barkod okunabilirliğini kontrol edin.
Kaynak kod için henüz bir açık kaynak lisansı seçilmedi; bağımlılık lisansları pakete dahildir.

## Başlangıç

1. Windows uygulamasını açın. **Ayarlar → Firma Bilgileri** bölümüne kendi bilgilerinizi girin ve logonuzu seçin.
2. **Firma bilgileriyle yeni örnek şablonlar oluştur** seçeneğini işaretleyip kaydedin. Mevcut şablonlar korunur;
   aynı ad varsa numaralı yeni kopya eklenir. Firma bilgileri bu kopyalara kaydedilir; eski tasarımlar kendiliğinden değişmez.
3. **Şablonlar** panelinden etiketi seçin. Ürün adı, fiyat, barkod ve tasarımı düzenleyin.
4. Toplu baskı için **Veri → Excel/CSV** ile kendi dosyanızı bağlayın; Hızlı Baskı için ürün listenizi içeri alın.
5. Yazıcıyı seçip rulo ölçülerini ve kalibrasyonu doğrulayın. Hazır XP-470B profili yalnız başlangıç değerleridir.

Örnek dosyalar sentetiktir. Örnek barkodların kontrol basamaklarının geçerli olması GS1 tarafından tahsis
edildikleri anlamına gelmez; bunları gerçek ürün numarası olarak kullanmayın. Örnek ürün dosyasının ikinci
satırında hata raporlamasını göstermek için bilerek hatalı kontrol basamağı vardır.

## Özellikler

- mm cinsinden sürükle-bırak tasarım; metin, barkod, QR, görsel, şekil, simgeler, gruplama ve geri alma.
- 13 barkod türü; ortak 1-bit render motoru, barkod okunabilirlik kontrolü, PNG/PDF çıktı.
- Excel/XLS/CSV, değişken alanlar, kalıcı sayaç, ürün listesi, baskı geçmişi ve yeniden baskı.
- TSPL RAW ve Windows sürücüsüyle baskı, kalibrasyon ve yazıcı/rulo profilleri.
- İsteğe bağlı, varsayılan kapalı AI/MCP altyapısı. Yeni servis, hesap veya API anahtarı kurulmaz.
  Tasarım araçları fiziksel baskı başlatamaz; bulut sağlayıcısına veri göndermek ayrıca kullanıcı izni gerektirir.

## GS1 ve veri güvenliği

Yeni kurulumda firma adı, GLN, GCP ve GTIN listesi boştur. GLN bir şirket/lokasyon kimliğidir; ürün GTIN'i
veya firma öneki olarak kullanılmaz. Uygulama GLN'den ya da şirket adından kod türetmez. SSCC için
kullanıcının kendi GS1 belgesindeki GCP'yi girip doğrulaması gerekir. Normal etiket tasarımı için GS1 üyeliği gerekmez.

Veriler `Belgeler\Etiket Tasarım`, `%APPDATA%\Etiket Tasarım` ve `%LOCALAPPDATA%\Etiket Tasarım` altında saklanır.
Deneme/taşınabilir çalışma için `ETIKET_VERI` tüm klasörleri ayrı bir köke yönlendirir. Diğer kurulumlardan
ayar, logo, ürün, şablon, GS1 veya SSCC kayıtları otomatik alınmaz. `.akelabel` dosya biçimi uyumluluk için korunur.

## Geliştirme

C# / .NET 10, WPF, CommunityToolkit.Mvvm, SkiaSharp, ZXing.Net, ClosedXML, ExcelDataReader ve CsvHelper.
Masaüstü uygulaması Windows içindir; ortak kütüphanelerin çoğu arayüz olmadan test edilebilir.

```powershell
dotnet restore AkelEtiket.sln
dotnet build AkelEtiket.sln
dotnet test AkelEtiket.sln
powershell -ExecutionPolicy Bypass -File build.ps1
```

Derleme/paket komutları yerel çıktı üretir; GitHub'a gönderim veya dağıtım yapmaz. Bu genel sürüm için Windows
kurulum/kaldırma, WPF ekranları ve gerçek yazıcı kabulü ayrıca tamamlanmalıdır.

[Geçiş ve yayın kapsamı](docs/GENEL-URUN.md) · [Mimari](docs/MIMARI.md) · [Bağımlılık lisansları](docs/LISANSLAR.md)

## Yerel Windows kurulum adayı

Genel sürümün NSIS tabanlı, kullanıcı hesabına kurulan adayı Mac/Linux/Windows üzerinde üretilebilir:

```text
python tools/distribution/build_candidate.py --dotnet <dotnet-yolu> --makensis <makensis-yolu> --cache <NuGet-paket-klasörü> --output <yeni-boş-çıktı-klasörü>
```

Çıktı: .NET/WPF/ASP.NET içeren Windows x64 uygulaması, kurulum EXE'si, taşınabilir ZIP, lisans metinleri ve
SHA-256 dosya manifesti. Bu komut yayın yapmaz. Çıktı klasörü varsa üzerine yazmaz. Bu aday için SDK 10.0.402
ve .NET 10.0.13 kullanılır; `PublishReadyToRun` platformlar arası derlemede kapalıdır.

Gerçek Windows kabulü, temiz kullanıcı profilinin **etkileşimli masaüstünde** şu betikle çalıştırılır:

```powershell
powershell -ExecutionPolicy Bypass -File tools/distribution/windows_acceptance.ps1 -Installer <kurulum-adayı.exe>
```

Betik ayrık geçici klasöre kurar, uygulamayı açar, firma adı/adresi/siteyi gerçek WPF ayar ekranından girer,
şablon üretip yeniden açar, kaldırmayı ve kullanıcı dosyalarının korunmasını kontrol eder. Sonucu
`%TEMP%\Etiket-Kabul-<kimlik>\windows-kabul.json` içine yazar. Gerçek yazıcıya baskı göndermez.
Bu betiğin burada hazırlanmış olması Windows'ta çalıştırıldığı anlamına gelmez.
