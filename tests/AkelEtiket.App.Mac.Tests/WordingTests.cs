using AkelEtiket.App.Mac.Localization;

namespace AkelEtiket.App.Mac.Tests;

/// <summary>Ortak katalogdaki Windows'a özgü ifadelerin macOS uyarlaması: yalnızca bilinen ifadeler değişir.</summary>
public class WordingTests
{
    [Theory]
    [InlineData("2) Windows yazdırma kuyruğuna gönderildi.", "2) macOS yazdırma kuyruğuna gönderildi.")]
    [InlineData("İlerlemeyi Windows yazıcı kuyruğundan izleyebilirsiniz.", "İlerlemeyi macOS yazıcı kuyruğundan izleyebilirsiniz.")]
    [InlineData("The job left the Windows print queue.", "The job left the macOS print queue.")]
    [InlineData("Ctrl+tekerlek: yakınlaştır • Ctrl+ok: 1 nokta", "⌘tekerlek: yakınlaştır • ⌘ok: 1 nokta")]
    [InlineData("Ctrl+Shift+D", "⌘Shift+D")]
    public void KnownPhrases_AreAdapted(string source, string expected) => Assert.Equal(expected, MacWording.Adapt(source));

    [Theory]
    [InlineData("Türkçe (Windows-1254)")]          // kodlama adı
    [InlineData("Ctrl+Tab")]                        // sekme geçişi macOS'ta da Ctrl+Tab
    [InlineData("Ctrl+Shift+Tab")]
    [InlineData("Örnek Ürün A")]
    [InlineData("")]
    public void EverythingElse_IsLeftAlone(string source) => Assert.Equal(source, MacWording.Adapt(source));
}
