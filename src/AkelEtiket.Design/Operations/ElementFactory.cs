using AkelEtiket.BarcodeEngine;
using AkelEtiket.Core;
using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Data;
using AkelEtiket.ImageProcessor;
using AkelEtiket.Rendering;
using AkelEtiket.Rendering.Symbols;

namespace AkelEtiket.Design.Operations;

/// <summary>
/// Araç girdilerinden öğe oluşturur: varsayılanlar, içeriğe göre otomatik boyut, hazır konumlar ve barkod
/// doğrulaması. Oluşan öğeler editördeki öğelerle birebir aynıdır (kullanıcı normal şekilde düzenleyebilir).
/// </summary>
public static class ElementFactory
{
    public const double DefaultMarginMm = 3;

    public static TextElement CreateText(AddTextArgs args, LabelDocument doc, IFieldValueSource? previewFields)
    {
        if (string.IsNullOrEmpty(args.Text))
            throw new ToolException(ToolErrorCodes.InvalidArgument, "text boş olamaz.");
        string text = args.Text.Replace("\r\n", "\n");
        bool hasFields = FieldTemplate.ContainsFields(text);
        var element = new TextElement
        {
            Text = text,
            FontSize = args.FontSizePt ?? 12,
            FontFamily = args.FontFamily ?? TextElement.DefaultFontFamily,
            Bold = args.Bold ?? false,
            Italic = args.Italic ?? false,
            Underline = args.Underline ?? false,
            HorizontalAlignment = args.Align switch
            {
                TextAlign.Center => TextHorizontalAlignment.Center,
                TextAlign.Right => TextHorizontalAlignment.Right,
                _ => TextHorizontalAlignment.Left,
            },
            VerticalAlignment = args.VerticalAlign switch
            {
                TextVAlign.Middle => TextVerticalAlignment.Middle,
                TextVAlign.Bottom => TextVerticalAlignment.Bottom,
                _ => TextVerticalAlignment.Top,
            },
            WordWrap = args.WordWrap ?? true,
            AutoShrink = args.AutoShrink ?? hasFields,
            TextColor = args.WhiteText == true ? InkColor.White : InkColor.Black,
            Background = args.Background ?? FillStyle.None,
            Name = args.Name ?? "",
        };

        // Ölçü verilmediyse metne göre hesapla (veri alanı varsa önizleme değeriyle).
        string measureText = hasFields ? FieldTemplate.Resolve(text, previewFields).Text : text;
        double maxWidth = Math.Max(5, doc.Label.WidthMm - 2 * (args.MarginMm ?? DefaultMarginMm));
        double width, height;
        if (args.Width is { } w)
        {
            width = w;
            height = args.Height ?? MeasureHeight(element, measureText, w);
        }
        else
        {
            var single = TextMetrics.Measure(measureText, element.FontFamily, element.FontSize, element.Bold, element.Italic, element.LineSpacing);
            width = Math.Clamp(single.WidthMm + 1.0, 5, maxWidth);
            height = args.Height ?? MeasureHeight(element, measureText, width);
        }
        element.Width = width;
        element.Height = height;
        Place(element, args, doc);
        return element;
    }

    private static double MeasureHeight(TextElement element, string text, double width)
    {
        var extent = TextMetrics.Measure(text, element.FontFamily, element.FontSize, element.Bold, element.Italic, element.LineSpacing,
            element.WordWrap ? Math.Max(1, width - 0.2) : null);
        return Math.Max(2, Math.Round(extent.HeightMm + 0.6, 1));
    }

    public static BarcodeElement CreateBarcode(AddBarcodeArgs args, LabelDocument doc, IFieldValueSource? previewFields)
    {
        if (string.IsNullOrWhiteSpace(args.Content))
            throw new ToolException(ToolErrorCodes.InvalidArgument, "content boş olamaz.");
        var element = new BarcodeElement
        {
            Symbology = args.Symbology,
            Content = args.Content.Trim(),
            ShowText = args.ShowText ?? true,
            QrErrorCorrection = args.QrErrorCorrection ?? QrErrorCorrectionLevel.M,
            QuietZone = args.QuietZone ?? true,
            Name = args.Name ?? "",
        };
        string sampleContent = ValidateBarcodeContent(element, previewFields, out var validation);
        if (validation is { IsValid: true } && !FieldTemplate.ContainsFields(element.Content)
            && element.Symbology is BarcodeType.Ean13 or BarcodeType.Ean8 or BarcodeType.UpcA or BarcodeType.Itf14
            && validation.EncoderData.Length > element.Content.Replace(" ", "").Length)
        {
            // Kontrol basamağı otomatik hesaplandı: öğeye tam numarayı yaz (kullanıcı da görsün).
            element.Content = validation.EncoderData;
        }

        var (w, h) = RecommendedBarcodeSize(element, sampleContent, doc);
        element.Width = args.Width ?? w;
        element.Height = args.Height ?? h;
        if (args.Rotation is { } r) element.Rotation = r;
        Place(element, args, doc);
        return element;
    }

    /// <summary>Barkod içeriğini doğrular; veri alanı varsa önizleme değeriyle. Geçersizse ToolException.</summary>
    public static string ValidateBarcodeContent(BarcodeElement element, IFieldValueSource? previewFields, out BarcodeValidationResult? validation)
    {
        validation = null;
        bool hasFields = FieldTemplate.ContainsFields(element.Content);
        string content = element.Content;
        if (hasFields)
        {
            var resolution = FieldTemplate.Resolve(element.Content, previewFields);
            if (!resolution.IsComplete) return SampleFor(element.Symbology);
            content = resolution.Text;
        }
        validation = BarcodeValidator.Validate(element.Symbology, content, BarcodeSettings.From(element));
        if (!validation.IsValid)
        {
            string where = hasFields ? $" (önizleme verisindeki değer: \"{DesignViews.Clip(content, 60)}\")" : "";
            throw new ToolException(ToolErrorCodes.InvalidBarcode,
                $"{BarcodeCatalog.GetDisplayName(element.Symbology)} içeriği geçersiz{where}: {string.Join(" ", validation.Errors)}",
                new { errors = validation.Errors, warnings = validation.Warnings });
        }
        return content;
    }

    /// <summary>Okunabilir varsayılan ölçü: 1D için EAN/UPC 3 nokta (0,375 mm), diğerleri 2 nokta modül; 2D 4 nokta.</summary>
    public static (double Width, double Height) RecommendedBarcodeSize(BarcodeElement element, string content, LabelDocument doc)
    {
        const double dotsPerMm = 8.0;
        var encoded = BarcodeEncoder.Encode(element.Symbology, content, BarcodeSettings.From(element));
        double maxWidth = Math.Max(10, doc.Label.WidthMm - 2 * DefaultMarginMm);
        var options = new BarcodeLayoutOptions { ShowText = element.ShowText, IncludeQuietZone = element.QuietZone, DotsPerMm = dotsPerMm };
        if (encoded.Symbol is null) return element.Is2D ? (20, 20) : (Math.Min(50, maxWidth), 15);

        int preferredModule = element.Symbology switch
        {
            BarcodeType.Ean13 or BarcodeType.Ean8 or BarcodeType.UpcA or BarcodeType.Itf14 => 3,
            BarcodeType.QrCode or BarcodeType.DataMatrix or BarcodeType.Gs1QrCode or BarcodeType.Gs1DataMatrix => 4,
            _ => 2,
        };
        for (int module = preferredModule; module >= 1; module--)
        {
            int widthDots = BarcodeLayoutEngine.GetRequiredWidthDots(encoded.Symbol, module, options);
            double widthMm = widthDots / dotsPerMm;
            if (widthMm > maxWidth && module > 1) continue;
            if (encoded.Symbol is MatrixSymbol matrix)
            {
                double heightMm = BarcodeLayoutEngine.GetRequiredHeightDots(matrix, module, options) / dotsPerMm;
                return (Math.Ceiling(widthMm * 4) / 4, Math.Ceiling(heightMm * 4) / 4);
            }
            double barHeight = element.Symbology is BarcodeType.Gs1_128 or BarcodeType.Itf14 ? 18 : 15;
            return (Math.Ceiling(widthMm * 4) / 4, barHeight + (element.ShowText ? 3.5 : 0));
        }
        return element.Is2D ? (20, 20) : (Math.Min(50, maxWidth), 15);
    }

    private static string SampleFor(BarcodeType type) => type switch
    {
        BarcodeType.Ean13 => "8691234567890",
        BarcodeType.Ean8 => "96385074",
        BarcodeType.UpcA => "036000291452",
        BarcodeType.Itf14 => "18691234567897",
        BarcodeType.Itf => "12345678",
        BarcodeType.Gs1_128 => "(01)08691234567890(10)LOT1",
        BarcodeType.Gs1DataMatrix or BarcodeType.Gs1QrCode => "(01)08691234567890(17)281231(10)LOT1",
        BarcodeType.Code39 => "DEMO001",
        _ => "DEMO0001",
    };

    public static ImageElement CreateImage(AddImageArgs args, LabelDocument doc, ImageAsset asset)
    {
        var symbol = BuiltinSymbol(args.Builtin);
        LoadedImage? loaded = null;
        try
        {
            loaded = ImageLoader.Load(asset.Data, asset.FileName);
        }
        catch (ImageLoadException ex)
        {
            throw new ToolException(ToolErrorCodes.InvalidArgument, $"Görsel okunamadı ({asset.FileName}): {ex.Message}");
        }
        using (loaded)
        {
            double aspect = loaded.Height > 0 ? (double)loaded.Width / loaded.Height : 1;
            double maxWidth = Math.Max(5, doc.Label.WidthMm - 2 * (args.MarginMm ?? DefaultMarginMm));
            double width, height;
            if (args.Width is { } w && args.Height is { } h)
            {
                width = w;
                height = h;
            }
            else if (args.Width is { } w2)
            {
                width = w2;
                height = w2 / aspect;
            }
            else if (args.Height is { } h2)
            {
                height = h2;
                width = h2 * aspect;
            }
            else
            {
                width = Math.Min(symbol is null ? 30 : 15, maxWidth);
                height = width / aspect;
            }
            var element = new ImageElement
            {
                AssetId = asset.Id,
                FitMode = args.FitMode ?? ImageFitMode.Contain,
                // Hazır simgeler çizim olduğu için keskin eşikleme (logo) ile basılır.
                Method = args.Method ?? (symbol is null ? ImageConversionMethod.Auto : ImageConversionMethod.Logo),
                SourceField = args.SourceField,
                Width = Math.Round(width, 2),
                Height = Math.Round(height, 2),
                Name = args.Name ?? symbol?.Name ?? Path.GetFileNameWithoutExtension(asset.FileName),
            };
            Place(element, args, doc);
            return element;
        }
    }

    public static ShapeElement CreateShape(AddShapeArgs args, LabelDocument doc)
    {
        var element = new ShapeElement
        {
            Kind = args.Kind,
            StrokeWidth = args.StrokeWidthMm ?? 0.5,
            Fill = args.Fill ?? FillStyle.None,
            CornerRadius = args.CornerRadiusMm ?? 0,
            LineOrientation = args.LineOrientation ?? LineOrientation.Horizontal,
            StrokeColor = args.WhiteStroke == true ? InkColor.White : InkColor.Black,
            Name = args.Name ?? "",
        };
        if (args.Kind == ShapeKind.Line)
        {
            element.Width = args.Width ?? (element.LineOrientation == LineOrientation.Vertical ? Math.Max(0.5, element.StrokeWidth) : doc.Label.WidthMm - 2 * DefaultMarginMm);
            element.Height = args.Height ?? (element.LineOrientation == LineOrientation.Horizontal ? Math.Max(0.5, element.StrokeWidth) : doc.Label.HeightMm - 2 * DefaultMarginMm);
        }
        else
        {
            element.Width = args.Width ?? 30;
            element.Height = args.Height ?? 20;
        }
        Place(element, args, doc);
        return element;
    }

    /// <summary>Konumu x/y'den ya da hazır konumdan belirler; döndürme ve ad uygular.</summary>
    public static void Place(LabelElement element, PlacedElementArgs args, LabelDocument doc)
    {
        if (args.Rotation is { } rotation) element.Rotation = rotation;
        double margin = args.MarginMm ?? DefaultMarginMm;
        if (args.Placement is { } placement)
        {
            var (x, y) = ComputePlacement(placement, element.Width, element.Height, doc.Label, margin);
            element.X = args.X ?? x;
            element.Y = args.Y ?? y;
            return;
        }
        if (args.X is null || args.Y is null)
            throw new ToolException(ToolErrorCodes.InvalidArgument, "Konum gerekli: x ve y (mm) ya da placement verin.");
        element.X = args.X.Value;
        element.Y = args.Y.Value;
    }

    public static (double X, double Y) ComputePlacement(Placement placement, double width, double height, LabelSetup label, double margin)
    {
        double left = margin, right = label.WidthMm - margin - width, centerX = (label.WidthMm - width) / 2;
        double top = margin, bottom = label.HeightMm - margin - height, centerY = (label.HeightMm - height) / 2;
        return placement switch
        {
            Placement.TopLeft => (left, top),
            Placement.TopCenter => (centerX, top),
            Placement.TopRight => (right, top),
            Placement.MiddleLeft => (left, centerY),
            Placement.Center => (centerX, centerY),
            Placement.MiddleRight => (right, centerY),
            Placement.BottomLeft => (left, bottom),
            Placement.BottomCenter => (centerX, bottom),
            _ => (right, bottom),
        } is var (x, y) ? (Math.Round(x, 2), Math.Round(y, 2)) : default;
    }

    /// <summary>Yerleşik görseller: "akel-logo" ve hazır simgeler ("sembol:&lt;anahtar&gt;", bkz. list_symbols).</summary>
    public static ImageAsset? GetBuiltin(string name, Func<byte[]?>? logoProvider)
    {
        if (BuiltinSymbol(name) is { } symbol) return SymbolLibrary.CreateAsset(symbol.Key);
        // Eski araç çağrıları için takma ad korunur; hiçbir şirket logosu gömülü gelmez.
        if (!string.Equals(name.Trim(), "firma-logo", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name.Trim(), "akel-logo", StringComparison.OrdinalIgnoreCase)) return null;
        byte[]? data = logoProvider?.Invoke();
        if (data is not { Length: > 0 }) return null;
        return new ImageAsset(AssetStore.ComputeId(data, "firma-logo.png"), "firma-logo.png", data);
    }

    /// <summary>builtin "sembol:&lt;anahtar&gt;" biçimindeyse simge bilgisi; değilse ya da simge yoksa null.</summary>
    private static SymbolInfo? BuiltinSymbol(string? builtin) =>
        builtin is { } name && name.Trim().StartsWith(SymbolLibrary.BuiltinPrefix, StringComparison.OrdinalIgnoreCase)
        && SymbolLibrary.TryGet(name, out var info) ? info : null;
}
