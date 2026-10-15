using System;
using System.Windows.Markup;
using AkelEtiket.Core.Localization;
namespace AkelEtiket.App.Localization;

[MarkupExtensionReturnType(typeof(string))]
public sealed class TextExtension : MarkupExtension
{
    public TextExtension(string key) => Key = key;
    public string Key { get; }
    public override object ProvideValue(IServiceProvider serviceProvider) => UiText.Get(Key);
}
