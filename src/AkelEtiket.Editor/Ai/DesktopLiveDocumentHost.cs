using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Model;
using AkelEtiket.Core.Serialization;
using AkelEtiket.Design;
using AkelEtiket.Design.Tools;
using AkelEtiket.Design.Workspace;
using AkelEtiket.Editor.Documents;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.ViewModels;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.Ai;

/// <summary>Arayüz iş parçacığında çalıştırıp sonucunu bekleme.</summary>
public static class UiDispatcherExtensions
{
    public static Task<T> InvokeAsync<T>(this IUiDispatcher dispatcher, Func<T> function)
    {
        if (dispatcher.IsUiThread)
        {
            try
            {
                return Task.FromResult(function());
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Post(() =>
        {
            try
            {
                completion.SetResult(function());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        return completion.Task;
    }
}

/// <summary>
/// Masaüstünde açık belgeyi yapay zekâ araçlarına açar. Değişiklikler önce belgenin kopyasında denenir; başarılıysa
/// açık belgeye tek bir açıklamalı geri alma adımı olarak uygulanır ve tuvalde hemen görünür. Hata olursa açık belgeye
/// hiç dokunulmaz.
/// </summary>
public sealed class DesktopLiveDocumentHost : ILiveDocumentHost
{
    private readonly MainViewModel _main;
    private readonly IUiDispatcher _dispatcher;
    private volatile LiveDocumentInfo? _info;

    public DesktopLiveDocumentHost(MainViewModel main, IUiDispatcher dispatcher)
    {
        _main = main;
        _dispatcher = dispatcher;
    }

    /// <summary>Yeni bir öneri geldi (arayüz iş parçacığında tetiklenir).</summary>
    public event EventHandler<ProposalInfo>? ProposalArrived;

    /// <summary>Yapay zekâ yeni bir tasarım dosyası kaydetti (arayüz iş parçacığında).</summary>
    public event EventHandler<SavedDesignInfo>? DesignSavedByAi;

    /// <summary>Açık belge özetini yeniler; arayüz iş parçacığından çağrılmalı (oturum/seçim/kayıt değişince).</summary>
    public void Refresh()
    {
        var session = _main.Session;
        _info = Describe(session);
    }

    private static LiveDocumentInfo Describe(DocumentSession session) => new(
        session.SessionNumber,
        session.Project.FilePath is { } path ? Path.GetFileNameWithoutExtension(path) : session.Document.Name,
        session.Project.FilePath,
        session.Version,
        session.IsDirty,
        session.SelectedElements.Select(e => e.Id).ToList());

    public IReadOnlyList<LiveDocumentInfo> GetOpenDocuments() => _info is { } info ? [info] : [];

    public Task<LiveSnapshot> SnapshotAsync(int sessionNumber, CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() =>
        {
            var session = Require(sessionNumber);
            return new LiveSnapshot(Describe(session), ModelJson.Clone(session.Document), session.Assets.Assets.ToList(), session.Fields);
        });

    public Task<LiveMutationResult<T>> MutateAsync<T>(int sessionNumber, string? expectedVersion, string description,
        Func<LabelDocument, AssetStore, T> mutate, CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() =>
        {
            var session = Require(sessionNumber);
            if (!string.IsNullOrWhiteSpace(expectedVersion) && expectedVersion.Trim() != session.Version)
                throw new ToolException(ToolErrorCodes.VersionConflict,
                    L10n.T($"Açık tasarım siz okuduktan sonra değişti (beklenen {expectedVersion}, güncel {session.Version}). get_design ile yeniden okuyun."),
                    new { current_version = session.Version });
            var copy = ModelJson.Clone(session.Document);
            var assets = DesignService.CopyAssets(session.Assets);
            T value = mutate(copy, assets);
            bool changed = ModelJson.Serialize(copy, indented: false) != ModelJson.Serialize(session.Document, indented: false);
            if (changed)
            {
                var newAssets = assets.Assets.Where(a => !session.Assets.Contains(a.Id)).ToList();
                session.ReplaceContent(copy, newAssets, description);
            }
            Refresh();
            return new LiveMutationResult<T>(value, session.Version, changed);
        });

    public Task<string> ReplaceContentAsync(int sessionNumber, LabelDocument content, IReadOnlyList<ImageAsset> assets, string description,
        CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() =>
        {
            var session = Require(sessionNumber);
            session.ReplaceContent(content, assets, description);
            Refresh();
            return session.Version;
        });

    public void ProposalCreated(ProposalInfo proposal) => _dispatcher.Post(() => ProposalArrived?.Invoke(this, proposal));

    public void DesignSaved(string designId, string name, string filePath, string clientName) =>
        _dispatcher.Post(() => DesignSavedByAi?.Invoke(this, new SavedDesignInfo(designId, name, filePath, clientName)));

    /// <summary>Ajan bir tasarımın açılmasını önerdi (arayüz iş parçacığında).</summary>
    public event EventHandler<OpenRequest>? OpenRequestedByAi;

    public void OpenRequested(string designId, string name, string clientName) =>
        _dispatcher.Post(() => OpenRequestedByAi?.Invoke(this, new OpenRequest(designId, name, clientName, DateTimeOffset.Now)));

    private DocumentSession Require(int sessionNumber)
    {
        var session = _main.Session;
        if (session.SessionNumber != sessionNumber)
            throw new ToolException(ToolErrorCodes.NotFound,
                L10n.T($"acik:{sessionNumber} artık açık değil (kullanıcı başka bir belge açtı). list_designs ile güncel listeyi alın."));
        return session;
    }
}

/// <summary>Yapay zekânın kaydettiği tasarım.</summary>
public sealed record SavedDesignInfo(string DesignId, string Name, string FilePath, string ClientName);

/// <summary>Ajanın "uygulamada aç" önerisi.</summary>
public sealed record OpenRequest(string DesignId, string Name, string ClientName, DateTimeOffset Time)
{
    public string Header => $"{ClientName} · {Time.ToLocalTime():HH:mm}";
}
