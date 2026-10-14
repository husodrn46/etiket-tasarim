# Üçüncü taraf bileşenler ve lisanslar

Bu genel sürümün kaynak kodu lisansı public yayın öncesinde kesinleştirilmelidir. Aşağıdaki liste yalnız bağımlılıkların lisanslarını açıklar; kaynak kod için kendiliğinden lisans vermez.
Hepsi **ticari kullanıma izin veren** lisanslardır; ücretli lisans, abonelik veya çevrimiçi aktivasyon gerektirmez.
Sürümler ve lisans ifadeleri 13.10.2026'da nuget.org üzerinden, uygulamanın gerçekte paketlenen bağımlılıklarından
(`project.assets.json`) doğrulanmıştır.

| Bileşen | Sürüm | Lisans | Kullanım |
|---|---|---|---|
| .NET 10 çalışma zamanı + WPF | 10.0 | MIT | Uygulama altyapısı (kurulum paketine dahil) |
| ASP.NET Core çalışma zamanı (Kestrel) | 10.0 | MIT | Yerel API (yalnızca 127.0.0.1; kurulum paketine dahil) |
| SkiaSharp (+ NativeAssets.Win32) | 3.119.4 | MIT (Skia: BSD-3-Clause) | Çizim, metin, görsel çözme |
| ZXing.Net | 0.16.11 | Apache-2.0 | Barkod kodlama ve okunabilirlik kontrolü |
| ClosedXML | 0.105.1 | MIT | Excel (XLSX) okuma |
| ClosedXML.Parser | 2.0.0 | MIT | ClosedXML bağımlılığı |
| DocumentFormat.OpenXml (+ Framework) | 3.1.1 | MIT | ClosedXML bağımlılığı |
| ExcelNumberFormat | 1.1.0 | MIT | ClosedXML bağımlılığı; eski Excel (.xls) hücre biçimleri de bununla uygulanır |
| RBush.Signed | 4.0.0 | MIT | ClosedXML bağımlılığı |
| **SixLabors.Fonts** | **1.0.1** | **Apache-2.0** | ClosedXML bağımlılığı — **bilerek sabitlendi** (aşağıya bakın) |
| ExcelDataReader | 3.9.0 | MIT | Eski Excel (.xls, Excel 97–2003) okuma; bağımlılığı yok |
| CsvHelper | 33.1.0 | MS-PL veya Apache-2.0 | CSV okuma |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | MVVM altyapısı |
| Svg.Skia | 4.9.1 | MIT | SVG görselleri ve hazır simgeler |
| Svg.Model / Svg.Animation / Svg.SceneGraph | 4.9.1 | MIT | Svg.Skia bağımlılıkları |
| Svg.Custom (SVG.NET) | 4.9.1 | MS-PL | Svg.Skia bağımlılığı |
| ShimSkiaSharp | 4.9.1 | MIT | Svg.Skia bağımlılığı |
| ExCSS | 4.3.1 | MIT | SVG içindeki CSS |
| HarfBuzzSharp (+ NativeAssets.Win32) | 8.3.1.3 | MIT | Svg.Skia bağımlılığı (metin şekillendirme) |
| **ModelContextProtocol** (+ .Core, .AspNetCore) | 2.2.0 | **Apache-2.0** | Resmî C# MCP SDK: yapay zekâ ajanlarına tasarım araçlarını sunar |
| Microsoft.Extensions.AI.Abstractions | 10.8.3 | MIT | MCP SDK bağımlılığı |
| Microsoft.Extensions.* (Hosting, Logging, Configuration, DependencyInjection, Options…) | 10.0.10–10.0.12 | MIT | MCP köprüsü ve Worker altyapısı |
| System.Security.Cryptography.ProtectedData | 10.0.12 | MIT | Yapay zekâ servis anahtarını Windows DPAPI ile şifreli saklama |

Yalnızca geliştirmede kullanılan araçlar (uygulamayla dağıtılmaz): xUnit (Apache-2.0), Microsoft.NET.Test.Sdk (MIT),
coverlet (MIT), **Inno Setup** (Inno Setup License — ticari kurulum dosyaları oluşturmak dahil ücretsiz),
xlwt 1.3.0 (BSD; yalnızca `tests/AkelEtiket.DataImport.Tests/Fixtures/eski-excel.xls` test dosyasını üretmek için, isteğe bağlı).

Yapay zekâ sağlayıcıları (Anthropic Claude, OpenAI uyumlu yerel modeller) için SDK kullanılmaz; bağlantı .NET'in kendi
`HttpClient`'ıyla yapılır. Bu servisler **isteğe bağlıdır**, varsayılan olarak kapalıdır ve kullanılırsa kendi hizmet
şartlarına tabidir; uygulamanın çalışması için hiçbiri gerekmez.

Simgeler: Windows'la gelen *Segoe Fluent Icons / Segoe MDL2 Assets* yazı tipi kullanılır (uygulamayla dağıtılmaz,
Windows'ta zaten bulunur) ve projeye özel çizilmiş vektör simgeler. Hazır etiket simgeleri (gıdaya uygun, bulaşık
makinesi, geri dönüşüm, koli işaretleri vb.) bu proje için çizildi; ticari marka içermez (Yeşil Nokta, CE gibi işaretler
bilerek yoktur). Hazır örnekler şirket logosu içermez.

## SixLabors.Fonts neden 1.0.1'e sabit?

ClosedXML, sütun genişliği hesapları için SixLabors.Fonts'a bağımlıdır ve [1.0.0, 3.0.0) aralığını kabul eder.
SixLabors.Fonts **1.0.x Apache-2.0**'dır; **2.0 ve sonrası "Six Labors Split License"** ile dağıtılır ve belirli
ciro üzerindeki şirketlerin ticari kullanımı için ücretli lisans gerektirebilir. Bu yüzden `Directory.Packages.props`
içinde `CentralPackageTransitivePinningEnabled` ile 1.0.1'e sabitlenmiştir. **Yükseltmeyin.**

## Lisans metinleri

Her paketin tam lisans metni NuGet paketinin içinde ve proje sayfasında bulunur:
- MIT: https://opensource.org/license/mit
- Apache-2.0: https://www.apache.org/licenses/LICENSE-2.0
- MS-PL: https://opensource.org/license/ms-pl-html
