# LabelHarbor 2.0.0-preview.2

[Download / İndir](https://github.com/husodrn46/etiket-tasarim/releases/tag/v2.0.0-preview.2)

LabelHarbor is the chosen product name. Project-owned source code uses the MIT License. Source attribution stays in NOTICE. Dependencies retain their
own license terms; full notices accompany the installer and portable ZIP. No paid service or account was added.

## Compatibility

The existing `Etiket Tasarim.exe` filename, internal namespaces, installer identities, data folders,
`ETIKET_VERI` and `.akelabel` extension remain stable. The Windows shortcut and visible product name are
LabelHarbor. Existing labels, settings, counters, templates and other company installations are not renamed,
imported or deleted. This release's opt-in regional formatting uses label format 3; older labels keep their
minimum required format. Test upgrades and removal on Windows before relying on them for production.

Assembly/file versions remain numeric `2.0.0.0`. The visible application version and installer display
version include `2.0.0-preview.2`. The installer uses a numeric version resource separately from that display.

## What has been checked

The preceding workflow change passed 979 selected headless tests across ten projects. The branding branch
reruns the same suites and the four-process/320-record history check. Exact final results and binary hashes
are attached to the release as VALIDATION.json and SHA256SUMS.txt. Historical docs/VALIDATION-*.json files
remain evidence of their original commits, not proof of this build.

The local builder produces self-contained Windows x64 apphosts, verifies dependency merging, includes the
MIT file, dependency notices, English satellite resources and all pack data/guides. The verifier checks ZIP
CRC/file hashes, runtime versions, installer hash, package-owned uninstall paths and known private-data
patterns. It does not execute Windows or prove physical printing. Packages are unsigned previews.

## Still pending

Windows installation/upgrade/uninstall, WPF layout and DPI, keyboard/accessibility, DPAPI, spooler/native
integration, and physical media/barcode-scanner acceptance remain pending. The user deferred these to
16 October 2026; no execution time or automatic test run was scheduled. The current source includes an
interactive Windows acceptance helper; hardware checks must be performed on the actual printer.

## Türkçe

Ürün adı LabelHarbor, kaynak lisansı MIT olarak seçildi. Bağımlılıklar kendi lisanslarını korur.
İndirme paketleri İngilizce/Türkçe arayüzü ve yeni kullanım özelliklerini içerir. Veri yolları, eski EXE adı
ve `.akelabel` uyumluluğu korunur. Sürüm bilgisi `2.0.0-preview.2` gösterir; dört sayılı dosya sürümü ayrıdır.

Yerel derleme, otomatik test, dosya bütünlüğü ve bilinen özel veri kalıpları kontrol edilir. Windows kurulum,
arayüz ve gerçek yazıcı testleri 16 Ekim'e bırakılmıştır. Bu nedenle yayın ön sürüm olarak sunulur; tamamlanmış
Windows kabulü veya üretime hazır kararlı sürüm olarak tanıtılmaz. Sonuç ve özetler sürüm eklerindedir.
