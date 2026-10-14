namespace AkelEtiket.ImageProcessor;

/// <summary>Görsel dosyası okunamadığında fırlatılır. Mesaj kullanıcıya gösterilecek Türkçe metindir.</summary>
public sealed class ImageLoadException : Exception
{
    public ImageLoadException(string message)
        : base(message)
    {
    }

    public ImageLoadException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
