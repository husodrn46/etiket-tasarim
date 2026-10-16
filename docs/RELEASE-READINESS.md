# LabelHarbor 2.1.0 validation scope

The release adds independent document tabs, save-all/duplicate/pin commands, focus mode and short native WPF feedback transitions. English and Turkish guides are in [Documents and motion](DOCUMENTS-AND-MOTION.md). Source code is MIT licensed; dependency terms and full notices accompany each package.

## Compatibility

The `Etiket Tasarim.exe` filename, installer identities, data folders, `ETIKET_VERI` and `.akelabel` extension stay stable. Existing labels and settings are preserved. Other company installations are not imported or modified. No new label format is required by tabs or motion. The preceding opt-in regional formats still use format 3; older labels retain their minimum required format.

Assembly/file versions are numeric `2.1.0.0`; the visible app and installer version is `2.1.0`. Packages are unsigned, so Windows may display an unknown-publisher warning.

## Evidence

Release attachments `VALIDATION.json` and `SHA256SUMS.txt` are the source of truth for the exact source revisions, package hashes, automated results and remaining limits. Historical `docs/VALIDATION-*.json` files describe their original builds.

The Mac checks cover ten headless test projects, independent document sessions, async import/reprint isolation, save cancellation/collisions, recovery, duplicate/pin behavior, settings persistence and four processes writing 320 history records. Windows CI runs the full solution tests, including Windows-only DPAPI/native/local-MCP cases. Hardware-dependent probes may return early when a device is absent; their test counters do not prove physical printing.

The native WPF smoke runner opens isolated windows, switches real tab controls, invokes focus mode through UI Automation, checks cancellation and reduced motion, and captures English light/dark/settings layouts. Results record actual window size and DPI. A 2x raster capture is a capture scale, not a physical 200% monitor DPI test.

The package verifier checks hashes, ZIP CRC, bundled runtime, dependency merge, notices, known private-data patterns and package-owned uninstall paths. Windows package acceptance additionally downloads exact hash-pinned installer/ZIP assets, installs a prior preview, upgrades, starts the packaged app, rolls back, reinstalls, starts the portable app and checks uninstall preservation of settings/user/unknown files. Only a successful results file confirms execution of those steps.

## Practical limits

Physical printer/media calibration, printed barcode scanning, real monitor DPI changes, the interactive installer wizard and a full keyboard/screen-reader review require hands-on checks. Automated installation checks use silent mode on a clean Windows test user. Try a small print before a production batch. No signing certificate, paid service, account or API key was added.

## Türkçe

Sekmeler; tasarım, geri alma, seçim, veri, yakınlaştırma ve kurtarma durumunu ayrı tutar. Kayıt iptalinde belgeler korunur. Odak modu ve animasyon ayarı eklenmiştir; Windows hareket azaltma ve yüksek karşıtlık tercihleri önceliklidir. Eski veri yolları ve dosya uyumluluğu korunur.

Kesin test sayıları, kaynak kimlikleri ve paket özetleri sürümün `VALIDATION.json` ve `SHA256SUMS.txt` eklerindedir. Windows otomatik testleri ile fiziksel yazıcı kabulü ayrı değerlendirilir. Sessiz kurulum/yükseltme/geri dönüş/kaldırma ve gerçek WPF pencere kontrolleri otomatik sınanabilir; gerçek yazıcı, tarayıcı, monitör DPI geçişleri ve etkileşimli kurulum sihirbazı ayrıca denenmelidir. Paketler imzasızdır.
