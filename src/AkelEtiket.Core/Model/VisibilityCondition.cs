namespace AkelEtiket.Core.Model;

/// <summary>Koşullu gösterme işleci.</summary>
public enum ConditionOperator
{
    /// <summary>Alan dolu (boşluktan başka bir şey içeriyor).</summary>
    NotEmpty,

    /// <summary>Alan boş.</summary>
    Empty,

    /// <summary>Alan değeri verilen değere eşit (Türkçe büyük/küçük harf duyarsız; baştaki/sondaki boşluklar yok sayılır).</summary>
    IsEqual,

    /// <summary>Alan değeri verilen değere eşit değil.</summary>
    NotEqual,

    /// <summary>Alan değeri verilen metni içeriyor (Türkçe büyük/küçük harf duyarsız).</summary>
    Contains,
}

/// <summary>
/// Öğenin yalnızca belirli bir veri koşulunda basılması (v2). Örnek: "İNDİRİM" yazısı yalnızca {Indirim} doluysa.
/// Kayıt değişmez (immutable): değiştirmek için yeni bir koşul atanır, böylece geri al ve yeniden çizim çalışır.
/// v1 bu alanı tanımaz; öğe v1'de her zaman görünür.
/// </summary>
/// <param name="Field">Veri sütunu ya da yerleşik alan adı (süslü parantezsiz, ör. "Indirim").</param>
/// <param name="Operator">İşleç.</param>
/// <param name="Value">Eşittir/eşit değildir/içerir için karşılaştırılacak değer.</param>
public sealed record VisibilityCondition(string Field, ConditionOperator Operator = ConditionOperator.NotEmpty, string? Value = null)
{
    /// <summary>Alan adını sadeleştirir (boşluk ve süslü parantez atılır).</summary>
    public static string CleanField(string? field) => (field ?? "").Trim().Trim('{', '}').Trim();

    /// <summary>Kullanıcıya gösterilecek Türkçe açıklama: "{Indirim} doluysa".</summary>
    public string Describe()
    {
        string field = "{" + CleanField(Field) + "}";
        string value = Value ?? "";
        return Operator switch
        {
            ConditionOperator.Empty => $"{field} boşsa",
            ConditionOperator.IsEqual => $"{field} = \"{value}\" ise",
            ConditionOperator.NotEqual => $"{field} ≠ \"{value}\" ise",
            ConditionOperator.Contains => $"{field} \"{value}\" içeriyorsa",
            _ => $"{field} doluysa",
        };
    }
}
