# LabelHarbor

[English](README.md) · [Türkçe](README.tr.md)

An offline Windows desktop app for designing labels and barcodes. Bring your own company name, logo,
address, templates and Excel/CSV data. No account or subscription is required. Licensed under the [MIT License](LICENSE).

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/workspace-dark.png">
  <img alt="LabelHarbor: independent label tabs and the native Windows editor" src="docs/images/workspace-light.png">
</picture>

## Download

[Windows x64 installer](https://github.com/husodrn46/etiket-tasarim/releases/download/v2.1.0/LabelHarbor-Setup-2.1.0.exe)
· [Portable ZIP](https://github.com/husodrn46/etiket-tasarim/releases/download/v2.1.0/LabelHarbor-2.1.0-win-x64.zip)
· [Release and checksums](https://github.com/husodrn46/etiket-tasarim/releases/tag/v2.1.0)

For Windows 10/11 x64; the .NET runtime is included. Install or extract the ZIP and open `Etiket Tasarim.exe`.

Version 2.1.0 adds independent document tabs, save all, pin/duplicate commands, focus mode and optional
interface animations. English/Turkish UI, personalization, favorites, regional label formats, starter packs
and import profiles are included. See [validation scope](docs/RELEASE-READINESS.md) for automated evidence
and hands-on printer/DPI checks.

## Work with several labels

Open several files in tabs, keep their data and undo history separate, and save all with Ctrl+Alt+S.
Use Ctrl+Tab to switch, Ctrl+W to close, Ctrl+Shift+D to duplicate a label and Ctrl+Shift+F for focus mode.
[Read the tab and motion guide](docs/DOCUMENTS-AND-MOTION.md).

## Get started

The development branch adds a [production workspace](docs/PRODUCTION-WORKSPACE.md) with local job recipes,
whole-job quality checks and batch PDF/PNG output. It is not included in the 2.1.0 downloads above.
See the [six-stage product roadmap](docs/ROADMAP-3.0.md).

1. Open the app and complete the setup wizard. Choose **English**, **Türkçe**, or **System / Otomatik**.
   Restart after changing language. New installations use English unless the system UI language is Turkish;
   existing completed installations keep Turkish until you choose another language.
2. Enter your company details and optionally a logo. Generate new company templates; existing designs stay intact.
3. Choose a template or create a label in millimeters. Edit text, images, shapes, barcodes and QR codes.
4. For batch labels, load Excel/XLS/CSV in **Data** and use **Map columns**. For repeated product printing,
   import a product list once, then use **Quick Print** to search or scan a product and enter quantity.
5. Select your printer, check media size and preview, then calibrate and print a small test before a batch.

[Read the English user guide](docs/USER-GUIDE.md) · [Internationalization and compatibility](docs/INTERNATIONALIZATION.md)

## Personalization and starter workflows

Choose an accent, interface scale, new-text font, startup template and remembered panels. Favorite templates
and print jobs, choose regional date/number/currency formats per label, and reuse Excel/CSV mapping profiles.
Four starter packs provide eight templates with fictional data and bilingual recipes.
[English workflow guide](docs/WORKFLOWS.md) · [Türkçe kullanım](docs/WORKFLOWS.tr.md).

## Features

- English and Turkish interface resources for setup, editor tools, data mapping, print dialogs, help and common messages.
- Drag-and-drop layout in mm; text, images, shapes, symbols, layers, grouping and undo/redo.
- 13 barcode types, dot-aligned 1-bit rendering, barcode decoding checks, PNG/PDF export.
- Excel/XLS/CSV fields, row quantities, product catalog, persistent counters, print history and reprint.
- Direct TSPL RAW and Windows driver printing; media/printer profiles and calibration tools.
- Portable `.etiketyedek` backups of company settings, templates and products.
- Optional AI/MCP integration, disabled by default. No new service, account or API key is provisioned.

English sample files: [products](samples/sample-products.csv), [shipping](samples/sample-shipping.csv).
They contain fictional demonstration data. Sample barcode check digits do **not** establish GS1 ownership;
replace all demonstration identifiers with your own assigned values before production. The Turkish samples
also include deliberately invalid data for testing error reporting.

## Your data and GS1

The interface language does not translate product names, company information, imported cells, saved designs,
or barcode contents. Prices in English examples use raw text with no assumed currency. Measurements follow
your operating system's regional decimal convention; labels remain in mm.

Company identity, GLN, GCP and registered GTINs start empty. **A GLN identifies a location or party; it is not
a product GTIN or a GS1 Company Prefix.** The app never derives identifiers from a company name or GLN.
SSCC generation requires your own GCP verified against your GS1 membership document.

Files are stored locally under `Documents\Etiket Tasarım`, `%APPDATA%\Etiket Tasarım` and
`%LOCALAPPDATA%\Etiket Tasarım`. Set `ETIKET_VERI` to isolate all application data under a separate root.
Stable folder names and the `.akelabel` format are preserved across languages. Other installations are not
imported automatically. Review a support package or backup before sharing it: it may contain your company or product data.

## Build and validation

C# / .NET 10, WPF, CommunityToolkit.Mvvm, SkiaSharp, ZXing.Net, ClosedXML, ExcelDataReader and CsvHelper.
The desktop app requires Windows. Many shared libraries and headless tests can run on macOS/Linux.

```powershell
dotnet restore AkelEtiket.sln
dotnet build AkelEtiket.sln
dotnet test AkelEtiket.sln
powershell -ExecutionPolicy Bypass -File build.ps1
```

A local self-contained Windows x64 installer and ZIP can also be cross-built with NSIS:

```text
python tools/distribution/build_candidate.py --dotnet <dotnet-path> --makensis <makensis-path> --cache <NuGet-cache> --output <new-output-folder>
```

Build commands produce local files; they do not publish or upload. The candidate builder verifies the
English resource satellite is included. For Windows acceptance, see `tools/distribution/windows_acceptance.ps1`.
Preparing that script or cross-building a package does not mean it has been executed on Windows.

Project-owned source code is licensed under the [MIT License](LICENSE). Dependency licenses and notices are included
in distribution packages; they retain their own terms. The product name is LabelHarbor.

[Architecture (Turkish)](docs/MIMARI.md) · [Generalization scope (Turkish)](docs/GENEL-URUN.md)
· [Dependency licenses (Turkish)](docs/LISANSLAR.md)
