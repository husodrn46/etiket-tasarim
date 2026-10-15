using L10n = AkelEtiket.Core.Localization.UiText;
namespace AkelEtiket.DataImport.Internal;

/// <summary>
/// Dosya okuma yardımcıları. Dosyalar FileShare.ReadWrite | FileShare.Delete ile açılır;
/// böylece Excel'de açık olan bir dosya da okunabilir.
/// </summary>
internal static class FileAccessHelper
{
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;

    public static FileStream OpenRead(string path)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, bufferSize: 81920, FileOptions.SequentialScan);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new DataImportException(L10n.T($"Dosya bulunamadı: {path}"), ex) { FilePath = path };
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new DataImportException(
                L10n.T($"Dosyaya erişim izni yok: {path}. Dosyanın başka bir kullanıcıya ait olmadığını veya salt korumalı bir klasörde bulunmadığını kontrol edin."),
                ex) { FilePath = path };
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is ErrorSharingViolation or ErrorLockViolation)
        {
            throw new DataImportException(
                L10n.T("Dosya başka bir program tarafından kilitlenmiş, okunamıyor. Dosya Excel'de veya başka bir programda açıksa kapatıp yeniden deneyin."),
                ex) { FilePath = path };
        }
        catch (IOException ex)
        {
            throw new DataImportException(L10n.T($"Dosya okunamadı: {path}. ({ex.Message})"), ex) { FilePath = path };
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw new DataImportException(L10n.T($"Geçersiz dosya yolu: {path}"), ex) { FilePath = path };
        }
    }

    /// <summary>Dosyanın tamamını belleğe okur.</summary>
    public static byte[] ReadAllBytes(string path, long maxBytes)
    {
        using var stream = OpenRead(path);
        long length;
        try
        {
            length = stream.Length;
        }
        catch (NotSupportedException)
        {
            length = -1;
        }
        if (length > maxBytes)
        {
            throw new DataImportException(
                L10n.T($"Dosya çok büyük ({length / (1024 * 1024)} MB). En fazla {maxBytes / (1024 * 1024)} MB boyutunda dosyalar aktarılabilir."))
            { FilePath = path };
        }
        try
        {
            using var memory = length > 0 ? new MemoryStream((int)length) : new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
        catch (IOException ex)
        {
            throw new DataImportException(L10n.T($"Dosya okunurken hata oluştu: {path}. ({ex.Message})"), ex) { FilePath = path };
        }
    }
}
