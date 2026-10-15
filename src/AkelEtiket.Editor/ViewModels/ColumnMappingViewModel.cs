using System.Collections.ObjectModel;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.Core.Serialization;
using AkelEtiket.DataImport;
using AkelEtiket.Editor.Printing;
using AkelEtiket.Editor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

public sealed partial class ColumnMappingRow : ObservableObject
{
    private readonly DataRecord? _sample;
    public ColumnMappingRow(string field, IReadOnlyList<string> columns, DataRecord? sample)
    {
        Field = field; Columns = columns; _sample = sample;
        SuggestedColumn = ColumnSuggestions.Suggest(field, columns);
        SelectedColumn = SuggestedColumn;
    }
    public string? SuggestedColumn { get; }
    public string Suggestion => SuggestedColumn is null ? L10n.T("Elle seçim gerekli") : L10n.T("Öneri: ") + SuggestedColumn;
    public string Field { get; }
    public IReadOnlyList<string> Columns { get; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Example))]
    public partial string? SelectedColumn { get; set; }
    public string Example => SelectedColumn is null ? L10n.T("Sütun seçin") : _sample?.GetValue(SelectedColumn) ?? "";
}

/// <summary>Kaynak Excel/CSV'ye yazmaz; tasarımın alan adlarını bir geri alma adımında değiştirir.</summary>
public sealed partial class ColumnMappingViewModel : ObservableObject
{
    private readonly LabelDocument _document;
    private readonly DataSourceTable _table;
    private IDialogService? _dialogs;
    private Func<ImportMappingProfile,bool>? _persist;
    public ObservableCollection<ImportMappingProfile> Profiles { get; } = [];
    [ObservableProperty] public partial ImportMappingProfile? SelectedProfile { get; set; }
    [ObservableProperty] public partial string? ProfileMessage { get; set; }
    partial void OnSelectedProfileChanged(ImportMappingProfile? value)
    {
        if (value is null) return;
        int missing = 0;
        foreach (var row in Rows)
        {
            string? key = value.Mappings.ContainsKey(row.Field) ? row.Field : ColumnSuggestions.Suggest(row.Field, value.Mappings.Keys.ToArray());
            row.SelectedColumn = key is not null && value.Mappings.TryGetValue(key,out var column) && row.Columns.Contains(column) ? column : null;
            if (row.SelectedColumn is null) missing++;
        }
        ProfileMessage = missing == 0 ? L10n.T("Profil uygulandı; örnek değerleri kontrol edin.") : L10n.T($"{missing} alan eşleştirilmedi; eksik sütunları seçin.");
    }
    public void ConfigureProfiles(IEnumerable<ImportMappingProfile> profiles, IDialogService dialogs, Func<ImportMappingProfile,bool> persist, string? selectedId = null)
    {
        _dialogs = dialogs; _persist = persist;
        foreach (var profile in profiles) Profiles.Add(ModelJson.Clone(profile));
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == selectedId);
    }
    [RelayCommand] private void SaveProfile()
    {
        if (!IsValid || _dialogs is null || _persist is null) return;
        string? name = _dialogs.Prompt(L10n.T("Aktarım profilini kaydet"), L10n.T("Profil adı:"), SelectedProfile?.Name ?? L10n.T("Yeni profil"));
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim(); if (name.Length > 100) name = name[..100];
        var existing = Profiles.FirstOrDefault(p => string.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !_dialogs.Confirm(L10n.T("Aktarım profili"), L10n.T("Aynı adlı profil güncellensin mi?"))) return;
        var profile = new ImportMappingProfile
        {
            Id = existing?.Id ?? Guid.NewGuid().ToString("N"), Name = name,
            Mappings = Rows.ToDictionary(r => r.Field,r => r.SelectedColumn!,StringComparer.Ordinal),
            Delimiter = _table.Delimiter, EncodingName = ImportChoices.ResolveEncoding(_table.EncodingName)?.WebName, SheetName = _table.SheetName, HeaderRow = _table.HeaderRowNumber,
        };
        if (!_persist(profile)) return;
        if (existing is not null) Profiles.Remove(existing);
        Profiles.Add(profile); SelectedProfile = profile;
        ProfileMessage = L10n.T("Profil kaydedildi. Ürün satırları ve dosya yolu profile eklenmedi.");
    }
    public ColumnMappingViewModel(LabelDocument document, DataSourceTable table, DataRecord? sample = null)
    {
        _document = document; _table = table;
        var builtIns = EditorFields.BuiltIns(1, DateTime.Now);
        foreach (string field in document.Elements.SelectMany(DataSourceViewModel.FieldReferencesOf).Select(f => f.Base)
            .Where(f => !builtIns.TryGetValue(f, null, out _)).Distinct(StringComparer.Ordinal))
        {
            var row = new ColumnMappingRow(field, table.Columns, sample ?? table.Rows.FirstOrDefault());
            row.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IsValid));
            Rows.Add(row);
        }
    }
    public ObservableCollection<ColumnMappingRow> Rows { get; } = [];
    public bool IsValid => Rows.Count > 0 && Rows.All(r => r.SelectedColumn is not null && r.Columns.Contains(r.SelectedColumn));
    public LabelDocument CreateMappedDocument()
    {
        if (!IsValid) throw new InvalidOperationException(L10n.T("Her etiket alanı için bir veri sütunu seçin."));
        var names = Rows.Select(r => r.Field).ToList();
        var map = Rows.ToDictionary(r => r.Field, r => r.SelectedColumn!, StringComparer.Ordinal);
        string Name(string field)
        {
            if (map.TryGetValue(field, out string? exact)) return exact;
            int index = FieldNames.FindIndex(names, field);
            return index >= 0 ? Rows[index].SelectedColumn! : field;
        }
        string Text(string text)
        {
            foreach (var token in FieldTemplate.GetTokens(text).OrderByDescending(t => t.Start))
            {
                string replacement = Name(token.BaseName);
                if (replacement == token.BaseName) continue;
                string original = text.Substring(token.Start, token.Length);
                int index = original.IndexOf(token.BaseName, StringComparison.Ordinal);
                string mapped = original.Remove(index, token.BaseName.Length).Insert(index, replacement);
                text = text.Remove(token.Start, token.Length).Insert(token.Start, mapped);
            }
            return text;
        }
        var copy = ModelJson.Clone(_document);
        foreach (var element in copy.Elements)
        {
            if (element is TextElement t) t.Text = Text(t.Text);
            if (element is BarcodeElement b) b.Content = Text(b.Content);
            if (element is ImageElement image && image.SourceField is { } field) image.SourceField = Name(field);
            if (element.VisibleWhen is { } condition) element.VisibleWhen = condition with { Field = Name(condition.Field) };
        }
        return copy;
    }
}

public sealed partial class MainViewModel
{
    [RelayCommand]
    private void OpenColumnMapping()
    {
        if (Data.Table is not { } table) return;
        var mapping = new ColumnMappingViewModel(Session.Document, table, Data.PreviewRow?.Record);
        mapping.ConfigureProfiles(Settings.ImportProfiles, Dialogs, profile => UpdatePreferences(s =>
        {
            s.ImportProfiles.RemoveAll(p => p.Id == profile.Id); s.ImportProfiles.Add(profile);
        }), SelectedImportProfileId);
        if (mapping.Rows.Count == 0)
        {
            Dialogs.ShowMessage(L10n.T("Sütunları eşleştir"), L10n.T("Tasarımda veri alanı yok. Önce bir metne {UrunAdi} gibi bir alan ekleyin."));
            return;
        }
        if (!Dialogs.ShowDialog(mapping) || !mapping.IsValid) return;
        Session.ReplaceContent(mapping.CreateMappedDocument(), null, L10n.T("Excel/CSV sütunları eşleştirildi"));
        Data.ValidateAgainst(Session.Document);
        ApplyPreviewFields();
        StatusMessage = L10n.T("Sütunlar eşleştirildi. Kaynak dosya değişmedi; eşleştirmeyi tasarımla birlikte kaydedebilirsiniz.");
    }
}
