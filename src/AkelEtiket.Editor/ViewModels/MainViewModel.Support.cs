using System.Collections.ObjectModel;
using AkelEtiket.Design.Workspace;
using AkelEtiket.Editor.Services;
using AkelEtiket.PrintEngine;
using AkelEtiket.ProjectManager;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

/// <summary>Bir tasarımın otomatik yedeği.</summary>
public sealed record BackupItem(string Path, DateTime Time, long Size)
{
    public string Display => $"{Time:dd.MM.yyyy HH:mm:ss} · {Size / 1024.0:0.#} KB";
}

/// <summary>Açık tasarımın kayıt öncesi alınan yedekleri; seçilen yedek yeni belge olarak açılır (asıl dosya değişmez).</summary>
public sealed partial class BackupsViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public BackupsViewModel(MainViewModel main, string designPath)
    {
        _main = main;
        DesignName = Path.GetFileNameWithoutExtension(designPath);
        foreach (var file in main.Backups.ListFor(designPath))
            Items.Add(new BackupItem(file.FullName, file.LastWriteTime, file.Length));
    }

    public string DesignName { get; }
    public ObservableCollection<BackupItem> Items { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    public partial BackupItem? Selected { get; set; }

    /// <summary>Pencere kapanmalı.</summary>
    public event EventHandler? CloseRequested;

    private bool CanOpen() => Selected is not null;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open()
    {
        if (Selected is null || !_main.ConfirmCloseDocument()) return;
        try
        {
            var result = ProjectFile.Load(Selected.Path);
            _main.OpenProjectAsNew(result.Project, L10n.T($"\"{DesignName}\" yedeği ({Selected.Time:dd.MM.yyyy HH:mm}) yeni belge olarak açıldı. Gerekirse farklı kaydedin."));
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (ProjectFileException ex)
        {
            _main.Dialogs.ShowMessage(L10n.T("Yedek açılamadı"), ex.Message, MessageKind.Error);
        }
    }
}

/// <summary>Yardım penceresi (kısayollar ve kısa rehber).</summary>
public sealed class HelpViewModel
{
    public HelpViewModel(string documentsFolder)
    {
        DocumentsFolder = documentsFolder;
    }

    /// <summary>Kurulumdaki Belgeler klasörü (README, kurulum, XP-470B testi, yapay zekâ rehberi).</summary>
    public string DocumentsFolder { get; }

    public IReadOnlyList<KeyValuePair<string, string>> Shortcuts { get; } =
    [
        new("Ctrl+N / Ctrl+O / Ctrl+S", L10n.T("Yeni / Aç / Kaydet (Ctrl+Shift+S farklı kaydet)")),
        new("Ctrl+P", L10n.T("Yazdır")),
        new("Ctrl+Shift+P", L10n.T("Hızlı Baskı (şablon + ürün + adet)")),
        new("F5", L10n.T("Baskı önizlemesi")),
        new("Ctrl+Z / Ctrl+Y", L10n.T("Geri al / İleri al")),
        new("Ctrl+C / Ctrl+X / Ctrl+V", L10n.T("Kopyala / Kes / Yapıştır")),
        new("Ctrl+D", L10n.T("Çoğalt")),
        new("Delete", L10n.T("Seçili öğeleri sil")),
        new("Ctrl+A", L10n.T("Tümünü seç")),
        new("Ctrl+G / Ctrl+Shift+G", L10n.T("Grupla / Grubu çöz")),
        new("Ctrl+L", L10n.T("Kilitle / kilidi aç")),
        new("Ctrl+PgUp / Ctrl+PgDn", L10n.T("Bir öne / bir arkaya (Shift ile en öne / en arkaya)")),
        new(L10n.T("Yön tuşları"), L10n.T("0,5 mm taşı (Shift ile 5 mm, Ctrl ile 1 nokta = 0,125 mm)")),
        new("Ctrl+0 / Ctrl+1", L10n.T("Pencereye sığdır / gerçek boyut")),
        new("Ctrl+E", L10n.T("PNG olarak dışa aktar")),
        new(L10n.T("Alt (sürüklerken)"), L10n.T("Yakalamayı geçici kapat")),
        new("F2", L10n.T("Öğeyi yeniden adlandır")),
        new("F1", L10n.T("Bu yardım")),
    ];

    public IReadOnlyList<string> Tips { get; } =
    [
        L10n.T("Tuvaldeki siyah-beyaz noktalar baskıyla birebir aynıdır (203 dpi, 1-bit)."),
        L10n.T("Excel/CSV verisi: Veri panelinden dosya seçin; metne {SütunAdı} yazarak alan bağlayın."),
        L10n.T("Hızlı Baskı: ürün listesini bir kez aktarın; sonra ürünü okutup adet yazmanız yeterli."),
        L10n.T("Yapay Zekâ paneli: dış ajanların (Claude vb.) ve uygulama içi asistanın önerileri burada; siz onaylamadan tasarım değişmez."),
        L10n.T("Her kayıtta önceki sürümün yedeği alınır: ? → Bu tasarımın yedekleri."),
        L10n.T("Sorun bildirirken ? → Destek paketi oluştur; paket yalnızca bu bilgisayara kaydedilir."),
    ];
}

public sealed partial class MainViewModel
{
    /// <summary>Kayıt öncesi yedekler (%LOCALAPPDATA%\Etiket Tasarım\Yedekler).</summary>
    public BackupService Backups { get; } = new();

    [RelayCommand]
    private void OpenHelp()
    {
        string docs = Path.Combine(AppContext.BaseDirectory, "Belgeler");
        Dialogs.ShowDialog(new HelpViewModel(docs));
    }

    [RelayCommand]
    private void OpenBackups()
    {
        if (Session.Project.FilePath is not { } path)
        {
            Dialogs.ShowMessage(L10n.T("Yedekler"), L10n.T("Bu tasarım henüz kaydedilmedi; yedekler kaydedilen dosyalar için tutulur."));
            return;
        }
        var vm = new BackupsViewModel(this, path);
        if (vm.Items.Count == 0)
        {
            Dialogs.ShowMessage(L10n.T("Yedekler"), L10n.T($"\"{Path.GetFileName(path)}\" için henüz yedek yok. Dosyanın üzerine her kayıtta önceki hâli yedeklenir (son {BackupService.KeepPerDesign})."));
            return;
        }
        Dialogs.ShowDialog(vm);
    }

    [RelayCommand]
    private void CreateSupportBundle()
    {
        string? document = Session.Project.FilePath;
        bool includeDocument = document is not null && Dialogs.Confirm(L10n.T("Destek paketi"),
            L10n.T($"Açık tasarım ({Path.GetFileName(document)}) da pakete eklensin mi?"), L10n.T("Ekle"), L10n.T("Ekleme"));
        try
        {
            var printers = PrinterDiscovery.GetPrinters().Select(p => p.Name).ToList();
            string path = SupportBundle.Create(includeDocument ? document : null, printers);
            Services.OpenFolder?.Invoke(Path.GetDirectoryName(path)!);
            Dialogs.ShowMessage(L10n.T("Destek paketi"),
                L10n.T($"Oluşturuldu:\n{path}\n\nPaket yalnızca bu bilgisayara kaydedildi; hiçbir yere gönderilmedi. Bağlantı anahtarları pakete eklenmez."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.ShowMessage(L10n.T("Destek paketi"), L10n.T("Oluşturulamadı: ") + ex.Message, MessageKind.Error);
        }
    }
}
