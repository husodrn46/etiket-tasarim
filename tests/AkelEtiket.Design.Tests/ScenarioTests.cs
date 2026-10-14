using System.Text.Json.Nodes;
using AkelEtiket.Core.Automation;
using AkelEtiket.Core.Model;
using AkelEtiket.Design.Workspace;
using AkelEtiket.ProjectManager;
using ClosedXML.Excel;

namespace AkelEtiket.Design.Tests;

/// <summary>
/// Kullanıcının verdiği 5 doğal dil örneğinin, bir yapay zekâ ajanının yapacağı araç çağrılarıyla uçtan uca karşılanabildiğini
/// doğrular. (Doğal dili anlamak ajanın işidir; burada araçların bu istekleri gerçek, düzenlenebilir öğelerle karşıladığı denetlenir.)
/// </summary>
public sealed class ScenarioTests : IDisposable
{
    private readonly DesignTestHost _host = new();

    public void Dispose() => _host.Dispose();

    /// <summary>Örnek 1: "100×100 mm ürün etiketi hazırla. Üste AKEL logosunu, ortaya ürün adını, alta EAN-13 barkodunu ekle.
    /// Sağ alt köşeye Made in Türkiye yaz."</summary>
    [Fact]
    public async Task Example1_CreateProductLabelFromScratch()
    {
        var created = await _host.Call("create_design", new { name = "Melamin Tabak Etiketi", width_mm = 100, height_mm = 100 });
        string id = created["design_id"]!.GetValue<string>();
        Assert.StartsWith("yeni:", id);

        var logo = await _host.Call("add_image", new { design_id = id, builtin = "firma-logo", placement = "top_center", width = 40, name = "Logo" });
        var title = await _host.Call("add_text", new { design_id = id, text = "Melamin Servis Tabağı", font_size_pt = 20, bold = true, align = "center", placement = "center", name = "Ürün adı" });
        var barcode = await _host.Call("add_barcode", new { design_id = id, symbology = "ean13", content = "869123456789", placement = "bottom_center", name = "EAN-13" });
        var made = await _host.Call("add_text", new { design_id = id, text = "Made in Türkiye", font_size_pt = 8, placement = "bottom_right", name = "Menşe" });

        // 12 hane verildi: kontrol basamağı hesaplanıp öğeye yazıldı.
        Assert.Equal(Ean("869123456789"), barcode["element"]!["content"]!.GetValue<string>());

        var preview = await _host.CallRaw("render_preview", new { design_id = id, annotate = true });
        Assert.False(preview.IsError);
        var image = Assert.Single(preview.Images);
        Assert.Equal("image/png", image.MimeType);
        Assert.True(image.Data.Length > 1000);

        var inspect = await _host.Call("inspect_layout", new { design_id = id });
        Assert.Equal(0, inspect["errors"]!.GetValue<int>());

        var validation = await _host.Call("validate_design", new { design_id = id });
        Assert.True(validation["ok"]!.GetValue<bool>(), validation.ToJsonString());

        var saved = await _host.Call("save_design", new { design_id = id });
        Assert.True(saved["created_new_file"]!.GetValue<bool>());
        string file = Path.Combine(_host.Templates, saved["file"]!.GetValue<string>());
        Assert.True(File.Exists(file));

        // Sonuç düzleştirilmiş bir resim değil: editörün açabileceği gerçek öğeler.
        var project = ProjectFile.Load(file).Project;
        var elements = project.Document.Elements;
        Assert.Equal(4, elements.Count);
        Assert.IsType<ImageElement>(elements[0]);
        Assert.IsType<TextElement>(elements[1]);
        Assert.IsType<BarcodeElement>(elements[2]);
        Assert.IsType<TextElement>(elements[3]);
        Assert.Equal("Logo", elements[0].Name);
        Assert.Contains(project.Assets.Assets, a => a.FileName == "firma-logo.png");
        Assert.All(elements, e => Assert.True(e.X >= 0 && e.Y >= 0 && e.X + e.Width <= 100.01 && e.Y + e.Height <= 100.01));
        var madeIn = (TextElement)elements[3];
        Assert.True(madeIn.X > 50 && madeIn.Y > 80, "Made in Türkiye sağ alt köşede olmalı");
        Assert.True(elements[0].Y < 20, "Logo üstte olmalı");
        _ = logo;
        _ = title;
        _ = made;
    }

    /// <summary>Örnek 2: "Logoyu 5 mm sağa taşı, ürün adını büyüt ve barkodu biraz aşağı al." (onaylı mod: taslak + öneri)</summary>
    [Fact]
    public async Task Example2_EditExistingDesignThroughDraftAndApproval()
    {
        string fileId = await CreateSavedLabel("Mevcut Etiket");
        var design = await _host.Call("get_design", new { design_id = fileId });
        var elements = design["elements"]!.AsArray();
        string logoId = Find(elements, "Logo"), titleId = Find(elements, "Ürün adı"), barcodeId = Find(elements, "EAN-13");
        double logoX = Value(elements, logoId, "x"), titleSize = Value(elements, titleId, "fontSize"), barcodeY = Value(elements, barcodeId, "y");

        // Onaylı modda dosya doğrudan değiştirilemez.
        await _host.ExpectError("move_element", new { design_id = fileId, element_ids = new[] { logoId }, dx = 5 }, "APPROVAL_REQUIRED");

        var draft = await _host.Call("begin_draft", new { design_id = fileId });
        string draftId = draft["draft_id"]!.GetValue<string>();
        await _host.Call("move_element", new { design_id = draftId, element_ids = new[] { logoId }, dx = 5 });
        await _host.Call("update_element", new { design_id = draftId, element_id = titleId, changes = new JsonObject { ["fontSize"] = titleSize + 4 } });
        await _host.Call("move_element", new { design_id = draftId, element_ids = new[] { barcodeId }, dy = 2 });
        var proposal = await _host.Call("submit_draft", new { draft_id = draftId, summary = "Logo 5 mm sağa, ürün adı büyütüldü, barkod 2 mm aşağı." });
        Assert.Equal("Pending", proposal["status"]!.GetValue<string>());
        Assert.Equal(3, proposal["changes"]!.AsArray().Count);

        // Kullanıcı onaylamadan dosya değişmedi.
        var unchanged = await _host.Call("get_design", new { design_id = fileId });
        Assert.Equal(logoX, Value(unchanged["elements"]!.AsArray(), logoId, "x"), 3);

        // Kullanıcı uygulamada kabul eder (masaüstü bu yöntemi çağırır).
        var accepted = await _host.Service.AcceptProposalAsync(proposal["proposal_id"]!.GetValue<string>(), CancellationToken.None);
        Assert.Equal(ProposalStatus.Accepted, accepted.Status);
        var after = (await _host.Call("get_design", new { design_id = fileId }))["elements"]!.AsArray();
        Assert.Equal(logoX + 5, Value(after, logoId, "x"), 3);
        Assert.Equal(titleSize + 4, Value(after, titleId, "fontSize"), 3);
        Assert.Equal(barcodeY + 2, Value(after, barcodeId, "y"), 3);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_host.Root, "Yedekler"), "*.akelabel", SearchOption.AllDirectories));

        var status = await _host.Call("get_draft_status", new { draft_id = draftId });
        Assert.Equal("Applied", status["state"]!.GetValue<string>());
    }

    /// <summary>Örnek 3: "Bu ürün görselini siyah-beyaza dönüştür, kenarları keskinleştir ve etiketin ortasına sığdır."</summary>
    [Fact]
    public async Task Example3_ProcessProductImage()
    {
        File.WriteAllBytes(Path.Combine(_host.Extra, "urun.png"), DesignTestHost.MakePng(400, 300, photo: true));
        string id = (await _host.Call("create_design", new { name = "Görsel deneme" }))["design_id"]!.GetValue<string>();
        var files = await _host.Call("list_files", new { kind = "images" });
        string path = files["files"]!.AsArray().Select(f => f!["path"]!.GetValue<string>()).Single(p => p.EndsWith("urun.png"));
        var added = await _host.Call("add_image", new { design_id = id, path, x = 2, y = 2 });
        string imageId = added["element"]!["id"]!.GetValue<string>();

        var result = await _host.CallRaw("process_image", new { design_id = id, element_id = imageId, method = "photo", sharpen = 60, fit_to_label = true });
        Assert.False(result.IsError, result.Text);
        Assert.Single(result.Images);
        var element = ((JsonObject)result.Structured!)["element"]!;
        Assert.Equal("Photo", element["method"]!.GetValue<string>());
        Assert.Equal(60, element["sharpen"]!.GetValue<double>());
        double x = element["x"]!.GetValue<double>(), w = element["width"]!.GetValue<double>();
        Assert.Equal(50, x + w / 2, 1);
        Assert.True(w > 90, "Etikete sığdırılmalı (3 mm kenar)");
    }

    /// <summary>Örnek 4: "Bu etiketi daha düzenli ve profesyonel hale getir. Barkodun okunabilirliği bozulmasın."</summary>
    [Fact]
    public async Task Example4_InspectAndImproveLayoutKeepingBarcodeReadable()
    {
        string id = (await _host.Call("create_design", new { name = "Dağınık" }))["design_id"]!.GetValue<string>();
        string a = Id(await _host.Call("add_text", new { design_id = id, text = "Ürün", x = 10, y = 10, font_size_pt = 14 }));
        string b = Id(await _host.Call("add_text", new { design_id = id, text = "Kod: TST001", x = 10.6, y = 22, font_size_pt = 10 }));
        string c = Id(await _host.Call("add_text", new { design_id = id, text = "Adet: 6", x = 10.3, y = 40, font_size_pt = 10 }));
        string bc = Id(await _host.Call("add_barcode", new { design_id = id, symbology = "code128", content = "TST001-6", x = 0.5, y = 70 }));

        var inspect = await _host.Call("inspect_layout", new { design_id = id });
        var issues = inspect["issues"]!.AsArray();
        Assert.Contains(issues, i => i!["code"]!.GetValue<string>() == "MISALIGNED");
        Assert.Contains(issues, i => i!["code"]!.GetValue<string>() == "NEAR_EDGE");
        Assert.Contains(issues, i => i!["code"]!.GetValue<string>() == "UNEVEN_SPACING");

        // Ajan önerileri uygular.
        foreach (var issue in issues.Where(i => i!["suggestion"] is not null && i["code"]!.GetValue<string>() is "MISALIGNED" or "NEAR_EDGE").ToList())
        {
            var suggestion = issue!["suggestion"]!;
            await _host.Call(suggestion["tool"]!.GetValue<string>(), suggestion["arguments"]!.DeepClone());
        }
        await _host.Call("align_elements", new { design_id = id, element_ids = new[] { a, b, c }, edge = "left", relative_to = "selection" });
        await _host.Call("distribute_elements", new { design_id = id, element_ids = new[] { a, b, c }, direction = "vertical", spacing_mm = 4 });

        var again = await _host.Call("inspect_layout", new { design_id = id });
        var remaining = again["issues"]!.AsArray().Where(i => i!["code"]!.GetValue<string>() is "MISALIGNED" or "NEAR_EDGE" or "BARCODE_OVERLAP").ToList();
        Assert.True(remaining.Count == 0, string.Join(Environment.NewLine, remaining.Select(r => r!.ToJsonString())));
        var validation = await _host.Call("validate_design", new { design_id = id });
        Assert.True(validation["ok"]!.GetValue<bool>(), validation.ToJsonString());
        _ = bc;
    }

    /// <summary>Örnek 5: "Bu tasarımı Excel'deki ürünlerin tamamına uygula. Her ürünün adı, barkodu ve görseli farklı olsun."</summary>
    [Fact]
    public async Task Example5_BatchTemplateFromExcelWithPerRowImages()
    {
        string folder = Path.Combine(_host.Extra, "Katalog");
        Directory.CreateDirectory(Path.Combine(folder, "Görseller"));
        File.WriteAllBytes(Path.Combine(folder, "Görseller", "TST001.png"), DesignTestHost.MakePng(200, 200));
        File.WriteAllBytes(Path.Combine(folder, "Görseller", "TST002.png"), DesignTestHost.MakePng(200, 200, photo: true));
        string excel = Path.Combine(folder, "urunler.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Ürünler");
            ws.Cell(1, 1).Value = "UrunAdi";
            ws.Cell(1, 2).Value = "Barkod";
            ws.Cell(1, 3).Value = "Gorsel";
            object[][] rows =
            [
                ["Melamin Tabak", Ean("869123456789"), "TST001.png"],
                ["Melamin Kase", Ean("869123456780"), "TST002"],
                ["Hatalı Barkod", WrongCheckDigit(Ean("869123456788")), "TST001.png"],
                ["Görseli Yok", Ean("869123456781"), "YOK999.png"],
            ];
            for (int r = 0; r < rows.Length; r++)
            {
                for (int col = 0; col < 3; col++) ws.Cell(r + 2, col + 1).SetValue(rows[r][col].ToString());
            }
            wb.SaveAs(excel);
        }

        string id = (await _host.Call("create_design", new { name = "Toplu ürün etiketi" }))["design_id"]!.GetValue<string>();
        var source = await _host.Call("set_data_source", new { design_id = id, path = "Gorseller/Katalog/urunler.xlsx" });
        Assert.Equal(4, source["row_count"]!.GetValue<int>());
        Assert.NotNull(source["untrusted_data"]);

        string name = Id(await _host.Call("add_text", new { design_id = id, text = "Ürün adı", placement = "top_center", width = 90, font_size_pt = 16, align = "center" }));
        string code = Id(await _host.Call("add_barcode", new { design_id = id, symbology = "ean13", content = Ean("869123456789"), placement = "bottom_center" }));
        string image = Id(await _host.Call("add_image", new { design_id = id, builtin = "firma-logo", placement = "center", width = 40 }));
        await _host.Call("bind_field", new { design_id = id, element_id = name, column = "UrunAdi" });
        await _host.Call("bind_field", new { design_id = id, element_id = code, column = "Barkod" });
        await _host.Call("bind_field", new { design_id = id, element_id = image, column = "Gorsel" });

        var batch = await _host.CallRaw("render_batch_preview", new { design_id = id });
        Assert.False(batch.IsError, batch.Text);
        Assert.Single(batch.Images);
        var data = (JsonObject)batch.Structured!;
        var statuses = data["rows"]!.AsArray();
        Assert.Equal(4, statuses.Count);
        Assert.True(statuses[0]!["ok"]!.GetValue<bool>(), statuses[0]!.ToJsonString());
        Assert.True(statuses[1]!["ok"]!.GetValue<bool>(), statuses[1]!.ToJsonString());
        Assert.False(statuses[2]!["ok"]!.GetValue<bool>());   // kontrol basamağı hatalı
        Assert.False(statuses[3]!["ok"]!.GetValue<bool>());   // görsel yok
        Assert.Equal(2, data["error_rows"]!.GetValue<int>());
        Assert.Contains("YOK999", statuses[3]!["problems"]!.ToJsonString());

        var validation = await _host.Call("validate_design", new { design_id = id });
        Assert.False(validation["ok"]!.GetValue<bool>());
        Assert.Contains(validation["errors"]!.AsArray(), e => e!.GetValue<string>().StartsWith("Satır 4", StringComparison.Ordinal));
    }

    // ───────────────────────── yardımcılar ─────────────────────────

    private async Task<string> CreateSavedLabel(string name)
    {
        string id = (await _host.Call("create_design", new { name }))["design_id"]!.GetValue<string>();
        await _host.Call("add_image", new { design_id = id, builtin = "firma-logo", x = 30, y = 4, width = 40, name = "Logo" });
        await _host.Call("add_text", new { design_id = id, text = "Melamin Kase", font_size_pt = 18, x = 10, y = 40, width = 80, align = "center", name = "Ürün adı" });
        await _host.Call("add_barcode", new { design_id = id, symbology = "ean13", content = Ean("869123456789"), x = 28, y = 70, name = "EAN-13" });
        var saved = await _host.Call("save_design", new { design_id = id });
        return saved["file_design_id"]!.GetValue<string>();
    }

    private static string Id(JsonObject result) => result["element"]!["id"]!.GetValue<string>();

    /// <summary>12 haneye GS1 kontrol basamağını ekler.</summary>
    private static string Ean(string twelve) => twelve + AkelEtiket.BarcodeEngine.CheckDigits.Gs1Mod10(twelve);

    private static string WrongCheckDigit(string ean) => ean[..^1] + (char)('0' + (ean[^1] - '0' + 1) % 10);

    private static string Find(JsonArray elements, string name) =>
        elements.First(e => e!["name"]!.GetValue<string>() == name)!["id"]!.GetValue<string>();

    private static double Value(JsonArray elements, string id, string property) =>
        elements.First(e => e!["id"]!.GetValue<string>() == id)![property]!.GetValue<double>();
}
