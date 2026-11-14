using AkelEtiket.Core.Localization;
using Avalonia.Markup.Xaml;

namespace AkelEtiket.App.Mac.Localization;

/// <summary>Arayüz metni: <c>{loc:Text T_…}</c>. Anahtarlar Windows uygulamasıyla aynı katalogdan gelir.</summary>
public sealed class TextExtension(string key) : MarkupExtension
{
    public string Key { get; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => UiText.Get(Key);
}
