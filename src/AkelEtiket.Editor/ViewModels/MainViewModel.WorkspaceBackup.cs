using System.Text.Json;
using AkelEtiket.Core;
using AkelEtiket.Editor.Services;
using AkelEtiket.ProjectManager;
using CommunityToolkit.Mvvm.Input;

namespace AkelEtiket.Editor.ViewModels;

public sealed partial class MainViewModel
{
    public const string WorkspaceBackupScope = "Paket firma/arayüz ayarlarını, şablonları ve ürün listesini içerir. " +
        "Harici Excel/CSV ve görsel dosyaları, GS1/SSCC tahsisleri, sayaçlar, baskı geçmişi ve bağlantı anahtarları dahil değildir.";

    [RelayCommand]
    private void ExportWorkspaceBackup()
    {
        string? path = Dialogs.ShowSaveFileDialog("Firma, şablon ve ürün yedeği", WorkspaceBackup.FileFilter,
            $"Etiket-yedek-{DateTime.Now:yyyyMMdd-HHmmss}.etiketyedek");
        if (path is null) return;
        try
        {
            WorkspaceBackup.Export(path, Settings, Templates.Library, Products.Data);
            Dialogs.ShowMessage("Yedek oluşturuldu", $"{path}\n\n{WorkspaceBackupScope}\n\nYedek yalnız bu bilgisayara kaydedildi.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ProjectFileException)
        { Dialogs.ShowMessage("Yedek oluşturulamadı", ex.Message, MessageKind.Error); }
    }

    [RelayCommand]
    private void ImportWorkspaceBackup()
    {
        string? path = Dialogs.ShowOpenFileDialog("Firma, şablon ve ürün yedeğini aç", WorkspaceBackup.FileFilter);
        if (path is null) return;
        try
        {
            var backup = WorkspaceBackup.Read(path);
            if (!Dialogs.Confirm("Yedeği geri yükle", $"Firma: {backup.Settings.Company.DisplayName}\n" +
                $"{backup.TemplateCount} şablon, {backup.Products.Products.Count} ürün.\n\n" +
                "Firma ve görünüm ayarları uygulanacak. Şablonlar yeni bir klasöre alınacak; mevcut şablonlar korunacak. " +
                "Ürün listesi değişmeden önce ayrı bir kopyası saklanacak.\n\n" + WorkspaceBackupScope,
                "Geri yükle", "Vazgeç")) return;
            string folder = Path.Combine(AppIdentity.DocumentsFolder, "Geri yüklenenler", Guid.NewGuid().ToString("N"));
            WorkspaceBackup.ExtractToNewFolder(backup, folder);
            // Ürün listesi başarısızsa ayar/şablon seçimi değiştirilmez. Önceki liste ayrıca korunur.
            Products.RestoreBackup(backup.Products);
            var profile = new SettingsViewModel(backup.Settings)
            {
                TemplatesFolder = Path.Combine(folder, "templates"),
                Gs1Prefix = Settings.Gs1CompanyPrefix,
            };
            var (theme, _, _) = profile.ApplyTo(Settings);
            Settings.Printer.PrinterName = ""; // Başka bilgisayardaki kuyruk adı otomatik kullanılmaz.
            _services.SettingsStore.Save(Settings);
            Templates.ChangeFolder(Settings.EffectiveTemplatesFolder);
            Session.SetDotsPerMm(Settings.Printer.DotsPerMm);
            OnPropertyChanged(nameof(Settings)); OnPropertyChanged(nameof(LabelSizeText)); OnPropertyChanged(nameof(DotsPerMm));
            if (theme) ThemeChanged?.Invoke(this, EventArgs.Empty);
            ApplyPreviewFields();
            StatusMessage = $"{backup.TemplateCount} şablon ve {backup.Products.Products.Count} ürün geri yüklendi. Yazıcınızı baskı sırasında seçin.";
            Dialogs.ShowMessage("Yedek geri yüklendi", StatusMessage + "\n\nŞablonlar: " + Settings.EffectiveTemplatesFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ProjectFileException or ArgumentException)
        { Dialogs.ShowMessage("Yedek geri yüklenemedi", ex.Message, MessageKind.Error); }
    }
}
