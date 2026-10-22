# LabelHarbor 3: a local production workspace

[English](#english) · [Türkçe](#türkçe)

## English

The product direction is a complete offline workflow: **prepare data → choose a label → check the whole job → generate a package → print with an explicit operator action → inspect the result**. A solo shop and a larger warehouse should be able to use the same application with their own company information. Account creation, subscriptions and hosted infrastructure are outside this roadmap.

### Milestone 1 — Production workbench (implemented foundation; Windows acceptance pending)

Three parallel workstreams converge into one native Windows window:

- **Quality engine:** check every selected record at output resolution, identify missing fields/images, text overflow, label-edge errors and unreadable/overlapped barcodes; preserve source-row and element references. Cancellation and resource limits produce an explicitly incomplete result.
- **Output engine:** exact-size multi-page PDF and a PNG ZIP with per-record copy counts, progress, cancellation, bounded resource use and atomic destination writes. An error cannot leave a partial result masquerading as the final file.
- **Workflow shell:** named local job recipes, document/data/selection capture, quality findings, stale-check prevention and one guided export flow. Saving a recipe does not publish its local paths or data.

Acceptance gates: independent document state, unchanged printer/counter behavior, no allocation of GS1 identifiers during checks/exports, English/Turkish text, safe overwrite/cancel behavior, deterministic tests and native Windows layout proof. This is the foundation milestone, not a claim that all of version 3 is complete.

### Milestone 2 — Template systems

Reusable components, linked styles, controlled template versions, parameter forms and size variants. Conditional elements already exist; expand them through a readable rule editor with before/after preview. Keep old labels readable and require explicit migration for new file-format features.

Acceptance: component updates are previewed, reversible and never silently rewrite existing customer labels.

### Milestone 3 — Data workbench

Reusable preparation pipelines for Excel/CSV: column mapping, typed fields, date/number normalization, derived values, duplicate detection and validation rules. Explain row-level changes in a dry run before applying them. Add local database adapters only after concrete demand and credential-storage design.

Acceptance: original files stay intact, leading-zero identifiers remain strings, rules have test examples and data errors point back to their source rows.

### Milestone 4 — Operator and print center

Barcode-first operation, large operator controls, explicit reviewed queues, printer/media presets, repeatable calibration and recoverable job state. Integrate reservations for counters and user-owned GS1 ranges with the existing print transaction. Hardware confirmation is required before declaring printer support.

Acceptance: no automatic physical printing from file watching, startup, AI tools or recipe loading; cancellation/resume cannot duplicate allocated serial identifiers.

### Milestone 5 — Local integrations and extensions

A documented local CLI/API, versioned recipe/package formats, optional file-drop ingestion with approval, and a small extension surface for importers and exporters. Reuse existing local MCP tooling within its permissions; any cloud provider remains an explicit optional configuration.

Acceptance: the base app works without a network connection, plugin failures are isolated, and importing a recipe never executes arbitrary code.

### Milestone 6 — Global adoption and maintainability

English/Turkish operator guides, accessible keyboard flows, polished task-specific sample packs, contributor architecture docs, reproducible Windows builds, release evidence and a hardware compatibility matrix backed by real tests. Consider more languages and distribution channels after the first outside-user feedback.

Acceptance: a new user can install, customize, create a label, connect a synthetic sample and generate a package without reading developer documentation.

## Türkçe

Hedef: **veriyi hazırla → etiketi seç → işin tamamını kontrol et → çıktı paketi oluştur → operatör onayıyla yazdır → sonucu denetle** akışını tek yerel üründe toplamak. Ürün, kullanıcının kendi firma bilgileri ve kendi verileriyle çalışır.

İlk dilim üç ajanla paralel geliştiriliyor: kalite kontrol motoru, güvenli toplu çıktı motoru ve kayıtlı iş tariflerini birleştiren üretim ekranı. Tamamı kontrol edilmeyen bir iş hazır sayılmayacak; iptal/hata eski çıktı dosyasını koruyacak. Kontrol sırasında veya dışa aktarımda sayaç ilerletilmeyecek, SSCC ayrılmayacak ve yazıcıya iş gönderilmeyecek.

Sonraki büyük adımlar: tekrar kullanılabilir şablon bileşenleri ve sürümleri; veri hazırlama kuralları; barkod odaklı operatör ve baskı merkezi; belgelenmiş yerel entegrasyonlar; gerçek cihaz kanıtlarıyla global dağıtım ve erişilebilirlik. Her aşama kendi kabul kapısını geçecek. İlk dilimin tamamlanması, bu yol haritasının tamamının yapıldığı anlamına gelmez.
