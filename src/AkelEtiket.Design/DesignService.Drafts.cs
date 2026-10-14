using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Automation;
using AkelEtiket.Core.Serialization;
using AkelEtiket.Design.Operations;
using AkelEtiket.Design.Security;
using AkelEtiket.ProjectManager;

namespace AkelEtiket.Design;

public sealed partial class DesignService
{
    /// <summary>Var olan bir tasarım (dosya ya da açık belge) için taslak açar.</summary>
    public async Task<WorkingDesign> BeginDraftAsync(string targetDesignId, ToolCallContext context, CancellationToken cancellationToken)
    {
        var id = DesignId.Parse(targetDesignId);
        if (id.Kind is DesignKind.Draft or DesignKind.New)
            throw new ToolException(ToolErrorCodes.InvalidArgument,
                "Taslak yalnızca var olan bir tasarım (dosya: veya acik:) için açılır. Yeni tasarımlar doğrudan düzenlenebilir.");
        var state = await ReadAsync(targetDesignId, new ToolCallContext { ClientName = context.ClientName, ClientId = context.ClientId }, cancellationToken)
            .ConfigureAwait(false);
        var project = new LabelProject(ModelJson.Clone(state.Document), CopyAssets(state.Assets));
        return Working.Create(DesignKind.Draft, project, context.ClientName, meta =>
        {
            // Açık belgenin dosyası izinli klasördeyse hedef dosya kimliğiyle kaydedilir (uygulama yeniden açılsa da bulunur).
            meta.TargetDesignId = state.Kind == DesignKind.Live && state.RelativePath is { } rel ? DesignId.File(rel).ToString() : state.DesignId;
            meta.TargetName = state.Name;
            meta.BaseVersion = state.Version;
            meta.BaseDocumentJson = ModelJson.Serialize(state.Document, indented: false);
        });
    }

    /// <summary>
    /// Masaüstünün uygulama içi asistanı için açık belgenin taslağını açar ("açık tasarımı paylaş" ayarından bağımsız;
    /// asistan yine de yalnızca bu taslağı değiştirebilir).
    /// </summary>
    public async Task<WorkingDesign> BeginDraftForHostAsync(string targetDesignId, string owner, CancellationToken cancellationToken)
    {
        var state = await ReadTrustedAsync(targetDesignId, cancellationToken).ConfigureAwait(false);
        var project = new LabelProject(ModelJson.Clone(state.Document), CopyAssets(state.Assets));
        return Working.Create(DesignKind.Draft, project, owner, meta =>
        {
            meta.TargetDesignId = state.Kind == DesignKind.Live && state.RelativePath is { } rel ? DesignId.File(rel).ToString() : state.DesignId;
            meta.TargetName = state.Name;
            meta.BaseVersion = state.Version;
            meta.BaseDocumentJson = ModelJson.Serialize(state.Document, indented: false);
        });
    }

    /// <summary>
    /// Taslağı öneri olarak gönderir. Onaylı modda kullanıcıya gösterilir; doğrudan modda hemen uygulanır.
    /// </summary>
    public async Task<ProposalInfo> SubmitDraftAsync(string draftId, string summary, ToolCallContext context, CancellationToken cancellationToken)
    {
        var item = GetDraftForOwner(draftId, context);
        if (item.Meta.State != DraftState.Open)
            throw new ToolException(ToolErrorCodes.InvalidArgument, $"Taslak zaten {item.Meta.State} durumunda.");
        var baseDoc = item.GetBaseDocument() ?? throw new ToolException(ToolErrorCodes.Internal, "Taslağın başlangıç hâli bulunamadı.");
        var changes = DocumentDiff.Compare(baseDoc, item.Project.Document);
        if (changes.IsEmpty)
            throw new ToolException(ToolErrorCodes.InvalidArgument, "Taslakta değişiklik yok; göndermeden önce taslağı düzenleyin ya da discard_draft ile kapatın.");

        var proposal = new ProposalInfo
        {
            Id = DesignId.NewShortId(),
            DraftId = item.DesignId,
            TargetDesignId = item.Meta.TargetDesignId ?? "",
            TargetName = item.Meta.TargetName ?? "",
            ClientName = context.ClientName,
            Summary = DesignViews.Clip(string.IsNullOrWhiteSpace(summary) ? "(açıklama yok)" : summary.Trim(), 1000),
            BaseVersion = item.Meta.BaseVersion ?? "",
            Changes = changes.ToLines().Take(100).ToList(),
        };
        await item.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            item.Meta.State = DraftState.Submitted;
            item.Meta.ProposalId = proposal.Id;
            Working.Persist(item);
        }
        finally
        {
            item.Gate.Release();
        }
        Proposals.Save(proposal);

        if (Settings.ChangeMode == AiChangeMode.Direct)
            return await AcceptProposalAsync(proposal.Id, cancellationToken).ConfigureAwait(false);
        LiveHost?.ProposalCreated(proposal);
        return proposal;
    }

    /// <summary>Taslaktan vazgeçer (gönderilmiş ve bekleyen öneri de geri çekilir).</summary>
    public async Task DiscardDraftAsync(string draftId, ToolCallContext context, CancellationToken cancellationToken)
    {
        var item = GetDraftForOwner(draftId, context);
        await item.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (item.Meta.ProposalId is { } pid && Proposals.TryGet(pid, out var proposal) && proposal.Status == ProposalStatus.Pending)
            {
                proposal.Status = ProposalStatus.Rejected;
                proposal.StatusMessage = "Ajan öneriyi geri çekti.";
                proposal.DecidedAt = DateTimeOffset.Now;
                Proposals.Save(proposal);
            }
            item.Meta.State = DraftState.Discarded;
            Working.Persist(item);
        }
        finally
        {
            item.Gate.Release();
        }
    }

    /// <summary>
    /// Öneriyi uygular (yalnızca masaüstünde kullanıcı kabul ettiğinde ya da doğrudan modda). Taslak açıldıktan sonra
    /// hedef değiştiyse öğe düzeyinde birleştirilir; çakışmalar öneriye yazılır.
    /// </summary>
    public async Task<ProposalInfo> AcceptProposalAsync(string proposalId, CancellationToken cancellationToken)
    {
        if (!Proposals.TryGet(proposalId, out var proposal))
            throw new ToolException(ToolErrorCodes.NotFound, $"Öneri bulunamadı: {proposalId}");
        if (proposal.Status != ProposalStatus.Pending)
            throw new ToolException(ToolErrorCodes.InvalidArgument, $"Öneri zaten {proposal.Status} durumunda.");
        if (!DesignId.TryParse(proposal.DraftId, out var draftId) || !Working.TryGet(draftId.Value, out var draft))
            return Fail(proposal, "Önerinin taslağı bulunamadı.");
        var baseDoc = draft.GetBaseDocument();
        if (baseDoc is null) return Fail(proposal, "Taslağın başlangıç hâli bulunamadı.");

        try
        {
            var system = new ToolCallContext { ClientName = HostClientName };
            var target = DesignId.Parse(proposal.TargetDesignId);
            string description = $"Yapay zekâ önerisi ({proposal.ClientName}): {DesignViews.Clip(proposal.Summary, 80)}";
            var resolved = Resolve(proposal.TargetDesignId, system, trusted: true);
            if (resolved.Kind == DesignKind.Live)
            {
                var live = resolved;
                var host = RequireLiveHost(trusted: true);
                var current = await host.SnapshotAsync(LiveNumber(live), cancellationToken).ConfigureAwait(false);
                var merge = DocumentDiff.Merge(baseDoc, current.Document, draft.Project.Document);
                string version = await host.ReplaceContentAsync(LiveNumber(live), merge.Merged, draft.Project.Assets.Assets.ToList(), description, cancellationToken)
                    .ConfigureAwait(false);
                return Complete(proposal, draft, version, merge.Conflicts);
            }
            if (target.Kind == DesignKind.File)
            {
                string path = Guard.ResolveExistingFile(target.Value, GuardedFileKind.Design);
                if (!Guard.IsWritableDesignPath(path))
                    return Fail(proposal, "Hedef dosya salt okunur bir klasörde.");
                var gate = _fileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var loaded = LoadProject(path);
                    var merge = DocumentDiff.Merge(baseDoc, loaded.Project.Document, draft.Project.Document);
                    var assets = CopyAssets(loaded.Project.Assets);
                    foreach (var asset in draft.Project.Assets.Assets)
                    {
                        if (!assets.Contains(asset.Id)) assets.AddExisting(asset);
                    }
                    Backups.Backup(path);
                    SaveProject(new LabelProject(merge.Merged, assets), path);
                    return Complete(proposal, draft, FileVersions.Compute(path), merge.Conflicts);
                }
                finally
                {
                    gate.Release();
                }
            }
            return Fail(proposal, "Önerinin hedefi geçersiz.");
        }
        catch (ToolException ex)
        {
            return Fail(proposal, ex.Message);
        }
    }

    /// <summary>Kullanıcı öneriyi reddetti.</summary>
    public ProposalInfo RejectProposal(string proposalId, string? reason)
    {
        if (!Proposals.TryGet(proposalId, out var proposal))
            throw new ToolException(ToolErrorCodes.NotFound, $"Öneri bulunamadı: {proposalId}");
        if (proposal.Status != ProposalStatus.Pending) return proposal;
        proposal.Status = ProposalStatus.Rejected;
        proposal.StatusMessage = string.IsNullOrWhiteSpace(reason) ? "Kullanıcı reddetti." : reason.Trim();
        proposal.DecidedAt = DateTimeOffset.Now;
        Proposals.Save(proposal);
        if (DesignId.TryParse(proposal.DraftId, out var draftId) && Working.TryGet(draftId.Value, out var draft))
        {
            draft.Meta.State = DraftState.Rejected;
            Working.Persist(draft);
        }
        return proposal;
    }

    /// <summary>Önerinin önce/sonra görüntüsü için taslak ve hedefin güncel hâli.</summary>
    public async Task<(LabelDocument Before, LabelDocument After, AssetStore Assets, IReadOnlyList<string> Conflicts)> GetProposalPreviewAsync(
        string proposalId, CancellationToken cancellationToken)
    {
        if (!Proposals.TryGet(proposalId, out var proposal))
            throw new ToolException(ToolErrorCodes.NotFound, $"Öneri bulunamadı: {proposalId}");
        if (!DesignId.TryParse(proposal.DraftId, out var draftId) || !Working.TryGet(draftId.Value, out var draft))
            throw new ToolException(ToolErrorCodes.NotFound, "Önerinin taslağı bulunamadı.");
        var baseDoc = draft.GetBaseDocument() ?? ModelJson.Clone(draft.Project.Document);
        var assets = CopyAssets(draft.Project.Assets);
        LabelDocument before = baseDoc;
        LabelDocument after = ModelJson.Clone(draft.Project.Document);
        IReadOnlyList<string> conflicts = [];
        try
        {
            var current = await ReadTrustedAsync(proposal.TargetDesignId, cancellationToken).ConfigureAwait(false);
            foreach (var asset in current.Assets.Assets)
            {
                if (!assets.Contains(asset.Id)) assets.AddExisting(asset);
            }
            var merge = DocumentDiff.Merge(baseDoc, current.Document, draft.Project.Document);
            before = current.Document;
            after = merge.Merged;
            conflicts = merge.Conflicts;
        }
        catch (ToolException)
        {
            // Hedef okunamazsa taslağın kendi başlangıç hâli gösterilir.
        }
        return (before, after, assets, conflicts);
    }

    private WorkingDesign GetDraftForOwner(string draftId, ToolCallContext context)
    {
        var id = DesignId.Parse(draftId);
        if (id.Kind != DesignKind.Draft) throw new ToolException(ToolErrorCodes.InvalidArgument, "draft_id bir taslak kimliği olmalı (taslak:…).");
        var item = GetWorking(id);
        if (!IsOwner(item, context))
            throw new ToolException(ToolErrorCodes.PermissionDenied, $"Bu taslak \"{item.Meta.Owner}\" istemcisine ait.");
        return item;
    }

    private ProposalInfo Complete(ProposalInfo proposal, WorkingDesign draft, string version, IReadOnlyList<string> conflicts)
    {
        proposal.Status = ProposalStatus.Accepted;
        proposal.StatusMessage = conflicts.Count == 0 ? "Uygulandı." : $"Uygulandı ({conflicts.Count} çakışmada yapay zekânın sürümü kullanıldı).";
        proposal.DecidedAt = DateTimeOffset.Now;
        proposal.ResultVersion = version;
        proposal.Conflicts = conflicts.ToList();
        Proposals.Save(proposal);
        draft.Meta.State = DraftState.Applied;
        Working.Persist(draft);
        return proposal;
    }

    private ProposalInfo Fail(ProposalInfo proposal, string message)
    {
        proposal.Status = ProposalStatus.Failed;
        proposal.StatusMessage = message;
        proposal.DecidedAt = DateTimeOffset.Now;
        Proposals.Save(proposal);
        return proposal;
    }

    /// <summary>Bir ürünü/veri satırını önizleme verisi olarak seçer (tasarımı değiştirmez).</summary>
    public void SetPreviewData(string designId, string? productKey, int? row, ToolCallContext context)
    {
        var id = Resolve(designId, context);
        if (id.Kind is DesignKind.Draft or DesignKind.New)
        {
            var item = GetWorking(id);
            item.Meta.PreviewProductKey = productKey;
            item.Meta.PreviewRow = row;
            Working.Persist(item);
        }
        else
        {
            _livePreview[id.ToString()] = (productKey, row);
        }
    }

    /// <summary>Var olan bir görseli tasarıma eklemek için çözer (yerleşik, tasarımdaki varlık ya da izinli dosya).</summary>
    internal ImageAsset ResolveImageSource(AddImageArgs args, AssetStore assets)
    {
        if (args.Builtin is { Length: > 0 } builtin)
            return ElementFactory.GetBuiltin(builtin, _options.LogoProvider)
                   ?? throw new ToolException(ToolErrorCodes.NotFound, $"Yerleşik görsel yok: {builtin}. Kullanılabilir: firma-logo ve sembol:<anahtar> (list_symbols).");
        if (args.AssetId is { Length: > 0 } assetId)
            return assets.TryGet(assetId, out var existing) ? existing
                : throw new ToolException(ToolErrorCodes.NotFound, $"assetId \"{assetId}\" bu tasarımda yok.");
        if (args.Path is { Length: > 0 } path)
        {
            string full = Guard.ResolveExistingFile(path, GuardedFileKind.Image);
            byte[] data = File.ReadAllBytes(full);
            return new ImageAsset(AssetStore.ComputeId(data, Path.GetFileName(full)), Path.GetFileName(full), data);
        }
        throw new ToolException(ToolErrorCodes.InvalidArgument, "path, builtin ya da asset_id verin.");
    }
}
