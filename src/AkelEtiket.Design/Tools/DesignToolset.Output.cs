using AkelEtiket.Core.Data;
using AkelEtiket.Design.Inspection;
using AkelEtiket.Design.Operations;
using AkelEtiket.Design.Preview;
using AkelEtiket.Rendering;

namespace AkelEtiket.Design.Tools;

public sealed partial class DesignToolset
{
    private void RegisterOutputTools()
    {
        Registry.Add<ProcessImageArgs>("process_image", "Görseli işle",
            "Görsel öğesinin 1-bit siyah-beyaz dönüşümünü ayarlar (yöntem, eşik, parlaklık, kontrast, keskinlik, ters çevirme, kırpma, " +
            "yerleşim). center_on_label ve fit_to_label ile görseli etikete ortalar/sığdırır. Orijinal görsel hiç değişmez; sonuç " +
            "baskı çözünürlüğünde önizleme olarak döner." + MutationNote,
            async (args, ctx, ct) =>
            {
                var result = await _service.MutateAsync(args.DesignId, args.ExpectedVersion, ctx, "görsel ayarlandı",
                    edit =>
                    {
                        if (edit.GetElement(args.ElementId, forWrite: true) is not ImageElement image)
                            throw new ToolException(ToolErrorCodes.InvalidArgument, "Bu öğe bir görsel değil.");
                        var changes = new JsonObject();
                        if (args.Method is { } m) changes["method"] = m.ToString();
                        if (args.Threshold is { } t) changes["threshold"] = Math.Clamp(t, 1, 254);
                        if (args.Brightness is { } b) changes["brightness"] = Math.Clamp(b, -100, 100);
                        if (args.Contrast is { } c) changes["contrast"] = Math.Clamp(c, -100, 100);
                        if (args.Sharpen is { } s) changes["sharpen"] = Math.Clamp(s, 0, 100);
                        if (args.Invert is { } inv) changes["invert"] = inv;
                        if (args.FitMode is { } fit) changes["fitMode"] = fit.ToString();
                        if (args.Crop is { } crop)
                        {
                            if (crop.Left is { } l) changes["cropLeft"] = Math.Clamp(l, 0, 0.9);
                            if (crop.Top is { } tp) changes["cropTop"] = Math.Clamp(tp, 0, 0.9);
                            if (crop.Right is { } r) changes["cropRight"] = Math.Clamp(r, 0, 0.9);
                            if (crop.Bottom is { } bt) changes["cropBottom"] = Math.Clamp(bt, 0, 0.9);
                        }
                        var label = edit.Document.Label;
                        if (args.FitToLabel == true)
                        {
                            double margin = args.MarginMm ?? 3;
                            double maxW = label.WidthMm - 2 * margin, maxH = label.HeightMm - 2 * margin;
                            double aspect = image.Height > 0 ? image.Width / image.Height : 1;
                            double w = maxW, h = maxW / aspect;
                            if (h > maxH)
                            {
                                h = maxH;
                                w = maxH * aspect;
                            }
                            changes["width"] = Math.Round(w, 2);
                            changes["height"] = Math.Round(h, 2);
                        }
                        if (changes.Count > 0) ElementPatcher.Apply(image, changes, edit);
                        if (args.CenterOnLabel == true || args.FitToLabel == true)
                        {
                            image.X = Math.Round((label.WidthMm - image.Width) / 2, 3);
                            image.Y = Math.Round((label.HeightMm - image.Height) / 2, 3);
                            edit.MarkChanged(image);
                        }
                        return image.Id;
                    }, ct).ConfigureAwait(false);

                var state = await _service.ReadAsync(result.DesignId, ctx, ct).ConfigureAwait(false);
                var render = RenderState(state);
                var element = state.Document.FindElement(result.Value)!;
                var info = render.Elements.FirstOrDefault(e => e.ElementId == element.Id);
                var data = WithMutation(result, new JsonObject { ["element"] = DesignViews.Element(element, state.Document.Elements.IndexOf(element), info) });
                return ToolRegistry.Ok("Görsel ayarlandı. Önizleme ekte.", data,
                    [new ToolImage("image/png", PreviewRenderer.EncodeMono(render.Bitmap, render.DotsPerMm), "Görsel ayarlarıyla etiket önizlemesi (1-bit)")]);
            });

        Registry.Add<RenderPreviewArgs>("render_preview", "Önizleme",
            "Tasarımı gerçek baskı çözünürlüğünde (8 nokta/mm, 1-bit) çizer ve PNG olarak döndürür — yazıcıdan çıkacak görüntüyle aynıdır. " +
            "annotate=true öğe sınırlarını ve kimliklerini renkli çerçeveyle gösterir (yerleşimi değerlendirmek için; baskıda yoktur). " +
            "row veya product_key ile veri alanları doldurulur.",
            async (args, ctx, ct) =>
            {
                var state = await _service.ReadAsync(args.DesignId, ctx, ct).ConfigureAwait(false);
                IFieldValueSource? fields = state.PreviewFields;
                string source = state.PreviewSource ?? "yerleşik değerler";
                if (args.Row is not null || args.ProductKey is not null)
                {
                    fields = _service.PreviewFieldsFor(state.Document, args.ProductKey, args.Row, null, out var src);
                    source = src ?? source;
                }
                var render = PreviewRenderer.Render(state.Document, state.Assets, fields, _service.ImageResolverFor(state.Document));
                int scale = Math.Clamp(args.Scale ?? 1, 1, 3);
                byte[] png = args.Annotate == true
                    ? PreviewRenderer.EncodeAnnotated(render, state.Document, Math.Max(2, scale))
                    : PreviewRenderer.EncodeMono(render.Bitmap, render.DotsPerMm, scale);
                var data = new JsonObject
                {
                    ["design_id"] = state.DesignId,
                    ["version"] = state.Version,
                    ["size_px"] = $"{render.Bitmap.Width}×{render.Bitmap.Height} (×{(args.Annotate == true ? Math.Max(2, scale) : scale)})",
                    ["dots_per_mm"] = render.DotsPerMm,
                    ["preview_data"] = source,
                    ["annotated"] = args.Annotate == true,
                    ["issues"] = new JsonArray(render.Issues.Select(i => (JsonNode)$"{i.ElementName}: {i.Message}").ToArray()),
                };
                return ToolRegistry.Ok($"Önizleme ({source}).", data, [new ToolImage("image/png", png, "Etiket önizlemesi")]);
            }, readOnly: true);

        Registry.Add<RenderBatchPreviewArgs>("render_batch_preview", "Toplu önizleme",
            "Bağlı veri kaynağının satırlarıyla etiketleri üretir ve ızgara hâlinde gösterir (en fazla 24). Hatalı satırlar kırmızı " +
            "çerçeveli ve satır numarasıyla listelenir; hatalı etiket baskıda atlanır.",
            async (args, ctx, ct) =>
            {
                var state = await _service.ReadAsync(args.DesignId, ctx, ct).ConfigureAwait(false);
                if (!_service.TryGetDataTable(state.Document, out var table) || table is null)
                    throw new ToolException(ToolErrorCodes.InvalidArgument, "Tasarıma bağlı veri kaynağı yok ya da izinli klasörde değil. Önce set_data_source.");
                int from = Math.Max(1, args.FromRow ?? 1);
                int count = Math.Clamp(args.Count ?? 12, 1, 24);
                var rows = table.Rows.Skip(from - 1).Take(count).ToList();
                if (rows.Count == 0) throw new ToolException(ToolErrorCodes.InvalidArgument, $"Bu aralıkta satır yok (toplam {table.Rows.Count}).");
                var images = _service.ImageResolverFor(state.Document);
                var items = new List<(string, Core.Imaging.MonoBitmap?, bool)>();
                var statuses = new JsonArray();
                using (var producer = new LabelProducer(state.Document, state.Assets, verifyBarcodes: false, images))
                {
                    int seq = from;
                    foreach (var row in rows)
                    {
                        ct.ThrowIfCancellationRequested();
                        var fields = new CompositeFieldSource(row.ToFieldSource(), new BuiltInFieldSource(seq++, DateTime.Now));
                        var (bitmap, problems, warnings) = producer.Produce(new PrintItem(seq, row.RowNumber, 1, fields), PreviewRenderer.PrintDotsPerMm);
                        bool error = problems.Count > 0;
                        MonoBitmapOrPreview(state, fields, images, bitmap, out var shown);
                        items.Add(($"Satır {row.RowNumber}{(error ? " — HATA" : "")}", shown, error));
                        var status = new JsonObject { ["row"] = row.RowNumber, ["ok"] = !error };
                        if (error) status["problems"] = new JsonArray(problems.Select(p => (JsonNode)DesignViews.Clip(p, 300)).ToArray());
                        if (warnings.Count > 0) status["warnings"] = new JsonArray(warnings.Take(3).Select(w => (JsonNode)DesignViews.Clip(w, 200)).ToArray());
                        statuses.Add(status);
                    }
                }
                var (w, h) = LabelRenderer.GetLabelSizeDots(state.Document.Label, PreviewRenderer.PrintDotsPerMm);
                byte[] sheet = PreviewRenderer.EncodeContactSheet(items, w, h);
                int errors = items.Count(i => i.Item3);
                var data = new JsonObject
                {
                    ["design_id"] = state.DesignId,
                    ["data_file"] = table.FileName,
                    ["total_rows"] = table.Rows.Count,
                    ["shown_rows"] = rows.Count,
                    ["error_rows"] = errors,
                    ["rows"] = statuses,
                    ["untrusted_content_note"] = DesignViews.UntrustedNote,
                };
                return ToolRegistry.Ok($"{rows.Count} etiket üretildi, {errors} satırda hata.", data,
                    [new ToolImage("image/png", sheet, "Toplu önizleme")]);
            }, readOnly: true);

        Registry.Add<InspectLayoutArgs>("inspect_layout", "Yerleşimi incele",
            "Taşma, kenara yakınlık, çakışma (barkod üstüne binme hata), metin taşması, küçük yazı, ince barkod modülü, neredeyse hizalı " +
            "kenarlar ve düzensiz aralıkları bulur. Her sorun için mümkünse doğrudan çağrılabilecek bir araç önerisi (suggestion) döner.",
            async (args, ctx, ct) =>
            {
                var state = await _service.ReadAsync(args.DesignId, ctx, ct).ConfigureAwait(false);
                var render = RenderState(state);
                var issues = LayoutInspector.Inspect(state.Document, render, args.MarginMm ?? 1.5, state.DesignId);
                int errors = issues.Count(i => i.Severity == "error"), warnings = issues.Count(i => i.Severity == "warning");
                var data = new JsonObject
                {
                    ["design_id"] = state.DesignId,
                    ["version"] = state.Version,
                    ["errors"] = errors,
                    ["warnings"] = warnings,
                    ["infos"] = issues.Count - errors - warnings,
                    ["issues"] = new JsonArray(issues.Select(i => (JsonNode)i.ToJson()).ToArray()),
                };
                string summary = issues.Count == 0 ? "Yerleşimde sorun bulunmadı." : $"{errors} hata, {warnings} uyarı, {issues.Count - errors - warnings} öneri.";
                return ToolRegistry.Ok(summary, data);
            }, readOnly: true);

        Registry.Add<ValidateDesignArgs>("validate_design", "Doğrula",
            "Barkod ve baskı kurallarını doğrular: etiket baskıdaki gibi üretilir, her barkod birleşik görüntüden kesilip bağımsız " +
            "çözücüyle okunur; veri kaynağı bağlıysa satırlar tek tek denenir ve hatalı satırlar numarasıyla bildirilir.",
            async (args, ctx, ct) =>
            {
                var state = await _service.ReadAsync(args.DesignId, ctx, ct).ConfigureAwait(false);
                _service.TryGetDataTable(state.Document, out var table);
                var report = await Task.Run(() => DesignValidator.Validate(state.Document, state.Assets, state.PreviewFields, table,
                    _service.ImageResolverFor(state.Document), args.VerifyBarcodes ?? true, args.MaxRows ?? 50), ct).ConfigureAwait(false);
                var data = report.ToJson();
                data["design_id"] = state.DesignId;
                data["version"] = state.Version;
                return ToolRegistry.Ok(report.Ok ? "Doğrulama başarılı: barkodlar ve baskı kuralları uygun." : $"{report.Errors.Count} sorun bulundu.", data);
            }, readOnly: true);
    }

    /// <summary>Hatalı etiket için yine de (hatalarıyla) önizleme göster.</summary>
    private void MonoBitmapOrPreview(DesignState state, IFieldValueSource fields, IImageSourceResolver images, Core.Imaging.MonoBitmap? produced,
        out Core.Imaging.MonoBitmap? shown)
    {
        if (produced is not null)
        {
            shown = produced;
            return;
        }
        shown = PreviewRenderer.Render(state.Document, state.Assets, fields, images).Bitmap;
    }
}
