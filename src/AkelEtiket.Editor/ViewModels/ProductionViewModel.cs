using System.Collections.ObjectModel;
using System.ComponentModel;
using AkelEtiket.Editor.Production;
using AkelEtiket.DataImport;
using AkelEtiket.Editor.Production.Export;
using AkelEtiket.Editor.Production.Quality;
using AkelEtiket.Editor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

public sealed partial class ProductionViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _main;
    private readonly ProductionRecipeStore _store;
    private DocumentTabViewModel? _owner;
    private CancellationTokenSource? _operation;
    private ProductionSnapshot? _snapshot;
    private PreflightReport? _report;
    private string? _stamp;
    private bool _disposed;
    public ObservableCollection<ProductionRecipe> Recipes { get; } = [];
    public ObservableCollection<ProductionFindingViewModel> Findings { get; } = [];
    [ObservableProperty] public partial ProductionRecipe? SelectedRecipe { get; set; }
    [ObservableProperty] public partial bool UseData { get; set; }
    [ObservableProperty] public partial int Copies { get; set; } = 1;
    [ObservableProperty] public partial int StartNumber { get; set; } = 1;
    [ObservableProperty] public partial bool ExportPng { get; set; }
    public bool ExportPdf { get => !ExportPng; set => ExportPng = !value; }
    partial void OnExportPngChanged(bool value) => OnPropertyChanged(nameof(ExportPdf));
    [ObservableProperty] public partial bool HasInputErrors { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; private set; }
    [ObservableProperty] public partial string Status { get; private set; } = L10n.T("Kalite denetimi için hazır. Henüz çıktı doğrulanmadı.");
    [ObservableProperty] public partial int Progress { get; private set; }
    public bool IsIdle => !IsBusy;
    public bool CanExport => !IsBusy && !_disposed && !HasInputErrors && !_main.Data.IsLoading && _report?.CanExport == true && _snapshot is not null && _stamp == SourceStamp();
    public string SourceSummary => _main.Session.Project.DisplayName + " · " + (_main.Data.Table?.FileName ?? L10n.T("Veri dosyası yok"));
    public string PrivacyHint => L10n.T("Tarifler yalnız bu bilgisayarda saklanır. Etiket değerleri ve hesap sırları tarife eklenmez.");
    public string QualitySummary => _report is null ? L10n.T("Güncel kalite raporu yok") :
        L10n.T($"{_report.RowsChecked}/{_report.TotalRows} kayıt · {_report.LabelsChecked}/{_report.TotalLabels} etiket · {_report.ErrorCount} hata · {_report.WarningCount} uyarı");

    public ProductionViewModel(MainViewModel main, ProductionRecipeStore? store = null)
    {
        _main = main; _store = store ?? new(); UseData = main.Data.HasData;
        _main.PropertyChanged += OnMainChanged; AttachOwner(); ReloadRecipes();
    }
    private void AttachOwner()
    {
        if (_owner is not null) { _owner.Session.Changed -= OnSourceChanged; _owner.Data.PropertyChanged -= OnDataChanged; }
        _owner = _main.ActiveDocument;
        if (_owner is not null) { _owner.Session.Changed += OnSourceChanged; _owner.Data.PropertyChanged += OnDataChanged; }
        OnPropertyChanged(nameof(SourceSummary));
    }
    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(MainViewModel.ActiveDocument)) { AttachOwner(); Invalidate(); } }
    private void OnSourceChanged(object? sender, EventArgs e) => Invalidate();
    private void OnDataChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(DataSourceViewModel.Table) or nameof(DataSourceViewModel.SelectedLabelCount) or nameof(DataSourceViewModel.IsLoading)) { Invalidate(); OnPropertyChanged(nameof(SourceSummary)); Refresh(); } }
    private string SourceStamp() => _main.Session.Version + "|" + _main.Session.DotsPerMm + "|" +
        System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_main.Data.Table ?? (object)_main.Data) + "|" +
        string.Join(';', _main.Data.Rows.Select(r => $"{r.RowNumber}:{r.IsSelected}:{r.Copies}")) + $"|{UseData}|{Copies}|{StartNumber}";
    private void Invalidate()
    {
        if (_disposed) return;
        _stamp = null; _snapshot = null; _report = null; Findings.Clear();
        Status = L10n.T("Kaynak veya iş ayarı değişti. Çıktı almadan önce kaliteyi yeniden denetleyin."); Refresh();
    }
    partial void OnUseDataChanged(bool value) => Invalidate();
    partial void OnHasInputErrorsChanged(bool value) => Invalidate();
    partial void OnCopiesChanged(int value) => Invalidate();
    partial void OnStartNumberChanged(int value) => Invalidate();
    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(IsIdle)); Refresh(); }
    partial void OnSelectedRecipeChanged(ProductionRecipe? value) => Refresh();
    private bool CanWork() => !IsBusy && !_disposed && !HasInputErrors && !_main.Data.IsLoading;
    private bool CanOpenRecipe() => CanWork() && SelectedRecipe is not null;
    private void Refresh()
    {
        OnPropertyChanged(nameof(CanExport)); OnPropertyChanged(nameof(QualitySummary));
        CheckQualityCommand.NotifyCanExecuteChanged(); ExportCommand.NotifyCanExecuteChanged();
        SaveRecipeCommand.NotifyCanExecuteChanged(); OpenRecipeCommand.NotifyCanExecuteChanged(); DeleteRecipeCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }
    private void ReloadRecipes()
    {
        try { var list = _store.Load(); Recipes.Clear(); foreach (var item in list) Recipes.Add(item); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = ex.Message; }
    }
    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task CheckQualityAsync()
    {
        try
        {
            Invalidate();
            var snapshot = ProductionSnapshot.Capture(_main, UseData, Copies, StartNumber);
            string stamp = SourceStamp();
            using var operation = _operation = new CancellationTokenSource();
            IsBusy = true; Progress = 0; Status = L10n.T("Seçilen işin tamamı denetleniyor…");
            var report = await new PreflightService().AnalyzeAsync(new(snapshot.Project, snapshot.Items, snapshot.DotsPerMm, snapshot.Images, snapshot.SkippedRows), operation.Token);
            if (_disposed) return;
            if (operation.IsCancellationRequested) { Invalidate(); Status = L10n.T("Denetim iptal edildi. Çıktı doğrulanmadı."); return; }
            if (stamp != SourceStamp()) { Invalidate(); return; }
            _snapshot = snapshot; _report = report; _stamp = stamp;
            Findings.Clear(); foreach (var issue in report.Issues) Findings.Add(new(issue));
            Progress = report.IsComplete ? 100 : 0;
            Status = report.IsCancelled ? L10n.T("Denetim iptal edildi. Çıktı doğrulanmadı.") : report.CanExport ?
                L10n.T("İşin tamamı denetlendi. Uyarıları inceleyerek çıktı alabilirsiniz.") : L10n.T("İş çıktı almaya hazır değil. Bulguları düzeltip yeniden denetleyin.");
        }
        catch (Exception ex) { if (!_disposed) { Invalidate(); Status = ex.Message; } }
        finally { _operation = null; IsBusy = false; Refresh(); }
    }
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (!CanExport || _snapshot is not { } snapshot) return;
        var format = ExportPng ? BatchExportFormat.PngZip : BatchExportFormat.Pdf;
        string? path = _main.Dialogs.ShowSaveFileDialog(L10n.T("Üretim çıktısını kaydet"), ExportPng ? "PNG ZIP (*.zip)|*.zip" : "PDF (*.pdf)|*.pdf", ExportPng ? "LabelHarbor-labels.zip" : "LabelHarbor-labels.pdf");
        if (path is null || !CanExport) return;
        bool overwrite = File.Exists(path);
        if (overwrite && !_main.Dialogs.Confirm(L10n.T("Çıktıyı değiştir"), L10n.T("Seçilen çıktı dosyası mevcut. Başarılı işlem sonunda bu dosya değiştirilsin mi?"))) return;
        if (!CanExport) return;
        try
        {
            using var operation = _operation = new CancellationTokenSource();
            IsBusy = true; Progress = 0; Status = L10n.T("Doğrulanan işten çıktı hazırlanıyor…");
            var progress = new Progress<BatchExportProgress>(p => { if (!_disposed) Progress = (int)(100L * p.CompletedLabels / Math.Max(1, p.TotalLabels)); });
            var result = await new BatchExportService().ExportAsync(new(snapshot.Project, snapshot.Items, snapshot.DotsPerMm, snapshot.Images, overwrite), format, path, progress, operation.Token);
            if (!_disposed) { Progress = 100; Status = L10n.T($"{result.LabelCount} etiket kaydedildi: {result.OutputPath}"); }
        }
        catch (OperationCanceledException) { if (!_disposed) Status = L10n.T("Çıktı iptal edildi. Önceki hedef dosya korundu."); }
        catch (Exception ex) { if (!_disposed) Status = ex.Message; }
        finally { _operation = null; IsBusy = false; Refresh(); }
    }
    private bool CanCancel() => IsBusy && _operation is not null;
    [RelayCommand(CanExecute = nameof(CanCancel))] private void Cancel() => _operation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanWork))]
    private void SaveRecipe()
    {
        try
        {
            if (_main.Session.Project.FilePath is null || _main.Session.IsDirty)
                if (!_main.Save()) return;
            string? name = _main.Dialogs.Prompt(L10n.T("İş tarifini kaydet"), L10n.T("Tarif adı"), SelectedRecipe?.Name ?? _main.Session.Project.DisplayName);
            if (string.IsNullOrWhiteSpace(name)) return;
            string? dataPath = UseData ? _main.Data.Table?.SourcePath : null;
            if (UseData && (dataPath is null || !File.Exists(dataPath))) throw new IOException(L10n.T("Veri dosyası bulunamadı; tarifi kaydetmeden önce dosyayı yükleyin."));
            var dataInfo = dataPath is null ? null : new FileInfo(dataPath);
            // Capture validates counts, fields and serial-number restrictions before persisting a reusable job.
            _ = ProductionSnapshot.Capture(_main, UseData, Copies, StartNumber);
            var recipe = new ProductionRecipe { Id = Guid.NewGuid().ToString("N"), Name = name.Trim(),
                ProjectPath = Path.GetFullPath(_main.Session.Project.FilePath!), DataPath = dataPath,
                ImportProfileId = _main.SelectedImportProfileId,
                Import = UseData && _main.Data.Table is { } table ? new(table.SheetName, table.HeaderRowNumber, table.EncodingName, table.Delimiter) : null,
                UseData = UseData, Copies = Copies, StartNumber = StartNumber,
                Format = ExportPng ? BatchExportFormat.PngZip : BatchExportFormat.Pdf,
                DataLength = dataInfo?.Length, DataWriteUtc = dataInfo?.LastWriteTimeUtc,
                Rows = UseData ? _main.Data.Rows.Where(r => r.IsSelected && r.Copies > 0).Select(r => new ProductionRecipeRow(r.RowNumber, r.Copies)).ToList() : [] };
            _store.Save(recipe); ReloadRecipes(); SelectedRecipe = Recipes.First(r => r.Id == recipe.Id);
            Status = L10n.T("Yerel iş tarifi kaydedildi. Açıldığında yeniden kalite denetimi gerekir.");
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    [RelayCommand(CanExecute = nameof(CanOpenRecipe))]
    private async Task OpenRecipeAsync()
    {
        if (SelectedRecipe is not { } recipe) return;
        try
        {

            if (!File.Exists(recipe.ProjectPath)) throw new IOException(L10n.T("Tarifin etiket dosyası bulunamadı."));
            if (recipe.UseData)
            {
                if (!File.Exists(recipe.DataPath)) throw new IOException(L10n.T("Tarifin veri dosyası bulunamadı."));
                var info = new FileInfo(recipe.DataPath!);
                if ((info.Length != recipe.DataLength || info.LastWriteTimeUtc != recipe.DataWriteUtc) &&
                    !_main.Dialogs.Confirm(L10n.T("Veri dosyası değişti"), L10n.T("Tarif kaydedildikten sonra veri dosyası değişmiş. Kayıtlı satır numaraları farklı ürünleri seçebilir. Yükleyip seçimi incelemek istiyor musunuz?"))) return;
            }
            if (!_main.OpenProject(recipe.ProjectPath, askToSave: true, offerLinkedData: false)) return;
            Invalidate();
            using var operation = _operation = new CancellationTokenSource();
            IsBusy = true; Status = L10n.T("Tarifin yerel kaynakları yükleniyor…");
            var owner = _main.ActiveDocument;
            if (recipe.UseData)
            {
                _main.SelectedImportProfileId = recipe.ImportProfileId ?? "";
                var previousTable = _main.Data.Table;
                string previousVersion = _main.Session.Version;
                var table = await DataImporter.ImportAsync(recipe.DataPath!, recipe.Import?.ToOptions(), operation.Token);
                var sheets = await Task.Run(() => DataImporter.IsExcelFile(recipe.DataPath!) ? DataImporter.GetSheetNames(recipe.DataPath!) : [], operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                if (_disposed || owner != _main.ActiveDocument) return;
                if (_main.Data.Table != previousTable || _main.Session.Version != previousVersion)
                    throw new IOException(L10n.T("Yükleme sırasında kaynak değişti. Tarif uygulanmadı; yeniden açın."));
                _main.Data.ApplyTable(table, sheets);
                var selections = recipe.Rows.ToDictionary(r => r.RowNumber, r => r.Copies);
                if (selections.Keys.Except(_main.Data.Rows.Select(r => r.RowNumber)).Any()) throw new IOException(L10n.T("Tarifteki bazı satırlar artık yok. Veri seçimini inceleyip tarifi yeniden kaydedin."));
                _main.Data.ApplyRowSelection(selections);
            }
            UseData = recipe.UseData; Copies = recipe.Copies; StartNumber = recipe.StartNumber; ExportPng = recipe.Format == BatchExportFormat.PngZip;
            Status = L10n.T("Tarif açıldı. Kaynakları ve satır seçimini inceleyip kaliteyi denetleyin.");
        }
        catch (OperationCanceledException) { if (!_disposed) Status = L10n.T("Tarif yükleme iptal edildi."); }
        catch (Exception ex) { if (!_disposed) Status = ex.Message; }
        finally { _operation = null; IsBusy = false; Refresh(); }
    }
    [RelayCommand(CanExecute = nameof(CanOpenRecipe))]
    private void DeleteRecipe()
    {
        if (SelectedRecipe is not { } recipe || !_main.Dialogs.Confirm(L10n.T("Tarifi kaldır"), L10n.T("Yalnız yerel iş tarifi kaldırılsın mı? Etiket ve veri dosyaları korunur."))) return;
        try { _store.Delete(recipe.Id); SelectedRecipe = null; ReloadRecipes(); }
        catch (Exception ex) { Status = ex.Message; }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _operation?.Cancel();
        _main.PropertyChanged -= OnMainChanged;
        if (_owner is not null) { _owner.Session.Changed -= OnSourceChanged; _owner.Data.PropertyChanged -= OnDataChanged; }
        _snapshot = null; _report = null;
    }
}

public sealed record ProductionFindingViewModel(PreflightIssue Issue)
{
    public string Severity => Issue.Severity switch { PreflightSeverity.Error => L10n.T("Hata"), PreflightSeverity.Warning => L10n.T("Uyarı"), _ => L10n.T("Bilgi") };
    public int? RowNumber => Issue.RowNumber;
    public string? ElementName => Issue.ElementName;
    public string Message => Issue.Message;
    public string? Recommendation => Issue.Recommendation;
    public string Code => Issue.Code;
}
