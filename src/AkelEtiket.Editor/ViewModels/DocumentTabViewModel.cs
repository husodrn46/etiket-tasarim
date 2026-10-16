using System.ComponentModel;
using AkelEtiket.Editor.Documents;
using AkelEtiket.ProjectManager;
using CommunityToolkit.Mvvm.ComponentModel;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

/// <summary>A tab owns its document, data selection, history, recovery and view state.</summary>
public sealed partial class DocumentTabViewModel : ObservableObject, IDisposable
{
    public DocumentTabViewModel(DocumentSession session, DataSourceViewModel data, AutoRecoveryService recovery, bool placeholder = false)
    {
        Session = session;
        Data = data;
        Recovery = recovery;
        IsPlaceholder = placeholder;
        session.PropertyChanged += OnSessionPropertyChanged;
        session.Changed += OnChanged;
        data.PreviewChanged += OnPreviewChanged;
        data.TableChanged += OnTableChanged;
    }

    public DocumentSession Session { get; }
    public DataSourceViewModel Data { get; }
    public AutoRecoveryService Recovery { get; }
    public bool IsDisposed { get; private set; }
    public bool IsPlaceholder { get; set; }
    public bool RecoveryDirty { get; set; }
    public double Zoom { get; set; } = 2;
    public double HorizontalOffset { get; set; }
    public double VerticalOffset { get; set; }
    public string ImportProfileId { get; set; } = "";
    public int? QuickPrintCopies { get; set; }
    public string Status { get; set; } = L10n.T("Hazır");
    public bool HasViewPosition { get; set; }
    [ObservableProperty] public partial bool IsPinned { get; set; }
    public string Name => Session.Project.DisplayName;
    public bool IsDirty => Session.IsDirty;
    public string Caption => (IsPinned ? "◆ " : "") + Name;
    public string Description => (Session.Project.FilePath ?? L10n.T("Kaydedilmemiş etiket")) + (IsDirty ? " · " + L10n.T("Değiştirildi") : "");
    public bool CanRecycle => IsPlaceholder && !IsPinned && !IsDirty && Session.Project.FilePath is null && Session.Document.Elements.Count == 0 && !Data.HasData;
    public event EventHandler? Changed;

    partial void OnIsPinnedChanged(bool value) { OnPropertyChanged(nameof(Caption)); Changed?.Invoke(this, EventArgs.Empty); }
    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentSession.Title) or nameof(DocumentSession.IsDirty)) Refresh();
    }
    private void OnChanged(object? sender, EventArgs e) { RecoveryDirty = true; Refresh(); }
    private void OnPreviewChanged(object? sender, EventArgs e)
    {
        if (!IsDisposed) Session.SetFields(Data.GetPreviewFields(DateTime.Now));
    }
    private void OnTableChanged(object? sender, EventArgs e) { RecoveryDirty = true; OnPreviewChanged(sender,e); }
    public void Refresh()
    {
        OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(Caption)); OnPropertyChanged(nameof(Description));
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        Session.PropertyChanged -= OnSessionPropertyChanged;
        Session.Changed -= OnChanged;
        Data.PreviewChanged -= OnPreviewChanged;
        Data.TableChanged -= OnTableChanged;
        Recovery.Dispose();
        Session.Dispose();
    }
}

public sealed record DocumentTabChangeEventArgs(DocumentTabViewModel? Previous, DocumentTabViewModel Current);
