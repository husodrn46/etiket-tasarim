using System.Collections.ObjectModel;
using AkelEtiket.Core.Model;
using AkelEtiket.Core.Serialization;
using AkelEtiket.Design;
using AkelEtiket.ProjectManager;
using AkelEtiket.Editor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

public sealed partial class MainViewModel
{
    public const int MaximumOpenDocuments = 32;
    public ObservableCollection<DocumentTabViewModel> Documents { get; } = [];
    [ObservableProperty] public partial DocumentTabViewModel? ActiveDocument { get; set; }
    [ObservableProperty] public partial bool IsFocusMode { get; set; }
    public string DocumentCountText => L10n.T($"{Documents.Count} etiket açık");
    public int UnsavedDocumentCount => Documents.Count(t => t.IsDirty);
    public event EventHandler<DocumentTabChangeEventArgs>? DocumentChanging;
    public event EventHandler<DocumentTabChangeEventArgs>? DocumentChanged;

    private void InitializeDocuments()
    {
        Documents.CollectionChanged += (_,_) => RefreshDocumentCommands();
        var tab = new DocumentTabViewModel(Session, Data, _initialRecovery, placeholder:true);
        tab.Changed += OnAnyDocumentChanged;
        Documents.Add(tab);
        ActiveDocument = tab;
    }
    private void OnAnyDocumentChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(UnsavedDocumentCount));
        SaveAllCommand.NotifyCanExecuteChanged();
        Ai?.LiveHost.Refresh();
    }
    private void RefreshDocumentCommands()
    {
        OnPropertyChanged(nameof(DocumentCountText)); OnPropertyChanged(nameof(UnsavedDocumentCount));
        SaveAllCommand.NotifyCanExecuteChanged(); NextDocumentCommand.NotifyCanExecuteChanged();
        PreviousDocumentCommand.NotifyCanExecuteChanged(); CloseOtherDocumentsCommand.NotifyCanExecuteChanged();
        DuplicateDocumentCommand.NotifyCanExecuteChanged();
    }
    partial void OnActiveDocumentChanging(DocumentTabViewModel? oldValue, DocumentTabViewModel? newValue)
    {
        if (newValue is null || newValue.IsDisposed) return;
        if (oldValue is not null)
        {
            oldValue.Zoom = Zoom; oldValue.Status = StatusMessage;
            oldValue.ImportProfileId = SelectedImportProfileId; oldValue.QuickPrintCopies = _quickPrintCopies;
        }
        DocumentChanging?.Invoke(this,new(oldValue,newValue));
    }
    partial void OnActiveDocumentChanged(DocumentTabViewModel? oldValue, DocumentTabViewModel? newValue)
    {
        if (newValue is null || newValue.IsDisposed) return;
        Data = newValue.Data;
        Session = newValue.Session;
        Zoom = newValue.Zoom; StatusMessage = newValue.Status;
        SelectedImportProfileId = newValue.ImportProfileId; _quickPrintCopies = newValue.QuickPrintCopies;
        OnPropertyChanged(nameof(SelectedImportProfileId));
        OnSelectionChanged(this,EventArgs.Empty);
        DocumentChanged?.Invoke(this,new(oldValue,newValue));
    }

    internal bool CanOpenNewDocument() => Settings.Personalization.OpenDocumentsInTabs || ConfirmCloseDocument();

    private DocumentTabViewModel? OpenDocument(LabelProject project, bool forceNewTab = false)
    {
        var previous = ActiveDocument;
        bool replace = previous is not null && !forceNewTab && (!Settings.Personalization.OpenDocumentsInTabs || previous.CanRecycle);
        if (!replace && Documents.Count >= MaximumOpenDocuments)
        {
            Dialogs.ShowMessage(L10n.T("Açık etiketler"),L10n.T($"En fazla {MaximumOpenDocuments} etiket açık olabilir. Önce bir sekmeyi kaydedip kapatın."),MessageKind.Warning);
            return null;
        }
        if (project.FilePath is { } path && Documents.Any(t => SameProjectPath(t.Session.Project.FilePath,path))) project.FilePath=null;
        var tab = new DocumentTabViewModel(CreateSession(project), new DataSourceViewModel(), new AutoRecoveryService(_services.RecoveryFolder));
        tab.Changed += OnAnyDocumentChanged;
        Documents.Add(tab);
        ActiveDocument = tab;
        if (project.FilePath is null && project.Document.Elements.Count > 0) tab.Session.MarkUnsaved();
        if (replace)
        {
            Documents.Remove(previous!); previous!.Changed -= OnAnyDocumentChanged; previous.Dispose();
        }
        return tab;
    }

    private static bool SameProjectPath(string? left,string? right) => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
        string.Equals(Path.GetFullPath(left),Path.GetFullPath(right),StringComparison.OrdinalIgnoreCase);

    [RelayCommand] private void ActivateDocument(DocumentTabViewModel? tab)
    {
        if (tab is not null && Documents.Contains(tab) && !tab.IsDisposed) ActiveDocument = tab;
    }
    private bool HasMultipleDocuments() => Documents.Count > 1;
    [RelayCommand(CanExecute=nameof(HasMultipleDocuments))] private void NextDocument() => CycleDocument(1);
    [RelayCommand(CanExecute=nameof(HasMultipleDocuments))] private void PreviousDocument() => CycleDocument(-1);
    private void CycleDocument(int delta)
    {
        if (ActiveDocument is not { } current || Documents.Count < 2) return;
        ActiveDocument = Documents[(Documents.IndexOf(current)+delta+Documents.Count)%Documents.Count];
    }
    [RelayCommand] private void PinDocument(DocumentTabViewModel? tab)
    {
        tab ??= ActiveDocument;
        if (tab is not null && Documents.Contains(tab)) tab.IsPinned = !tab.IsPinned;
    }
    [RelayCommand] private void CloseDocument(DocumentTabViewModel? tab)
    {
        tab ??= ActiveDocument;
        if (tab is null || !Documents.Contains(tab)) return;
        var initial = ActiveDocument; ActiveDocument = tab;
        if (!ConfirmCloseDocument()) { ActiveDocument = initial; return; }
        var next = Documents.Skip(Documents.IndexOf(tab)+1).FirstOrDefault() ?? Documents.FirstOrDefault(t=>t!=tab);
        if (next is null) next = OpenDocument(LabelProject.CreateBlank(Settings.DefaultLabelWidthMm,Settings.DefaultLabelHeightMm),forceNewTab:true);
        if (next is null) return;
        ActiveDocument = initial is not null && initial != tab && Documents.Contains(initial) ? initial : next;
        Documents.Remove(tab); tab.Changed -= OnAnyDocumentChanged; tab.Dispose();
        Ai.LiveHost.Refresh();
    }
    [RelayCommand(CanExecute=nameof(HasMultipleDocuments))] private void CloseOtherDocuments(DocumentTabViewModel? keep)
    {
        keep ??= ActiveDocument;
        if (keep is null) return;
        // Resolve every save/cancel decision before discarding any other document.
        var candidates = Documents.Where(t=>t!=keep && !t.IsPinned).ToArray();
        var initial=ActiveDocument;
        foreach (var tab in candidates)
        {
            ActiveDocument=tab;
            if (!ConfirmCloseDocument()) { ActiveDocument=initial; return; }
        }
        ActiveDocument=keep;
        foreach (var tab in candidates) { Documents.Remove(tab);tab.Changed-=OnAnyDocumentChanged;tab.Dispose(); }
        Ai.LiveHost.Refresh();
    }
    public bool ConfirmCloseAllDocuments()
    {
        var initial=ActiveDocument;
        foreach (var tab in Documents.ToArray())
        {
            ActiveDocument=tab;
            if (!ConfirmCloseDocument()) { ActiveDocument=initial; return false; }
        }
        ActiveDocument=initial;return true;
    }
    private bool CanSaveAll() => Documents.Any(t=>t.IsDirty);
    [RelayCommand(CanExecute=nameof(CanSaveAll))] private void SaveAll()
    {
        var initial=ActiveDocument;
        bool saved=false;
        try
        {
            foreach (var tab in Documents.Where(t=>t.IsDirty).ToArray()) { ActiveDocument=tab;if(!Save())return; }
            saved=true;
        }
        finally { ActiveDocument=initial; if(saved) SetStatus(L10n.T("Tüm etiketler kaydedildi.")); }
    }
    private bool CanDuplicateDocument() => Documents.Count < MaximumOpenDocuments;
    [RelayCommand(CanExecute=nameof(CanDuplicateDocument))] private void DuplicateDocument()
    {
        if (ActiveDocument is not { } source) return;
        var copy = new LabelProject(ModelJson.Clone(source.Session.Document),DesignService.CopyAssets(source.Session.Assets));
        var tab = OpenDocument(copy,forceNewTab:true);
        if (tab is null) return;
        using (Session.BeginTransaction()) Session.Document.Name = L10n.T($"{source.Name} kopyası");
        if (source.Data.Table is { } table)
        {
            Data.ApplyTable(table,source.Data.SheetNames.ToArray());
            for (int i=0;i<Math.Min(Data.Rows.Count,source.Data.Rows.Count);i++)
            { Data.Rows[i].IsSelected=source.Data.Rows[i].IsSelected;Data.Rows[i].Copies=source.Data.Rows[i].Copies; }
        }
        tab.RecoveryDirty=true;RequestFit();
    }
    [RelayCommand] private void ToggleFocusMode() => IsFocusMode=!IsFocusMode;
}
