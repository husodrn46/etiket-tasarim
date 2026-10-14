using System.Collections.ObjectModel;
using System.Globalization;
using AkelEtiket.Assistant;
using AkelEtiket.Assistant.Chat;
using AkelEtiket.Core.Automation;
using AkelEtiket.Design;
using AkelEtiket.Design.Preview;
using AkelEtiket.Design.Tools;
using AkelEtiket.Design.Workspace;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.LocalApi;
using AkelEtiket.Rendering;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AkelEtiket.Editor.Ai;

/// <summary>Sohbet satırının türü.</summary>
public enum AssistantMessageKind
{
    User,
    Assistant,
    Step,
    Info,
    Error,
}

/// <summary>Asistan panelindeki bir satır.</summary>
public sealed partial class AssistantMessageViewModel(AssistantMessageKind kind, string text) : ObservableObject
{
    public AssistantMessageKind Kind { get; } = kind;

    [ObservableProperty]
    public partial string Text { get; set; } = text;

    public bool IsUser => Kind == AssistantMessageKind.User;
    public bool IsStep => Kind == AssistantMessageKind.Step;
    public bool IsError => Kind == AssistantMessageKind.Error;
}

/// <summary>Onay bekleyen yapay zekâ önerisi: önce/sonra görüntüsü, değişiklikler, çakışmalar.</summary>
public sealed partial class ProposalItemViewModel : ObservableObject
{
    public ProposalItemViewModel(ProposalInfo info)
    {
        Info = info;
    }

    public ProposalInfo Info { get; }
    public string Id => Info.Id;
    public string Header => $"{Info.ClientName} · {Info.CreatedAt.ToLocalTime():dd.MM HH:mm}";
    public string Target => string.IsNullOrWhiteSpace(Info.TargetName) ? Info.TargetDesignId : Info.TargetName;
    public string Summary => Info.Summary;
    public IReadOnlyList<string> Changes => Info.Changes;

    [ObservableProperty]
    public partial byte[]? BeforePng { get; set; }

    [ObservableProperty]
    public partial byte[]? AfterPng { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> Conflicts { get; set; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public bool HasConflicts => Conflicts.Count > 0;

    partial void OnConflictsChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(HasConflicts));
}

/// <summary>
/// Yapay Zekâ paneli: dış ajanlardan (MCP/yerel API) ve uygulama içi asistandan gelen önerileri önce/sonra
/// önizlemesiyle gösterir; kabul edilen öneri açık belgeye tek geri alma adımı olarak uygulanır. Uygulama içi
/// asistan, açık tasarımın bir taslağında çalışır ve sonucu yine öneri olarak sunar.
/// </summary>
public sealed partial class AiPanelViewModel : ObservableObject, IAsyncDisposable
{
    public const string AssistantClientName = "Uygulama içi asistan";

    private readonly MainViewModel _main;
    private readonly IUiDispatcher _dispatcher;
    private readonly AiSettingsMonitor _settings;
    private readonly HttpClient _http = ChatModelFactory.CreateHttpClient();
    private CancellationTokenSource? _runCancellation;

    public AiPanelViewModel(MainViewModel main, IUiDispatcher dispatcher, AiSettingsMonitor settings, DesignService service,
        DesktopLiveDocumentHost liveHost, AiConnectionService connection)
    {
        _main = main;
        _dispatcher = dispatcher;
        _settings = settings;
        Service = service;
        LiveHost = liveHost;
        Connection = connection;
        Tools = new DesignToolset(service);
        liveHost.ProposalArrived += (_, _) => RefreshProposals();
        liveHost.DesignSavedByAi += OnDesignSavedByAi;
        liveHost.OpenRequestedByAi += (_, request) =>
        {
            if (OpenRequests.All(r => r.DesignId != request.DesignId)) OpenRequests.Add(request);
            _main.IsAiPanelOpen = true;
        };
        service.Proposals.Changed += (_, _) => _dispatcher.Post(RefreshProposals);
        connection.StatusChanged += (_, _) => _dispatcher.Post(() =>
        {
            ConnectionStatus = connection.StatusText;
            IsConnected = connection.State == AiConnectionState.Hosting;
        });
        connection.Activity += (_, activity) => _dispatcher.Post(() => AddActivity(activity));
        ConnectionStatus = connection.StatusText;
    }

    /// <summary>Testler için: gerçek sağlayıcı yerine senaryolu model.</summary>
    internal Func<AssistantProviderSettings, string?, IChatModel>? ModelFactoryOverride { get; set; }

    public DesignService Service { get; }
    public DesktopLiveDocumentHost LiveHost { get; }
    public AiConnectionService Connection { get; }
    public DesignToolset Tools { get; }

    public ObservableCollection<ProposalItemViewModel> Proposals { get; } = [];
    public ObservableCollection<AssistantMessageViewModel> Messages { get; } = [];
    public ObservableCollection<string> Activity { get; } = [];

    /// <summary>Ajanların "uygulamada aç" önerileri.</summary>
    public ObservableCollection<OpenRequest> OpenRequests { get; } = [];

    /// <summary>Önerilen tasarımı açar (kaydedilmemiş değişiklik varsa önce sorulur).</summary>
    [RelayCommand]
    private void OpenRequested(OpenRequest? request)
    {
        if (request is null) return;
        if (Service.ResolveFilePath(request.DesignId) is { } path)
        {
            if (_main.OpenProject(path, askToSave: true)) OpenRequests.Remove(request);
            return;
        }
        if (Service.GetNewDesignCopy(request.DesignId) is { } project)
        {
            if (!_main.ConfirmCloseDocument()) return;
            _main.OpenProjectAsNew(project, $"Yapay zekânın hazırladığı \"{request.Name}\" açıldı (kaydetmek için Ctrl+S).");
            OpenRequests.Remove(request);
            return;
        }
        Messages.Add(new AssistantMessageViewModel(AssistantMessageKind.Error, $"\"{request.Name}\" artık bulunamıyor."));
        OpenRequests.Remove(request);
    }

    [RelayCommand]
    private void DismissOpenRequest(OpenRequest? request)
    {
        if (request is not null) OpenRequests.Remove(request);
    }

    [ObservableProperty]
    public partial string ConnectionStatus { get; set; } = "";

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Prompt { get; set; } = "";

    [ObservableProperty]
    public partial bool OnlySelection { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial int PendingCount { get; set; }

    /// <summary>Asistan sağlayıcısının kısa durumu.</summary>
    public string AssistantStatus
    {
        get
        {
            var a = _settings.Current.Assistant;
            if (a.Kind == AssistantProviderKind.None)
                return "Uygulama içi asistan için sağlayıcı seçilmedi (Ayarlar → Yapay Zekâ). Dış ajanlar MCP ile bağlanabilir.";
            string model = string.IsNullOrWhiteSpace(a.Model) ? ChatModelFactory.DefaultModel(a.Kind) ?? "" : a.Model;
            string provider = a.Kind == AssistantProviderKind.Anthropic ? "Anthropic Claude" : "OpenAI uyumlu uç nokta";
            return a.HasValidConsent ? $"{provider} · {model}" : $"{provider} · veri gönderme izni verilmedi (Ayarlar → Yapay Zekâ)";
        }
    }

    public int SelectionCount => _main.Session.SelectedElements.Count;

    /// <summary>Panel ilk açıldığında ve uygulama açılışında: diskteki bekleyen önerileri yükler.</summary>
    public void Initialize()
    {
        Service.Proposals.Reload();
        Service.Working.Reload();
        RefreshProposals();
        OnPropertyChanged(nameof(AssistantStatus));
    }

    /// <summary>Ayarlar değişince.</summary>
    public void OnSettingsChanged()
    {
        _settings.Invalidate();
        OnPropertyChanged(nameof(AssistantStatus));
    }

    public void OnSelectionChanged() => OnPropertyChanged(nameof(SelectionCount));

    private void RefreshProposals()
    {
        var pending = Service.Proposals.Pending;
        foreach (var stale in Proposals.Where(p => pending.All(x => x.Id != p.Id)).ToList()) Proposals.Remove(stale);
        foreach (var info in pending.OrderBy(p => p.CreatedAt))
        {
            if (Proposals.Any(p => p.Id == info.Id)) continue;
            var item = new ProposalItemViewModel(info);
            Proposals.Add(item);
            _ = LoadPreviewAsync(item);
        }
        PendingCount = Proposals.Count;
    }

    private async Task LoadPreviewAsync(ProposalItemViewModel item)
    {
        try
        {
            var (before, after, assets, conflicts) = await Service.GetProposalPreviewAsync(item.Id, CancellationToken.None);
            var (beforePng, afterPng) = await Task.Run(() =>
            {
                var b = PreviewRenderer.Render(before, assets, null, null, RenderMode.Design, 4);
                var a = PreviewRenderer.Render(after, assets, null, null, RenderMode.Design, 4);
                return (PreviewRenderer.EncodeMono(b.Bitmap, 4), PreviewRenderer.EncodeMono(a.Bitmap, 4));
            });
            item.BeforePng = beforePng;
            item.AfterPng = afterPng;
            item.Conflicts = conflicts;
        }
        catch (ToolException ex)
        {
            item.Conflicts = [ex.Message];
        }
    }

    [RelayCommand]
    private async Task AcceptAsync(ProposalItemViewModel? item)
    {
        if (item is null || item.IsBusy) return;
        item.IsBusy = true;
        try
        {
            var result = await Service.AcceptProposalAsync(item.Id, CancellationToken.None);
            if (result.Status == ProposalStatus.Accepted)
            {
                _main.ShowStatus($"Yapay zekâ önerisi uygulandı ({result.ClientName}). Geri almak için Ctrl+Z.");
                if (result.Conflicts.Count > 0)
                    Messages.Add(new AssistantMessageViewModel(AssistantMessageKind.Info, "Çakışmalar: " + string.Join(" ", result.Conflicts)));
            }
            else
            {
                Messages.Add(new AssistantMessageViewModel(AssistantMessageKind.Error, "Öneri uygulanamadı: " + result.StatusMessage));
            }
        }
        catch (ToolException ex)
        {
            Messages.Add(new AssistantMessageViewModel(AssistantMessageKind.Error, ex.Message));
        }
        finally
        {
            item.IsBusy = false;
            RefreshProposals();
            LiveHost.Refresh();
        }
    }

    [RelayCommand]
    private void Reject(ProposalItemViewModel? item)
    {
        if (item is null) return;
        Service.RejectProposal(item.Id, "Kullanıcı reddetti.");
        RefreshProposals();
    }

    private bool CanSend() => !IsRunning && !string.IsNullOrWhiteSpace(Prompt);

    /// <summary>Uygulama içi asistan: açık tasarımın taslağında çalışır, sonucu öneri olarak sunar.</summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        string request = Prompt.Trim();
        var settings = _settings.Current;
        var provider = settings.Assistant;
        if (provider.Kind == AssistantProviderKind.None)
        {
            Messages.Add(new AssistantMessageViewModel(AssistantMessageKind.Info,
                "Uygulama içi asistan için Ayarlar → Yapay Zekâ bölümünden bir sağlayıcı seçin (Anthropic Claude ya da yerel/OpenAI uyumlu model)."));
            return;
        }
        if (!provider.HasValidConsent)
        {
            Messages.Add(new AssistantMessageViewModel(AssistantMessageKind.Info,
                "Tasarım verilerinin seçilen yapay zekâ sağlayıcısına gönderilmesine henüz izin verilmedi. Ayarlar → Yapay Zekâ bölümünden izin verebilirsiniz."));
            return;
        }
        string? key = SecretProtector.Unprotect(provider.ProtectedApiKey);
        if (provider.Kind == AssistantProviderKind.Anthropic && string.IsNullOrEmpty(key))
        {
            Messages.Add(new AssistantMessageViewModel(AssistantMessageKind.Info, "Anthropic API anahtarı girilmemiş (Ayarlar → Yapay Zekâ)."));
            return;
        }

        Messages.Add(new AssistantMessageViewModel(AssistantMessageKind.User, request));
        Prompt = "";
        IsRunning = true;
        _runCancellation = new CancellationTokenSource();
        var token = _runCancellation.Token;
        WorkingDesign? draft = null;
        try
        {
            var session = _main.Session;
            string liveId = DesignId.Live(session.SessionNumber).ToString();
            draft = await Service.BeginDraftForHostAsync(liveId, AssistantClientName, token);
            IReadOnlySet<string>? scope = OnlySelection && session.SelectedElements.Count > 0
                ? session.SelectedElements.Select(e => e.Id).ToHashSet(StringComparer.Ordinal)
                : null;
            var context = new ToolCallContext
            {
                ClientName = AssistantClientName,
                ClientId = draft.Meta.Id,
                AllowedElementIds = scope,
                ForcedDraftId = draft.DesignId,
            };

            var state = await Service.ReadAsync(draft.DesignId, context, token);
            var render = PreviewRenderer.Render(state.Document, state.Assets, state.PreviewFields, Service.ImageResolverFor(state.Document));
            string designJson = DesignViews.Design(state, render).ToJsonString(ToolJson.OutputOptions);
            byte[]? preview = provider.SendPreviewImages ? PreviewRenderer.EncodeMono(render.Bitmap, render.DotsPerMm) : null;

            var model = ModelFactoryOverride?.Invoke(provider, key) ?? ChatModelFactory.Create(provider, key, _http);
            var assistant = new DesignAssistantSession(model, Tools.Registry, context, new AssistantSessionOptions());
            var progress = new Progress<AssistantEvent>(OnAssistantEvent);
            var result = await assistant.RunAsync(new AssistantRequest
            {
                UserText = request,
                DesignJson = designJson,
                PreviewPng = preview,
                LabelSizeText = $"{state.Document.Label.WidthMm.ToString("0.#", CultureInfo.InvariantCulture)} × " +
                                $"{state.Document.Label.HeightMm.ToString("0.#", CultureInfo.InvariantCulture)} mm",
            }, progress, token);

            if (!string.IsNullOrWhiteSpace(result.FinalText))
            {
                var kind = result.Status is AssistantRunStatus.Failed or AssistantRunStatus.Refused ? AssistantMessageKind.Error : AssistantMessageKind.Assistant;
                Messages.Add(new AssistantMessageViewModel(kind, result.FinalText));
            }
            await FinishDraftAsync(draft, result, token);
        }
        catch (Exception ex) when (ex is ToolException or ChatModelException or AssistantConfigurationException or AssistantConsentException)
        {
            Messages.Add(new AssistantMessageViewModel(AssistantMessageKind.Error, ex.Message));
            if (draft is not null) await DiscardQuietlyAsync(draft);
        }
        catch (OperationCanceledException)
        {
            Messages.Add(new AssistantMessageViewModel(AssistantMessageKind.Info, "Durduruldu."));
            if (draft is not null) await DiscardQuietlyAsync(draft);
        }
        finally
        {
            IsRunning = false;
            _runCancellation?.Dispose();
            _runCancellation = null;
        }
    }

    /// <summary>Taslakta değişiklik varsa öneri olarak sunar (doğrudan modda hemen uygulanır); yoksa kapatır.</summary>
    private async Task FinishDraftAsync(WorkingDesign draft, AssistantRunResult result, CancellationToken token)
    {
        var baseDoc = draft.GetBaseDocument();
        bool changed = baseDoc is not null && !DocumentDiff.Compare(baseDoc, draft.Project.Document).IsEmpty;
        if (!changed || result.Status is AssistantRunStatus.Cancelled or AssistantRunStatus.Failed or AssistantRunStatus.Refused)
        {
            await DiscardQuietlyAsync(draft);
            if (!changed && result.Status == AssistantRunStatus.Completed)
                Messages.Add(new AssistantMessageViewModel(AssistantMessageKind.Info, "Tasarımda değişiklik yapılmadı."));
            return;
        }
        var owner = new ToolCallContext { ClientName = AssistantClientName };
        var proposal = await Service.SubmitDraftAsync(draft.DesignId, result.FinalText, owner, token);
        Messages.Add(new AssistantMessageViewModel(AssistantMessageKind.Info, proposal.Status == ProposalStatus.Accepted
            ? "Değişiklikler uygulandı (doğrudan mod). Geri almak için Ctrl+Z."
            : "Öneri hazır: aşağıda önce/sonra görüntüsünü inceleyip kabul edebilir veya reddedebilirsiniz."));
        RefreshProposals();
        LiveHost.Refresh();
    }

    private async Task DiscardQuietlyAsync(WorkingDesign draft)
    {
        try
        {
            await Service.DiscardDraftAsync(draft.DesignId, new ToolCallContext { ClientName = AssistantClientName }, CancellationToken.None);
        }
        catch (ToolException)
        {
        }
    }

    private bool CanStop() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _runCancellation?.Cancel();

    [RelayCommand]
    private void ClearMessages() => Messages.Clear();

    /// <summary>Yapay Zekâ ayarları penceresi; kaydedilince bağlantı yeni ayarlarla yeniden başlatılır.</summary>
    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        var vm = new AiSettingsViewModel(AiSettingsStore.LoadWithToken(), _main.Dialogs);
        if (!_main.Dialogs.ShowDialog(vm)) return;
        var settings = vm.BuildSettings();
        try
        {
            AiSettingsStore.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _main.Dialogs.ShowMessage("Yapay Zekâ Ayarları", "Ayarlar kaydedilemedi: " + ex.Message, MessageKind.Error);
            return;
        }
        OnSettingsChanged();
        await Connection.ApplySettingsAsync();
        _main.ShowStatus(settings.Enabled
            ? "Yapay zekâ ayarları kaydedildi. " + Connection.StatusText
            : "Yapay zekâ ayarları kaydedildi. Dış ajan bağlantısı kapalı.");
    }

    private void OnAssistantEvent(AssistantEvent e)
    {
        switch (e.Kind)
        {
            case AssistantEventKind.ToolCallStarted:
                Messages.Add(new AssistantMessageViewModel(AssistantMessageKind.Step, $"▸ {e.ToolName} {e.Text}".TrimEnd()));
                break;
            case AssistantEventKind.ToolCallFinished when e.IsError:
                Messages.Add(new AssistantMessageViewModel(AssistantMessageKind.Step, $"  ✕ {e.ToolName}: {e.Text}"));
                break;
            case AssistantEventKind.AssistantText when !string.IsNullOrWhiteSpace(e.Text):
                Messages.Add(new AssistantMessageViewModel(AssistantMessageKind.Assistant, e.Text!));
                break;
        }
    }

    private void AddActivity(ApiActivity activity)
    {
        Activity.Insert(0, $"{activity.Time:HH:mm:ss} · {activity.Client} · {activity.Tool}{(activity.Ok ? "" : " (hata)")}");
        while (Activity.Count > 50) Activity.RemoveAt(Activity.Count - 1);
    }

    private void OnDesignSavedByAi(object? sender, SavedDesignInfo info)
    {
        _main.ShowStatus($"Yapay zekâ ({info.ClientName}) yeni bir tasarım kaydetti: {Path.GetFileName(info.FilePath)} — Şablonlar panelinde.");
        _main.Templates.Refresh();
    }

    public async ValueTask DisposeAsync()
    {
        _runCancellation?.Cancel();
        _http.Dispose();
        await Connection.DisposeAsync();
    }
}
