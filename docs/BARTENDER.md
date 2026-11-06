# Importing BarTender labels (.btw)

[Türkçe](BARTENDER.tr.md)

LabelHarbor opens label files made with **BarTender 10.1 and 2016** (`.btw`) and converts them into editable
LabelHarbor labels. The original file is only read; it is never changed, and nothing inside it (scripts,
database connections, printer commands) is executed.

## Open one label

- **Open** accepts `.btw` files next to `.akelabel` files. You can also drag a `.btw` file onto the window, or
  use **More (▾ next to Save) → Open BarTender label…**.
- The label opens in a new, unsaved tab. **Save** stores it as a normal `.akelabel` project.
- If something could not be converted exactly, a short list is shown once. Check it before printing.

## Convert a whole folder

**More → Convert BarTender folder…** asks for a folder and converts every `.btw` file in it, including subfolders.

- Output: `Documents\Etiket Tasarım\BarTender'dan aktarılanlar\<folder name>`, with the same subfolders.
- Existing files are never overwritten; delete a converted file and run it again to convert that file again.
- `Aktarım raporu.txt` (import report) in the output folder lists the files that need a look.
- Converted labels are saved with a preview image.

## What is converted

| BarTender | LabelHarbor |
|---|---|
| Template size | Label width and height (one label) |
| Text | Text with the same font, size, bold/italic/underline, rotation, alignment and position. Character width (for example 94.8%) is kept. Fixed-width text is broken into lines where BarTender breaks it. |
| Text border | Rectangle with the same thickness and rounded corners |
| EAN-13 | EAN-13 with the same data, bar height and position. The module width is rounded to whole dots of the printer resolution stored in the file, as BarTender does, so bars print in the same place on the same printer. |
| Picture | The image embedded in the file (PNG, JPEG, GIF, BMP) at the same size and rotation |
| Box | Rectangle; cut (chamfered) corners become rounded corners |

Positions follow BarTender's anchor point (one of nine points of the object) and rotation.

## Limits

- **Lines** are not converted. In the files this importer was tested with, BarTender did not print them.
  Add a line with Shape → Line if you need one.
- Only the **current value** of text and barcodes is imported. Data sources, serial numbers, prompts and
  database links are not.
- Barcode types other than EAN-13 are recognized by name, but their size has not been verified; check them.
- BarTender can print several labels **side by side** on one roll (for example 5 × 20 mm on a 104 mm roll).
  LabelHarbor prints one label at a time; the import note tells you when this applies. Check the printer's media settings.
- If a font is not installed, LabelHarbor uses Arial and says so.
- Text drawn by LabelHarbor and by BarTender can differ in length by a few percent because the two use
  different font hinting.
- The `.btw` format is not documented. The importer reads the parts needed for labels and reports anything
  it skips. Always print a test label before a batch.

Labels that use a character width other than 100% are saved as format 4. Older LabelHarbor versions open
them with a "newer version" note and draw that text at normal width.

## How it was checked

The importer was developed against 610 real labels from BarTender 10.1 SR4 and 2016 R7 (20 × 40 mm,
12.5 × 35 mm and 100 × 150 mm; text, EAN-13, rounded borders, logos). Every file was converted, drawn by
LabelHarbor at 300 dpi and compared with the preview image BarTender stores inside the file. The median
ink match was 97.5%, and none of the converted labels had text cut off by its box. Those customer files are
not part of the repository; the automated tests use small synthetic `.btw` files with the same structure.
