using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Documents;
using AkelEtiket.Editor.Services;
using AkelEtiket.Rendering.Symbols;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

/// <summary>Sol paneldeki hazır simge (önizlemesi arka planda çizilir).</summary>
public sealed partial class SymbolItemViewModel(SymbolInfo info) : ObservableObject
{
    public SymbolInfo Info { get; } = info;
    public string Key => Info.Key;
    public string Name => Info.Name;
    public string ShortName => Info.ShortName;
    public string Category => Info.Category;
    public string ToolTip => $"{Info.Name}\n{Info.Description}";

    /// <summary>Baskıdaki gibi 1-bit önizleme (PNG).</summary>
    [ObservableProperty]
    public partial byte[]? PreviewPng { get; set; }
}

/// <summary>Tasarım kolaylıkları: tuvalde yazı düzenleme, hazır simgeler, biçim boyacısı, geri alma geçmişi.</summary>
public sealed partial class MainViewModel
{
    private ElementStyle? _copiedStyle;
    private IReadOnlyList<SymbolItemViewModel>? _symbols;

    /// <summary>Tuval, bu öğe için yerinde yazı düzenleyicisini açmalı.</summary>
    public event EventHandler<ElementViewModel>? InlineTextEditRequested;

    /// <summary>Metin öğesini tuvalde düzenlemeye açar (kilitli/gizli öğe ya da metin değilse açılmaz).</summary>
    public bool RequestInlineTextEdit(ElementViewModel? vm)
    {
        if (vm is null || !InlineTextEditing.CanEdit(vm.Model)) return false;
        InlineTextEditRequested?.Invoke(this, vm);
        return true;
    }

    /// <summary>Tuvaldeki düzenleyiciden gelen metni tek geri alma adımıyla yazar.</summary>
    public bool CommitInlineText(ElementViewModel vm, string text)
    {
        if (vm.Model is not TextElement element) return false;
        bool changed = InlineTextEditing.Commit(Session, element, text);
        if (changed) SetStatus(L10n.T("Metin güncellendi (Ctrl+Z ile geri alınır)."));
        return changed;
    }

    // ───────────────────────── Hazır simgeler ─────────────────────────

    /// <summary>Hazır simgeler (önizlemeler ilk erişimde arka planda üretilir).</summary>
    public IReadOnlyList<SymbolItemViewModel> Symbols
    {
        get
        {
            if (_symbols is not null) return _symbols;
            var items = SymbolLibrary.All.Select(s => new SymbolItemViewModel(s)).ToList();
            _symbols = items;
            var dispatcher = _services.Dispatcher;
            _ = Task.Run(() =>
            {
                foreach (var item in items)
                {
                    byte[]? png;
                    try
                    {
                        png = SymbolLibrary.RenderPreviewPng(item.Key);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        png = null;
                    }
                    dispatcher.Post(() => item.PreviewPng = png);
                }
            });
            return items;
        }
    }

    /// <summary>Hazır simgeyi etiketin ortasına 20 mm görsel olarak ekler (Logo yöntemi, keskin 1-bit).</summary>
    [RelayCommand]
    private void InsertSymbol(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || !SymbolLibrary.TryGet(key, out var info)) return;
        var asset = SymbolLibrary.CreateAsset(info.Key);
        if (!Session.Assets.Contains(asset.Id)) Session.Assets.AddExisting(asset);
        var label = Session.Document.Label;
        double size = Math.Round(Math.Clamp(Math.Min(label.WidthMm, label.HeightMm) * 0.2, 8, 25), 1);
        Session.AddCentered(SymbolLibrary.CreateElement(info, asset, size));
        SetStatus(L10n.T($"\"{info.Name}\" simgesi eklendi ({size:0.#} mm). Boyutunu tutamaçlardan değiştirebilirsiniz."));
    }

    // ───────────────────────── Biçim boyacısı ─────────────────────────

    private bool CanCopyStyle() => Session.SelectedElements.Count == 1;

    /// <summary>Seçili öğenin biçimini kopyalar (Ctrl+Shift+C).</summary>
    [RelayCommand(CanExecute = nameof(CanCopyStyle))]
    private void CopyStyle()
    {
        if (Session.SelectedElements.Count != 1) return;
        _copiedStyle = ElementStyle.Capture(Session.SelectedElements[0].Model);
        PasteStyleCommand.NotifyCanExecuteChanged();
        SetStatus(L10n.T($"{_copiedStyle.KindName} biçimi kopyalandı. Aynı türdeki öğeleri seçip Ctrl+Shift+V ile uygulayın."));
    }

    private bool CanPasteStyle() => _copiedStyle is not null && Session.SelectedElements.Count > 0;

    /// <summary>Kopyalanan biçimi seçili, aynı türdeki kilitsiz öğelere uygular (Ctrl+Shift+V); tek geri alma adımı.</summary>
    [RelayCommand(CanExecute = nameof(CanPasteStyle))]
    private void PasteStyle()
    {
        if (_copiedStyle is not { } style) return;
        var targets = Session.SelectedModels.Where(style.CanApplyTo).ToList();
        if (targets.Count == 0)
        {
            SetStatus(L10n.T($"Biçim yalnızca {style.KindName.ToLowerInvariant()} öğelerine uygulanabilir (kilitli öğeler hariç)."));
            return;
        }
        int count;
        using (Session.BeginTransaction(L10n.T("Biçim yapıştırıldı")))
        {
            count = style.ApplyTo(targets);
        }
        SetStatus(L10n.T($"Biçim {count} öğeye uygulandı."));
    }

    // ───────────────────────── Geri alma geçmişi ─────────────────────────

    /// <summary>Geri alma geçmişi (en yeni önce).</summary>
    public IReadOnlyList<UndoHistoryItem> GetUndoHistory() => Session.GetUndoHistory();

    /// <summary>Geçmişten seçilen değişikliğe kadar (o da dahil) geri alır.</summary>
    [RelayCommand]
    private void UndoTo(UndoHistoryItem? item)
    {
        if (item is null) return;
        int done = Session.UndoSteps(item.Steps);
        if (done > 0) SetStatus(done == 1 ? L10n.T("Geri alındı.") : L10n.T($"{done} adım geri alındı (Ctrl+Y ile ileri alınır)."));
    }
}
