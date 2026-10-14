using AkelEtiket.Core.Data;

namespace AkelEtiket.Core.Tests.Core;

/// <summary>Alan ifadeleri: tarih hesabı ve Türkçe yazı dönüşümleri; eski sözdizimiyle tam uyum.</summary>
public sealed class FieldExpressionTests
{
    private static readonly DateTime Today = new(2026, 10, 13, 9, 30, 0);
    private static readonly BuiltInFieldSource BuiltIns = new(7, Today);

    private static string Resolve(string text, IFieldValueSource? source = null) => FieldTemplate.Resolve(text, source ?? BuiltIns).Text;

    [Theory]
    [InlineData("{TARIH+30}", "12.11.2026")]
    [InlineData("{TARIH-7:dd.MM.yyyy}", "06.10.2026")]
    [InlineData("{TARIH+0}", "13.10.2026")]
    [InlineData("{TARIH+2 hafta}", "27.10.2026")]
    [InlineData("{TARIH+2h}", "27.10.2026")]
    [InlineData("{TARIH+6ay}", "13.04.2027")]
    [InlineData("{TARIH+6 AY:dd.MM.yy}", "13.04.27")]
    [InlineData("{TARIH+1yıl}", "13.10.2027")]
    [InlineData("{TARIH+1y:yyyy}", "2027")]
    [InlineData("{TARIH+10g}", "23.10.2026")]
    [InlineData("{tarih + 30}", "12.11.2026")]
    [InlineData("SKT: {TARIH+365:dd MMMM yyyy}", "SKT: 13 Ekim 2027")]
    public void DateArithmetic_OnBuiltInDate(string template, string expected) => Assert.Equal(expected, Resolve(template));

    [Theory]
    [InlineData("13.10.2026")]
    [InlineData("13.10.2026 08:15")]
    [InlineData("2026-10-13")]
    [InlineData("13/10/2026")]
    public void DateArithmetic_OnDateColumn(string cell)
    {
        var record = new RecordFieldSource(["Üretim Tarihi"], [cell]);
        Assert.Equal("13.10.2027", Resolve("{UretimTarihi+1yıl}", record));
        Assert.Equal("23.10.2026", Resolve("{Üretim Tarihi+10}", record));
    }

    [Fact]
    public void DateArithmetic_OnNonDateValue_IsReportedAsProblem_NotAsMissingColumn()
    {
        var record = new RecordFieldSource(["Kod"], ["TST001"]);
        var result = FieldTemplate.Resolve("Kod {Kod+30}", record);
        Assert.False(result.IsComplete);
        Assert.Empty(result.MissingFields);
        var problem = Assert.Single(result.Problems);
        Assert.Contains("{Kod+30}", problem);
        Assert.Contains("tarih değil", problem);
        Assert.Equal("Kod {Kod+30}", result.Text);
    }

    [Fact]
    public void DateArithmetic_OnMissingColumn_IsMissing()
    {
        var result = FieldTemplate.Resolve("{Yok+30}", new RecordFieldSource(["Kod"], ["1"]));
        Assert.Equal(["Yok+30"], result.MissingFields);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void ColumnNamesThatLookLikeArithmetic_StillResolveAsColumns()
    {
        var record = new RecordFieldSource(["Model-2", "Adet+1"], ["X200", "5"]);
        Assert.Equal("X200 / 5", Resolve("{Model-2} / {Adet+1}", record));
    }

    [Fact]
    public void CompositeSource_TakesDateFromFirstSourceThatHasTheField()
    {
        var composite = new CompositeFieldSource(new RecordFieldSource(["UrunAdi"], ["Kase"]), BuiltIns);
        Assert.Equal("Kase 12.11.2026", Resolve("{UrunAdi} {TARIH+30}", composite));
        var shadowing = new CompositeFieldSource(new RecordFieldSource(["Tarih"], ["01.01.2026"]), BuiltIns);
        Assert.Equal("31.01.2026", Resolve("{TARIH+30}", shadowing));
    }

    [Fact]
    public void Offset_TooLargeOrMalformed_IsNotTreatedAsArithmetic()
    {
        Assert.Equal(["TARIH+999999"], FieldTemplate.Resolve("{TARIH+999999}", BuiltIns).MissingFields);
        Assert.Equal(["TARIH+3 gün sonra"], FieldTemplate.Resolve("{TARIH+3 gün sonra}", BuiltIns).MissingFields);
        Assert.Equal(["+30"], FieldTemplate.Resolve("{+30}", BuiltIns).MissingFields);
    }

    [Theory]
    [InlineData("{Ad:BUYUK}", "istanbul ılık çay", "İSTANBUL ILIK ÇAY")]
    [InlineData("{Ad:büyük}", "iğne", "İĞNE")]
    [InlineData("{Ad:KUCUK}", "ISPARTA İZMİR", "ısparta izmir")]
    [InlineData("{Ad:küçük harf}", "IŞIK", "ışık")]
    [InlineData("{Ad:BASHARF}", "MELAMİN KASE ıŞIK", "Melamin Kase Işık")]
    [InlineData("{Ad:başlık}", "servis tabağı", "Servis Tabağı")]
    public void TurkishTextTransforms(string template, string value, string expected)
    {
        var record = new RecordFieldSource(["Ad"], [value]);
        Assert.Equal(expected, Resolve(template, record));
    }

    [Fact]
    public void Transforms_CombineWithFormatsUsingPipe()
    {
        Assert.Equal("13 EKİM 2026", Resolve("{TARIH:dd MMMM yyyy|BUYUK}"));
        Assert.Equal("12 KASIM 2026", Resolve("{TARIH+30:dd MMMM yyyy|BUYUK}"));
        var record = new RecordFieldSource(["Fiyat"], ["12,5"]);
        Assert.Equal("12,50", Resolve("{Fiyat:0.00|BUYUK}", record));
        // Dönüşüm sözcüğü sayı biçimi gibi uygulanmaz ("12" → "BUYUK" olmaz).
        Assert.Equal("12", Resolve("{Fiyat:BUYUK}", new RecordFieldSource(["Fiyat"], ["12"])));
    }

    [Fact]
    public void ExistingSyntax_IsUnchanged()
    {
        var record = new RecordFieldSource(["Fiyat", "Barkod"], ["1234,5", "0012345678905"]);
        var composite = new CompositeFieldSource(record, BuiltIns);
        Assert.Equal("No 0007", Resolve("No {SIRA:0000}", composite));
        Assert.Equal("13 Ekim 2026", Resolve("{TARIH:dd MMMM yyyy}", composite));
        Assert.Equal("1.234,50 TL", Resolve("{Fiyat:#,##0.00} TL", composite));
        Assert.Equal("0012345678905", Resolve("{Barkod}", composite));
        Assert.Equal("{Barkod} = 0012345678905", Resolve("{{Barkod}} = {Barkod}", composite));
        Assert.Equal("09:30", Resolve("{SAAT}", composite));
    }

    [Fact]
    public void Tokens_ExposeExpressionParts()
    {
        var token = Assert.Single(FieldTemplate.GetTokens("SKT {TARIH+30:dd.MM.yyyy|BUYUK}"));
        Assert.Equal("TARIH+30", token.Name);
        Assert.Equal("TARIH", token.BaseName);
        Assert.Equal(new FieldDateOffset(30, DateOffsetUnit.Day), token.Expression.Offset);
        Assert.Equal("dd.MM.yyyy", token.Expression.ValueFormat);
        Assert.Equal([TextTransform.Upper], token.Expression.Transforms);

        var plain = Assert.Single(FieldTemplate.GetTokens("{UrunAdi}"));
        Assert.Equal("UrunAdi", plain.BaseName);
        Assert.Null(plain.Expression.Offset);
        Assert.Empty(plain.Expression.Transforms);
    }
}
