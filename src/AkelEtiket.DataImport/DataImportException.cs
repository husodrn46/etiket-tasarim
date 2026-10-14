namespace AkelEtiket.DataImport;

/// <summary>
/// Dosya hiç okunamadığında fırlatılır. Mesaj her zaman kullanıcıya gösterilebilecek
/// açıklamalı Türkçe metindir; teknik ayrıntı <see cref="Exception.InnerException"/> içindedir.
/// </summary>
public sealed class DataImportException : Exception
{
    public DataImportException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    /// <summary>Sorunlu dosyanın yolu (biliniyorsa).</summary>
    public string? FilePath { get; init; }
}
