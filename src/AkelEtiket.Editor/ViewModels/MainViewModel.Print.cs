using CommunityToolkit.Mvvm.Input;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>Baskı önizlemesi; önizlemeden "Yazdır" seçilirse baskı penceresi açılır.</summary>
    [RelayCommand]
    private void Preview()
    {
        var preview = new PreviewViewModel(this);
        bool printRequested;
        try
        {
            printRequested = Dialogs.ShowDialog(preview);
        }
        finally
        {
            preview.Close();
        }
        if (printRequested) Print();
    }

    [RelayCommand]
    private void Print()
    {
        if (Data.HasData) Data.ValidateAgainst(Session.Document);
        Dialogs.ShowDialog(new PrintViewModel(this));
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var vm = new SettingsViewModel(Settings) { Dialogs = Dialogs };
        bool accepted = Dialogs.ShowDialog(vm);
        // GS1 Firma Profili ayarlar penceresinden kaydedilmiş olabilir: {GLN}/{GCP} alanlarını yenile.
        ApplyPreviewFields();
        if (!accepted) return;
        var (themeChanged, resolutionChanged, folderChanged) = vm.ApplyTo(Settings);
        SaveSettings();
        if (themeChanged) ThemeChanged?.Invoke(this, EventArgs.Empty);
        if (resolutionChanged) Session.SetDotsPerMm(Settings.Printer.DotsPerMm);
        if (folderChanged) Templates.ChangeFolder(Settings.EffectiveTemplatesFolder);
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(LabelSizeText));
        OnPropertyChanged(nameof(DotsPerMm));
        StatusMessage = L10n.T("Ayarlar kaydedildi.");
        if (vm.CreateCompanyTemplates)
        {
            try
            {
                int created = Templates.AddCompanyTemplates();
                StatusMessage = L10n.T($"Ayarlar kaydedildi; firma bilgileriyle {created} yeni şablon eklendi. Mevcut şablonlar korundu.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectManager.ProjectFileException)
            {
                Dialogs.ShowMessage(L10n.T("Firma şablonları"), L10n.T("Ayarlar kaydedildi, ancak yeni şablonlar eklenemedi: ") + ex.Message, AkelEtiket.Editor.Services.MessageKind.Warning);
            }
        }
    }
}
