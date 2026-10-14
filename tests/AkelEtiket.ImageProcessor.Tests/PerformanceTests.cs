using System.Diagnostics;
using AkelEtiket.Core.Model;
using Xunit.Abstractions;

namespace AkelEtiket.ImageProcessor.Tests;

/// <summary>
/// 100 × 100 mm etiket 203 DPI'da 800 × 800 noktadır. Sınırlar Debug derlemesinde de kararlı
/// geçecek kadar geniştir; gerçek süreler test çıktısına yazılır.
/// </summary>
public class PerformanceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(ImageConversionMethod.FloydSteinberg)]
    [InlineData(ImageConversionMethod.Photo)]
    [InlineData(ImageConversionMethod.Ordered)]
    [InlineData(ImageConversionMethod.Logo)]
    [InlineData(ImageConversionMethod.Auto)]
    public void Convert_800x800_IsFast(ImageConversionMethod method)
    {
        GrayImage gray = TestImages.PhotoLike(800, 800, seed: 4);
        var settings = new ImageConversionSettings(method);
        MonoConverter.Convert(gray, settings); // ısınma (JIT)

        const int runs = 5;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < runs; i++) MonoConverter.Convert(gray, settings);
        sw.Stop();
        double average = sw.Elapsed.TotalMilliseconds / runs;
        output.WriteLine($"{method}: ortalama {average:0.0} ms (800×800)");
        Assert.True(average < 400, $"{method} çok yavaş: {average:0.0} ms");
    }
}
