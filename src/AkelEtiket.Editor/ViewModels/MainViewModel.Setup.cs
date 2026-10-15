using AkelEtiket.Editor.Services;
using AkelEtiket.Core.Serialization;
using AkelEtiket.ProjectManager;
using CommunityToolkit.Mvvm.Input;

namespace AkelEtiket.Editor.ViewModels;

public sealed partial class MainViewModel
{
    [RelayCommand]
    private void OpenFirstSetup()
    {
        if (RunFirstSetup(firstRun: false) && _setupPreviewRequested)
        { _setupPreviewRequested = false; PreviewCommand.Execute(null); }
    }

    private bool RunFirstSetup(bool firstRun)
    {
        _setupSaveFailed = false;
        var setup = new FirstSetupViewModel(Settings, Dialogs);
        if (!Dialogs.ShowDialog(setup)) return false;
        var candidate = ModelJson.Clone(Settings);
        setup.ApplyTo(candidate);
        candidate.FirstRunCompleted |= firstRun;
        candidate.Recent.Items = RecentFiles.ToList();
        try { _services.SettingsStore.Save(candidate); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _setupSaveFailed = true;
            Dialogs.ShowMessage("Kurulum kaydedilemedi", ex.Message + "\nMevcut ayarlar korundu; kaydetmeyi yeniden deneyebilirsiniz.", MessageKind.Error);
            return false;
        }
        Settings = candidate;
        Session.SetDotsPerMm(Settings.Printer.DotsPerMm);
        Templates.ChangeFolder(Settings.EffectiveTemplatesFolder);
        OnPropertyChanged(nameof(Settings)); OnPropertyChanged(nameof(LabelSizeText)); OnPropertyChanged(nameof(DotsPerMm));
        if (!firstRun && setup.AddTemplates)
        {
            try { Templates.AddCompanyTemplates(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectFileException)
            { Dialogs.ShowMessage("Şablonlar", "Ayarlar kaydedildi; şablonlar eklenemedi: " + ex.Message, MessageKind.Warning); }
        }
        // Baskı ve önizleme yalnız kullanıcı düğmesiyle açılır; kurulum hiçbir iş göndermez.
        _setupPreviewRequested = setup.OpenPreview;
        _setupAddTemplates = setup.AddTemplates;
        StatusMessage = "İlk kurulum kaydedildi. Tasarımınızı açıp baskı önizlemesini kontrol edebilirsiniz.";
        return true;
    }

    private bool _setupSaveFailed;
    private bool _setupPreviewRequested;
    private bool _setupAddTemplates = true;
}
