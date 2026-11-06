using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.BarTender;

/// <summary>
/// BarTender 10.1 ve 2016 etiket dosyalarını (.btw) okur. Biçim belgelenmemiştir; bu okuyucu dosyadaki
/// MFC arşivinden yalnızca etiket ölçüsünü ve yazı, barkod, görsel, kutu nesnelerini çıkarır.
/// Dosya yalnızca okunur; içindeki betikler, veritabanı bağlantıları ve yazıcı ayarları kullanılmaz.
/// </summary>
public static class BtwReader
{
    public const string Extension = ".btw";
    public const long MaxFileBytes = 64L * 1024 * 1024;
    private const int MaxDocumentBytes = 256 * 1024 * 1024;

    internal static readonly byte[] Signature = Encoding.ASCII.GetBytes("Bar Tender Format File");
    internal static readonly byte[] Separator = Encoding.ASCII.GetBytes(new string('-', 46) + "\r\n");
    internal static readonly byte[] Marker = [0xFF, 0xFE, 0xFF];

    /// <summary>Her çizim nesnesinin adından hemen sonra gelen sabit 15 baytlık kimlik.</summary>
    internal static readonly byte[] ObjectGuid = Convert.FromHexString("8BE69CE282D62B46963519DC91A038");

    /// <summary>Tasarım akışı "BarTender" dizesiyle başlar (MFC Unicode CString).</summary>
    internal static readonly byte[] DocumentPrefix =
        [0xFF, 0xFE, 0xFF, 0x09, .. Encoding.Unicode.GetBytes("BarTender")];

    /// <summary>Dosyanın ilk baytları BarTender imzasını taşıyor mu?</summary>
    public static bool LooksLikeBtw(ReadOnlySpan<byte> head) =>
        head[..Math.Min(head.Length, 96)].IndexOf(Signature) >= 0;

    public static bool IsBtwPath(string? path) =>
        path is not null && Path.GetExtension(path).Equals(Extension, StringComparison.OrdinalIgnoreCase);

    public static BtwLabel Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var info = new FileInfo(path);
        if (!info.Exists) throw new BtwFormatException(L10n.T($"Dosya bulunamadı: {path}"));
        if (info.Length > MaxFileBytes) throw new BtwFormatException(L10n.T("Dosya çok büyük; BarTender etiketi olarak açılmadı."));
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BtwFormatException(L10n.T($"Dosya okunamadı: {ex.Message}"), ex);
        }
        return Read(bytes);
    }

    public static BtwLabel Read(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!LooksLikeBtw(file))
            throw new BtwFormatException(L10n.T("Bu dosya bir BarTender etiketi (.btw) değil."));
        int first = file.AsSpan().IndexOf(Separator);
        int second = first < 0 ? -1 : IndexOf(file, Separator, first + Separator.Length);
        if (second < 0) throw new BtwFormatException(L10n.T("BarTender dosya başlığı okunamadı."));
        string header = Encoding.UTF8.GetString(file, 0, second);
        byte[] document = FindDocument(file, second + Separator.Length)
            ?? throw new BtwFormatException(L10n.T("BarTender etiketinin tasarım bölümü bulunamadı. Dosya bozuk ya da desteklenmeyen bir sürüm olabilir."));
        return new Parser(document, header).Parse();
    }

    /// <summary>Dosyadaki zlib akışlarından tasarım akışını bulur ve açar.</summary>
    internal static byte[]? FindDocument(byte[] file, int start)
    {
        Span<byte> probe = stackalloc byte[DocumentPrefix.Length];
        for (int k = start; k < file.Length - 2; k++)
        {
            if (file[k] != 0x78 || ((0x78 << 8) | file[k + 1]) % 31 != 0) continue;
            try
            {
                using (var head = new ZLibStream(new MemoryStream(file, k, file.Length - k, writable: false), CompressionMode.Decompress))
                {
                    if (head.ReadAtLeast(probe, probe.Length, throwOnEndOfStream: false) < probe.Length) continue;
                }
                if (!probe.SequenceEqual(DocumentPrefix)) continue;
                using var z = new ZLibStream(new MemoryStream(file, k, file.Length - k, writable: false), CompressionMode.Decompress);
                using var output = new MemoryStream();
                byte[] buffer = new byte[81920];
                int read;
                while ((read = z.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + read > MaxDocumentBytes)
                        throw new BtwFormatException(L10n.T("BarTender etiketinin tasarım bölümü çok büyük."));
                    output.Write(buffer, 0, read);
                }
                return output.ToArray();
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                // Bu konumda geçerli bir akış yok; sonrakine bakılır.
            }
        }
        return null;
    }

    internal static int IndexOf(byte[] data, ReadOnlySpan<byte> pattern, int start, int end = -1)
    {
        if (end < 0 || end > data.Length) end = data.Length;
        if (start < 0) start = 0;
        if (start >= end) return -1;
        int i = data.AsSpan(start, end - start).IndexOf(pattern);
        return i < 0 ? -1 : start + i;
    }

    /// <summary>MFC CString (Unicode) okur: FF FE FF + uzunluk (+ genişletilmiş uzunluk) + UTF-16.</summary>
    internal static bool TryReadString(byte[] data, int at, out string text, out int next)
    {
        text = "";
        next = at;
        if (at < 0 || at + 4 > data.Length || data[at] != 0xFF || data[at + 1] != 0xFE || data[at + 2] != 0xFF) return false;
        int p = at + 4;
        long length = data[at + 3];
        if (length == 0xFF)
        {
            if (p + 2 > data.Length) return false;
            length = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p));
            p += 2;
            if (length == 0xFFFF)
            {
                if (p + 4 > data.Length) return false;
                length = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p));
                p += 4;
            }
        }
        if (length < 0 || p + length * 2 > data.Length) return false;
        text = Encoding.Unicode.GetString(data, p, (int)length * 2);
        next = p + (int)length * 2;
        return true;
    }

    internal static int I32(byte[] data, int at) =>
        at >= 0 && at + 4 <= data.Length ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at)) : 0;

    private sealed record ClassDef(int Offset, string Name, int Payload);

    private sealed record Record(int Offset, string? ClassName, int Payload, int End);

    private sealed class Parser(byte[] data, string header)
    {
        private readonly List<string> _notes = [];

        public BtwLabel Parse()
        {
            var defs = FindClassDefs();
            var records = FindRecords(defs);
            var (width, height, columns, rows, fromHeader) = ReadLabelSize();
            var objects = new List<BtwObject>();
            var classes = defs.Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var record in records)
            {
                var obj = ReadObject(record, classes);
                if (obj is not null) objects.Add(obj);
            }
            return new BtwLabel
            {
                Application = HeaderValue(@"Application:\s*Version=([^;\r\n]+)"),
                Printer = HeaderValue(@"Printer:\s*Name=([^;\r\n]+)"),
                PrinterDpi = ReadPrinterDpi(),
                Title = HeaderValue(@"<Title>([^<]*)</Title>"),
                Width = width,
                Height = height,
                Columns = columns,
                Rows = rows,
                SizeFromHeader = fromHeader,
                Objects = objects,
                Notes = _notes,
            };
        }

        private string? HeaderValue(string pattern)
        {
            var m = Regex.Match(header, pattern);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        // ───────────── Arşiv yapısı ─────────────

        /// <summary>Yeni sınıf kayıtları: FF FF + şema (u16) + ad uzunluğu (u16) + ASCII ad.</summary>
        private List<ClassDef> FindClassDefs()
        {
            var list = new List<ClassDef>();
            for (int i = 0; i + 6 < data.Length; i++)
            {
                if (data[i] != 0xFF || data[i + 1] != 0xFF) continue;
                int schema = data[i + 2] | (data[i + 3] << 8);
                int length = data[i + 4] | (data[i + 5] << 8);
                if (schema is < 1 or > 15 || length is < 3 or > 64 || i + 6 + length > data.Length) continue;
                bool ok = IsIdentifierStart(data[i + 6]);
                for (int k = 1; ok && k < length; k++) ok = IsIdentifierPart(data[i + 6 + k]);
                if (!ok) continue;
                list.Add(new ClassDef(i, Encoding.ASCII.GetString(data, i + 6, length), i + 6 + length));
                i += 5 + length;
            }
            return list;
        }

        private static bool IsIdentifierStart(byte b) => b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or (byte)'_';

        private static bool IsIdentifierPart(byte b) => IsIdentifierStart(b) || b is >= (byte)'0' and <= (byte)'9';

        /// <summary>Çizim nesnesinin yükü: X, Y (i32), 8 sıfır bayt, bayrak, 3 sıfır bayt, ardından bir CString.</summary>
        private bool LooksLikeObjectPayload(int p) =>
            p + 24 <= data.Length
            && data.AsSpan(p + 8, 8).IndexOfAnyExcept((byte)0) < 0
            && data[p + 16] <= 0x0F && data[p + 17] == 0 && data[p + 18] == 0 && data[p + 19] == 0
            && data[p + 20] == 0xFF && data[p + 21] == 0xFE && data[p + 22] == 0xFF;

        private static bool IsDrawableClass(string name) =>
            name is "TextData" or "LineData" or "BoxData" or "PictureData" or "EllipseData" || IsBarcodeClass(name);

        private static bool IsTrailingSection(string name) =>
            name is "BackgroundData" or "DialogData" or "NumberFormatData" or "FormatData" or "ScreenDs" or "BackgroundRFIDData"
            || name.EndsWith("ControlData", StringComparison.Ordinal) || name.EndsWith("ControlStringData", StringComparison.Ordinal);

        internal static bool IsBarcodeClass(string name) => name.StartsWith("Bc", StringComparison.Ordinal) && name.EndsWith("Data", StringComparison.Ordinal);

        private List<Record> FindRecords(List<ClassDef> defs)
        {
            var starts = new List<(int Offset, string? Class, int Payload)>();
            foreach (var d in defs)
                if (IsDrawableClass(d.Name) && LooksLikeObjectPayload(d.Payload)) starts.Add((d.Offset, d.Name, d.Payload));
            // Aynı sınıftan sonraki nesneler sınıf adını tekrar yazmaz: 0x8000 | sınıf sırası etiketiyle başlar.
            for (int i = 0; i + 2 + 24 <= data.Length; i++)
            {
                if ((data[i + 1] & 0x80) == 0 || (data[i] == 0xFF && data[i + 1] == 0xFF)) continue;
                if (!LooksLikeObjectPayload(i + 2)) continue;
                if (defs.Any(d => Math.Abs(d.Offset - i) < 8 || (d.Payload == i + 2))) continue;
                starts.Add((i, null, i + 2));
            }
            starts.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            // Nesne kaydı bir sonraki nesneye ya da nesnelerden sonra gelen bölüme (arka plan, form…) kadar sürer.
            // Görsel gibi nesnelerin içinde ilk kez tanımlanan alt sınıflar (ImageHandle…) sınır sayılmaz.
            var boundaries = defs.Where(d => IsTrailingSection(d.Name)).Select(d => d.Offset)
                .Concat(starts.Select(s => s.Offset)).Append(data.Length)
                .Distinct().OrderBy(o => o).ToList();
            var records = new List<Record>();
            foreach (var s in starts)
            {
                int end = boundaries.First(b => b > s.Offset);
                records.Add(new Record(s.Offset, s.Class, s.Payload, end));
            }
            return records;
        }

        /// <summary>
        /// Gömülü DEVMODEW: 64 baytlık aygıt adı, dmSize (+68) = 156 ya da 220, dmPrintQuality (+90) ve
        /// dmYResolution (+96) aynı nokta/inç değeri.
        /// </summary>
        private int? ReadPrinterDpi()
        {
            int limit = Math.Min(data.Length, 16 * 1024) - 100;
            for (int b = 0; b < limit; b++)
            {
                int size = data[b + 68] | (data[b + 69] << 8);
                if (size is not (156 or 220)) continue;
                int quality = (short)(data[b + 90] | (data[b + 91] << 8));
                int resolution = (short)(data[b + 96] | (data[b + 97] << 8));
                if (quality == resolution && quality is >= 100 and <= 2400) return quality;
            }
            return null;
        }

        /// <summary>Sayfa kaydı: sayfa G/Y, 3 alan, boşluk X/Y, etiket G/Y, satır, sütun (mil).</summary>
        private (int Width, int Height, int Columns, int Rows, bool FromHeader) ReadLabelSize()
        {
            ReadOnlySpan<byte> start = [0xFF, 0xFF, 0x00, 0x01, 0x00, 0x00];
            int limit = Math.Min(data.Length, 64 * 1024);
            for (int i = IndexOf(data, start, 0, limit); i >= 0; i = IndexOf(data, start, i + 1, limit))
            {
                int p = i + start.Length;
                if (p + 44 > data.Length) break;
                int pageW = I32(data, p), pageH = I32(data, p + 4);
                int w = I32(data, p + 28), h = I32(data, p + 32), rows = I32(data, p + 36), cols = I32(data, p + 40);
                if (w is < 50 or > 60000 || h is < 50 or > 60000) continue;
                if (w > pageW + 2 || h > pageH + 2) continue;
                if (rows is < 1 or > 1000 || cols is < 1 or > 1000) continue;
                return (w, h, cols, rows, false);
            }
            var m = Regex.Match(header, @"<TemplateSize>\s*([\d.,]+)\s*x\s*([\d.,]+)\s*mm", RegexOptions.IgnoreCase);
            if (m.Success
                && double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double wm)
                && double.TryParse(m.Groups[2].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double hm))
            {
                _notes.Add(L10n.T("Etiket ölçüsü dosya başlığındaki özetten alındı."));
                return ((int)Math.Round(wm / 0.0254), (int)Math.Round(hm / 0.0254), 1, 1, true);
            }
            _notes.Add(L10n.T("Etiket ölçüsü okunamadı; 100 × 100 mm varsayıldı. Etiket ölçüsünü kontrol edin."));
            return (3937, 3937, 1, 1, true);
        }

        // ───────────── Nesneler ─────────────

        private BtwObject? ReadObject(Record r, HashSet<string> classes)
        {
            int guid = IndexOf(data, ObjectGuid, r.Payload, r.End);
            var placement = ReadPlacement(r, guid, out string name);
            string kind = r.ClassName is { } cls ? KindOf(cls) : GuessKind(r, guid, classes);
            return kind switch
            {
                "text" => ReadText(r, name, placement, guid),
                "barcode" => ReadBarcode(r, name, placement, guid, r.ClassName ?? classes.FirstOrDefault(IsBarcodeClass) ?? "Bc?Data"),
                "picture" => ReadPicture(r, name, placement),
                "box" => ReadBox(name, placement, guid),
                "line" => new BtwLine(name, placement),
                _ => new BtwUnknown(name, placement, r.ClassName ?? "?"),
            };
        }

        private static string KindOf(string cls) => cls switch
        {
            "TextData" => "text",
            "LineData" => "line",
            "BoxData" => "box",
            "PictureData" => "picture",
            _ when IsBarcodeClass(cls) => "barcode",
            _ => "unknown",
        };

        /// <summary>Sınıf adı yazılmamış (tekrar eden) nesneyi, ad ve kimlikten sonraki yapıya bakarak sınıflandırır.</summary>
        private string GuessKind(Record r, int guid, HashSet<string> classes)
        {
            if (guid < 0) return "unknown";
            int s = guid + ObjectGuid.Length;
            if (s + 8 > data.Length || data[s] != 0xFF || data[s + 1] != 0xFF || data[s + 2] != 0xFF) return "unknown";
            bool startsWithString = data[s + 3] == 0 && data[s + 4] == 0xFF && data[s + 5] == 0xFE && data[s + 6] == 0xFF;
            if (!startsWithString)
            {
                bool hasLine = classes.Contains("LineData"), hasBox = classes.Contains("BoxData");
                if (hasLine && !hasBox) return "line";
                if (hasBox && !hasLine) return "box";
                int length = I32(data, s + 3), x1 = I32(data, s + 7), y1 = I32(data, s + 11), x2 = I32(data, s + 15), y2 = I32(data, s + 19);
                return Math.Abs(Math.Sqrt((double)(x2 - x1) * (x2 - x1) + (double)(y2 - y1) * (y2 - y1)) - length) <= 3 ? "line" : "box";
            }
            if (!TryReadString(data, s + 4, out string first, out _)) return "unknown";
            if (first.Length > 0) return "barcode";
            if (classes.Contains("PictureData") && FindImage(r.Payload, r.End) is not null) return "picture";
            return "text";
        }

        /// <summary>
        /// Yer bilgisi nesne adının hemen önündeki 24 baytlık blokta durur: 00 01, bağlantı noktası (i32),
        /// döndürme (derece × 1000), …; ad dizesi sabit nesne kimliğiyle biter.
        /// </summary>
        private BtwPlacement ReadPlacement(Record r, int guid, out string name)
        {
            int x = I32(data, r.Payload), y = I32(data, r.Payload + 4);
            name = "";
            if (guid > r.Payload)
            {
                for (int q = IndexOf(data, Marker, r.Payload, guid); q >= 0; q = IndexOf(data, Marker, q + 1, guid))
                {
                    if (!TryReadString(data, q, out string text, out int next) || next != guid) continue;
                    name = text;
                    int b = q - 24;
                    if (b >= r.Payload && data[b] == 0 && data[b + 1] == 1)
                    {
                        int anchor = I32(data, b + 2), rotation = I32(data, b + 6);
                        if (anchor is >= 0 and <= 8 && rotation is >= 0 and < 360000)
                            return new BtwPlacement(x, y, (BtwAnchor)anchor, rotation / 1000.0, true);
                    }
                    break;
                }
            }
            _notes.Add(L10n.T($"\"{name}\" nesnesinin döndürme/bağlantı bilgisi okunamadı; sol üst köşe ve 0° varsayıldı."));
            return new BtwPlacement(x, y, BtwAnchor.TopLeft, 0, false);
        }

        private static readonly byte[] SampleMask = [0xFF, 0xFE, 0xFF, 0x0E, .. Encoding.Unicode.GetBytes("(???) ???-????")];

        /// <summary>Nesnenin değeri: "(???) ???-????" maskesinden sonra boş dize, FF FF FF FF ve değer gelir.</summary>
        private bool TryReadValue(int from, int end, out string value, out int next)
        {
            value = "";
            next = from;
            int m = IndexOf(data, SampleMask, from, end);
            if (m < 0) return false;
            if (!TryReadString(data, m + SampleMask.Length, out _, out int p)) return false;
            if (p + 4 > data.Length || I32(data, p) != -1) return false;
            return TryReadString(data, p + 4, out value, out next);
        }

        private static readonly byte[] FontLead = [0xF4, 0x01, 0, 0, 0, 0, 0, 0];

        /// <summary>LOGFONT: önünde F4 01 00 00 00 00 00 00 ve yüksekliğin bir kopyası bulunur.</summary>
        private int FindLogFont(int from, int end)
        {
            for (int i = IndexOf(data, FontLead, from, end); i >= 0; i = IndexOf(data, FontLead, i + 1, end))
            {
                int lf = i + 12;
                if (lf + 92 > data.Length) return -1;
                int height = I32(data, i + 8);
                if (height != I32(data, lf) || height == 0 || Math.Abs(height) > 20000) continue;
                int escapement = I32(data, lf + 8), weight = I32(data, lf + 16);
                if (Math.Abs(escapement) > 3600 || weight is < 0 or > 1000) continue;
                return lf;
            }
            return -1;
        }

        private BtwFont ReadFont(int lf)
        {
            string face = Encoding.Unicode.GetString(data, lf + 28, 64);
            int zero = face.IndexOf('\0');
            if (zero >= 0) face = face[..zero];
            return new BtwFont(I32(data, lf), I32(data, lf + 8), I32(data, lf + 16), data[lf + 20] != 0, data[lf + 21] != 0, face.Trim());
        }

        private BtwText? ReadText(Record r, string name, BtwPlacement placement, int guid)
        {
            int from = guid > 0 ? guid : r.Payload;
            if (!TryReadValue(from, r.End, out string content, out int next))
            {
                _notes.Add(L10n.T($"\"{name}\" yazısının içeriği okunamadı; atlandı."));
                return null;
            }
            int lf = FindLogFont(next, r.End);
            BtwFont? font = null;
            int? fixedWidth = null;
            int alignment = 0, widthScale = 1000;
            BtwBorder? border = null;
            if (lf > 0)
            {
                font = ReadFont(lf);
                int mode = lf - 130;
                if (mode - 8 >= 0 && data.AsSpan(mode - 8, 8).IndexOfAnyExcept((byte)0xFF) < 0)
                {
                    int sizing = I32(data, mode), align = I32(data, mode + 4), width = I32(data, mode + 12);
                    if (sizing == 1 && width is > 0 and < 100000) fixedWidth = width;
                    if (align is >= 0 and <= 2) alignment = align;
                }
                int scale = I32(data, lf + 101);
                if (scale is >= 100 and <= 5000) widthScale = scale;
                border = ReadBorder(lf + 92, r.End);
            }
            else
            {
                _notes.Add(L10n.T($"\"{name}\" yazısının yazı tipi okunamadı; Arial varsayıldı."));
            }
            return new BtwText(name, placement, content, font, fixedWidth, alignment, widthScale, border);
        }

        /// <summary>Yazının iç kutu nesnesi: açık mı (i32), kalınlık (i32), …, köşe biçimi ve yarıçapı.</summary>
        private BtwBorder? ReadBorder(int from, int end)
        {
            int guid = IndexOf(data, ObjectGuid, from, end);
            if (guid < 0) return null;
            int s = guid + ObjectGuid.Length;
            if (s + 11 > data.Length || data[s] != 0xFF || data[s + 1] != 0xFF || data[s + 2] != 0xFF) return null;
            if (I32(data, s + 3) != 1) return null;
            int thickness = I32(data, s + 7);
            if (thickness is <= 0 or > 2000) return null;
            var (corner, radius) = ReadCorner(s, Math.Min(end, s + 240));
            return new BtwBorder(thickness, corner, radius);
        }

        private (int Corner, int Radius) ReadCorner(int from, int end)
        {
            for (int i = from; i + 15 <= end; i++)
            {
                if (data[i] != 1 || data[i + 1] != 1 || data[i + 2] > 3 || data[i + 3] != 0 || data[i + 4] != 0 || data[i + 5] != 0 || data[i + 6] != 1)
                    continue;
                int rx = I32(data, i + 7), ry = I32(data, i + 11);
                if (rx is < 0 or > 20000 || ry is < 0 or > 20000) continue;
                return (data[i + 2], Math.Min(rx, ry));
            }
            return (0, 0);
        }

        private BtwBarcode? ReadBarcode(Record r, string name, BtwPlacement placement, int guid, string className)
        {
            int from = guid > 0 ? guid : r.Payload;
            if (!TryReadValue(from, r.End, out string value, out int next))
            {
                _notes.Add(L10n.T($"\"{name}\" barkodunun verisi okunamadı; atlandı."));
                return null;
            }
            int barsWidth = 0, barHeight = 0;
            int p = next;
            if (p < data.Length && data[p] == 1 && TryReadString(data, p + 1, out _, out int afterCopy)) p = afterCopy;
            ReadOnlySpan<byte> ff8 = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
            int block = IndexOf(data, ff8, p, Math.Min(r.End, p + 24));
            if (block >= 0)
            {
                int w = I32(data, block + 8), h = I32(data, block + 14);
                if (w is > 0 and < 60000) barsWidth = w;
                if (h is > 0 and < 60000) barHeight = h;
            }
            int lf = FindLogFont(block >= 0 ? block : p, r.End);
            return new BtwBarcode(name, placement, className, value, barsWidth, barHeight, lf > 0 ? ReadFont(lf) : null);
        }

        private static readonly string[] ImageExtensions = ["png", "jpg", "jpeg", "bmp", "gif", "tif", "tiff"];

        private BtwPicture? ReadPicture(Record r, string name, BtwPlacement placement)
        {
            var image = FindImage(r.Payload, r.End);
            if (image is null)
            {
                _notes.Add(L10n.T($"\"{name}\" görseli dosyanın içinde bulunamadı; atlandı."));
                return null;
            }
            int width = 0, height = 0;
            string? originalPath = null;
            // Özgün dosya yolu + uzantı dizesinden sonra: … yükseklik (+20), genişlik (+24) — mil cinsinden görünen ölçü.
            string? lastPath = null;
            for (int q = IndexOf(data, Marker, r.Payload, image.Value.Offset); q >= 0; q = IndexOf(data, Marker, q + 1, image.Value.Offset))
            {
                if (!TryReadString(data, q, out string text, out int next)) continue;
                if (text.Length > 3 && (text.Contains('\\') || text.Contains('/')))
                {
                    lastPath = text;
                    continue;
                }
                if (lastPath is null || !ImageExtensions.Contains(text, StringComparer.OrdinalIgnoreCase)
                    || !lastPath.EndsWith("." + text, StringComparison.OrdinalIgnoreCase)) continue;
                int p = next;
                while (TryReadString(data, p, out string empty, out int afterEmpty) && empty.Length == 0) p = afterEmpty;
                int h = I32(data, p + 20), w = I32(data, p + 24);
                if (w is > 0 and < 100000 && h is > 0 and < 100000)
                {
                    width = w;
                    height = h;
                    originalPath = lastPath;
                    break;
                }
            }
            if (width == 0)
            {
                _notes.Add(L10n.T($"\"{name}\" görselinin etiket üzerindeki ölçüsü okunamadı; özgün ölçüsü kullanıldı."));
            }
            return new BtwPicture(name, placement, width, height, image.Value.Data, image.Value.Extension, originalPath);
        }

        /// <summary>Uzunluk (u32) + tanınan görsel imzası (PNG, JPEG, GIF, BMP).</summary>
        private (int Offset, byte[] Data, string Extension)? FindImage(int from, int end)
        {
            for (int i = from; i + 8 < end; i++)
            {
                string? ext = data[i + 4] switch
                {
                    0x89 when data[i + 5] == (byte)'P' && data[i + 6] == (byte)'N' && data[i + 7] == (byte)'G' => ".png",
                    0xFF when data[i + 5] == 0xD8 && data[i + 6] == 0xFF => ".jpg",
                    (byte)'G' when data[i + 5] == (byte)'I' && data[i + 6] == (byte)'F' && data[i + 7] == (byte)'8' => ".gif",
                    (byte)'B' when data[i + 5] == (byte)'M' => ".bmp",
                    _ => null,
                };
                if (ext is null) continue;
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i));
                if (length < 32 || length > (uint)(data.Length - i - 4)) continue;
                if (ext == ".bmp" && BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i + 6)) != length) continue;
                return (i + 4, data.AsSpan(i + 4, (int)length).ToArray(), ext);
            }
            return null;
        }

        /// <summary>Kutu: kimlikten sonra FF FF FF, yükseklik, genişlik (i32), bir bayt, kalınlık (i32); köşe biçimi.</summary>
        private BtwBox? ReadBox(string name, BtwPlacement placement, int guid)
        {
            if (guid < 0) return null;
            int s = guid + ObjectGuid.Length;
            int height = I32(data, s + 3), width = I32(data, s + 7), thickness = I32(data, s + 12);
            if (width is <= 0 or > 100000 || height is <= 0 or > 100000) return null;
            if (thickness is < 0 or > 5000) thickness = 10;
            var (corner, radius) = ReadCorner(s, Math.Min(data.Length, s + 240));
            return new BtwBox(name, placement, width, height, thickness, corner, radius);
        }
    }
}
