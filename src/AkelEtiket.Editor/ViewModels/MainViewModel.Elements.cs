using AkelEtiket.Core.Geometry;
using AkelEtiket.BarcodeEngine;
using AkelEtiket.Core;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Documents;
using AkelEtiket.Editor.Services;
using AkelEtiket.ImageProcessor;
using CommunityToolkit.Mvvm.Input;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

public sealed partial class MainViewModel
{
    // ───────────────────────── Öğe ekleme ─────────────────────────

    [RelayCommand]
    private void AddText() => AddTextWithContent(L10n.T("Metin"));

    /// <summary>Hazır metin stili ekler: "Baslik", "AltBaslik", "Govde", "Kucuk".</summary>
    [RelayCommand]
    private void AddTextStyle(string? style)
    {
        var text = style switch
        {
            "Baslik" => new TextElement { Text = L10n.T("Başlık"), FontSize = 24, Bold = true, Width = 70, Height = 11 },
            "AltBaslik" => new TextElement { Text = L10n.T("Alt başlık"), FontSize = 14, Bold = true, Width = 60, Height = 7 },
            "Kucuk" => new TextElement { Text = L10n.T("Küçük metin"), FontSize = 8, Width = 40, Height = 5 },
            _ => new TextElement { Text = L10n.T("Metin"), FontSize = 12, Width = 50, Height = 7 },
        };
        var added = Session.AddCentered(text);
        SetStatus(L10n.T("Metin eklendi: yazmaya başlayın (Enter: bitir, Shift+Enter: yeni satır, Esc: vazgeç)."));
        RequestInlineTextEdit(added);
    }

    private void AddTextWithContent(string content)
    {
        var text = new TextElement { Text = content, FontSize = 12, Width = 50, Height = 7 };
        RequestInlineTextEdit(Session.AddCentered(text));
    }

    /// <summary>Veri sütununa bağlı metin ekler ({Sütun}).</summary>
    [RelayCommand]
    private void AddFieldText(string? column)
    {
        if (string.IsNullOrWhiteSpace(column)) return;
        var text = new TextElement { Text = FieldTemplate.MakePlaceholder(column), FontSize = 12, Width = 60, Height = 7, AutoShrink = true, WordWrap = false };
        Session.AddCentered(text);
        SetStatus(L10n.T($"{{{column}}} alanına bağlı metin eklendi."));
    }

    /// <summary>Seçili metin/barkoda alan ekler; seçim yoksa yeni metin oluşturur.</summary>
    [RelayCommand]
    private void InsertField(string? column)
    {
        if (string.IsNullOrWhiteSpace(column)) return;
        string placeholder = FieldTemplate.MakePlaceholder(column);
        switch (SelectedElement?.Model)
        {
            case TextElement text:
                text.Text = string.IsNullOrEmpty(text.Text) || (text.Text is "Metin" or "Text") ? placeholder : text.Text + " " + placeholder;
                SetStatus(L10n.T($"{placeholder} metne eklendi."));
                break;
            case BarcodeElement barcode:
                barcode.Content = placeholder;
                SetStatus(L10n.T($"Barkod içeriği {placeholder} alanına bağlandı."));
                break;
            default:
                AddFieldText(column);
                break;
        }
    }

    [RelayCommand]
    private void AddBarcode(BarcodeType type)
    {
        bool is2D = BarcodeElement.IsTwoDimensional(type);
        var barcode = new BarcodeElement
        {
            Symbology = type,
            Content = BarcodeValueGenerator.GetSample(type),
            ShowText = !is2D,
            Width = type switch
            {
                BarcodeType.QrCode or BarcodeType.DataMatrix or BarcodeType.Gs1QrCode or BarcodeType.Gs1DataMatrix => 25,
                BarcodeType.Pdf417 => 60,
                BarcodeType.Gs1_128 => 90,
                BarcodeType.Itf14 => 80,
                BarcodeType.Ean8 => 35,
                _ => 50,
            },
            Height = type switch
            {
                BarcodeType.QrCode or BarcodeType.DataMatrix or BarcodeType.Gs1QrCode or BarcodeType.Gs1DataMatrix => 25,
                BarcodeType.Pdf417 => 20,
                BarcodeType.Itf14 => 30,
                _ => 22,
            },
        };
        if (Data.HasData && !is2D)
        {
            // Veri yüklüyse barkod sütununu otomatik bağla.
            var column = Data.Columns.FirstOrDefault(c => FieldNames.Normalize(c) is "barkod" or "barcode" or "ean" or "ean13" or "gtin");
            if (column is not null) barcode.Content = FieldTemplate.MakePlaceholder(column);
        }
        Session.AddCentered(barcode);
        SetStatus(L10n.T($"{BarcodeCatalog.GetDisplayName(type)} eklendi."));
    }

    [RelayCommand]
    private void AddShape(string? kind)
    {
        var shape = kind switch
        {
            "Ellipse" => new ShapeElement { Kind = ShapeKind.Ellipse, Width = 20, Height = 20 },
            "Circle" => new ShapeElement { Kind = ShapeKind.Ellipse, Width = 20, Height = 20, Name = "Daire" },
            "HLine" => new ShapeElement { Kind = ShapeKind.Line, LineOrientation = LineOrientation.Horizontal, Width = 50, Height = 1, StrokeWidth = 0.5 },
            "VLine" => new ShapeElement { Kind = ShapeKind.Line, LineOrientation = LineOrientation.Vertical, Width = 1, Height = 40, StrokeWidth = 0.5 },
            "DLine" => new ShapeElement { Kind = ShapeKind.Line, LineOrientation = LineOrientation.DiagonalDown, Width = 30, Height = 20, StrokeWidth = 0.5 },
            "RoundRect" => new ShapeElement { Kind = ShapeKind.Rectangle, Width = 40, Height = 25, CornerRadius = 3 },
            "FilledRect" => new ShapeElement { Kind = ShapeKind.Rectangle, Width = 40, Height = 12, Fill = FillStyle.Black, StrokeWidth = 0 },
            "Frame" => new ShapeElement
            {
                Kind = ShapeKind.Rectangle, Name = L10n.T("Çerçeve"), X = 1.5, Y = 1.5, StrokeWidth = 0.5, CornerRadius = 2,
                Width = Session.Document.Label.WidthMm - 3, Height = Session.Document.Label.HeightMm - 3,
            },
            _ => new ShapeElement { Kind = ShapeKind.Rectangle, Width = 40, Height = 25 },
        };
        if (kind == "Frame") Session.AddElement(shape);
        else Session.AddCentered(shape);
    }

    [RelayCommand]
    private void AddImage()
    {
        string? path = Dialogs.ShowOpenFileDialog(L10n.T("Görsel ekle"), ImageLoader.OpenFileFilter, Settings.LastImageFolder);
        if (path is null) return;
        Settings.LastImageFolder = Path.GetDirectoryName(path);
        AddImageFromFile(path, null, null);
    }

    /// <summary>Dosyadan görsel ekler; orijinal proje içine gömülür. Konum verilmezse ortalanır.</summary>
    public void AddImageFromFile(string path, double? xMm, double? yMm)
    {
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.ShowMessage(L10n.T("Görsel okunamadı"), ex.Message, MessageKind.Error);
            return;
        }
        AddImageFromBytes(data, Path.GetFileName(path), xMm, yMm);
    }

    public void AddImageFromBytes(byte[] data, string fileName, double? xMm, double? yMm)
    {
        int width, height;
        string? warning;
        try
        {
            using var loaded = ImageLoader.Load(data, fileName);
            width = loaded.Width;
            height = loaded.Height;
            warning = loaded.Warning;
        }
        catch (ImageLoadException ex)
        {
            Dialogs.ShowMessage(L10n.T("Görsel eklenemedi"), ex.Message, MessageKind.Error);
            return;
        }
        var asset = Session.Assets.Add(data, fileName);
        var label = Session.Document.Label;
        double maxW = label.WidthMm * 0.6, maxH = label.HeightMm * 0.6;
        double scale = Math.Min(maxW / width, maxH / height);
        double w = Math.Round(Math.Max(5, width * scale), 2), h = Math.Round(Math.Max(5, height * scale), 2);
        var image = new ImageElement { AssetId = asset.Id, Width = w, Height = h, Name = Path.GetFileNameWithoutExtension(fileName) };
        if (xMm is { } x && yMm is { } y)
        {
            image.X = Math.Round(x - w / 2, 2);
            image.Y = Math.Round(y - h / 2, 2);
            Session.AddElement(image);
        }
        else
        {
            Session.AddCentered(image);
        }
        SetStatus(L10n.T($"Görsel eklendi ve 1-bit siyah-beyaza dönüştürüldü ({width} × {height} px). Ayarlar sağ panelde.") +
                  (warning is null ? "" : L10n.T(" Uyarı: ") + warning));
    }

    // ───────────────────────── Düzen ─────────────────────────

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        Session.Undo();
        SetStatus(L10n.T("Geri alındı."));
    }

    private bool CanUndo() => Session.CanUndo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        Session.Redo();
        SetStatus(L10n.T("İleri alındı."));
    }

    private bool CanRedo() => Session.CanRedo;

    private bool CanEditSelection() => Session.SelectedElements.Count > 0;

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void Copy()
    {
        string? payload = Session.BuildClipboardPayload();
        if (payload is null) return;
        string plain = string.Join(Environment.NewLine, Session.SelectedModels.Select(m => m switch
        {
            TextElement t => t.Text,
            BarcodeElement b => b.Content,
            _ => m.DisplayName,
        }));
        _services.Clipboard.SetElements(payload, plain);
        _pasteCount = 0;
        SetStatus(L10n.T($"{Session.SelectedElements.Count} öğe kopyalandı."));
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void Cut()
    {
        Copy();
        Session.DeleteSelection();
    }

    private int _pasteCount;

    [RelayCommand]
    private void Paste()
    {
        var clipboard = _services.Clipboard;
        string? payload = clipboard.GetElements();
        if (payload is not null && Session.PastePayload(payload, 3 * ++_pasteCount))
        {
            SetStatus(L10n.T("Yapıştırıldı."));
            return;
        }
        if (clipboard.GetImagePng() is { Length: > 0 } png)
        {
            AddImageFromBytes(png, "panodan-gorsel.png", null, null);
            return;
        }
        if (clipboard.GetText() is { Length: > 0 } text)
        {
            var element = new TextElement { Text = text.Length > 500 ? text[..500] : text, FontSize = 12, Width = 60, Height = 10, AutoShrink = true };
            Session.AddCentered(element);
            SetStatus(L10n.T("Panodaki metin eklendi."));
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void Duplicate() => Session.DuplicateSelection();

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void Delete()
    {
        int lockedCount = Session.SelectedElements.Count(e => e.Model.Locked);
        Session.DeleteSelection();
        if (lockedCount > 0) SetStatus(L10n.T($"{lockedCount} kilitli öğe silinmedi (önce kilidi açın)."));
    }

    [RelayCommand]
    private void SelectAll() => Session.SelectAll();

    /// <summary>Ok tuşlarıyla kaydırma (mm).</summary>
    public void Nudge(double dxMm, double dyMm) => Session.MoveSelection(dxMm, dyMm);

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void ToggleLock() => Session.ToggleLockSelection();

    /// <summary>Seçili öğeleri gruplar (Ctrl+G).</summary>
    [RelayCommand]
    private void GroupSelection()
    {
        if (Session.GroupSelection()) SetStatus(L10n.T($"{Session.SelectedElements.Count} öğe gruplandı (birlikte seçilir ve taşınır). Çözmek için Ctrl+Shift+G."));
        else if (Session.SelectedElements.Count < 2) SetStatus(L10n.T("Gruplamak için en az iki öğe seçin."));
        else SetStatus(L10n.T("Kilitli öğeler gruplanamaz."));
    }

    /// <summary>Seçili öğelerin grubunu çözer (Ctrl+Shift+G).</summary>
    [RelayCommand]
    private void UngroupSelection()
    {
        if (Session.UngroupSelection()) SetStatus(L10n.T("Grup çözüldü."));
        else SetStatus(L10n.T("Seçimde grup yok."));
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void BringToFront() => Session.ChangeZOrder(ZOrderChange.BringToFront);

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void BringForward() => Session.ChangeZOrder(ZOrderChange.BringForward);

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void SendBackward() => Session.ChangeZOrder(ZOrderChange.SendBackward);

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void SendToBack() => Session.ChangeZOrder(ZOrderChange.SendToBack);

    [RelayCommand(CanExecute = nameof(CanAlign))]
    private void Align(AlignMode mode) => Session.AlignSelection(mode);

    private bool CanAlign(AlignMode mode) => mode is AlignMode.DistributeHorizontal or AlignMode.DistributeVertical
        ? Session.SelectedElements.Count >= 3
        : Session.SelectedElements.Count >= 1;

    [RelayCommand]
    private void ToggleVisibility(ElementViewModel? vm)
    {
        if (vm is null) return;
        vm.Model.Visible = !vm.Model.Visible;
    }

    [RelayCommand]
    private void ToggleElementLock(ElementViewModel? vm)
    {
        if (vm is null) return;
        vm.Model.Locked = !vm.Model.Locked;
    }

    [RelayCommand]
    private void RenameElement(ElementViewModel? vm)
    {
        if (vm is null) return;
        string? name = Dialogs.Prompt(L10n.T("Yeniden adlandır"), L10n.T("Öğe adı:"), vm.Model.DisplayName);
        if (name is not null) vm.Model.Name = name.Trim();
    }

    // ───────────────────────── Barkod yardımcıları ─────────────────────────

    /// <summary>Seçili barkodun okunabilirlik kontrolü (render edilmiş 1-bit görüntü çözülür).</summary>
    [RelayCommand]
    private void VerifyBarcode()
    {
        if (SelectedElement is not { Model: BarcodeElement } vm) return;
        Session.VerifyBarcode(vm);
        SetStatus(vm.VerificationMessage ?? "");
    }

    /// <summary>Barkod kutusunu verilen modül genişliğine (nokta) tam oturtur. 0 = mevcut modül.</summary>
    [RelayCommand]
    private void SetBarcodeModule(int moduleDots)
    {
        if (SelectedElement is not { Model: BarcodeElement barcode } vm) return;
        var info = vm.RenderResult?.Barcode;
        if (info?.Symbol is null)
        {
            Dialogs.ShowMessage(L10n.T("Barkod"), L10n.T("Önce geçerli bir barkod içeriği girin (değişken alanlı barkodlarda veri kaynağı yükleyin)."), MessageKind.Info);
            return;
        }
        int module = moduleDots > 0 ? moduleDots : Math.Max(1, info.ModuleDots);
        var options = new BarcodeLayoutOptions
        {
            ShowText = barcode.ShowText,
            IncludeQuietZone = barcode.QuietZone,
            DotsPerMm = DotsPerMm,
            TextHeightDots = info.Layout?.TextHeightDots is > 0 and var th ? th : null,
        };
        int widthDots = BarcodeLayoutEngine.GetRequiredWidthDots(info.Symbol, module, options);
        using (Session.BeginTransaction())
        {
            double oldCenterX = barcode.X + barcode.Width / 2;
            barcode.Width = widthDots / DotsPerMm;
            if (info.Symbol is MatrixSymbol matrix)
                barcode.Height = BarcodeLayoutEngine.GetRequiredHeightDots(matrix, module, options) / DotsPerMm;
            barcode.X = Math.Round(oldCenterX - barcode.Width / 2, 3);
        }
        SetStatus(L10n.T($"Modül genişliği {module} nokta ({module / DotsPerMm:0.###} mm); kutu barkoda oturtuldu."));
    }

    [RelayCommand]
    private void BarcodeModuleUp() => SetBarcodeModule(Math.Max(1, SelectedElement?.RenderResult?.Barcode?.ModuleDots ?? 1) + 1);

    [RelayCommand]
    private void BarcodeModuleDown() => SetBarcodeModule(Math.Max(1, (SelectedElement?.RenderResult?.Barcode?.ModuleDots ?? 2) - 1));

    [RelayCommand]
    private void BarcodeFit() => SetBarcodeModule(0);

    /// <summary>Görsel kutusunun yüksekliğini, kırpılmış görselin özgün oranına göre ayarlar.</summary>
    [RelayCommand]
    private void RestoreImageAspect()
    {
        if (SelectedElement is not { Model: ImageElement image } vm || vm.RenderResult?.Image is not { SourceWidth: > 0, SourceHeight: > 0 } info) return;
        var (l, t, r, b) = image.GetEffectiveCrop();
        double ratio = info.SourceWidth * (1 - l - r) / (info.SourceHeight * (1 - t - b));
        using (Session.BeginTransaction())
        {
            double centerY = image.Y + image.Height / 2;
            image.Height = Math.Round(image.Width / ratio, 3);
            image.Y = Math.Round(centerY - image.Height / 2, 3);
        }
    }

    /// <summary>Seçili görselin dosyasını değiştirir (ayarlar korunur).</summary>
    [RelayCommand]
    private void ReplaceImage()
    {
        if (SelectedElement?.Model is not ImageElement image) return;
        string? path = Dialogs.ShowOpenFileDialog(L10n.T("Görseli değiştir"), ImageLoader.OpenFileFilter, Settings.LastImageFolder);
        if (path is null) return;
        try
        {
            byte[] data = File.ReadAllBytes(path);
            using (ImageLoader.Load(data, path)) { }
            image.AssetId = Session.Assets.Add(data, Path.GetFileName(path)).Id;
            Settings.LastImageFolder = Path.GetDirectoryName(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ImageLoadException)
        {
            Dialogs.ShowMessage(L10n.T("Görsel değiştirilemedi"), ex.Message, MessageKind.Error);
        }
    }

    /// <summary>
    /// Otomatik değer: EAN/UPC/ITF-14 için firma öneki + sıradaki ürün numarası + kontrol basamağı;
    /// diğer türlerde geçerli örnek değer.
    /// </summary>
    [RelayCommand]
    private void GenerateBarcodeValue()
    {
        if (SelectedElement?.Model is not BarcodeElement barcode) return;
        if (barcode.Symbology is BarcodeType.Ean13 or BarcodeType.Ean8 or BarcodeType.UpcA or BarcodeType.Itf14)
        {
            string? prefix = Dialogs.Prompt(L10n.T("Barkod numarası üret"),
                L10n.T("GS1 üyelik belgenizdeki firma öneki (GCP). GLN veya ülke kodu girmeyin. Ürün numarası sıradaki değerden devam eder:"),
                Settings.Gs1CompanyPrefix);
            if (prefix is null) return;
            if (!Gs1.Gs1Identifiers.IsValidGcpFormat(prefix.Trim(), out var prefixProblem))
            {
                Dialogs.ShowMessage(L10n.T("Firma öneki gerekli"), prefixProblem ?? L10n.T("GS1 belgenizdeki firma önekini girin."), MessageKind.Warning);
                return;
            }
            try
            {
                barcode.Content = BarcodeValueGenerator.GenerateGtin(barcode.Symbology, prefix.Trim(), Settings.NextItemNumber);
                Settings.Gs1CompanyPrefix = prefix.Trim();
                Settings.NextItemNumber++;
                SaveSettings();
                SetStatus(L10n.T($"Üretilen barkod: {barcode.Content}"));
            }
            catch (ArgumentException ex)
            {
                Dialogs.ShowMessage(L10n.T("Barkod üretilemedi"), ex.Message, MessageKind.Warning);
            }
            return;
        }
        barcode.Content = BarcodeValueGenerator.GetSample(barcode.Symbology);
    }
}
