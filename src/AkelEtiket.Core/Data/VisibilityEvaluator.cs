using System.Globalization;
using AkelEtiket.Core.Model;

namespace AkelEtiket.Core.Data;

/// <summary>Koşullu göstermenin sonucu.</summary>
public enum ConditionResult
{
    /// <summary>Koşul sağlanıyor: öğe basılır.</summary>
    Show,

    /// <summary>Koşul sağlanmıyor: öğe bu veride basılmaz.</summary>
    Hide,

    /// <summary>Koşul alanı veride yok: değerlendirilemedi.</summary>
    FieldMissing,
}

/// <summary>
/// Öğe koşullarını (<see cref="VisibilityCondition"/>) alan değerleriyle değerlendirir. Karşılaştırmalar Türkçe
/// kültürle ve büyük/küçük harf duyarsızdır ("İSTANBUL" = "istanbul", "I" = "ı"); baştaki/sondaki boşluklar yok sayılır.
/// </summary>
public static class VisibilityEvaluator
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static ConditionResult Evaluate(VisibilityCondition? condition, IFieldValueSource? fields)
    {
        if (condition is null) return ConditionResult.Show;
        string field = VisibilityCondition.CleanField(condition.Field);
        if (field.Length == 0) return ConditionResult.Show;
        if (fields is null || !fields.TryGetValue(field, null, out string raw)) return ConditionResult.FieldMissing;
        return IsSatisfied(condition.Operator, raw, condition.Value) ? ConditionResult.Show : ConditionResult.Hide;
    }

    /// <summary>İşleci verilen değere uygular.</summary>
    public static bool IsSatisfied(ConditionOperator op, string? actual, string? expected)
    {
        string value = (actual ?? "").Trim();
        string target = (expected ?? "").Trim();
        return op switch
        {
            ConditionOperator.Empty => value.Length == 0,
            ConditionOperator.IsEqual => Same(value, target),
            ConditionOperator.NotEqual => !Same(value, target),
            ConditionOperator.Contains => target.Length == 0 || Turkish.CompareInfo.IndexOf(value, target, CompareOptions.IgnoreCase) >= 0,
            _ => value.Length > 0,
        };
    }

    private static bool Same(string a, string b) => string.Compare(a, b, Turkish, CompareOptions.IgnoreCase) == 0;
}
