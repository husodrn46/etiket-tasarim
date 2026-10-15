using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Printing;
using CommunityToolkit.Mvvm.Input;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>
    /// Seçili GS1 barkodunun (GS1-128, GS1 DataMatrix, GS1 QR) içeriğini "GS1 içerik oluşturucu" ile kurar.
    /// Uygula'ya basılırsa içerik tek geri alma adımıyla barkoda yazılır.
    /// </summary>
    [RelayCommand]
    private void OpenGs1Builder()
    {
        if (SelectedElement?.Model is not BarcodeElement barcode || !BarcodeElement.IsGs1(barcode.Symbology)) return;
        string id = barcode.Id;
        var builder = new Gs1ContentBuilderViewModel(barcode.Symbology, barcode.Content, Gs1Integration.Context.Profile,
            Data.HasData ? Data.Columns : null);
        if (!Dialogs.ShowDialog(builder)) return;
        // Pencere açıkken belge değişmiş olabilir (geri al, yapay zekâ): öğe kimliğiyle yeniden bulunur.
        if (Session.Document.FindElement(id) is not BarcodeElement target || target.Symbology != builder.Symbology)
        {
            SetStatus(L10n.T("GS1 içeriği yazılmadı: barkod bu arada değişti ya da silindi."));
            return;
        }
        if (target.Content == builder.Content) return;
        using (Session.BeginTransaction(L10n.T("GS1 içeriği")))
        {
            target.Content = builder.Content;
        }
        SetStatus(L10n.T($"GS1 içeriği yazıldı: {builder.Content}"));
    }
}
