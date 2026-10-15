# English/Turkish interface and compatibility

## Scope

The source adds an explicit UI resource catalog in `AkelEtiket.Core/Localization/UiStrings.resx` (Turkish)
and `UiStrings.en.resx` (English). WPF static labels use `TextExtension`; application-owned view-model
messages use `UiText.T`. Interpolated messages retain argument values and numeric format specifiers.
These mechanisms are applied at authored UI strings, never to a visual tree, database, imported value or saved document.

Setup, editor menus/tools, settings, common data and print dialogs, help text and new English sample templates
are covered. Low-level library/driver/import diagnostics and parts of the optional AI integration can still
return Turkish messages. An English interface is not a complete translation of every internal diagnostic.

## Selection and compatibility

`AppSettings.UiLanguage` accepts `auto`, `en`, `tr`. New settings normalize to `auto`; an older completed
installation without the setting normalizes to `tr`. Explicit language choice survives JSON persistence.
`auto` selects Turkish for a Turkish system UI and English otherwise. Apply at startup; restart after changing.
Startup reuses the settings object already loaded so corrupt-file diagnostics are not lost to a second load.
The compatibility-only v1 importer preserves Turkish; automatic import from other installations remains disabled.

Only `CurrentUICulture` changes. `CurrentCulture` remains the operating system's regional culture. NumberBox
measurements use a dedicated finite-number parser with no grouping support, accepting decimal dot/comma.
Barcode strings, raw cells, record formatting, built-in date/sequence/counter fields, serial allocation and
print coordinates keep their prior behavior. No currency, country, company prefix or GS1 ownership is inferred.

Namespaces, assembly/executable identities, installer registry keys/AppId, folder names and `.akelabel` remain
stable. The visible English title is "Label Designer"; this is a translation of a provisional name, not a new
brand decision. Catalog category keys remain stable and are displayed through localized key/value choices.
English template names are recognized in the same category filter.

English examples are created from fresh bundled projects before user company details are applied. Existing
files and company text are never translated. English price fields are raw `{Price}` strings, without an
assumed currency. English data columns and quantity aliases are supported. Existing Turkish examples stay available.

## Maintainer workflow

Add a source string with `UiText.T("Turkish source")`, or use a stable catalog key through the WPF markup
extension. Keys are `T_` plus the first 12 lowercase hex characters of the UTF-8 source SHA-256. Add matching
Turkish and English entries; do not apply localization to user values or identifiers. Interpolated strings
use composite-format placeholders in resource entries. Escape literal braces using composite-format rules.

`LocalizationTests` checks language resolution, migration/persistence, resource key/placeholder parity,
English resource loading, XAML references, dot/comma input, saved-data invariance and English sample rendering.
Existing Turkish tests retain their original UI culture. Windows WPF layout/accessibility and printer acceptance
are deliberately deferred; cross-compilation does not prove those behaviors.

Both installer sources offer English and Turkish. Candidate packaging includes both READMEs, the English guide,
English sample CSVs and the English satellite assembly. Building a local candidate never uploads a release.
