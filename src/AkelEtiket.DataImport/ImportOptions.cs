using System.Text;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.DataImport;

/// <summary>Veri aktarım seçenekleri. Boş bırakılan değerler otomatik algılanır.</summary>
public sealed class ImportOptions
{
    public const int DefaultMaxRows = 100_000;

    /// <summary>Excel sayfa adı; null ise veri içeren ilk (görünür) sayfa.</summary>
    public string? SheetName { get; set; }

    /// <summary>
    /// Başlık satırı (1'den başlar; Excel satır numarası / CSV fiziksel satır numarası).
    /// null ise ilk dolu satır başlık kabul edilir.
    /// </summary>
    public int? HeaderRow { get; set; }

    /// <summary>CSV ayırıcısı; null ise otomatik (virgül, noktalı virgül, sekme, dikey çizgi).</summary>
    public char? Delimiter { get; set; }

    /// <summary>CSV kodlaması; null ise otomatik (BOM → UTF-8 → Windows-1254).</summary>
    public Encoding? Encoding { get; set; }

    /// <summary>Değerlerin başındaki/sonundaki boşlukları kırp.</summary>
    public bool TrimValues { get; set; } = true;

    /// <summary>En fazla okunacak veri satırı (bellek koruması).</summary>
    public int MaxRows { get; set; } = DefaultMaxRows;
}

/// <summary>CSV kodlaması seçeneği (aktarım penceresindeki liste için).</summary>
public sealed record EncodingChoice(string DisplayName, Encoding? Encoding)
{
    public override string ToString() => DisplayName;
}

/// <summary>CSV ayırıcı seçeneği (aktarım penceresindeki liste için).</summary>
public sealed record DelimiterChoice(string DisplayName, char? Delimiter)
{
    public override string ToString() => DisplayName;
}

/// <summary>Arayüzde gösterilecek hazır kodlama ve ayırıcı seçenekleri.</summary>
public static class ImportChoices
{
    /// <summary>Resolve stable web names and display names written by older versions, independently of UI language.</summary>
    public static Encoding? ResolveEncoding(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string stable=name.Split('(')[0].Trim().ToUpperInvariant();
        int code=stable switch { "UTF-8"=>65001,"UTF-16 LE"=>1200,"UTF-16 BE"=>1201,"UTF-32 LE"=>12000,"UTF-32 BE"=>12001,"WINDOWS-1254"=>1254,"ISO-8859-9"=>28599,"DOS-857"=>857,_=>0 };
        try { return code==0 ? Encoding.GetEncoding(name) : Encoding.GetEncoding(code); }
        catch (ArgumentException) { return null; }
    }
    /// <summary>İlk öğe "Otomatik" (Encoding = null).</summary>
    public static IReadOnlyList<EncodingChoice> Encodings { get; } = CreateEncodings();

    // Not: statik özellik başlatıcıları statik kurucudan ÖNCE çalışır; bu yüzden kod sayfası
    // sağlayıcısı (Windows-1254, ISO-8859-9) listeyi oluşturan metodun içinde kaydedilir.
    private static IReadOnlyList<EncodingChoice> CreateEncodings()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return
        [
            new(L10n.T("Otomatik algıla"), null),
            new("UTF-8", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)),
            new(L10n.T("Windows-1254 (Türkçe)"), Encoding.GetEncoding(1254)),
            new(L10n.T("ISO-8859-9 (Türkçe)"), Encoding.GetEncoding(28599)),
            new("UTF-16 LE", Encoding.Unicode),
            new("UTF-16 BE", Encoding.BigEndianUnicode),
        ];
    }

    /// <summary>İlk öğe "Otomatik" (Delimiter = null).</summary>
    public static IReadOnlyList<DelimiterChoice> Delimiters { get; } =
    [
        new(L10n.T("Otomatik algıla"), null),
        new(L10n.T("Virgül ( , )"), ','),
        new(L10n.T("Noktalı virgül ( ; )"), ';'),
        new(L10n.T("Sekme"), '\t'),
        new(L10n.T("Dikey çizgi ( | )"), '|'),
    ];

    /// <summary>Ayırıcının Türkçe adı ("Noktalı virgül ( ; )" gibi).</summary>
    public static string DescribeDelimiter(char delimiter) =>
        Delimiters.FirstOrDefault(d => d.Delimiter == delimiter)?.DisplayName ?? $"\"{delimiter}\"";
}
