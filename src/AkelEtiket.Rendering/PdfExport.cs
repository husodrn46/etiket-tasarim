using AkelEtiket.Core;
using AkelEtiket.Core.Imaging;
using SkiaSharp;

namespace AkelEtiket.Rendering;

/// <summary>PDF'e yazılacak tek sayfa: yazıcı çözünürlüğünde 1-bit etiket görüntüsü ve gerçek etiket ölçüsü.</summary>
public sealed record PdfLabelPage(MonoBitmap Bitmap, double WidthMm, double HeightMm);

/// <summary>
/// Etiketleri PDF olarak kaydeder. Her sayfa etiketin gerçek ölçüsündedir (1 mm = 72/25,4 nokta) ve içeriği ortak
/// render motorunun baskıyla birebir aynı 1-bit görüntüsüdür; görüntü sayfaya yumuşatmasız (en yakın komşu) yerleştirilir.
/// Ofis yazıcısında %100 ölçekle basıldığında etiket gerçek boyutunda çıkar.
/// </summary>
public static class PdfExport
{
    /// <summary>mm → PDF noktası (1/72 inç).</summary>
    public static float MmToPoints(double mm) => (float)(mm / Units.MmPerInch * 72.0);

    /// <summary>Sayfaları akışa PDF olarak yazar; yazılan sayfa sayısını döndürür.</summary>
    public static int Write(Stream output, IEnumerable<PdfLabelPage> pages, string title)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(pages);
        var metadata = new SKDocumentPdfMetadata
        {
            Title = title,
            Creator = AppIdentity.ProductName,
            Producer = AppIdentity.ProductName,
            Creation = DateTime.Now,
            Modified = DateTime.Now,
            // Skia sayfa ölçüsünü raster ızgarasına yuvarlar: 2540 DPI = 0,01 mm adım, etiket ölçüsü korunur. Etiket
            // görüntüsü bundan etkilenmez (PDF görüntüsü olarak, yeniden örneklenmeden gömülür).
            RasterDpi = 2540,
            EncodingQuality = 101,
        };
        int count = 0;
        using (var document = SKDocument.CreatePdf(output, metadata))
        {
            foreach (var page in pages)
            {
                float width = MmToPoints(page.WidthMm);
                float height = MmToPoints(page.HeightMm);
                using var canvas = document.BeginPage(width, height);
                using (var bitmap = ToGrayBitmap(page.Bitmap))
                using (var image = SKImage.FromBitmap(bitmap))
                {
                    var target = new SKRect(0, 0, width, height);
                    canvas.DrawImage(image, target, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
                }
                document.EndPage();
                count++;
            }
            if (count == 0) throw new InvalidOperationException("PDF'e yazılacak etiket yok.");
            document.Close();
        }
        return count;
    }

    /// <summary>Dosyaya atomik olarak yazar (geçici dosya + taşıma).</summary>
    public static int WriteFile(string path, IEnumerable<PdfLabelPage> pages, string title)
    {
        string full = Path.GetFullPath(path);
        string temp = full + ".tmp";
        try
        {
            int count;
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                count = Write(stream, pages, title);
            }
            File.Move(temp, full, overwrite: true);
            return count;
        }
        catch
        {
            // Yarım kalan geçici dosya bırakılmaz; var olan PDF'e dokunulmaz.
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            throw;
        }
    }

    private static SKBitmap ToGrayBitmap(MonoBitmap mono)
    {
        var bitmap = new SKBitmap(new SKImageInfo(mono.Width, mono.Height, SKColorType.Gray8, SKAlphaType.Opaque));
        byte[] gray = mono.ToGray8();
        nint pixels = bitmap.GetPixels();
        int rowBytes = bitmap.RowBytes;
        for (int y = 0; y < mono.Height; y++)
            System.Runtime.InteropServices.Marshal.Copy(gray, y * mono.Width, pixels + y * rowBytes, mono.Width);
        return bitmap;
    }
}
