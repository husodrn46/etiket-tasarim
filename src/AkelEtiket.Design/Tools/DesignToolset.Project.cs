using AkelEtiket.Design.Preview;

namespace AkelEtiket.Design.Tools;

public sealed partial class DesignToolset
{
    private void RegisterProjectTools()
    {
        Registry.Add<ListDesignsArgs>("list_designs", "Tasarımları listele",
            "Erişilebilir tasarımları listeler: uygulamada açık belge (paylaşım açıksa), izinli klasörlerdeki .akelabel dosyaları, " +
            "bu istemcinin taslakları ve yeni tasarımları. Her kayıt design_id içerir.",
            (args, ctx, _) =>
            {
                var designs = _service.ListDesigns(args.Query, ctx);
                var data = new JsonObject
                {
                    ["designs"] = new JsonArray(designs.Select(d => (JsonNode)d).ToArray()),
                    ["change_mode"] = _service.Settings.ChangeMode.ToString(),
                    ["mode_note"] = ModeText(_service.Settings),
                    ["allowed_folders"] = new JsonArray(_service.Guard.Roots.Select(r => (JsonNode)$"{r.Alias}{(r.Writable ? " (yazılabilir)" : " (salt okunur)")}").ToArray()),
                };
                return Task.FromResult(ToolRegistry.Ok($"{designs.Count} tasarım bulundu.", data));
            }, readOnly: true);

        Registry.Add<GetDesignArgs>("get_design", "Tasarımı oku",
            "Tasarımın bütün öğelerini (id, type, x, y, width, height, rotation, z_index, visible, locked, groupId ve türe özgü " +
            "özellikler) ve hesaplanan bilgileri (gerçek sınırlar, kullanılan yazı boyutu, barkod modülü, taşma) döndürür. " +
            "Değiştirmeden önce okuyun; version değerini expected_version olarak kullanın.",
            async (args, ctx, ct) =>
            {
                var state = await _service.ReadAsync(args.DesignId, ctx, ct).ConfigureAwait(false);
                var render = RenderState(state);
                var data = DesignViews.Design(state, render);
                var images = args.IncludePreview == true
                    ? new[] { new ToolImage("image/png", PreviewRenderer.EncodeMono(render.Bitmap, render.DotsPerMm), "Baskı çözünürlüğünde önizleme") }
                    : [];
                return ToolRegistry.Ok($"\"{state.Name}\" ({state.Document.Elements.Count} öğe, {state.Document.Label.WidthMm:0.#} × {state.Document.Label.HeightMm:0.#} mm).",
                    data, images);
            }, readOnly: true);

        Registry.Add<CreateDesignArgs>("create_design", "Yeni etiket oluştur",
            "Yeni, boş bir etiket tasarımı oluşturur (varsayılan 100 × 100 mm). Sonra öğe ekleyin; bitince save_design ile " +
            "Şablonlar klasörüne yeni dosya olarak kaydedin. Kaydedilmemiş tasarımlar uygulamada kullanıcıya görünmez.",
            (args, ctx, _) =>
            {
                var item = _service.CreateDesign(args, ctx);
                var doc = item.Project.Document;
                var data = new JsonObject
                {
                    ["design_id"] = item.DesignId,
                    ["version"] = item.Version,
                    ["name"] = doc.Name,
                    ["label"] = new JsonObject { ["width_mm"] = doc.Label.WidthMm, ["height_mm"] = doc.Label.HeightMm },
                    ["next"] = "add_text / add_barcode / add_image / add_shape ile öğe ekleyin; render_preview ile bakın; save_design ile kaydedin.",
                };
                return Task.FromResult(ToolRegistry.Ok($"Yeni tasarım oluşturuldu: {item.DesignId}", data));
            });

        Registry.Add<DuplicateDesignArgs>("duplicate_design", "Tasarımı çoğalt",
            "Var olan bir tasarımın kopyasını yeni tasarım olarak oluşturur (asıl dosya değişmez). Kopya doğrudan düzenlenebilir; " +
            "save_design ile yeni dosya olarak kaydedilir.",
            async (args, ctx, ct) =>
            {
                var item = await _service.DuplicateAsync(args.DesignId, args.Name, ctx, ct).ConfigureAwait(false);
                var data = new JsonObject
                {
                    ["design_id"] = item.DesignId,
                    ["version"] = item.Version,
                    ["name"] = item.Project.Document.Name,
                    ["element_count"] = item.Project.Document.Elements.Count,
                    ["note"] = "Öğe kimlikleri yenilendi; get_design ile okuyun.",
                };
                return ToolRegistry.Ok($"Kopya oluşturuldu: {item.DesignId}", data);
            });

        Registry.Add<SaveDesignArgs>("save_design", "Kaydet",
            "Yapay zekânın oluşturduğu yeni tasarımı Şablonlar klasörüne kaydeder. İlk kayıtta yeni dosya oluşturulur; var olan bir " +
            "dosyanın üzerine asla yazılmaz (aynı ad varsa başka file_name isteyin). Var olan tasarımlar onaylı modda submit_draft ile değişir.",
            async (args, ctx, ct) =>
            {
                var (id, path, version, created) = await _service.SaveAsync(args.DesignId, args.FileName, ctx, ct).ConfigureAwait(false);
                var data = new JsonObject
                {
                    ["file_design_id"] = id,
                    ["file"] = Path.GetFileName(path),
                    ["version"] = version,
                    ["created_new_file"] = created,
                    ["note"] = "Dosya uygulamanın Şablonlar panelinde görünür; kullanıcı açıp düzenleyebilir ve basabilir.",
                };
                return ToolRegistry.Ok(created ? $"Kaydedildi: {Path.GetFileName(path)}" : $"Güncellendi: {Path.GetFileName(path)}", data);
            });

        Registry.Add<DesignRefArgs>("show_in_app", "Uygulamada göster",
            "Tasarımın (kaydedilmiş dosya ya da yeni tasarım) LabelHarbor'de açılmasını kullanıcıya önerir: Yapay Zekâ panelinde " +
            "\"Aç\" düğmesi çıkar, kullanıcı açınca tasarım tuvalde normal şekilde düzenlenebilir. Kendiliğinden açılmaz; uygulama " +
            "kapalıysa UNAVAILABLE döner.",
            (args, ctx, _) =>
            {
                string message = _service.RequestOpenInApp(args.DesignId, ctx);
                return Task.FromResult(ToolRegistry.Ok(message, new JsonObject { ["design_id"] = args.DesignId, ["requested"] = true }));
            });

        Registry.Add<BeginDraftArgs>("begin_draft", "Taslak aç",
            "Var olan bir tasarım (dosya veya uygulamada açık belge) için taslak açar. Değişiklikleri draft_id üzerinde yapın; " +
            "asıl tasarım değişmez. Bitince submit_draft ile kullanıcı onayına sunun.",
            async (args, ctx, ct) =>
            {
                var draft = await _service.BeginDraftAsync(args.DesignId, ctx, ct).ConfigureAwait(false);
                var data = new JsonObject
                {
                    ["draft_id"] = draft.DesignId,
                    ["design_id"] = draft.DesignId,
                    ["version"] = draft.Version,
                    ["target_design_id"] = draft.Meta.TargetDesignId,
                    ["base_version"] = draft.Meta.BaseVersion,
                    ["next"] = "Araçlara design_id olarak bu draft_id'yi verin. Bitince submit_draft(draft_id, summary).",
                };
                return ToolRegistry.Ok($"Taslak açıldı: {draft.DesignId}", data);
            });

        Registry.Add<SubmitDraftArgs>("submit_draft", "Onaya sun",
            "Taslaktaki değişiklikleri öneri olarak kullanıcıya sunar. Kullanıcı uygulamada önce/sonra önizlemesini ve değişiklik " +
            "listesini görür; kabul ederse tek bir geri alınabilir adımda uygulanır. Doğrudan modda hemen uygulanır. " +
            "summary kısa ve Türkçe olsun (ne değişti, neden).",
            async (args, ctx, ct) =>
            {
                var proposal = await _service.SubmitDraftAsync(args.DraftId, args.Summary, ctx, ct).ConfigureAwait(false);
                var data = ProposalJson(proposal);
                string message = proposal.Status switch
                {
                    ProposalStatus.Pending => "Öneri kullanıcının onayını bekliyor. Durumu get_draft_status ile izleyebilirsiniz.",
                    ProposalStatus.Accepted => "Öneri uygulandı.",
                    _ => "Öneri uygulanamadı: " + proposal.StatusMessage,
                };
                return ToolRegistry.Ok(message, data);
            });

        Registry.Add<DraftRefArgs>("discard_draft", "Taslaktan vazgeç",
            "Taslağı kapatır; gönderilmiş ve bekleyen öneri varsa geri çekilir. Asıl tasarım değişmez.",
            async (args, ctx, ct) =>
            {
                await _service.DiscardDraftAsync(args.DraftId, ctx, ct).ConfigureAwait(false);
                return ToolRegistry.Ok("Taslak kapatıldı.", new JsonObject { ["draft_id"] = args.DraftId, ["state"] = "Discarded" });
            });

        Registry.Add<DraftRefArgs>("get_draft_status", "Öneri durumu",
            "Taslağın ve önerinin durumunu döndürür: Pending (onay bekliyor), Accepted (uygulandı), Rejected (reddedildi), Failed.",
            (args, ctx, _) =>
            {
                var id = DesignId.Parse(args.DraftId);
                if (!_service.Working.TryGet(id.Value, out var draft) || !draft.IsDraft)
                    throw new ToolException(ToolErrorCodes.NotFound, $"Taslak bulunamadı: {args.DraftId}");
                _service.Proposals.Reload();
                var data = new JsonObject
                {
                    ["draft_id"] = draft.DesignId,
                    ["state"] = draft.Meta.State.ToString(),
                    ["target_design_id"] = draft.Meta.TargetDesignId,
                };
                if (draft.Meta.ProposalId is { } pid && _service.Proposals.TryGet(pid, out var proposal))
                    data["proposal"] = ProposalJson(proposal);
                return Task.FromResult(ToolRegistry.Ok($"Taslak durumu: {draft.Meta.State}", data));
            }, readOnly: true);
    }

    private static JsonObject ProposalJson(ProposalInfo p) => new()
    {
        ["proposal_id"] = p.Id,
        ["draft_id"] = p.DraftId,
        ["target_design_id"] = p.TargetDesignId,
        ["status"] = p.Status.ToString(),
        ["status_message"] = p.StatusMessage,
        ["changes"] = new JsonArray(p.Changes.Select(c => (JsonNode)c).ToArray()),
        ["conflicts"] = new JsonArray(p.Conflicts.Select(c => (JsonNode)c).ToArray()),
        ["result_version"] = p.ResultVersion,
    };
}
