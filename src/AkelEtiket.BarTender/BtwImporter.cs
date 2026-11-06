using System.Buffers.Binary;
using AkelEtiket.BarcodeEngine;
using AkelEtiket.Core;
using AkelEtiket.Core.Model;
using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;
using SkiaSharp;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.BarTender;

/// <summary>BarTender etiketinin LabelHarbor projesine çevrilmiş hâli.</summary>
public sealed class BtwImportResult
{
    internal BtwImportResult(LabelProject project, BtwLabel source, IReadOnlyList<string> warnings, int imported, int skipped)
    {
        Project = project;
        Source = source;
        Warnings = warnings;
        ImportedCount = imported;
        SkippedCount = skipped;
    }

    public LabelProject Project { get; }
    public BtwLabel Source { get; }

    /// <summary>Kullanıcıya gösterilecek notlar (aktarılamayan ya da tahmin edilen şeyler).</summary>
    public IReadOnlyList<string> Warnings { get; }

    public int ImportedCount { get; }
    public int SkippedCount { get; }
}

/// <summary>
/// BarTender (.btw) etiketlerini LabelHarbor tasarımına çevirir. Konumlar BarTender'ın bağlantı noktası ve
/// döndürme kurallarıyla hesaplanır; yazı boyutu, satır aralığı ve karakter genişliği korunur.
/// Sonuç kaydedilmemiş yeni bir projedir; özgün dosya değiştirilmez.
/// </summary>
public static class BtwImporter
{
    public const string FileFilter = "BarTender etiketi (*.btw)|*.btw";

    /// <summary>
    /// BarTender satır aralığının yazı tipinin Windows yüksekliğine (usWinAscent + usWinDescent) oranı.
    /// Arial, Calibri ve Ebrima önizlemelerinde %1 içinde ölçüldü.
    /// </summary>
    internal const double LinePitchFactor = 1.074;

    /// <summary>Yazı çerçevesinin yazı kutusundan uzaklığı (mm).</summary>
    internal const double BorderPaddingMm = 0.3;

    private const double MilToMm = 0.0254;

    public static BtwImportResult Import(string path, FontProvider? fonts = null)
    {
        var label = BtwReader.Read(path);
        return Convert(label, Path.GetFileNameWithoutExtension(path), fonts);
    }

    public static BtwImportResult Convert(BtwLabel label, string name, FontProvider? fonts = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        fonts ??= FontProvider.Default;
        var warnings = new List<string>(label.Notes);
        double widthMm = Math.Clamp(label.Width * MilToMm, LabelSetup.MinSizeMm, LabelSetup.MaxSizeMm);
        double heightMm = Math.Clamp(label.Height * MilToMm, LabelSetup.MinSizeMm, LabelSetup.MaxSizeMm);
        var project = LabelProject.CreateBlank(Math.Round(widthMm, 2), Math.Round(heightMm, 2));
        var doc = project.Document;
        doc.Name = string.IsNullOrWhiteSpace(name) ? (label.Title ?? "BarTender") : name;
        // Belge içeriği arayüz diline çevrilmez; kaynağı sabit biçimde not edilir.
        doc.Description = string.IsNullOrWhiteSpace(label.Application) ? "BarTender (.btw)" : $"BarTender {label.Application} (.btw)";

        int imported = 0, skipped = 0, lines = 0, outside = 0;
        var missingFonts = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in label.Objects)
        {
            List<LabelElement> created;
            switch (obj)
            {
                case BtwText text:
                    created = ConvertText(text, label.PrinterDpi, fonts, missingFonts);
                    break;
                case BtwBarcode barcode:
                    created = ConvertBarcode(barcode, label.PrinterDpi, fonts, warnings);
                    break;
                case BtwPicture picture:
                    created = ConvertPicture(picture, project, warnings);
                    break;
                case BtwBox box:
                    created = [ConvertBox(box, warnings)];
                    break;
                case BtwLine:
                    lines++;
                    skipped++;
                    continue;
                default:
                    warnings.Add(L10n.T($"\"{obj.Name}\" nesnesi tanınmadı; aktarılmadı."));
                    skipped++;
                    continue;
            }
            if (created.Count == 0)
            {
                skipped++;
                continue;
            }
            if (created.All(e => IsOutside(e, widthMm, heightMm)))
            {
                outside++;
                skipped++;
                continue;
            }
            foreach (var element in created)
            {
                // Yazı çerçevesi adsız kalır (katmanlarda türüyle görünür); diğerleri BarTender'daki adı taşır.
                if (string.IsNullOrWhiteSpace(element.Name) && element is not ShapeElement) element.Name = obj.Name;
                doc.Elements.Add(element);
            }
            imported++;
        }

        if (lines > 0)
            warnings.Add(L10n.T($"{lines} çizgi nesnesi aktarılmadı (BarTender bu dosyalardaki çizgileri basmıyor). Gerekirse Şekil → Çizgi ile ekleyin."));
        if (outside > 0)
            warnings.Add(L10n.T($"{outside} nesne etiketin tamamen dışında olduğu için aktarılmadı."));
        foreach (string family in missingFonts)
            warnings.Add(L10n.T($"\"{family}\" yazı tipi bu bilgisayarda yok; yerine {FontProvider.FallbackFamily} kullanılacak ve yazılar farklı görünebilir."));
        if (label.Columns > 1 || label.Rows > 1)
            warnings.Add(L10n.T($"BarTender bu etiketi rulo üzerinde {label.Columns} × {label.Rows} (yan yana × alt alta) basıyordu. LabelHarbor her etiketi ayrı basar; yazıcının medya ayarını kontrol edin."));
        return new BtwImportResult(project, label, warnings, imported, skipped);
    }

    // ───────────── Yerleşim ─────────────

    /// <summary>Kendi kutusunda (döndürülmemiş, sol üst = 0,0) bağlantı noktasının yeri.</summary>
    private static (double X, double Y) AnchorPoint(BtwAnchor anchor, double width, double height)
    {
        int column = (int)anchor % 3, row = (int)anchor / 3;
        return (width * column / 2.0, height * row / 2.0);
    }

    /// <summary>LabelHarbor döndürmesi saat yönündedir; BarTender'ınki saatin tersine.</summary>
    internal static double ToClockwise(double counterClockwise)
    {
        double r = (360.0 - counterClockwise) % 360.0;
        return r < 0 ? r + 360.0 : r;
    }

    /// <summary>
    /// Kendi çerçevesinde verilen kutuyu (sol, üst, genişlik, yükseklik) etikete yerleştirir: bağlantı noktası
    /// BarTender'ın X/Y'sine oturur, kutu bu nokta çevresinde döndürülür. LabelHarbor kutuyu merkezi çevresinde
    /// döndürdüğü için döndürülmemiş sol üst köşe merkeze göre hesaplanır.
    /// </summary>
    internal static void Place(LabelElement element, BtwPlacement placement, (double X, double Y) anchorOwn,
        double left, double top, double width, double height)
    {
        double cw = ToClockwise(placement.RotationDegrees);
        double radians = cw * Math.PI / 180.0;
        double cos = Math.Round(Math.Cos(radians), 12), sin = Math.Round(Math.Sin(radians), 12);
        double cx = left + width / 2 - anchorOwn.X, cy = top + height / 2 - anchorOwn.Y;
        double centerX = placement.X * MilToMm + cx * cos - cy * sin;
        double centerY = placement.Y * MilToMm + cx * sin + cy * cos;
        element.Width = Math.Max(LabelElement.MinSizeMm, width);
        element.Height = Math.Max(LabelElement.MinSizeMm, height);
        element.Rotation = cw;
        element.X = Math.Round(centerX - element.Width / 2, 3);
        element.Y = Math.Round(centerY - element.Height / 2, 3);
    }

    private static bool IsOutside(LabelElement e, double labelWidth, double labelHeight)
    {
        double radians = e.Rotation * Math.PI / 180.0;
        double halfW = (Math.Abs(Math.Cos(radians)) * e.Width + Math.Abs(Math.Sin(radians)) * e.Height) / 2;
        double halfH = (Math.Abs(Math.Sin(radians)) * e.Width + Math.Abs(Math.Cos(radians)) * e.Height) / 2;
        double cx = e.X + e.Width / 2, cy = e.Y + e.Height / 2;
        return cx + halfW <= 0 || cy + halfH <= 0 || cx - halfW >= labelWidth || cy - halfH >= labelHeight;
    }

    // ───────────── Yazı ─────────────

    private static List<LabelElement> ConvertText(BtwText text, int? printerDpi, FontProvider fonts, ISet<string> missingFonts)
    {
        var font = text.Font ?? new BtwFont(-200, 0, 400, false, false, FontProvider.FallbackFamily);
        string family = string.IsNullOrWhiteSpace(font.Face) ? FontProvider.FallbackFamily : font.Face;
        var match = fonts.Get(family, font.Bold, font.Italic);
        if (!match.IsExactFamily) missingFonts.Add(family);
        var probe = FontVerticalMetrics.Of(match, 10);
        double points = Math.Clamp(font.Height > 0 ? font.Height / 20.0 / probe.WindowsHeight : font.SizePoints, 2, 500);
        double em = Units.PointsToMm(points);
        var metrics = FontVerticalMetrics.Of(match, em);
        double pitch = LinePitchFactor * metrics.WindowsHeight * em;
        double characterWidth = Math.Clamp(text.WidthScale / 10.0, TextElement.MinCharacterWidth, TextElement.MaxCharacterWidth);
        // BarTender sondaki boş satırları yerleşimde saymaz.
        string content = NormalizeNewLines(text.Content).TrimEnd('\n');

        // Satırları BarTender gibi burada kırıp metne yazıyoruz: LabelHarbor'ın kendi kaydırması çözünürlüğe göre
        // farklı yerden kırabilir ve son satır kutudan taşabilir.
        var printed = new LineMeasure(match, em, characterWidth / 100.0, printerDpi ?? 300);
        var ideal = new LineMeasure(match, em, characterWidth / 100.0, null);
        var paragraphs = content.Split('\n');
        List<string> lines = text.FixedWidth is { } fixedWidth
            ? Wrap(paragraphs, fixedWidth * MilToMm, printed.Width)
            : [.. paragraphs];
        content = string.Join('\n', lines);
        int lineCount = Math.Max(1, lines.Count);
        double longest = lines.Count == 0 ? 0 : lines.Max(l => Math.Max(printed.Width(l), ideal.Width(l)));
        double boxWidth = text.FixedWidth is { } fw ? fw * MilToMm : lines.Count == 0 ? 0 : lines.Max(printed.Width);

        // BarTender kutusu: (satır − 1) × satır aralığı + yazı tipinin Windows yüksekliği; ilk taban çizgisi
        // kutu üstünden Windows yükselmesi kadar aşağıda (önizlemelerle ölçüldü).
        double boxHeight = (lineCount - 1) * pitch + metrics.WindowsHeight * em;
        var anchor = AnchorPoint(text.Placement.Anchor, boxWidth, boxHeight);

        // LabelHarbor ilk taban çizgisini kutu üstü + kendi (Skia) yükselmesine koyar.
        double baseline = metrics.WindowsAscent * em;
        double top = baseline - metrics.SkiaAscent * em;
        double lineSpacing = Math.Clamp(pitch / (metrics.SkiaSpacing * em), 0.5, 3.0);
        double needed = (lineCount - 1) * metrics.SkiaSpacing * em * lineSpacing + (metrics.SkiaAscent + metrics.SkiaDescent) * em;
        // Pay: ipuçlu (hinted) çizim farklı çözünürlüklerde birkaç yüzde geniş/uzun olabilir; kutu yine de yetsin.
        double height = Math.Max(boxHeight - top, needed * 1.04) + 0.5;
        double width = Math.Max(boxWidth, longest * 1.06 + 0.4);
        double left = text.Alignment switch { 1 => -(width - boxWidth) / 2, 2 => -(width - boxWidth), _ => 0 };

        var element = new TextElement
        {
            Name = text.Name,
            Text = content,
            FontFamily = family,
            FontSize = Math.Round(points, 2),
            Bold = font.Bold,
            Italic = font.Italic,
            Underline = font.Underline,
            CharacterWidth = characterWidth,
            HorizontalAlignment = text.Alignment switch { 1 => TextHorizontalAlignment.Center, 2 => TextHorizontalAlignment.Right, _ => TextHorizontalAlignment.Left },
            VerticalAlignment = TextVerticalAlignment.Top,
            WordWrap = text.FixedWidth is not null,
            AutoShrink = false,
            LineSpacing = Math.Round(lineSpacing, 4),
        };
        Place(element, text.Placement, anchor, left, top, width, height);
        var result = new List<LabelElement>();
        if (text.Border is { } border)
        {
            double thickness = Math.Max(0.1, border.Thickness * MilToMm);
            var frame = new ShapeElement
            {
                Kind = ShapeKind.Rectangle,
                StrokeWidth = Math.Round(thickness, 3),
                CornerRadius = border.Corner is 1 or 2 ? Math.Round(border.CornerRadius * MilToMm, 3) : 0,
                Fill = FillStyle.None,
            };
            double pad = BorderPaddingMm;
            Place(frame, text.Placement, anchor, -pad, -pad, boxWidth + 2 * pad, boxHeight + 2 * pad);
            result.Add(frame);
        }
        result.Add(element);
        return result;
    }

    internal static string NormalizeNewLines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>
    /// Tek kelimenin harf harf bölünmesi için gereken taşma (mm). BarTender genişliği yazıcı çözünürlüğünde
    /// ipuçlu ölçer; önizlemelerde 5,41 mm kutuda 5,42 mm'lik tek kelime bölünmedi, aynı genişlikteki iki kelimelik
    /// satır boşluktan kırıldı, 7,7 mm'lik tek kelime bölündü.
    /// </summary>
    internal const double WrapToleranceMm = 0.1;

    /// <summary>
    /// Kelime kelime satır kaydırma; tek başına sığmayan kelime harf harf bölünür (BarTender gibi). Satır başındaki
    /// boşluklar korunur (BarTender'da yazıyı sağa itmek için kullanılıyor).
    /// </summary>
    internal static List<string> Wrap(IEnumerable<string> paragraphs, double maxWidthMm, Func<string, double> width)
    {
        var lines = new List<string>();
        foreach (string paragraph in paragraphs)
        {
            string current = "";
            foreach (string piece in SplitKeepingSpaces(paragraph))
            {
                string token = piece;
                if (char.IsWhiteSpace(token[0]))
                {
                    current += token;
                    continue;
                }
                if (current.Trim().Length > 0 && width(current + token) > maxWidthMm)
                {
                    lines.Add(current.TrimEnd());
                    current = "";
                }
                // Satıra tek başına belirgin biçimde sığmayan kelime: sığan en uzun parça bu satırda kalır.
                while (token.Length > 1 && width(current + token) > maxWidthMm + WrapToleranceMm)
                {
                    int fit = 1;
                    while (fit < token.Length - 1 && width(current + token[..(fit + 1)]) <= maxWidthMm) fit++;
                    if (fit < token.Length && char.IsHighSurrogate(token[fit - 1]) && fit > 1) fit--;
                    lines.Add((current + token[..fit]).TrimEnd());
                    current = "";
                    token = token[fit..];
                }
                current += token;
            }
            lines.Add(current.TrimEnd());
        }
        return lines;
    }

    private static IEnumerable<string> SplitKeepingSpaces(string text)
    {
        int start = 0;
        for (int i = 1; i <= text.Length; i++)
        {
            if (i == text.Length || char.IsWhiteSpace(text[i]) != char.IsWhiteSpace(text[start]))
            {
                yield return text[start..i];
                start = i;
            }
        }
    }

    // ───────────── Barkod ─────────────

    internal static BarcodeType? MapSymbology(string className) => className switch
    {
        "BcEAN13Data" or "BcJAN13Data" => BarcodeType.Ean13,
        "BcEAN8Data" or "BcJAN8Data" => BarcodeType.Ean8,
        "BcUPCAData" => BarcodeType.UpcA,
        "BcCode128Data" => BarcodeType.Code128,
        "BcGS1128Data" or "BcUCCEAN128Data" or "BcEAN128Data" => BarcodeType.Gs1_128,
        "BcCode39Data" => BarcodeType.Code39,
        "BcInterleaved2of5Data" or "BcI2of5Data" => BarcodeType.Itf,
        "BcITF14Data" => BarcodeType.Itf14,
        "BcQRCodeData" => BarcodeType.QrCode,
        "BcDataMatrixData" => BarcodeType.DataMatrix,
        "BcPDF417Data" => BarcodeType.Pdf417,
        _ => null,
    };

    private static List<LabelElement> ConvertBarcode(BtwBarcode barcode, int? printerDpi, FontProvider fonts, List<string> warnings)
    {
        var type = MapSymbology(barcode.ClassName);
        if (type is null)
        {
            warnings.Add(L10n.T($"\"{barcode.Name}\" barkodunun türü ({barcode.ClassName}) tanınmadı; Code 128 olarak aktarıldı, kontrol edin."));
            type = BarcodeType.Code128;
        }
        else if (type != BarcodeType.Ean13)
        {
            warnings.Add(L10n.T($"\"{barcode.Name}\" ({type}) barkodunun ölçüsü bu sürümde doğrulanmadı; boyutunu kontrol edin."));
        }

        var element = new BarcodeElement { Name = barcode.Name, Symbology = type.Value, Content = barcode.Data };
        if (BarcodeElement.IsTwoDimensional(type.Value))
        {
            double side = barcode.BarHeight > 0 ? barcode.BarHeight * MilToMm : 20;
            Place(element, barcode.Placement, AnchorPoint(barcode.Placement.Anchor, side, side), 0, 0, side, side);
            return [element];
        }

        var encoded = BarcodeEncoder.Encode(type.Value, barcode.Data);
        var symbol = encoded.Symbol as LinearSymbol;
        if (!encoded.Success)
            warnings.Add(L10n.T($"\"{barcode.Name}\" barkodu geçersiz: {encoded.Error}"));
        int modules = symbol?.Modules.Length ?? 95;
        bool eanFamily = symbol?.IsUpcEanFamily ?? type is BarcodeType.Ean13 or BarcodeType.Ean8 or BarcodeType.UpcA;
        int quiet = eanFamily ? symbol?.QuietZoneModules ?? 11 : 0;
        double module = barcode.BarsWidth > 0 ? barcode.BarsWidth * MilToMm / modules : 0.33;
        if (printerDpi is > 0)
        {
            // BarTender modülü yazıcı noktasına yuvarlar; aynı çözünürlükte çubuklar tam aynı yere düşsün.
            double dots = Math.Max(1, Math.Round(module / 25.4 * printerDpi.Value));
            module = dots * 25.4 / printerDpi.Value;
        }
        double barHeight = barcode.BarHeight > 0 ? barcode.BarHeight * MilToMm : 15;

        var hr = barcode.TextFont;
        double textBox = 0;
        if (hr is not null)
        {
            var match = fonts.Get(string.IsNullOrWhiteSpace(hr.Face) ? FontProvider.FallbackFamily : hr.Face, false, false);
            var metrics = FontVerticalMetrics.Of(match, Units.PointsToMm(Math.Clamp(hr.SizePoints, 2, 72)));
            double hrPoints = Math.Clamp(hr.SizePoints, 2, 72);
            // LabelHarbor rakamların büyük harf yüksekliğini metin kutusunun %92'si yapar.
            double size = Math.Clamp(Math.Round(hrPoints * metrics.CapHeight / 0.92, 1), 1, 72);
            element.TextFontFamily = match.IsExactFamily ? hr.Face : FontProvider.FallbackFamily;
            element.TextSize = size;
            textBox = Units.PointsToMm(size);
        }
        element.ShowText = true;
        element.QuietZone = eanFamily;
        double width = (modules + 2 * quiet) * module + 0.01;
        double height = barHeight + module + (textBox > 0 ? textBox : module * 8.5);
        double ownTextHeight = hr is null ? 0 : LinePitchFactor * Units.PointsToMm(hr.SizePoints) * 1.15;
        var anchor = AnchorPoint(barcode.Placement.Anchor, modules * module, barHeight + ownTextHeight);
        Place(element, barcode.Placement, anchor, -quiet * module, 0, width, height);
        return [element];
    }

    // ───────────── Görsel ve kutu ─────────────

    private static List<LabelElement> ConvertPicture(BtwPicture picture, LabelProject project, List<string> warnings)
    {
        double width = picture.Width * MilToMm, height = picture.Height * MilToMm;
        if (width <= 0 || height <= 0)
        {
            var size = ImagePixelSize(picture.Data);
            if (size is null)
            {
                warnings.Add(L10n.T($"\"{picture.Name}\" görseli açılamadı; aktarılmadı."));
                return [];
            }
            width = size.Value.Width * 25.4 / 96;
            height = size.Value.Height * 25.4 / 96;
        }
        string fileName = (string.IsNullOrWhiteSpace(picture.OriginalPath) ? picture.Name : Path.GetFileNameWithoutExtension(picture.OriginalPath.Replace('\\', '/').Split('/')[^1]))
            + picture.Extension;
        var asset = project.Assets.Add(picture.Data, fileName);
        var element = new ImageElement
        {
            Name = picture.Name,
            AssetId = asset.Id,
            FitMode = ImageFitMode.Stretch,
            // Renkli logolar termal yazıcıda sürücünün yaptığı gibi noktalarla (hata yayma) basılır; keskin eşik çoğunu siyaha boyar.
            Method = ImageConversionMethod.FloydSteinberg,
        };
        Place(element, picture.Placement, AnchorPoint(picture.Placement.Anchor, width, height), 0, 0, width, height);
        return [element];
    }

    private static ShapeElement ConvertBox(BtwBox box, List<string> warnings)
    {
        double width = box.Width * MilToMm, height = box.Height * MilToMm;
        var shape = new ShapeElement
        {
            Name = box.Name,
            Kind = ShapeKind.Rectangle,
            StrokeWidth = Math.Round(Math.Max(0.1, box.Thickness * MilToMm), 3),
            CornerRadius = box.Corner is 1 or 2 ? Math.Round(box.CornerRadius * MilToMm, 3) : 0,
            Fill = FillStyle.None,
        };
        if (box.Corner == 2)
            warnings.Add(L10n.T($"\"{box.Name}\" kutusunun köşeleri kesik (pah); LabelHarbor'da yuvarlak köşe olarak aktarıldı."));
        Place(shape, box.Placement, AnchorPoint(box.Placement.Anchor, width, height), 0, 0, width, height);
        return shape;
    }

    private static (int Width, int Height)? ImagePixelSize(byte[] data)
    {
        try
        {
            using var codec = SKCodec.Create(new MemoryStream(data));
            return codec is null ? null : (codec.Info.Width, codec.Info.Height);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>Yazı tipinin dikey ölçüleri (em oranı): Windows (OS/2) ve LabelHarbor'ın kullandığı Skia değerleri.</summary>
internal readonly record struct FontVerticalMetrics(
    double WindowsAscent, double WindowsDescent, double CapHeight, double SkiaAscent, double SkiaDescent, double SkiaSpacing)
{
    public double WindowsHeight => WindowsAscent + WindowsDescent;

    private const uint HeadTag = 0x68656164; // 'head'
    private const uint Os2Tag = 0x4F532F32;  // 'OS/2'

    /// <summary>
    /// <paramref name="emMm"/> boyutundaki ölçüler. Skia değerleri LabelHarbor'ın varsayılan baskı çözünürlüğünde
    /// (8 nokta/mm) ipuçlu yazıyla alınır; çizim motoru satır aralığını bu ölçüyle hesaplar.
    /// </summary>
    public static FontVerticalMetrics Of(FontMatch match, double emMm, double dotsPerMm = 8.0)
    {
        float size = (float)Math.Max(4.0, emMm * dotsPerMm);
        using var font = match.CreateFont(size);
        var m = font.Metrics;
        double skAscent = -m.Ascent / size, skDescent = m.Descent / size;
        double skSpacing = font.Spacing / size;
        using var probe = match.CreateFont(1000f);
        double cap = probe.Metrics.CapHeight > 0 ? probe.Metrics.CapHeight / 1000.0 : 0.7;
        double winAscent = skAscent, winDescent = skDescent;
        try
        {
            byte[]? head = match.Typeface.GetTableData(HeadTag);
            byte[]? os2 = match.Typeface.GetTableData(Os2Tag);
            if (head is { Length: >= 20 } && os2 is { Length: >= 78 })
            {
                int unitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(18));
                if (unitsPerEm > 0)
                {
                    winAscent = BinaryPrimitives.ReadUInt16BigEndian(os2.AsSpan(74)) / (double)unitsPerEm;
                    winDescent = BinaryPrimitives.ReadUInt16BigEndian(os2.AsSpan(76)) / (double)unitsPerEm;
                    if (os2.Length >= 90)
                    {
                        int capUnits = BinaryPrimitives.ReadInt16BigEndian(os2.AsSpan(88));
                        if (capUnits > 0) cap = capUnits / (double)unitsPerEm;
                    }
                }
            }
        }
        catch (Exception)
        {
            // Tablo okunamazsa Skia ölçüleri kullanılır.
        }
        if (winAscent + winDescent <= 0.2) { winAscent = 0.9; winDescent = 0.25; }
        if (skSpacing <= 0.2) skSpacing = winAscent + winDescent;
        return new FontVerticalMetrics(winAscent, winDescent, cap, skAscent, skDescent, skSpacing);
    }
}

/// <summary>
/// Satır genişliği (mm). Çözünürlük verilirse BarTender gibi o yazıcı çözünürlüğünde ipuçlu (tam nokta) ölçülür;
/// verilmezse ipuçsuz (ideal) genişliktir.
/// </summary>
internal sealed class LineMeasure(FontMatch match, double emMm, double scaleX, int? dpi)
{
    private const float IdealSize = 1000f;

    public double Width(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        if (dpi is > 0)
        {
            float pixels = (float)(emMm / 25.4 * dpi.Value);
            using var hinted = match.CreateFont(Math.Max(1f, pixels), (float)scaleX);
            hinted.Hinting = SKFontHinting.Normal;
            hinted.Subpixel = false;
            hinted.LinearMetrics = false;
            return hinted.MeasureText(text) / dpi.Value * 25.4;
        }
        using var font = match.CreateFont(IdealSize, (float)scaleX);
        font.Hinting = SKFontHinting.None;
        font.Subpixel = true;
        font.BaselineSnap = false;
        return font.MeasureText(text) / IdealSize * emMm;
    }
}
