using System.Collections.ObjectModel;
using AkelEtiket.Design.Workspace;
using AkelEtiket.Editor.Services;
using AkelEtiket.PrintEngine;
using AkelEtiket.ProjectManager;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
            _main.OpenProjectAsNew(result.Project, $"\"{DesignName}\" yedeği ({Selected.Time:dd.MM.yyyy HH:mm}) yeni belge olarak açıldı. Gerekirse farklı kaydedin.");
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (ProjectFileException ex)
        {
            _main.Dialogs.ShowMessage("Yedek açılamadı", ex.Message, MessageKind.Error);
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
        new("Ctrl+N / Ctrl+O / Ctrl+S", "Yeni / Aç / Kaydet (Ctrl+Shift+S farklı kaydet)"),
        new("Ctrl+P", "Yazdır"),
        new("Ctrl+Shift+P", "Hızlı Baskı (şablon + ürün + adet)"),
        new("F5", "Baskı önizlemesi"),
        new("Ctrl+Z / Ctrl+Y", "Geri al / İleri al"),
        new("Ctrl+C / Ctrl+X / Ctrl+V", "Kopyala / Kes / Yapıştır"),
        new("Ctrl+D", "Çoğalt"),
        new("Delete", "Seçili öğeleri sil"),
        new("Ctrl+A", "Tümünü seç"),
        new("Ctrl+G / Ctrl+Shift+G", "Grupla / Grubu çöz"),
        new("Ctrl+L", "Kilitle / kilidi aç"),
        new("Ctrl+PgUp / Ctrl+PgDn", "Bir öne / bir arkaya (Shift ile en öne / en arkaya)"),
        new("Yön tuşları", "0,5 mm taşı (Shift ile 5 mm, Ctrl ile 1 nokta = 0,125 mm)"),
        new("Ctrl+0 / Ctrl+1", "Pencereye sığdır / gerçek boyut"),
        new("Ctrl+E", "PNG olarak dışa aktar"),
        new("Alt (sürüklerken)", "Yakalamayı geçici kapat"),
        new("F2", "Öğeyi yeniden adlandır"),
        new("F1", "Bu yardım"),
    ];

    public IReadOnlyList<string> Tips { get; } =
    [
        "Tuvaldeki siyah-beyaz noktalar baskıyla birebir aynıdır (203 dpi, 1-bit).",
        "Excel/CSV verisi: Veri panelinden dosya seçin; metne {SütunAdı} yazarak alan bağlayın.",
        "Hızlı Baskı: ürün listesini bir kez aktarın; sonra ürünü okutup adet yazmanız yeterli.",
        "Yapay Zekâ paneli: dış ajanların (Claude vb.) ve uygulama içi asistanın önerileri burada; siz onaylamadan tasarım değişmez.",
        "Her kayıtta önceki sürümün yedeği alınır: ? → Bu tasarımın yedekleri.",
        "Sorun bildirirken ? → Destek paketi oluştur; paket yalnızca bu bilgisayara kaydedilir.",
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
            Dialogs.ShowMessage("Yedekler", "Bu tasarım henüz kaydedilmedi; yedekler kaydedilen dosyalar için tutulur.");
            return;
        }
        var vm = new BackupsViewModel(this, path);
        if (vm.Items.Count == 0)
        {
            Dialogs.ShowMessage("Yedekler", $"\"{Path.GetFileName(path)}\" için henüz yedek yok. Dosyanın üzerine her kayıtta önceki hâli yedeklenir (son {BackupService.KeepPerDesign}).");
            return;
        }
        Dialogs.ShowDialog(vm);
    }

    [RelayCommand]
    private void CreateSupportBundle()
    {
        string? document = Session.Project.FilePath;
        bool includeDocument = document is not null && Dialogs.Confirm("Destek paketi",
            $"Açık tasarım ({Path.GetFileName(document)}) da pakete eklensin mi?", "Ekle", "Ekleme");
        try
        {
            var printers = PrinterDiscovery.GetPrinters().Select(p => p.Name).ToList();
            string path = SupportBundle.Create(includeDocument ? document : null, printers);
            Services.OpenFolder?.Invoke(Path.GetDirectoryName(path)!);
            Dialogs.ShowMessage("Destek paketi",
                $"Oluşturuldu:\n{path}\n\nPaket yalnızca bu bilgisayara kaydedildi; hiçbir yere gönderilmedi. Bağlantı anahtarları pakete eklenmez.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.ShowMessage("Destek paketi", "Oluşturulamadı: " + ex.Message, MessageKind.Error);
        }
    }
}
