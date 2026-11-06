using AkelEtiket.BarTender;
using AkelEtiket.Core;
using AkelEtiket.Editor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

/// <summary>BarTender (.btw) etiketlerini açma ve klasörce dönüştürme.</summary>
public sealed partial class MainViewModel
{
    /// <summary>Aç penceresinin süzgeci: LabelHarbor ve BarTender etiketleri birlikte.</summary>
    public const string OpenLabelFilter =
        "Etiket dosyaları (*.akelabel;*.btw)|*.akelabel;*.btw|Etiket projesi (*.akelabel)|*.akelabel|BarTender etiketi (*.btw)|*.btw";

    /// <summary>Toplu dönüştürülen etiketlerin varsayılan üst klasörü (Belgeler\Etiket Tasarım altında).</summary>
    public static string BarTenderImportFolder => Path.Combine(AppIdentity.DocumentsFolder, "BarTender'dan aktarılanlar");

    [ObservableProperty]
    private bool _isConvertingBarTender;

    /// <summary>
    /// BarTender etiketini yeni, kaydedilmemiş bir sekmede açar. Özgün .btw dosyası değiştirilmez; kaydedince
    /// .akelabel olarak kaydedilir. Aktarılamayan ya da tahmin edilen şeyler kullanıcıya bir kez gösterilir.
    /// </summary>
    public bool OpenBarTender(string path)
    {
        try
        {
            SetStatus(L10n.T($"BarTender etiketi okunuyor: {Path.GetFileName(path)}…"));
            var result = BtwImporter.Import(path);
            OpenProjectAsNew(result.Project,
                L10n.T($"BarTender etiketi aktarıldı: {Path.GetFileName(path)} ({result.ImportedCount} nesne). Kaydedince .akelabel olur."));
            Settings.LastProjectFolder = Path.GetDirectoryName(path);
            var notes = new List<string>(result.Warnings);
            double currentDpi = Units.DotsPerMmToDpi(Settings.Printer.DotsPerMm);
            if (result.Source.PrinterDpi is { } dpi && Math.Abs(dpi - currentDpi) > dpi * 0.05)
                notes.Add(L10n.T($"Bu etiket {dpi} dpi yazıcı için hazırlanmış; LabelHarbor şu an {currentDpi:0} dpi ile çiziyor. Barkod çubukları farklı inceliğe yuvarlanır. Aynı yazıcıyla basacaksanız Ayarlar → Yazıcı profili → Çözünürlük'ten {dpi} DPI seçin."));
            if (notes.Count > 0)
                Dialogs.ShowMessage(L10n.T("BarTender etiketi aktarıldı"),
                    L10n.T("Etiket açıldı. Basmadan önce şunları kontrol edin:") + "\n\n• " + string.Join("\n• ", notes),
                    MessageKind.Warning);
            return true;
        }
        catch (BtwFormatException ex)
        {
            SetStatus(L10n.T("BarTender etiketi açılamadı."));
            Dialogs.ShowMessage(L10n.T("BarTender etiketi açılamadı"), ex.Message, MessageKind.Error);
            return false;
        }
    }

    [RelayCommand]
    private void OpenBarTenderFile()
    {
        if (!CanOpenNewDocument()) return;
        var paths = Dialogs.ShowOpenFilesDialog(L10n.T("BarTender etiketi aç"), BtwImporter.FileFilter, Settings.LastProjectFolder);
        foreach (string path in paths)
            if (!OpenBarTender(path)) break;
    }

    /// <summary>
    /// Bir klasördeki tüm BarTender etiketlerini (alt klasörler dahil) .akelabel'a çevirir. Hedef:
    /// Belgeler\Etiket Tasarım\BarTender'dan aktarılanlar\&lt;klasör adı&gt;. Var olan dosyaların üzerine yazılmaz.
    /// </summary>
    [RelayCommand]
    private async Task ConvertBarTenderFolder()
    {
        if (IsConvertingBarTender) return;
        string? source = Dialogs.ShowFolderDialog(L10n.T("BarTender etiketlerinin bulunduğu klasörü seçin"), Settings.LastProjectFolder);
        if (source is null) return;
        var files = BtwFolderConverter.FindFiles(source);
        if (files.Count == 0)
        {
            Dialogs.ShowMessage(L10n.T("BarTender klasörünü dönüştür"), L10n.T($"Bu klasörde BarTender etiketi (.btw) yok:\n{source}"), MessageKind.Warning);
            return;
        }
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
        if (string.IsNullOrWhiteSpace(name)) name = "BarTender";
        string target = Path.Combine(BarTenderImportFolder, name);
        if (BtwFolderConverter.IsInside(target, source))
        {
            Dialogs.ShowMessage(L10n.T("BarTender klasörünü dönüştür"), L10n.T("Bu klasör, dönüştürülen etiketlerin kaydedildiği klasörün üstünde; başka bir klasör seçin."), MessageKind.Warning);
            return;
        }
        if (!Dialogs.Confirm(L10n.T("BarTender klasörünü dönüştür"),
                L10n.T($"{files.Count} BarTender etiketi bulundu.\n\nHer biri LabelHarbor etiketine (.akelabel) çevrilip alt klasörleriyle birlikte şuraya kaydedilecek:\n{target}\n\nÖzgün dosyalar değişmez; hedefte aynı adlı dosya varsa üzerine yazılmaz."),
                L10n.T("Dönüştür"), L10n.T("Vazgeç")))
            return;

        IsConvertingBarTender = true;
        var dispatcher = _services.Dispatcher;
        var progress = new Progress<(int Done, int Total)>(p =>
            dispatcher.Post(() => SetStatus(L10n.T($"BarTender etiketleri dönüştürülüyor: {p.Done}/{p.Total}…"))));
        try
        {
            var result = await Task.Run(() => BtwFolderConverter.Convert(source, target, progress));
            Settings.LastProjectFolder = source;
            SetStatus(L10n.T($"BarTender dönüştürme bitti: {result.Converted} etiket kaydedildi."));
            string summary = L10n.T($"Dönüştürülen: {result.Converted}\nKontrol edilmesi gereken: {result.WithNotes}\nZaten vardı (atlandı): {result.Skipped}\nAçılamayan: {result.Failed}");
            string where = L10n.T($"\n\nKlasör:\n{result.TargetFolder}");
            if (result.ReportPath is not null)
                where += L10n.T($"\n\nAyrıntılar \"{BtwFolderConverter.ReportFileName}\" dosyasında.");
            Services.OpenFolder?.Invoke(result.TargetFolder);
            Dialogs.ShowMessage(L10n.T("BarTender klasörü dönüştürüldü"), summary + where,
                result.Failed > 0 ? MessageKind.Warning : MessageKind.Info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SetStatus(L10n.T("BarTender dönüştürme yapılamadı."));
            Dialogs.ShowMessage(L10n.T("BarTender klasörünü dönüştür"), L10n.T("Dönüştürülemedi: ") + ex.Message, MessageKind.Error);
        }
        finally
        {
            IsConvertingBarTender = false;
        }
    }
}
