# Document tabs and interface motion

[English](#english) · [Türkçe](#türkçe)

## English

LabelHarbor can keep up to 32 labels open. Use **Open** to select several `.akelabel` files, drag files into the editor, or create a new label. Opening the same saved file activates its existing tab and keeps unsaved edits.

Each tab has its own design, undo history, selected elements, data table, row quantities, preview record, zoom, scroll position and recovery copy. Switching tabs never closes a document. A small accent dot marks unsaved changes. Hover over a tab to see its path. Right-click to pin a tab, close it, or close other unpinned tabs.

| Shortcut | Action |
| --- | --- |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous document |
| Ctrl+W | Close the current tab, with a save prompt if needed |
| Ctrl+Alt+S | Save all changed labels; cancel stops the remaining saves |
| Ctrl+Shift+D | Duplicate the label and its data-row selection into a new unsaved tab |
| Ctrl+Shift+F | Toggle focus mode to give the label more space |
| Ctrl+D | Duplicate selected elements in the current label |

**Save all** and **Duplicate label in a new tab** are also in the file menu beside Save. Closing the application asks about every changed document. Cancelling any prompt keeps all documents open. Save As refuses to overwrite a file currently open in another tab. Pinned tabs stay open when using Close other tabs; they can still be closed explicitly.

After a crash, recoverable labels can open together in separate tabs. Recovered work remains marked unsaved until you save it. Unreadable recovery copies are preserved for investigation.

In **Settings**, switch off **Open labels in separate tabs** for the previous single-document behavior. Existing tabs stay open until closed. This preference does not alter saved label formats.

The interface keeps the native Windows title bar, light/dark themes and your chosen accent. The toolbar wraps when there is less room. Short feedback transitions apply to controls, panels, document switches, dialogs and theme changes. They do not change label dimensions, artwork positions, barcode rendering or print coordinates. Turn off **Enable interface animations** for immediate feedback. Windows reduced motion and high contrast settings take precedence and clear running animations.

AI/MCP document tools address each open document by its session ID. Background data imports stay with the originating tab. A tab switch during history-data loading cancels that automatic reprint flow; review the original tab before printing again. No cloud account, API key or paid dependency is added.

## Türkçe

Aynı anda en fazla 32 etiket açılabilir. Aç penceresinden birden fazla `.akelabel` seçebilir, dosyaları sürükleyebilir veya yeni etiket oluşturabilirsiniz. Aynı dosyayı yeniden açmak, kaydedilmemiş değişiklikleri koruyarak mevcut sekmeye geçer.

Her sekmenin tasarımı, geri alma geçmişi, seçimi, veri tablosu, satır adetleri, önizleme kaydı, yakınlaştırması, kaydırma konumu ve kurtarma kopyası ayrıdır. Renkli nokta kaydedilmemiş değişikliği gösterir. Sağ tıklayarak sekmeyi sabitleyebilir veya diğer sabitlenmemiş sekmeleri kapatabilirsiniz.

Ctrl+Tab / Ctrl+Shift+Tab sekmeler arasında geçer; Ctrl+W sekmeyi kapatır; Ctrl+Alt+S tüm değişen etiketleri kaydeder; Ctrl+Shift+D etiketi ve veri satırı seçimini yeni sekmeye kopyalar; Ctrl+Shift+F odak modunu açar. Ctrl+D mevcut etiketteki öğeleri kopyalamayı sürdürür.

Uygulamayı kapatırken her değişen etiket için kayıt sorulur. İptal seçilirse bütün sekmeler açık kalır. Başka sekmede açık bir dosyanın üzerine Farklı Kaydet ile yazılamaz. Kurtarılan etiketler kaydedilene kadar değişmiş olarak görünür; okunamayan kurtarma dosyaları korunur.

Ayarlar bölümünde sekmeli açmayı ve arayüz animasyonlarını ayrı ayrı kapatabilirsiniz. Windows hareket azaltma ve yüksek karşıtlık ayarları önceliklidir. Animasyonlar etiketin ölçülerini, öğe konumlarını, barkod üretimini veya baskıyı değiştirmez. Mevcut veri yolları, dosya uzantıları ve eski etiket uyumluluğu korunur.
