using System.Collections.ObjectModel;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.Core.Serialization;
using AkelEtiket.DataImport;
using AkelEtiket.Editor.Printing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AkelEtiket.Editor.ViewModels;

public sealed partial class ColumnMappingRow : ObservableObject
{
    private readonly DataRecord? _sample;
    public ColumnMappingRow(string field, IReadOnlyList<string> columns, DataRecord? sample)
    {
        Field = field; Columns = columns; _sample = sample;
        int index = FieldNames.FindIndex(columns, field);
        SelectedColumn = index >= 0 ? columns[index] : null;
    }
    public string Field { get; }
    public IReadOnlyList<string> Columns { get; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Example))]
    public partial string? SelectedColumn { get; set; }
    public string Example => SelectedColumn is null ? "Sütun seçin" : _sample?.GetValue(SelectedColumn) ?? "";
}

/// <summary>Kaynak Excel/CSV'ye yazmaz; tasarımın alan adlarını bir geri alma adımında değiştirir.</summary>
public sealed partial class ColumnMappingViewModel : ObservableObject
{
    private readonly LabelDocument _document;
    public ColumnMappingViewModel(LabelDocument document, DataSourceTable table, DataRecord? sample = null)
    {
        _document = document;
        var builtIns = EditorFields.BuiltIns(1, DateTime.Now);
        foreach (string field in document.Elements.SelectMany(DataSourceViewModel.FieldReferencesOf).Select(f => f.Base)
            .Where(f => !builtIns.TryGetValue(f, null, out _)).DistinctBy(FieldNames.Normalize))
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
        if (!IsValid) throw new InvalidOperationException("Her etiket alanı için bir veri sütunu seçin.");
        var map = Rows.ToDictionary(r => FieldNames.Normalize(r.Field), r => r.SelectedColumn!);
        string Name(string field) => map.GetValueOrDefault(FieldNames.Normalize(field), field);
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
        if (mapping.Rows.Count == 0)
        {
            Dialogs.ShowMessage("Sütunları eşleştir", "Tasarımda veri alanı yok. Önce bir metne {UrunAdi} gibi bir alan ekleyin.");
            return;
        }
        if (!Dialogs.ShowDialog(mapping) || !mapping.IsValid) return;
        Session.ReplaceContent(mapping.CreateMappedDocument(), null, "Excel/CSV sütunları eşleştirildi");
        Data.ValidateAgainst(Session.Document);
        ApplyPreviewFields();
        StatusMessage = "Sütunlar eşleştirildi. Kaynak dosya değişmedi; eşleştirmeyi tasarımla birlikte kaydedebilirsiniz.";
    }
}
