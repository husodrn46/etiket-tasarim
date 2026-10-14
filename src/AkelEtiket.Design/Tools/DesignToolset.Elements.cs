using AkelEtiket.Design.Operations;

namespace AkelEtiket.Design.Tools;

public sealed partial class DesignToolset
{
    private const string MutationNote = " Onaylı modda var olan tasarımlar için önce begin_draft ile taslak açın ve taslağın kimliğini kullanın.";

    private void RegisterElementTools()
    {
        Registry.Add<AddTextArgs>("add_text", "Metin ekle",
            "Metin kutusu ekler. Konum x/y (mm) veya placement ile; genişlik/yükseklik verilmezse metne göre hesaplanır. " +
            "Veri alanı için {SütunAdı} kullanın (ör. \"{UrunAdi}\"); bu durumda autoShrink varsayılan olarak açılır." + MutationNote,
            async (args, ctx, ct) =>
            {
                var result = await _service.MutateAsync(args.DesignId, args.ExpectedVersion, ctx, $"metin eklendi \"{DesignViews.Clip(args.Text, 30)}\"",
                    edit =>
                    {
                        var element = ElementFactory.CreateText(args, edit.Document, edit.PreviewFields);
                        edit.Document.Elements.Add(element);
                        edit.MarkChanged(element);
                        WarnBounds(edit, element);
                        return DesignViews.Brief(element, edit.Document.Elements.Count - 1);
                    }, ct).ConfigureAwait(false);
                return ToolRegistry.Ok($"Metin eklendi ({result.Value["id"]}).", WithMutation(result, new JsonObject { ["element"] = result.Value }));
            });

        Registry.Add<AddBarcodeArgs>("add_barcode", "Barkod ekle",
            "Geçerli bir barkod ekler (EAN-13, EAN-8, UPC-A, Code 128, GS1-128, Code 39, ITF, ITF-14, QR, Data Matrix, PDF417, " +
            "GS1 DataMatrix, GS1 QR). " +
            "İçerik eklenmeden önce doğrulanır (kontrol basamağı, uzunluk, GS1 kuralları); geçersizse INVALID_BARCODE döner. " +
            "Ölçü verilmezse okunabilir bir modül genişliğiyle hesaplanır (EAN/UPC 0,375 mm, diğer 1D 0,25 mm, 2D 0,5 mm)." + MutationNote,
            async (args, ctx, ct) =>
            {
                var result = await _service.MutateAsync(args.DesignId, args.ExpectedVersion, ctx, $"barkod eklendi ({args.Symbology})",
                    edit =>
                    {
                        var element = ElementFactory.CreateBarcode(args, edit.Document, edit.PreviewFields);
                        edit.Document.Elements.Add(element);
                        edit.MarkChanged(element);
                        WarnBounds(edit, element);
                        var brief = DesignViews.Brief(element, edit.Document.Elements.Count - 1);
                        brief["content"] = element.Content;
                        brief["symbology"] = element.Symbology.ToString();
                        return brief;
                    }, ct).ConfigureAwait(false);
                return ToolRegistry.Ok($"Barkod eklendi ({result.Value["id"]}).", WithMutation(result, new JsonObject { ["element"] = result.Value }));
            });

        Registry.Add<AddImageArgs>("add_image", "Görsel ekle",
            "Görsel ekler: izinli klasörden dosya (path; list_files images ile bulun), yerleşik \"firma-logo\" ya da hazır simge " +
            "\"sembol:<anahtar>\" (builtin; list_symbols) ya da tasarımdaki bir görsel (asset_id). Görsel baskıda 1-bit siyah-beyaza " +
            "çevrilir; orijinal dosya saklanır. Genişlik/yükseklikten biri verilirse oran korunur." + MutationNote,
            async (args, ctx, ct) =>
            {
                var state = await _service.ReadAsync(args.DesignId, ctx, ct).ConfigureAwait(false);
                var asset = _service.ResolveImageSource(args, state.Assets);
                var result = await _service.MutateAsync(args.DesignId, args.ExpectedVersion, ctx, $"görsel eklendi ({asset.FileName})",
                    edit =>
                    {
                        if (!edit.Assets.Contains(asset.Id)) edit.Assets.AddExisting(asset);
                        var element = ElementFactory.CreateImage(args, edit.Document, asset);
                        edit.Document.Elements.Add(element);
                        edit.MarkChanged(element);
                        WarnBounds(edit, element);
                        var brief = DesignViews.Brief(element, edit.Document.Elements.Count - 1);
                        brief["asset_id"] = asset.Id;
                        return brief;
                    }, ct).ConfigureAwait(false);
                return ToolRegistry.Ok($"Görsel eklendi ({result.Value["id"]}).", WithMutation(result, new JsonObject { ["element"] = result.Value }));
            });

        Registry.Add<AddShapeArgs>("add_shape", "Şekil/çizgi ekle",
            "Dikdörtgen, elips veya çizgi ekler (çerçeve, ayırıcı çizgi, siyah zemin şeridi gibi). Siyah zemin üzerine beyaz yazı için " +
            "önce fill=black dikdörtgen, sonra white_text=true metin ekleyin." + MutationNote,
            async (args, ctx, ct) =>
            {
                var result = await _service.MutateAsync(args.DesignId, args.ExpectedVersion, ctx, $"şekil eklendi ({args.Kind})",
                    edit =>
                    {
                        var element = ElementFactory.CreateShape(args, edit.Document);
                        edit.Document.Elements.Add(element);
                        edit.MarkChanged(element);
                        WarnBounds(edit, element);
                        return DesignViews.Brief(element, edit.Document.Elements.Count - 1);
                    }, ct).ConfigureAwait(false);
                return ToolRegistry.Ok($"Şekil eklendi ({result.Value["id"]}).", WithMutation(result, new JsonObject { ["element"] = result.Value }));
            });

        Registry.Add<UpdateElementArgs>("update_element", "Öğeyi değiştir",
            "Öğenin özelliklerini değiştirir. changes içindeki adlar get_design çıktısındaki gibidir (camelCase): text, fontSize, bold, " +
            "horizontalAlignment, content, symbology, showText, x, y, width, height, rotation, visible, name, method, threshold, " +
            "brightness, contrast, sharpen, invert, fitMode, fill, strokeWidth vb. Barkod içeriği değişirse doğrulanır. " +
            "Kilitli öğeler değiştirilemez." + MutationNote,
            async (args, ctx, ct) =>
            {
                var result = await _service.MutateAsync(args.DesignId, args.ExpectedVersion, ctx, "öğe değiştirildi",
                    edit =>
                    {
                        var element = edit.GetElement(args.ElementId, forWrite: true);
                        var changed = ElementPatcher.Apply(element, args.Changes, edit);
                        WarnBounds(edit, element);
                        return (Element: DesignViews.Element(element, edit.Document.Elements.IndexOf(element), null), Changed: changed);
                    }, ct).ConfigureAwait(false);
                var data = WithMutation(result, new JsonObject
                {
                    ["changed_properties"] = new JsonArray(result.Value.Changed.Select(c => (JsonNode)c).ToArray()),
                    ["element"] = result.Value.Element,
                });
                return ToolRegistry.Ok(result.Value.Changed.Count == 0 ? "Değişiklik yok (değerler zaten aynıydı)." : $"Değişti: {string.Join(", ", result.Value.Changed)}", data);
            });

        Registry.Add<MoveElementArgs>("move_element", "Taşı",
            "Öğeleri taşır: dx/dy ile göreli (ör. 5 mm sağa: dx=5; yukarı: dy negatif) ya da x/y ile mutlak. Gruplu öğeler bütün grupla " +
            "birlikte taşınır." + MutationNote,
            async (args, ctx, ct) =>
            {
                var result = await _service.MutateAsync(args.DesignId, args.ExpectedVersion, ctx, "öğeler taşındı",
                    edit =>
                    {
                        var moved = LayoutOperations.Move(edit, args.ElementIds, args.Dx, args.Dy, args.X, args.Y);
                        foreach (var m in moved) WarnBounds(edit, m);
                        return new JsonArray(moved.Select(m => (JsonNode)DesignViews.Brief(m, edit.Document.Elements.IndexOf(m))).ToArray());
                    }, ct).ConfigureAwait(false);
                return ToolRegistry.Ok($"{result.Value.Count} öğe taşındı.", WithMutation(result, new JsonObject { ["elements"] = result.Value }));
            });

        Registry.Add<ResizeElementArgs>("resize_element", "Boyutlandır",
            "Öğeyi veya bütün grubu boyutlandırır: width/height (mm) ya da scale (ör. 1.2). anchor sabit kalacak noktadır. scale ile " +
            "metinlerin yazı boyutu da aynı oranda değişir (scale_font=false değilse)." + MutationNote,
            async (args, ctx, ct) =>
            {
                var result = await _service.MutateAsync(args.DesignId, args.ExpectedVersion, ctx, "öğe boyutlandırıldı",
                    edit =>
                    {
                        var resized = LayoutOperations.Resize(edit, args);
                        foreach (var r in resized) WarnBounds(edit, r);
                        return new JsonArray(resized.Select(r => (JsonNode)DesignViews.Brief(r, edit.Document.Elements.IndexOf(r))).ToArray());
                    }, ct).ConfigureAwait(false);
                return ToolRegistry.Ok($"{result.Value.Count} öğe boyutlandırıldı.", WithMutation(result, new JsonObject { ["elements"] = result.Value }));
            });

        Registry.Add<ElementIdsArgs>("delete_element", "Öğe sil",
            "Öğeleri tasarımdan siler (tasarım dosyası silinmez; kilitli öğeler silinmez). Uygulamada geri alınabilir." + MutationNote,
            async (args, ctx, ct) =>
            {
                var result = await _service.MutateAsync(args.DesignId, args.ExpectedVersion, ctx, "öğe silindi",
                    edit => LayoutOperations.Delete(edit, args.ElementIds), ct).ConfigureAwait(false);
                return ToolRegistry.Ok($"{result.Value.Count} öğe silindi.",
                    WithMutation(result, new JsonObject { ["deleted"] = new JsonArray(result.Value.Select(i => (JsonNode)i).ToArray()) }));
            }, destructive: true);

        Registry.Add<GroupElementsArgs>("group_elements", "Grupla",
            "Öğeleri gruplar: birlikte seçilir, taşınır, hizalanır ve boyutlandırılır. group_id döner." + MutationNote,
            async (args, ctx, ct) =>
            {
                var result = await _service.MutateAsync(args.DesignId, args.ExpectedVersion, ctx, "öğeler gruplandı",
                    edit => LayoutOperations.Group(edit, args.ElementIds), ct).ConfigureAwait(false);
                return ToolRegistry.Ok($"Gruplandı: {result.Value}", WithMutation(result, new JsonObject
                {
                    ["group_id"] = result.Value,
                    ["element_ids"] = new JsonArray(args.ElementIds.Select(i => (JsonNode)i).ToArray()),
                }));
            });

        Registry.Add<UngroupElementsArgs>("ungroup_elements", "Grubu çöz",
            "Grubu çözer (group_id ya da grup üyesi element_ids)." + MutationNote,
            async (args, ctx, ct) =>
            {
                var result = await _service.MutateAsync(args.DesignId, args.ExpectedVersion, ctx, "grup çözüldü",
                    edit => LayoutOperations.Ungroup(edit, args.ElementIds, args.GroupId), ct).ConfigureAwait(false);
                return ToolRegistry.Ok($"{result.Value.Count} öğenin grubu çözüldü.",
                    WithMutation(result, new JsonObject { ["element_ids"] = new JsonArray(result.Value.Select(i => (JsonNode)i).ToArray()) }));
            });

        Registry.Add<AlignElementsArgs>("align_elements", "Hizala",
            "Öğeleri hizalar: edge = left, center_horizontal, right, top, center_vertical, bottom. relative_to = label ise etikete göre " +
            "(ör. yatayda ortalamak için edge=center_horizontal, relative_to=label). Gruplar birim olarak hizalanır." + MutationNote,
            async (args, ctx, ct) =>
            {
                var result = await _service.MutateAsync(args.DesignId, args.ExpectedVersion, ctx, "öğeler hizalandı",
                    edit =>
                    {
                        LayoutOperations.Align(edit, args.ElementIds, args.Edge, args.RelativeTo ?? AlignTo.Auto);
                        return new JsonArray(edit.ChangedElementIds.Select(id => (JsonNode)DesignViews.Brief(edit.Document.FindElement(id)!,
                            edit.Document.Elements.IndexOf(edit.Document.FindElement(id)!))).ToArray());
                    }, ct).ConfigureAwait(false);
                return ToolRegistry.Ok("Hizalandı.", WithMutation(result, new JsonObject { ["elements"] = result.Value }));
            });

        Registry.Add<DistributeElementsArgs>("distribute_elements", "Aralıkları düzenle",
            "Öğeler arasındaki mesafeyi düzenler: spacing_mm verilirse aralarında sabit boşluk bırakılır; verilmezse ilk ve son öğe sabit " +
            "kalır, aradakiler eşit aralıklı olur." + MutationNote,
            async (args, ctx, ct) =>
            {
                var result = await _service.MutateAsync(args.DesignId, args.ExpectedVersion, ctx, "aralıklar düzenlendi",
                    edit =>
                    {
                        LayoutOperations.Distribute(edit, args.ElementIds, args.Direction, args.SpacingMm);
                        return new JsonArray(edit.ChangedElementIds.Select(id => (JsonNode)DesignViews.Brief(edit.Document.FindElement(id)!,
                            edit.Document.Elements.IndexOf(edit.Document.FindElement(id)!))).ToArray());
                    }, ct).ConfigureAwait(false);
                return ToolRegistry.Ok("Aralıklar düzenlendi.", WithMutation(result, new JsonObject { ["elements"] = result.Value }));
            });

        Registry.Add<SetLayerOrderArgs>("set_layer_order", "Katman sırası",
            "Öğenin katman sırasını değiştirir (z_index 0 = en alt, üstteki öğeler alttakini örter)." + MutationNote,
            async (args, ctx, ct) =>
            {
                var result = await _service.MutateAsync(args.DesignId, args.ExpectedVersion, ctx, "katman sırası değişti",
                    edit => LayoutOperations.SetLayerOrder(edit, args.ElementId, args.Action, args.Index), ct).ConfigureAwait(false);
                return ToolRegistry.Ok($"Yeni katman sırası: {result.Value}", WithMutation(result, new JsonObject
                {
                    ["element_id"] = args.ElementId,
                    ["z_index"] = result.Value,
                }));
            });
    }
}
