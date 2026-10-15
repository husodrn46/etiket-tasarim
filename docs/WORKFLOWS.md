# Personalization and everyday workflows

[English](WORKFLOWS.md) · [Türkçe](WORKFLOWS.tr.md)

These features require a build from the branch containing this guide. The published `v2.0.0-preview.1`
binaries do not include them. Windows interaction and physical printing acceptance remain pending.

## Make the workspace yours

Open **Settings → Personalization**. Choose red, blue, teal or purple; interface scales 90%, 100%, 110%
and 125%; a font for newly inserted text; and an optional startup `.akelabel` template. Dark, light and
system themes are available. The system choice reads the Windows app theme at startup or when settings
are applied. Appearance changes apply after accepting settings; language changes still require restart.

The default font affects new text, text styles and inserted text fields. Existing designs retain their
fonts and layout. Interface scaling does not alter label dimensions, printer resolution or exported artwork.
**Remember panel layout** restores the selected tool panel, its open state and the AI panel's open state;
it does not enable an AI provider or send data. Window placement keeps its existing separate setting.

A startup template opens as a new unsaved copy. Command-line files and recovered work take precedence.
A missing/unreadable startup template produces a warning and leaves the editor usable. Changing a template's
name in the gallery updates its favorite/startup reference; deleting it clears those references.

## Favorites

In **Templates**, select a template and use **Add to favorites**. **Favorites only** narrows the gallery;
favorites also appear first in Quick Print. Use the inline **Open** button on a favorite to open it with one click.
Favorites are local path references, so moving files outside the app can leave unavailable references.

In **Print history**, favorite a job and use **Favorites only** to find it again. **Reprint** opens the
existing print confirmation (also available from the inline row action) with the saved design snapshot and data selection. Nothing prints merely
because it was favorited or opened. External source files must still exist; review changed data warnings.
Snapshots belonging to favorite jobs are excluded from age-based cleanup. The pin file is written atomically;
if it cannot be read, cleanup skips deletion rather than risking a favorite snapshot.

## Regional formats belong to the label

In **Label size / setup**, enable **Use label-specific formats**. Choose the output region, the region
used by numbers/dates in your source data, a date format, number format and optional currency code.
The preview uses fixed sample values. The selection is saved in the label and works independently of UI language.

Use explicit text field formats:

| Text field | Behavior |
| --- | --- |
| `{Price}` | Original cell text, unchanged |
| `{Price:NUMBER}` | Number parsed in the input region and displayed with the chosen number format |
| `{Price:CURRENCY}` | Same number with the chosen currency code and the output region's currency layout |
| `{Date:DATE}` | Date parsed in the input region and displayed with the chosen date format |
| `{Date+30:DATE}` | Same date shifted by 30 days |

Examples: input `1234.56` with **en-US** input and **de-DE** output becomes `1.234,56` with `N2`.
Choosing `EUR` prints a currency label; it does **not** convert prices or establish a currency for your data.
With no currency selected, `CURRENCY` uses the chosen number format. Barcode content, GS1 identifiers, raw
unformatted fields, conditions and counter allocation retain their existing sources. Explicit regional
formatting is applied to text rendering only. Preview, export and print rendering share the implementation.

Excel imports preserve the app's existing deterministic text conversion, generally `tr-TR` for decimal/date
cells. Choose the input region for the resulting imported text, not for the language of the Excel interface.
CSV text uses its actual source convention. Ambiguous dates such as `01/02/2026` follow the selected input
region. Invalid numeric grouping or dates produce errors; review them before production.

Older labels have no regional format profile and keep their prior behavior. A label with this option saves
as format **3**; labels without it still use the minimum required older format (1 or 2). Older app versions
cannot reproduce the new formats reliably. Keep a copy before opening such labels in an older version.
Changing the regional profile is undoable; disabling it restores the legacy behavior.

## Starter packs

Open **Templates → Starter packs**. Each pack shows a preview, a short recipe and an **Open sample folder**
action. **Add templates** creates two new templates and never overwrites existing files; repeated installation
adds numbered copies. Company information comes from your local settings. Inspect each label before printing.

| Pack | Templates | Sample schema |
| --- | --- | --- |
| Retail | Product 70×50, shelf 58×40 mm | ProductName, Price, Barcode, Quantity |
| Warehouse | Stock 100×50, location 70×35 mm | ProductName, SKU, Location, Quantity |
| Shipping | Full 100×150, compact 100×75 mm | Recipient, Address, TrackingNumber, Quantity |
| Small business | Batch 70×50, asset 70×35 mm | ProductName, SKU, Batch, Date, Quantity |

The sample CSVs and bilingual recipes live in `samples/packs/` in source, and `Örnekler/packs/` in a packaged
build. Column names stay English in both UI languages. All records are fictional. Internal Code 128 sample
codes are not GS1 allocations. Obtain production tracking numbers from your carrier; these templates do not
purchase postage or create shipments. Product/food labels do not certify regulatory compliance.

## Reusable import profiles

**Data → Map columns** suggests exact header matches, then unique normalized matches, then a small set of
English/Turkish aliases (for example `UrunAdi` → `ProductName`, `Fiyat` → `Price`, `UrunKodu` → `SKU`).
Ambiguous matches remain unselected. Review the visible source column and sample value before applying.
Mapping changes the design's field references in one undo step; it never writes to Excel or CSV.

Choose **Save profile…** to save the mapping plus delimiter, encoding, sheet and header row. Reusing a name
asks before updating that profile. Select the profile in the Data panel before loading a file to reuse its
parsing options, then review the mapping dialog. A missing column is cleared and must be selected manually.
Profiles can be reused on templates whose field names have already been mapped. Parsing options are hints,
not a promise that a supplier's changed file has the same schema.

Profiles store schema and parsing options only: no product rows, source file path, credentials or API keys.
They can still contain your chosen supplier/profile and column names. Preferences, favorites and profiles
remain local; new machine-specific workflow preferences are excluded from portable company backups.
The original source files, print history, serial registries and other installations are not modified by setup.
