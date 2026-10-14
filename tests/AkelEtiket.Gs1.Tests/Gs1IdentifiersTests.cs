namespace AkelEtiket.Gs1.Tests;

public class Gs1IdentifiersTests
{
    [Fact]
    public void SyntheticGln_IsValid()
    {
        var result = Gs1Identifiers.ValidateGln("1234567890128");
        Assert.True(result.IsValid, result.Error);
        Assert.Equal(Gs1KeyType.Gln, result.Type);
        // Bağımsız hesapla da 8 olmalı.
        Assert.Equal(8, TestData.ReferenceCheckDigit("123456789012"));
        Assert.Equal(8, Gs1Identifiers.ComputeCheckDigit("123456789012"));
    }

    [Fact]
    public void Gln_WrongCheckDigit_ExplainsExpectedDigit()
    {
        var result = Gs1Identifiers.ValidateGln("1234567890129");
        Assert.False(result.IsValid);
        Assert.Contains("son hane 8 olmalı", result.Error);
    }

    [Theory]
    [InlineData("123 456 789 0128")]
    [InlineData("1234-5678-90128")]
    [InlineData(" 1234567890128 ")]
    public void Normalize_RemovesSpacesAndDashes(string input)
    {
        Assert.Equal("1234567890128", Gs1Identifiers.Normalize(input));
        Assert.True(Gs1Identifiers.ValidateGln(input).IsValid);
    }

    [Theory]
    [InlineData("", "boş")]
    [InlineData("12345678901AB", "yalnızca rakam")]
    [InlineData("123456789012", "13 hane")]
    public void Gln_InvalidInputs_HaveTurkishErrors(string input, string expected)
    {
        var result = Gs1Identifiers.ValidateGln(input);
        Assert.False(result.IsValid);
        Assert.Contains(expected, result.Error);
    }

    [Theory]
    [InlineData("73513537", Gs1KeyType.Gtin8)]
    [InlineData("036000291452", Gs1KeyType.Gtin12)]
    [InlineData("4006381333931", Gs1KeyType.Gtin13)]
    [InlineData("04006381333931", Gs1KeyType.Gtin14)]
    public void Gtin_KnownValidNumbers(string gtin, Gs1KeyType type)
    {
        var result = Gs1Identifiers.ValidateGtin(gtin);
        Assert.True(result.IsValid, result.Error);
        Assert.Equal(type, result.Type);
        Assert.Equal(TestData.ReferenceCheckDigit(gtin[..^1]), gtin[^1] - '0');
    }

    [Theory]
    [InlineData("4006381333932")]
    [InlineData("123456789")]
    [InlineData("12345678901234567")]
    public void Gtin_Invalid(string gtin) => Assert.False(Gs1Identifiers.ValidateGtin(gtin).IsValid);

    [Fact]
    public void ToGtin14_PadsWithoutChangingCheckDigit()
    {
        Assert.Equal("04006381333931", Gs1Identifiers.ToGtin14("4006381333931"));
        Assert.Null(Gs1Identifiers.ToGtin14("4006381333932"));
    }

    [Theory]
    [InlineData(6, 10)]
    [InlineData(7, 9)]
    [InlineData(9, 7)]
    [InlineData(12, 4)]
    public void SsccSerialLength_FollowsGs1Structure(int gcpLength, int serialLength) =>
        Assert.Equal(serialLength, Gs1Identifiers.SsccSerialLength(gcpLength));

    [Fact]
    public void BuildSscc_ProducesValid18DigitNumber()
    {
        string sscc = Gs1Identifiers.BuildSscc(3, TestData.TestGcp, 42);
        Assert.Equal(18, sscc.Length);
        Assert.Equal('3', sscc[0]);
        Assert.StartsWith("3" + TestData.TestGcp, sscc);
        Assert.Equal(42, TestData.SerialOf(sscc, TestData.TestGcp.Length));
        Assert.Equal(TestData.ReferenceCheckDigit(sscc[..17]), sscc[17] - '0');
        Assert.True(Gs1Identifiers.ValidateSscc(sscc).IsValid);
    }

    [Fact]
    public void BuildSscc_RejectsOutOfRangeSerialAndBadPrefix()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Gs1Identifiers.BuildSscc(0, TestData.TestGcp, 1_000_000_000));
        Assert.Throws<ArgumentException>(() => Gs1Identifiers.BuildSscc(0, "12345", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Gs1Identifiers.BuildSscc(10, TestData.TestGcp, 1));
    }

    [Theory]
    [InlineData("12345", false)]
    [InlineData("1234567890123", false)]
    [InlineData("86912A4", false)]
    [InlineData("", false)]
    [InlineData("869123", true)]
    [InlineData("869123456789", true)]
    public void GcpFormat_OnlySixToTwelveDigits(string gcp, bool valid)
    {
        Assert.Equal(valid, Gs1Identifiers.IsValidGcpFormat(gcp, out string? error));
        Assert.Equal(valid, error is null);
    }

    [Fact]
    public void PreviewSscc_IsStructurallyValidButSerialZero()
    {
        Assert.True(Gs1Identifiers.ValidateSscc(Gs1FieldSource.PreviewSscc).IsValid);
        Assert.Equal(0, TestData.SerialOf(Gs1FieldSource.PreviewSscc, 7));
    }
}
