using AkelEtiket.Core.Assets;
using AkelEtiket.Core.Data;
using AkelEtiket.Rendering;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.Production.Quality;

public sealed partial class PreflightService
{
    private static IFieldValueSource CacheFields(IFieldValueSource source, int maxLength, List<CachedFields> caches)
    {
        // RegionalFieldSource intentionally recognizes the original built-in DateTime and walks
        // composite leaves. Wrapping those types would reinterpret Turkish built-in date strings
        // using a foreign input culture, changing the production result during inspection.
        if (source is BuiltInFieldSource builtIn) return new BuiltInFieldSource(builtIn.Sequence, builtIn.Now);
        if (source is CompositeFieldSource composite)
            return new CompositeFieldSource(composite.Sources.Select(s => CacheFields(s, maxLength, caches)).ToArray());
        var cached = new CachedFields(source, maxLength);
        caches.Add(cached);
        return cached;
    }

    // The renderer and producer must observe the same values even if a third-party source is lazy.
    // These caches do not enumerate fields, rewrite identifiers or call any number-allocation API.
    private sealed class CachedFields(IFieldValueSource source, int maxLength) : IFieldValueSource, IDateFieldSource
    {
        private readonly Dictionary<(string Name, string? Format), (bool Found, string Value)> _values = [];
        private readonly Dictionary<string, (bool Parsed, DateTime Value, bool Exists)> _dates = new(StringComparer.Ordinal);
        private long _characters;
        public bool LimitReached { get; private set; }

        public bool TryGetValue(string name, string? format, out string value)
        {
            if (!_values.TryGetValue((name, format), out var cached))
            {
                bool found = source.TryGetValue(name, format, out string raw);
                raw ??= "";
                _characters += raw.Length;
                if (raw.Length > maxLength || _characters > maxLength * 10L)
                {
                    LimitReached = true;
                    throw new LimitException("field_value_limit", L10n.T("Bir veri alanı metin bellek sınırını aşıyor."));
                }
                cached = (found, raw);
                _values[(name, format)] = cached;
            }
            value = cached.Value;
            return cached.Found;
        }

        public bool TryGetDate(string name, out DateTime date, out bool exists)
        {
            if (!_dates.TryGetValue(name, out var cached))
            {
                bool parsed;
                if (source is IDateFieldSource dates)
                    parsed = dates.TryGetDate(name, out date, out exists);
                else
                {
                    exists = TryGetValue(name, null, out string raw);
                    date = default;
                    parsed = exists && FieldDates.TryParse(raw, out date);
                }
                cached = (parsed, date, exists);
                _dates[name] = cached;
            }
            date = cached.Value;
            exists = cached.Exists;
            return cached.Parsed;
        }
    }

    private sealed class CachedImages(IImageSourceResolver? source, long maxBytes) : IImageSourceResolver
    {
        private readonly Dictionary<string, (ImageAsset? Asset, string? Error)> _images = new(StringComparer.Ordinal);
        private long _bytes;
        public bool LimitReached { get; private set; }

        public ImageAsset? Resolve(string value, out string? error)
        {
            if (!_images.TryGetValue(value, out var cached))
            {
                string? message = null;
                ImageAsset? asset = source?.Resolve(value, out message);
                if (source is null) message = L10n.T($"\"{value}\" görseli için görsel klasörü tanımlı değil.");
                if (asset is not null)
                {
                    _bytes = checked(_bytes + asset.Size);
                    if (_bytes > maxBytes)
                    {
                        LimitReached = true;
                        throw new LimitException("row_image_limit", L10n.T("Satır görselleri bellek sınırını aşıyor."));
                    }
                    asset = new ImageAsset(asset.Id, asset.FileName, asset.Data.ToArray());
                }
                cached = (asset, message);
                _images[value] = cached;
            }
            error = cached.Error;
            return cached.Asset;
        }
    }
}
