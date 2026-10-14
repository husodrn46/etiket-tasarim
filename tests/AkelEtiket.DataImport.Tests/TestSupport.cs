using System.Text;
using ClosedXML.Excel;

namespace AkelEtiket.DataImport.Tests;

/// <summary>Test başına geçici klasör; Dispose ile silinir.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AkelEtiketTests", "DataImport", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal static class Fixtures
{
    static Fixtures() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static Encoding Windows1254 => Encoding.GetEncoding(1254);

    public static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);
    public static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Metni verilen kodlamayla (BOM dahil) dosyaya yazar.</summary>
    public static string WriteText(TempDirectory dir, string name, string content, Encoding encoding)
    {
        string path = dir.File(name);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(content)).ToArray();
        System.IO.File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>ClosedXML ile çalışma kitabı oluşturup kaydeder.</summary>
    public static string WriteWorkbook(TempDirectory dir, string name, Action<XLWorkbook> build)
    {
        string path = dir.File(name);
        using var workbook = new XLWorkbook();
        build(workbook);
        workbook.SaveAs(path);
        return path;
    }

    /// <summary>Depo kök klasörü (AkelEtiket.sln'nin bulunduğu klasör).</summary>
    public static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "AkelEtiket.sln"))) return dir.FullName;
        }
        throw new DirectoryNotFoundException("AkelEtiket.sln bulunamadı.");
    }

    public static string Ean13(string twelveDigits)
    {
        int sum = 0;
        for (int i = 0; i < 12; i++) sum += (twelveDigits[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return twelveDigits + (char)('0' + (10 - sum % 10) % 10);
    }
}
