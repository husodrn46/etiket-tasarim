using AkelEtiket.DataImport;
using AkelEtiket.Design.Data;
using AkelEtiket.Editor.Services;
using AkelEtiket.ProjectManager;
using CommunityToolkit.Mvvm.Input;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>Ürün listesi (yapay zekâ veri araçlarıyla aynı liste).</summary>
    public ProductCatalog Products => Ai.Service.Products;

    /// <summary>Ürün listesi penceresi.</summary>
    [RelayCommand]
    private void OpenProducts() => Dialogs.ShowDialog(new ProductsViewModel(Products, Dialogs));

    /// <summary>Hızlı Baskı penceresi.</summary>
    [RelayCommand]
    private void OpenQuickPrint()
    {
        Templates.Refresh();
        Dialogs.ShowDialog(new QuickPrintViewModel(this, new ProductsViewModel(Products, Dialogs)));
    }

    /// <summary>
    /// Hızlı Baskı: şablonu yeni belge olarak açar (şablon dosyası değişmez) ve ürünü tek satırlık veri kaynağı olarak
    /// adetiyle hazırlar. Açık belgede kaydedilmemiş değişiklik varsa önce sorulur.
    /// </summary>
    public bool StartQuickPrint(string templatePath, ProductRecord? product, IReadOnlyList<string> columns, int copies)
    {
        if (!ConfirmCloseDocument()) return false;
        LabelProject project;
        try
        {
            project = ProjectFile.Load(templatePath).Project;
        }
        catch (ProjectFileException ex)
        {
            Dialogs.ShowMessage(L10n.T("Hızlı Baskı"), L10n.T("Şablon açılamadı: ") + ex.Message, MessageKind.Error);
            return false;
        }
        project.FilePath = null;
        Session = CreateSession(project);
        if (product is not null)
        {
            var values = columns.Select(c => product.Fields.TryGetValue(c, out var v) ? v : "").ToList();
            var table = new DataSourceTable(L10n.T("Hızlı Baskı (ürün listesi)"), L10n.T("Ürün listesi"), null, 1, columns.ToList(),
                [new DataRecord(1, values, columns.ToList())], []);
            Data.ApplyTable(table);
            if (Data.Rows.Count == 1)
            {
                Data.Rows[0].IsSelected = true;
                Data.Rows[0].Copies = copies;
            }
            ApplyPreviewFields();
        }
        else
        {
            Data.Clear();
        }
        _quickPrintCopies = product is null ? copies : null;
        RequestFit();
        SetStatus(L10n.T($"Hızlı Baskı: \"{Path.GetFileNameWithoutExtension(templatePath)}\"") + (product is null ? "" : $" · {product.Key}") + $" · {copies} adet");
        return true;
    }

    private int? _quickPrintCopies;

    /// <summary>Hızlı Baskı penceresi kapandıktan sonra normal baskı penceresini açar.</summary>
    public void OpenPrintDialogAfterQuickPrint()
    {
        if (Data.HasData) Data.ValidateAgainst(Session.Document);
        var print = new PrintViewModel(this);
        if (_quickPrintCopies is { } copies) print.Copies = copies;
        _quickPrintCopies = null;
        Dialogs.ShowDialog(print);
    }
}
