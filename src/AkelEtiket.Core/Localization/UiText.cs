using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace AkelEtiket.Core.Localization;

/// <summary>Explicit UI strings only. Never applied to customer data, identifiers, document contents or file paths.</summary>
public static class UiText
{
    private static readonly ResourceManager Resources = new("AkelEtiket.Core.Localization.UiStrings", typeof(UiText).Assembly);
    public static string Language => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en";
    public static CultureInfo ResourceCulture => CultureInfo.GetCultureInfo(Language);
    public static string Key(string source) => "T_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..12];
    public static string Get(string key) => Resources.GetString(key, ResourceCulture) ?? key;
    public static string T(string source) => Resources.GetString(Key(source), ResourceCulture) ?? source;
    public static string T(ref UiStringHandler message) => string.Format(CultureInfo.CurrentCulture, T(message.Pattern), message.Arguments);
    public static string ResolveLanguage(string? preference, CultureInfo systemCulture) => preference switch
    {
        "tr" => "tr", "en" => "en", _ => systemCulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en",
    };
    public static void ApplyLanguage(string? preference, CultureInfo systemCulture)
    {
        var culture = CultureInfo.GetCultureInfo(ResolveLanguage(preference, systemCulture));
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        // CurrentCulture stays at the user's regional setting. Data/label formatting remains explicit.
    }
}

[InterpolatedStringHandler]
public ref struct UiStringHandler
{
    private readonly StringBuilder _pattern;
    private readonly List<object?> _values;
    public UiStringHandler(int literalLength, int formattedCount)
    { _pattern = new(literalLength + formattedCount * 4); _values = new(formattedCount); }
    public void AppendLiteral(string value) => _pattern.Append(value.Replace("{", "{{").Replace("}", "}}"));
    public void AppendFormatted<T>(T value) => Add(value, 0, null);
    public void AppendFormatted<T>(T value, string? format) => Add(value, 0, format);
    public void AppendFormatted<T>(T value, int alignment) => Add(value, alignment, null);
    public void AppendFormatted<T>(T value, int alignment, string? format) => Add(value, alignment, format);
    private void Add<T>(T value, int alignment, string? format)
    {
        _pattern.Append('{').Append(_values.Count);
        if (alignment != 0) _pattern.Append(',').Append(alignment);
        if (format is not null) _pattern.Append(':').Append(format);
        _pattern.Append('}'); _values.Add(value);
    }
    public string Pattern => _pattern.ToString();
    public object?[] Arguments => _values.ToArray();
}
