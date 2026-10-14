namespace AkelEtiket.Design.Workspace;

/// <summary>Tasarımın nerede durduğu.</summary>
public enum DesignKind
{
    /// <summary>İzin verilen bir klasördeki .akelabel dosyası.</summary>
    File,

    /// <summary>Masaüstü uygulamasında açık belge (paylaşım açıksa).</summary>
    Live,

    /// <summary>Var olan bir tasarımın yapay zekâ taslağı (onay için öneri olur).</summary>
    Draft,

    /// <summary>Yapay zekânın oluşturduğu yeni tasarım (yeni dosya olarak kaydedilir).</summary>
    New,
}

/// <summary>
/// Araçlarda kullanılan tasarım kimliği: <c>dosya:Şablonlar/Ürün.akelabel</c>, <c>acik:3</c>,
/// <c>taslak:a1b2c3d4</c>, <c>yeni:a1b2c3d4</c>.
/// </summary>
public readonly record struct DesignId(DesignKind Kind, string Value)
{
    public const string FilePrefix = "dosya:";
    public const string LivePrefix = "acik:";
    public const string DraftPrefix = "taslak:";
    public const string NewPrefix = "yeni:";

    public override string ToString() => Prefix(Kind) + Value;

    public static string Prefix(DesignKind kind) => kind switch
    {
        DesignKind.File => FilePrefix,
        DesignKind.Live => LivePrefix,
        DesignKind.Draft => DraftPrefix,
        _ => NewPrefix,
    };

    public static DesignId File(string rootAndRelativePath) => new(DesignKind.File, rootAndRelativePath);

    public static DesignId Live(int sessionNumber) => new(DesignKind.Live, sessionNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static bool TryParse(string? text, out DesignId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        foreach (var kind in new[] { DesignKind.File, DesignKind.Live, DesignKind.Draft, DesignKind.New })
        {
            string prefix = Prefix(kind);
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && text.Length > prefix.Length)
            {
                string value = text[prefix.Length..].Trim();
                if (value.Length == 0) return false;
                if (kind is DesignKind.Draft or DesignKind.New && !IsShortId(value)) return false;
                if (kind == DesignKind.Live && !int.TryParse(value, out _)) return false;
                id = new DesignId(kind, kind == DesignKind.File ? value.Replace('\\', '/') : value.ToLowerInvariant());
                return true;
            }
        }
        return false;
    }

    public static DesignId Parse(string? text) =>
        TryParse(text, out var id)
            ? id
            : throw new ToolException(ToolErrorCodes.InvalidArgument,
                $"Geçersiz tasarım kimliği: \"{text}\". Kimlikler list_designs çıktısındaki gibi olmalı " +
                "(ör. dosya:Şablonlar/Ürün.akelabel, acik:1, taslak:a1b2c3d4, yeni:a1b2c3d4).");

    private static bool IsShortId(string value) => value.Length is >= 4 and <= 32 && value.All(char.IsAsciiLetterOrDigit);

    /// <summary>Yeni kısa kimlik (8 onaltılık karakter).</summary>
    public static string NewShortId() => Guid.NewGuid().ToString("N")[..8];
}
