# Production workspace / Üretim çalışma alanı

**Development branch feature. This workspace is not included in the downloadable 2.1.0 release.**

[Validation scope and test counts](VALIDATION-PRODUCTION-WORKSPACE.json) · [Product roadmap](ROADMAP-3.0.md)

## English

Open **Production workspace** from the toolbar, the file menu, or **Ctrl+Shift+B**.

1. Open a label. For variable labels, load your Excel/CSV file, map its columns, select rows and set each row's copies in the existing Data panel.
2. In the workspace choose **Use selected data rows**, or turn it off and enter the number of identical labels. Labels containing `{SIRA}` receive successive sequence values in a data-free job. With data, sequence values advance per selected record; copies repeat that record's label.
3. Choose a starting number and **Check quality**. The workspace checks every included record at the current output resolution. Findings identify the source row and label element. Missing fields/images, text overflow, invalid or unreadable barcodes, out-of-bounds elements and other render problems are reported. Software verification cannot replace a test on the actual printer and scanner.
4. Resolve errors in the editor. Warnings require review. Cancelled, incomplete or resource-limited checks never enable export. Selected error rows cannot silently disappear from a successful job; correct them or explicitly deselect them in Data.
5. Choose **PDF** or **PNG ZIP**, then **Save output**. PDF uses one exact-size page per physical label, including every copy. The ZIP stores each distinct monochrome image once and includes a manifest connecting records, sequence values and quantities to images. It does not include raw source rows, credentials or source file paths. Visible label content is naturally present in the images and PDF.

Export is written to a temporary file in the destination directory and committed only on success. Cancellation and failures preserve an existing destination. Replacing a destination requires confirmation. Exporting does not send anything to a printer.

### Reuse a local job

**Save recipe** saves the current label first, then records its local file path, data file reference, actual sheet/header/encoding/delimiter options, selected source row numbers, copy counts and output options. It stores no cell values or account secrets. Recipes are private local settings, not portable templates; the referenced files must still exist on that computer. Multiple recipes can reference the same label.

**Open recipe** loads those local sources and restores the selection. If the data file's size or modification time changed, review the warning: stored row numbers may now identify different products. Changing an import profile later does not change the recipe's captured parsing settings. Column mapping is retained in the saved label itself. Existing unsaved tabs keep their normal save/cancel protections.

Opening a recipe always requires another quality check. Editing the document, changing the active tab, replacing data, changing row selections/copies or changing the job settings invalidates readiness. The checked job and export share frozen field values and external image bytes. A later job starts a fresh snapshot.

### Current limits

- Up to 10,000 labels per job, 16 million pixels per label, and explicit render/field/image work budgets. A job that exceeds a budget must be split or simplified; the app does not sample it and call it complete.
- Up to 200 recipes and an 8 MiB local recipe file. A malformed or newer-format recipe file is preserved and reported rather than overwritten.
- `{SSCC}` and `{SAYAC}` jobs stay in the existing **Print** workflow, which owns number allocation. The workspace never reserves serial numbers. GLN and GTIN remain distinct identifiers.
- Native Windows layout, keyboard, high-DPI and physical output acceptance remain separate release gates. The branch does not change the stable installer or portable ZIP.

## Türkçe

**Geliştirme dalındaki özelliktir; indirilebilir 2.1.0 paketinde henüz yoktur.**

Araç çubuğu, dosya menüsü veya **Ctrl+Shift+B** ile **Üretim çalışma alanı**nı açın.

1. Etiketi açın. Değişken veri kullanıyorsanız Excel/CSV dosyasını yükleyin, sütunları eşleştirin; Veri panelinde satırları ve adetleri seçin.
2. **Seçili veri satırlarını kullan** seçeneğini açın veya verisiz iş için adedi girin. Verisiz `{SIRA}` etiketleri sırayla numaralanır. Veri kullanımında sıra her seçili kayıtta ilerler; aynı kaydın kopyaları aynı etiketi tekrarlar.
3. **Kaliteyi denetle** ile işin tamamını kontrol edin. Hata ve uyarılar kaynak satır/öğe bilgisiyle gösterilir. İptal, kaynak sınırı, eksik denetim veya seçili hatalı satır, hazır sonucu üretmez.
4. Hataları düzeltin, uyarıları inceleyin ve yeniden denetleyin. PDF veya PNG ZIP seçerek çıktıyı kaydedin. PDF her kopyayı gerçek etiket ölçüsünde ayrı sayfaya koyar; ZIP, benzersiz görselleri ve adet manifestini içerir. Kaynak veri dosyası ve gizli hesap bilgileri pakete eklenmez; etiket üzerinde görünen içerik görsellerde/PDF'de bulunur.

**Tarifi kaydet**, etiketin kayıtlı dosyasını, veri bağlantısını, gerçek sayfa/başlık/kodlama/ayırıcı ayarlarını, seçili satır numaralarını ve adetleri yalnız bu bilgisayarda saklar. Etiket değerlerini saklamaz. Sütun eşlemesi kayıtlı etiket tasarımındadır. **Tarifi aç**, bu kaynakları yükleyip seçimi geri getirir; veri dosyası değişmişse satırların farklı ürünlere karşılık gelebileceği uyarısını gösterir. Dosyalar aynı bilgisayarda erişilebilir kalmalıdır.

Tarif açmak fiziksel baskı başlatmaz. Her açılışta yeni kalite denetimi gerekir. Tasarım, aktif belge, veri, seçim veya iş ayarı değişince hazır durumu iptal olur. Denetim ve çıktı aynı dondurulmuş veri/görselleri kullanır.

Bir iş en fazla **10.000 etiket** içerir. Daha ağır işler açık kaynak sınırlarıyla durdurulur ve bölünmelidir. Sayaç/SSCC kullanan işler numara ayıran mevcut **Yazdır** ekranında kalır. Gerçek Windows arayüzü, DPI ve yazıcı/tarayıcı kabulü ayrıca yapılmalıdır.
