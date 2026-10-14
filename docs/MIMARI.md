# Mimari

## Modüller ve bağımlılıklar

```mermaid
graph TD
  App[AkelEtiket.App<br/>WPF görünümler, tuval, diyaloglar,<br/>Yapay Zekâ paneli] --> Editor
  Editor[AkelEtiket.Editor<br/>MVVM, geri al, baskı akışı,<br/>canlı belge köprüsü, asistan paneli] --> Rendering
  Editor --> PrintEngine
  Editor --> DataImport
  Editor --> ProjectManager
  Editor --> Gs1
  Editor --> LocalApi
  Editor --> Assistant
  Worker[AkelEtiket.Worker<br/>penceresiz API barındırıcı] --> LocalApi
  Mcp[AkelEtiket.Mcp<br/>stdio MCP köprüsü] -. HTTP 127.0.0.1 .-> LocalApi
  LocalApi[AkelEtiket.LocalApi<br/>yerel API + HTTP MCP] --> Design
  Assistant[AkelEtiket.Assistant<br/>Anthropic / OpenAI uyumlu,<br/>araç döngüsü] --> Design
  Design[AkelEtiket.Design<br/>tasarım araçları, taslak/öneri,<br/>inceleme, doğrulama, güvenlik] --> Rendering
  Design --> ProjectManager
  Design --> DataImport
  Design --> Gs1
  Gs1[AkelEtiket.Gs1<br/>GLN, GCP, SSCC kaydı] --> BarcodeEngine
  Rendering[AkelEtiket.Rendering<br/>ortak render motoru] --> BarcodeEngine
  Rendering --> ImageProcessor
  BarcodeEngine[AkelEtiket.BarcodeEngine] --> Core
  ImageProcessor[AkelEtiket.ImageProcessor] --> Core
  PrintEngine[AkelEtiket.PrintEngine<br/>TSPL, Windows kuyruğu, GDI] --> Core
  DataImport[AkelEtiket.DataImport] --> Core
  ProjectManager[AkelEtiket.ProjectManager] --> Core
  Rendering --> Core
  Core[AkelEtiket.Core<br/>model mm, MonoBitmap, alanlar,<br/>yapay zekâ ayarları]
```

İstenen altı modül (Editor, BarcodeEngine, ImageProcessor, PrintEngine, DataImport, ProjectManager) ayrı
projelerdir; ortak **Core** (tasarım modeli) ve **Rendering** (ekran ve baskının ortak motoru) eklenmiştir.
Yalnızca `App` WPF'e bağlıdır; görünüm modelleri dahil tüm mantık arayüzsüz test edilebilir.

## Üç katmanın ayrılması: model → render → çıktı

1. **Tasarım modeli** (`Core.Model`): öğeler yalnızca **mm** cinsinden saklanır (X, Y, genişlik, yükseklik,
   döndürme). Ekran çözünürlüğü veya yakınlaştırma modeli etkilemez.
2. **Render** (`Rendering.LabelRenderer`): model, hedef çözünürlükte (XP-470B: 8 nokta/mm) **1-bit katmanlara**
   çevrilir. Her öğe kendi katmanını (mürekkep + kaplama maskesi) üretir; etiket bu katmanların z-sırasıyla
   birleşimidir.
3. **Çıktı**:
   - **Ekran**: aynı katmanlar tuvalde gösterilir (yakınlaştırınca noktalar keskin görünür). Bu yüzden tuvaldeki
     siyah-beyaz sonuç baskıyla birebir tutarlıdır (`ScreenLayers_ComposeExactlyToPrintBitmap` testi).
   - **TSPL**: birleşik 1-bit görüntü `BITMAP` verisine paketlenir (0 bit = siyah, MSB = en sol nokta).
   - **Windows sürücüsü**: aynı render, sürücünün bildirdiği gerçek DPI'da yapılır ve 1 kaynak pikseli = 1 aygıt
     noktası olarak, ölçeklemeden basılır. İki yöntem asla karıştırılmaz (çift ölçekleme olmaz).

## Öğe render ayrıntıları

| Öğe | Yöntem |
|---|---|
| Metin | Skia ile yumuşatmasız (alias) + ipuçlu (hinting) çizim; 1-bit'te gövde kalınlıkları piksel ızgarasına oturur. Satır kaydırma, kutuya sığdırma, eksik yazı tipi/glif uyarısı |
| Barkod | ZXing modül deseni → **tam sayı nokta** modül genişliğiyle dikdörtgenler (yumuşatma/dithering yok). Kutuya sığan en büyük modül seçilir. Döndürme 90° katları (kayıpsız bit döndürme) |
| Görsel | Kırpma + sığdır/doldur/uzat → yazıcı çözünürlüğünde gri ton (şeffaf → beyaz) → seçilen yöntemle 1-bit. Dönüşüm cihaz uzayında yapılır; orijinal dosya hiç değişmez |
| Şekil | Kenar kalınlıkları tam noktaya yuvarlanır; köşesiz dikdörtgenler yumuşatmasız dolu dikdörtgenlerle çizilir |

Konum yalnızca katmanı kaydırır: taşıma sırasında yeniden çizim yapılmaz. Yeniden çizim arka plan iş
parçacığında, öğe başına en son istek geçerli olacak şekilde yapılır; konumdan bağımsız içerik anahtarıyla
önbelleğe alınır (geri al/ileri al anında olur).

## Geri al / ileri al

Anlık görüntü (snapshot) tabanlıdır: her değişiklik öncesi belgenin JSON hâli saklanır (görseller kimlikle
başvurulduğu için küçüktür). Sürükleme, hizalama gibi çoklu değişiklikler tek adımda toplanır; bir metin
kutusuna yazmak birleştirilir. "Kaydedilmedi" durumu, mevcut durumun kayıtlı durumla karşılaştırılmasıyla bulunur.

## Proje paketi (.akelabel)

ZIP: `manifest.json` (biçim, sürüm, ölçü, varlık listesi + SHA-256), `label.json` (tasarım), `assets/<id>`
(orijinal görseller), `preview.png`. Kayıt atomiktir (geçici dosya + değiştirme). Eksik/bozuk görsel olsa da
belge açılır ve uyarı verilir.

## Baskı akışı

1. Plan: seçili satırlar × adet (veya `{SIRA}` varsa her etiket ayrı numara).
2. Her etiket baskı kipinde render edilir; hata (geçersiz barkod, sığmayan barkod, doldurulamayan alan) varsa
   **o etiket basılmaz**, satır numarasıyla raporlanır. İstenirse her barkod, birleşik görüntüden kesilerek
   okunur (üstüne binen öğeler de yakalanır).
3. TSPL: kurulum komutları + etiket blokları, en fazla 25 etiketlik Windows işleri halinde RAW gönderilir;
   iptal etiket sınırında durur.
4. Rapor dört ayrı aşama içerir: veri üretimi, kuyruğa gönderim, kuyruk sonucu, fiziksel doğrulama gerekliliği.

## Testler

| Proje | Kapsam |
|---|---|
| `AkelEtiket.Core.Tests` | Birimler, 1-bit bitmap, alan şablonları, model JSON, geometri, **barkod doğrulama/kontrol basamağı/GS1**, **kodla → çiz → çöz** (11 tür), proje paketi, şablon kütüphanesi, otomatik kurtarma |
| `AkelEtiket.ImageProcessor.Tests` | Yükleme (PNG/JPG/BMP/GIF/WEBP/SVG, EXIF), tüm dönüşüm yöntemleri, yalnızca siyah-beyaz çıktı, şeffaflık, sınıflandırma, performans |
| `AkelEtiket.DataImport.Tests` | XLSX/CSV, kodlamalar, ayırıcılar, baştaki sıfır, bilimsel gösterim, 15 hane, satır numaraları |
| `AkelEtiket.PrintEngine.Tests` | TSPL komut yapısı ve kutupluluk, ofsetler, iş bölme/iptal, olmayan yazıcı, PDF sürücü testi |
| `AkelEtiket.Rendering.Tests` | Metin/barkod/görsel/şekil render, ekran = baskı, PNG DPI |
| `AkelEtiket.Tests` | Editör (geri al, z-sırası, pano, render önbelleği), dönüşüm matematiği, yakalama, **Excel'den 120 satırlık uçtan uca toplu baskı** |

## v2: Yapay zekâ ile tasarım altyapısı

Yapay zekâ fareyi taklit etmez; tasarım modelini **araçlarla** doğrudan değiştirir. Araçlar (`AkelEtiket.Design.Tools`)
tek kaynaktır: yerel API (`/api/tools`), MCP (stdio köprüsü ve `/mcp`) ve uygulama içi asistan aynı araç kaydını
kullanır. Ayrı bir çizim motoru yoktur; önizleme, doğrulama ve baskı ortak render motorundan geçer.

| Konu | Uygulama |
|---|---|
| Kimlikler | `dosya:<Takma ad>/<yol>`, `acik:<n>` (masaüstünde açık belge), `taslak:<id>`, `yeni:<id>` |
| Sürüm | Dosya: içerik SHA-256 özeti; açık belge: `s<oturum>-r<değişiklik>`; taslak/yeni: `r<n>`. Değiştiren araçlar `expected_version` alır |
| Onaylı mod | Var olan tasarım yalnızca taslakta değişir → `submit_draft` öneri oluşturur → kullanıcı panelde önce/sonra görüntüsünü görüp kabul eder → `DocumentSession.ReplaceContent` ile **tek geri alma adımı** |
| Birleştirme | Taslak açıldıktan sonra kullanıcı da değiştirdiyse öğe düzeyinde üç yönlü birleştirme; aynı öğede iki taraflı değişiklik çakışma olarak gösterilir |
| Doğrudan mod | Açık belgede değişiklik kopyada denenir, başarılıysa tek açıklamalı geri alma adımı olarak uygulanır; dosyalarda yazmadan önce yedek alınır |
| Barındırma | Uygulama açıksa API'yi uygulama, kapalıysa Worker barındırır (kilit dosyası + `baglanti.json` keşfi). Uygulama açılınca Worker'dan devralır |
| Güvenlik | Yalnızca 127.0.0.1, Bearer anahtar (sabit zamanlı karşılaştırma), Host başlığı ve Origin denetimi; izinli klasörler (bağlantı/kavşakla kaçış engelli); kilitli öğe ve dosya silme/üzerine yazma koruması; dış veri "untrusted" işaretli; işlem günlüğü; yapay zekânın baskı aracı yok |
| Uygulama içi asistan | Sağlayıcıdan bağımsız (`IChatModel`): Anthropic Messages API veya OpenAI uyumlu uç nokta. Açık belgenin taslağında çalışır, sonucu öneri olarak sunar; veri gönderimi için sağlayıcıya özel açık izin; API anahtarı DPAPI ile şifreli |

Bağlantı rehberi: [GENEL-URUN.md](GENEL-URUN.md).

## v2: Yayın düzeni

Masaüstü uygulaması, `AkelEtiket.Mcp.exe` ve `AkelEtiket.Worker.exe` aynı klasörde, kendi .NET çalışma zamanıyla
(self-contained) yayınlanır. Her biri önce ayrı ara klasöre yayınlanır, sonra birleştirilir; ortak dosyalarda
masaüstü uygulamasının sürümleri kazanır (ör. WPF'nin istediği `System.IO.Packaging` 10.x) ve `build.ps1` bunu
dosya özetleriyle doğrular.

