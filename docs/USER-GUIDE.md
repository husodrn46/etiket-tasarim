# LabelHarbor user guide

[Overview](../README.md) · [Türkçe genel rehber](../README.tr.md)

This guide describes the source version with English/Turkish support. The earlier `v2.0.0-preview.1`
download is Turkish and does not contain this update. The desktop app runs on Windows x64. Installation,
WPF interaction and physical printing still need acceptance on Windows.

## Company setup and language

On first launch, complete Company details, Printer and size, then Save and start. A printer can be selected
later. Choose **English**, **Türkçe** or **System / Otomatik** under **Settings → Company details** (or the setup
wizard). Restart to apply a language change. The setting is local and saved with application settings.
English is the fallback for non-Turkish systems; existing completed installations default to Turkish for continuity.

Enter a company name, address, website and optional logo. The logo is stored as PNG in settings and embedded
in newly created templates. Use a complete `https://` website for a QR link. No company name, logo, GS1
identity, customer data or cloud account is preconfigured.

New company templates are separate copies. Changing your company profile does not rewrite older saved
labels. To get English bundled examples after switching from Turkish, restart first, then restore sample
templates or add company templates. Keep your existing files; translate their text yourself when appropriate.

## Designing a label

Choose a template or create a blank label with width and height in millimeters. Configure the media as gap,
black mark or continuous roll. The printer's maximum print width must cover the design.

Add text, barcode, QR/2D code, image or shape from the left panel. Drag to move; use handles to resize or rotate.
Use Ctrl+Z/Ctrl+Y to undo/redo and Ctrl+S to save a `.akelabel` project. Project files embed their image assets.
The file extension and existing file compatibility are unchanged by interface language.

Measurements display using the operating system's regional decimal separator. Both `12.5` and `12,5` are
accepted as decimal input. Do not use thousands separators in measurement fields. Barcode values and
Excel/CSV cells do not go through this measurement parser.

Images are converted to 1-bit black and white. Use the Logo method for sharp artwork and a photo/dithering
method for photos. Transparent areas become white. Preview is rendered at the configured printer resolution.
PNG/PDF export is available from the Save menu. PDFs use preview values for numbered fields; they do not
allocate production SSCCs or advance counters.

## Excel/CSV and field mapping

Load Excel (`.xlsx`, `.xls`) or CSV from the Data panel. Use Unicode text and keep barcode/code columns as
text in your spreadsheet to retain leading zeros. A number already rounded or truncated by a spreadsheet
cannot be reconstructed by changing the UI language.

Insert fields as `{ColumnName}`, for example `{ProductName}` and `{Barcode}`. Use **Map columns** to connect
your column names to a template's fields. Inspect the sample value before applying. Mapping is saved in the
design and can be undone with Ctrl+Z; it does not edit the source spreadsheet. Missing required fields and
invalid barcodes are reported per row; review the report before printing.

Use [sample-products.csv](../samples/sample-products.csv) with the English batch template. Columns are
`ProductCode`, `ProductName`, `Barcode`, `Price`, `Quantity`. `Quantity`, `Qty`, `Copies` and `Count` are recognized
as quantity headers, alongside the existing Turkish names. Change the selected quantity column if it is not
the number of labels you intend to print. English prices are raw strings without an assumed currency.

Built-in field tokens retain their existing names across both interface languages:

| Token | Meaning |
| --- | --- |
| `{SIRA:0000}` | Batch sequence, zero-padded |
| `{TARIH}` | Date in the existing `dd.MM.yyyy` format |
| `{TARIH:yyyy-MM-dd}` | Explicit ISO-style date |
| `{SAAT}` | Time in `HH:mm` |
| `{SAYAC:000000}` | Persistent label counter |
| `{SAYAC.Box:000000}` | Named persistent counter |
| `{SSCC}` | SSCC reserved at production print time |
| `{GLN}` | GLN entered in the GS1 profile |

Changing interface language does not change these tokens or the output of existing numeric/date/case
formats. Existing field formats use their original Turkish rules; prefer raw values for currency and add
an explicit format only after checking the result. Turkish `BUYUK`, `KUCUK`, `BASHARF` case transformations
also keep their original behavior. Some low-level import, render, printer and optional AI diagnostics remain
in Turkish; include the original message when reporting an issue.

## Quick Print

Import a product list once with **Product list → Import from Excel/CSV**. Open Quick Print (Ctrl+Shift+P),
choose a template, then search by product name, code or barcode. A scan/Enter selects a product only when
its code or barcode has one exact match across the full catalog. Missing or ambiguous scans clear the prior
selection; choose the correct result explicitly. Names and prices are not treated as scan identifiers.

Enter quantity and proceed to the Print window. Static templates that need no product data can be used
without selecting a product. Quick Print remembers the template; it still requires print confirmation.

## Printing and calibration

Select **Direct TSPL (RAW)** only for a printer that supports TSPL. Otherwise use the **Windows driver**.
Check roll size, media/gap, DPI, direction and margins. Built-in printer profiles are starting points, not
a guarantee of compatibility. Save your own profile after checking your printer.

Use sensor calibration only for gap/black-mark media. The command may feed blank labels. Print the ruler
calibration label, check the ▲ TOP direction and measure to the border. Expected left/top distances are
2 mm. Apply measured offsets, then print again. TSPL offsets do not affect Windows driver printing;
use driver paper settings for that method.

The print report separates rendered data, Windows queue submission and queue status. None of those proves
that a physical label printed correctly. Inspect size, alignment, direction and scanner readability yourself.
Start with a small quantity before a large job.

**Resume** selects labels that were not sent to the queue. A label sent to the queue might still fail at the
printer; inspect physical output before resuming or reprinting. Stopping in the app cannot recall labels
already received by the printer. A single-job submission prevents other jobs from interleaving.

## GS1 identifiers and numbering

GLN, GTIN and GCP are different identifiers. A GLN is not converted into a product GTIN or company prefix.
Company details do not generate GS1 codes. Enter only identifiers assigned to you. Demonstration codes are
fictional examples, even when their check digits are valid.

For SSCC printing, enter the GCP from your GS1 membership document and verify it against that document.
The app cannot establish ownership for you. With multiple computers, assign non-overlapping serial ranges.
SSCC/counter reservations are persistent; failed or cancelled allocations are not automatically reused.
Keep separate backups of these numbering records. A portable company backup does not contain them.

## Backups and local storage

Use **? → Create company, template and product backup** to save a `.etiketyedek` file. It includes company
settings/logo, templates and product catalog. Restore imports templates into a new folder and separately
preserves the previous product list. Existing templates remain intact; choose your printer locally afterwards.

Portable backups exclude external Excel/CSV files and row image folders, GS1/SSCC records, persistent counters,
print history and connection keys. Copy those separately when migrating, taking care not to reuse serial ranges.
Review backup contents before sharing because company and product details can be included.

Stable local folders are `Documents\Etiket Tasarım`, `%APPDATA%\Etiket Tasarım` and
`%LOCALAPPDATA%\Etiket Tasarım`. Internal subfolder names such as `Şablonlar`, `Ürünler`, `Belgeler`, `Ayarlar`
and `Görseller` remain unchanged in English. For an isolated trial, set `ETIKET_VERI` before launch.
Language selection never renames or migrates your data folders.

## Optional AI and troubleshooting

AI features are off by default. Ordinary design, barcode generation and printing work without any AI provider.
No account, API key or paid service is created by this app. If you deliberately configure a provider, its
terms apply; data sharing requires consent. Agents propose design changes and cannot send physical print jobs.
See the AI settings for connection details; never publish connection keys.

Use **? → Create support package** for diagnostics. The package is saved locally and is not uploaded.
Review it before sharing, especially if you included an open design. In a bug report, include app version,
UI language, regional settings, printer model/method, label size and the relevant report text.

## Personalization, favorites, regional formats and starter packs

See the [workflow guide](WORKFLOWS.md) for appearance settings, favorite templates/jobs, format-3 labels,
starter pack recipes and reusable import profiles. They are included in v2.0.0-preview.2 and later.
