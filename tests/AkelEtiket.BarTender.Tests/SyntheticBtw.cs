using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace AkelEtiket.BarTender.Tests;

/// <summary>
/// Testler için BarTender biçiminin okuyucunun kullandığı kısmını taklit eden küçük dosyalar üretir.
/// Gerçek müşteri dosyaları depoya konmaz; yapı, gerçek 10.1/2016 dosyalarındaki kayıt düzeninin aynısıdır.
/// </summary>
internal sealed class SyntheticBtw
{
    private readonly MemoryStream _doc = new();
    private readonly List<string> _defined = [];

    public int LabelWidth { get; init; } = 787;
    public int LabelHeight { get; init; } = 1575;
    public int Columns { get; init; } = 1;
    public int? Dpi { get; init; } = 300;

    public SyntheticBtw()
    {
        String("BarTender");
        String("idbtlf");
    }

    // ───────────── Düşük düzey yazıcılar ─────────────

    private void Bytes(params byte[] b) => _doc.Write(b);

    private void I32(int v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(b, v);
        _doc.Write(b);
    }

    private void U16(int v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(b, (ushort)v);
        _doc.Write(b);
    }

    private void Zeros(int n) => _doc.Write(new byte[n]);

    private void String(string s)
    {
        Bytes(0xFF, 0xFE, 0xFF);
        if (s.Length < 0xFF) Bytes((byte)s.Length);
        else { Bytes(0xFF); U16(s.Length); }
        _doc.Write(Encoding.Unicode.GetBytes(s));
    }

    /// <summary>Yeni sınıf ya da aynı sınıfın tekrarı (0x8000 | sıra etiketi).</summary>
    private void ClassTag(string name)
    {
        int index = _defined.IndexOf(name);
        if (index >= 0)
        {
            U16(0x8000 | (index + 3));
            return;
        }
        _defined.Add(name);
        Bytes(0xFF, 0xFF);
        U16(1);
        U16(name.Length);
        _doc.Write(Encoding.ASCII.GetBytes(name));
    }

    /// <summary>Nesne başı: X, Y, 8 sıfır bayt, bayrak, ilk dize; ardından yer bloğu, ad ve sabit kimlik.</summary>
    private void ObjectHead(string cls, int x, int y, string name, int anchor, double rotation, string first = "DataSource")
    {
        ClassTag(cls);
        I32(x);
        I32(y);
        Zeros(8);
        Bytes(1, 0, 0, 0);
        String(first);
        String("");
        Bytes(1);
        String("");
        Zeros(4);
        String("");
        Bytes(0, 1);
        I32(anchor);
        I32((int)Math.Round(rotation * 1000));
        Zeros(14);
        String(name);
        _doc.Write(BtwReader.ObjectGuid);
        Bytes(0xFF, 0xFF, 0xFF);
    }

    private void Value(string content)
    {
        Zeros(16);
        String("(???) ???-????");
        String("");
        I32(-1);
        String(content);
    }

    /// <summary>Yazı tipi bloğu: 8 × FF, kip (sabit genişlik), hizalama, 0, genişlik, …, LOGFONT, karakter genişliği.</summary>
    private void Font(int heightTwips, int escapement, bool bold, string face, int? fixedWidth, int alignment, int widthScale)
    {
        // LOGFONT'tan 138 bayt önce 8 × FF; kip alanı LOGFONT − 130'da.
        Bytes(0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF);
        I32(fixedWidth is null ? 0 : 1);
        I32(alignment);
        I32(0);
        I32(fixedWidth ?? 2000);
        Zeros(130 - 16 - 12);
        Bytes(0xF4, 0x01, 0, 0, 0, 0, 0, 0);
        I32(-heightTwips);
        // LOGFONTW
        I32(-heightTwips);
        I32(0);
        I32(escapement);
        I32(escapement);
        I32(bold ? 700 : 400);
        Bytes(0, 0, 0, 162, 3, 2, 1, 0x22);
        var faceBytes = new byte[64];
        Encoding.Unicode.GetBytes(face).CopyTo(faceBytes, 0);
        _doc.Write(faceBytes);
        Zeros(9);
        I32(widthScale);
        Zeros(20);
    }

    private void Corner(int style, int radius)
    {
        Bytes(1, 1, (byte)style, 0, 0, 0, 1);
        I32(radius);
        I32(radius);
        Zeros(8);
    }

    // ───────────── Nesneler ─────────────

    public SyntheticBtw Text(string name, int x, int y, int anchor, double rotation, string content, double points = 10,
        bool bold = false, string face = "Arial", int? fixedWidth = null, int alignment = 0, int widthScale = 1000,
        (int Thickness, int Corner, int Radius)? border = null)
    {
        ObjectHead("TextData", x, y, name, anchor, rotation);
        Bytes(0);
        String("");
        Value(content);
        Bytes(1);
        String("");
        Bytes(0xFF, 0xFF);
        Font((int)Math.Round(points * 20), (int)(rotation * 10), bold, face, fixedWidth, alignment, widthScale);
        // Yazının iç kutusu (çerçeve)
        String("Box Options");
        Zeros(24);
        String("Kutu 1");
        _doc.Write(BtwReader.ObjectGuid);
        Bytes(0xFF, 0xFF, 0xFF);
        if (border is { } b)
        {
            I32(1);
            I32(b.Thickness);
            Zeros(40);
            Corner(b.Corner, b.Radius);
        }
        else
        {
            I32(0);
            I32(-1);
            Zeros(40);
        }
        return this;
    }

    public SyntheticBtw Ean13(string name, int x, int y, double rotation, string data, int barsWidth, int barHeight,
        double textPoints = 8, string textFace = "Arial")
    {
        ObjectHead("BcEAN13Data", x, y, name, 0, rotation, "Root.MasterSelectedObject.DataSourceGeneral.DataSource");
        Bytes(0);
        String("Data Source");
        Value(data);
        Bytes(1);
        String(data);
        Bytes(0xFF, 0xFF);
        String("");
        Bytes(0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF);
        I32(barsWidth);
        Zeros(2);
        I32(barHeight);
        Zeros(30);
        // Okunabilir metin alt nesnesi ve yazı tipi
        String("Text 1");
        Value("Örnek Yazı");
        Font((int)Math.Round(textPoints * 20), (int)(rotation * 10), false, textFace, null, 0, 1000);
        return this;
    }

    public SyntheticBtw Picture(string name, int x, int y, int anchor, double rotation, int width, int height, byte[] png)
    {
        ObjectHead("PictureData", x, y, name, anchor, rotation, "Picture");
        Bytes(0);
        String("");
        Value("Örnek Yazı");
        Zeros(10);
        String(@"C:\Kullanıcı\Masaüstü\logo.png");
        String("png");
        String("");
        String("");
        String("");
        Bytes(2, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0);
        I32(height);
        I32(width);
        Zeros(16);
        ClassTag("ImageHandle");
        ClassTag("ImageHandleRef");
        String(@"C:\Temp\BTW{0}.TMP");
        String("png");
        I32(png.Length);
        _doc.Write(png);
        return this;
    }

    public SyntheticBtw Box(string name, int x, int y, int width, int height, int thickness, int corner = 0, int radius = 0)
    {
        ObjectHead("BoxData", x, y, name, 0, 0, "Box Options");
        I32(height);
        I32(width);
        Bytes(0);
        I32(thickness);
        Zeros(40);
        Corner(corner, radius);
        return this;
    }

    public SyntheticBtw Line(string name, int x, int y)
    {
        ObjectHead("LineData", x, y, name, 4, 0, "Root.MasterSelectedObject.Line Options");
        I32(1000);
        I32(x - 500);
        I32(y);
        I32(x + 500);
        I32(y);
        I32(500);
        I32(7);
        return this;
    }

    // ───────────── Dosya ─────────────

    public byte[] Build()
    {
        using var body = new MemoryStream();
        // Sayfa kaydı ve yazıcı ayarı, nesnelerden önce
        var head = new MemoryStream();
        void W(byte[] b) => head.Write(b);
        W([0xFF, 0xFF, 0x00, 0x01, 0x00, 0x00]);
        foreach (int v in new[] { 2 * 79 + LabelWidth * Columns, LabelHeight, 0, 0, 0, 79, 79, LabelWidth, LabelHeight, 1, Columns })
            W(BitConverter.GetBytes(v));
        if (Dpi is { } dpi)
        {
            var devmode = new byte[156];
            Encoding.Unicode.GetBytes("Test Printer").CopyTo(devmode, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(devmode.AsSpan(68), 156);
            BinaryPrimitives.WriteInt16LittleEndian(devmode.AsSpan(90), (short)dpi);
            BinaryPrimitives.WriteInt16LittleEndian(devmode.AsSpan(96), (short)dpi);
            W(devmode);
        }
        byte[] doc = _doc.ToArray();
        // "BarTender" + "idbtlf" dizelerinden sonra sayfa kaydını araya koy
        int split = 4 + 18 + 4 + 12;
        byte[] full = [.. doc.AsSpan(0, split), .. head.ToArray(), .. doc.AsSpan(split), .. Trailer()];

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) z.Write(full);

        string header = "\r\nBar Tender Format File\r\n(c) 1992 - 2026 Seagull Scientific, Inc.\r\n" + new string('-', 46) + "\r\n" +
                        "Application: Version=2016 R7; Build=3146; Edition=UltraLite\r\n" +
                        "Document: CompatibleVersion=2016 R1; ArchiveVersion=88734\r\n" +
                        "Printer: Name=Test Printer; Model=Test Printer; Port=USB001\r\n" +
                        "<Metadata><Title>Deneme</Title></Metadata>\r\n" + new string('-', 46) + "\r\n";
        body.Write(Encoding.UTF8.GetBytes(header));
        body.Write([0x1A, 0x9E, 0x5A, 0x01, 0x00]);
        body.Write(compressed.ToArray());
        return body.ToArray();
    }

    private static byte[] Trailer()
    {
        var t = new MemoryStream();
        t.Write([0xFF, 0xFF, 0x01, 0x00, 0x0E, 0x00]);
        t.Write(Encoding.ASCII.GetBytes("BackgroundData"));
        t.Write(new byte[32]);
        return t.ToArray();
    }

    /// <summary>Küçük, geçerli bir PNG (2 × 1 piksel siyah).</summary>
    public static byte[] TinyPng()
    {
        using var bitmap = new SkiaSharp.SKBitmap(2, 1);
        bitmap.Erase(SkiaSharp.SKColors.Black);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
