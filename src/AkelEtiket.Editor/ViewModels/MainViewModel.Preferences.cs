using AkelEtiket.Core.Serialization;
using AkelEtiket.Editor.Services;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

public sealed partial class MainViewModel
{
    internal bool UpdatePreferences(Action<AppSettings> update)
    {
        var candidate = ModelJson.Clone(Settings); update(candidate); candidate.Normalize();
        try { _services.SettingsStore.Save(candidate); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Dialogs.ShowMessage(L10n.T("Ayarlar kaydedilemedi"), ex.Message, MessageKind.Warning); return false; }
        Settings = candidate;
        OnPropertyChanged(nameof(Settings)); OnPropertyChanged(nameof(ImportProfileChoices));
        return true;
    }

    public IReadOnlyList<KeyValuePair<string,string>> ImportProfileChoices =>
        [new("", L10n.T("Otomatik aktarım")), .. Settings.ImportProfiles.Select(p => new KeyValuePair<string,string>(p.Id,p.Name))];
    public string SelectedImportProfileId { get; set; } = "";
    public ImportMappingProfile? SelectedImportProfile => Settings.ImportProfiles.FirstOrDefault(p => p.Id == SelectedImportProfileId);
}
