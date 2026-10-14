using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;

namespace AkelEtiket.Design.Workspace;

/// <summary>Masaüstünde açık bir belgenin özeti.</summary>
public sealed record LiveDocumentInfo(
    int SessionNumber,
    string Name,
    string? FilePath,
    string Version,
    bool IsDirty,
    IReadOnlyList<string> SelectedElementIds)
{
    public string DesignId => Workspace.DesignId.Live(SessionNumber).ToString();
}

/// <summary>Açık belgenin anlık kopyası (araçlar üzerinde rahatça çalışabilir; canlı belgeyi etkilemez).</summary>
public sealed record LiveSnapshot(LiveDocumentInfo Info, LabelDocument Document, IReadOnlyList<ImageAsset> Assets, IFieldValueSource? PreviewFields);

/// <summary>Canlı belgeye uygulanan değişikliğin sonucu.</summary>
public sealed record LiveMutationResult<T>(T Value, string NewVersion, bool Changed);

/// <summary>Kullanıcıya gösterilecek yapay zekâ olayları (öneri, yeni tasarım).</summary>
public interface IDesignNotificationSink
{
    /// <summary>Onay bekleyen yeni bir öneri oluştu.</summary>
    void ProposalCreated(ProposalInfo proposal);

    /// <summary>Yapay zekâ yeni bir tasarım dosyası kaydetti.</summary>
    void DesignSaved(string designId, string name, string filePath, string clientName);

    /// <summary>Ajan bir tasarımın uygulamada açılmasını öneriyor (kullanıcı tek tıkla açar; kendiliğinden açılmaz).</summary>
    void OpenRequested(string designId, string name, string clientName);
}

/// <summary>
/// Masaüstü uygulamasının yapay zekâ araçlarına açtığı canlı belge erişimi. Uygulama kapalıyken (Worker) yoktur.
/// Bütün yöntemler arayüz iş parçacığında güvenle çalışacak biçimde uygulanmalıdır.
/// </summary>
public interface ILiveDocumentHost : IDesignNotificationSink
{
    /// <summary>Açık belgeler. Kullanıcı paylaşımı kapattıysa boş döner.</summary>
    IReadOnlyList<LiveDocumentInfo> GetOpenDocuments();

    /// <summary>Belgenin anlık kopyasını alır.</summary>
    Task<LiveSnapshot> SnapshotAsync(int sessionNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Belgeyi değiştirir (doğrudan mod). <paramref name="mutate"/> arayüz iş parçacığında, açıklamalı tek bir
    /// geri al adımı içinde çalışır; belge kimliği/sürümü uyuşmazsa çağrılmaz.
    /// </summary>
    Task<LiveMutationResult<T>> MutateAsync<T>(int sessionNumber, string? expectedVersion, string description,
        Func<LabelDocument, AssetStore, T> mutate, CancellationToken cancellationToken);

    /// <summary>Belgenin içeriğini tek bir geri al adımıyla değiştirir (onaylanan öneri).</summary>
    Task<string> ReplaceContentAsync(int sessionNumber, LabelDocument content, IReadOnlyList<ImageAsset> assets, string description,
        CancellationToken cancellationToken);
}
