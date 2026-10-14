using AkelEtiket.BarcodeEngine;
using AkelEtiket.Core.Model;

namespace AkelEtiket.Core.Tests.Barcode;

public class BarcodeValidationTests
{
    [Theory]
    [InlineData("869123456789", 0)]   // 8691234567890
    [InlineData("590123412345", 7)]   // 5901234123457
    [InlineData("400638133393", 1)]   // 4006381333931
    [InlineData("9638507", 4)]        // EAN-8 96385074
    [InlineData("03600029145", 2)]    // UPC-A 036000291452
    [InlineData("1869123456789", 7)]  // ITF-14 18691234567897
    public void Gs1Mod10_KnownValues(string body, int expected) =>
        Assert.Equal(expected, CheckDigits.Gs1Mod10(body));

    [Fact]
    public void Ean13_UserExampleRows_AreCheckedCorrectly()
    {
        var ok = BarcodeValidator.Validate(BarcodeType.Ean13, "8691234567890");
        Assert.True(ok.IsValid);
        Assert.Equal("8691234567890", ok.EncoderData);

        var bad = BarcodeValidator.Validate(BarcodeType.Ean13, "8691234567891");
        Assert.False(bad.IsValid);
        Assert.Contains("kontrol basamağı hatalı", bad.FirstError);
        Assert.Contains("8691234567890", bad.FirstError); // doğrusu önerilir
    }

    [Fact]
    public void Ean13_TwelveDigits_ComputesCheckDigit()
    {
        var r = BarcodeValidator.Validate(BarcodeType.Ean13, "869123456789");
        Assert.True(r.IsValid);
        Assert.Equal("8691234567890", r.EncoderData);
        Assert.Contains(r.Notes, n => n.Contains("otomatik hesaplandı"));
    }

    [Theory]
    [InlineData("86912345678")]       // 11 hane
    [InlineData("86912345678901")]    // 14 hane
    [InlineData("86912345678A0")]     // harf
    [InlineData("")]                  // boş
    [InlineData("8,69123E+12")]       // Excel bilimsel gösterim bozulması
    public void Ean13_InvalidInputs_AreRejected(string input)
    {
        var r = BarcodeValidator.Validate(BarcodeType.Ean13, input);
        Assert.False(r.IsValid);
        Assert.NotNull(r.FirstError);
    }

    [Fact]
    public void Ean13_SpacesAreRemoved()
    {
        var r = BarcodeValidator.Validate(BarcodeType.Ean13, " 869 1234 56789 0 ");
        Assert.True(r.IsValid);
        Assert.Equal("8691234567890", r.EncoderData);
    }

    [Fact]
    public void Ean8_UpcA_Itf14_LengthsAndChecks()
    {
        Assert.Equal("96385074", BarcodeValidator.Validate(BarcodeType.Ean8, "9638507").EncoderData);
        Assert.False(BarcodeValidator.Validate(BarcodeType.Ean8, "96385075").IsValid);
        Assert.Equal("036000291452", BarcodeValidator.Validate(BarcodeType.UpcA, "03600029145").EncoderData);
        Assert.False(BarcodeValidator.Validate(BarcodeType.UpcA, "036000291453").IsValid);
        Assert.Equal("18691234567897", BarcodeValidator.Validate(BarcodeType.Itf14, "1869123456789").EncoderData);
        Assert.False(BarcodeValidator.Validate(BarcodeType.Itf14, "18691234567890").IsValid);
    }

    [Fact]
    public void Code128_RejectsTurkishCharacters_WithHelpfulMessage()
    {
        var r = BarcodeValidator.Validate(BarcodeType.Code128, "ŞEKERLİK");
        Assert.False(r.IsValid);
        Assert.Contains("Türkçe", r.FirstError);
        Assert.Contains("QR", r.FirstError);
        // ZXing'in FNC1 kaçış karakteri kullanıcı verisinde asla kabul edilmemeli.
        Assert.False(BarcodeValidator.Validate(BarcodeType.Code128, "TSTñ01").IsValid);
        Assert.True(BarcodeValidator.Validate(BarcodeType.Code128, "TST-001 abc/+").IsValid);
    }

    [Fact]
    public void Code39_RulesAndMod43()
    {
        Assert.Equal('W', CheckDigits.Code39Mod43("CODE39"));
        Assert.Equal('R', CheckDigits.Code39Mod43("CODE 39"));
        var lower = BarcodeValidator.Validate(BarcodeType.Code39, "akel");
        Assert.False(lower.IsValid);
        Assert.Contains("AKEL", lower.FirstError);
        Assert.False(BarcodeValidator.Validate(BarcodeType.Code39, "A*B").IsValid);
        var withCheck = BarcodeValidator.Validate(BarcodeType.Code39, "CODE39", new BarcodeSettings { AppendCheckDigit = true });
        Assert.Equal("CODE39W", withCheck.EncoderData);
    }

    [Fact]
    public void Itf_EvenLengthAndOptionalCheckDigit()
    {
        Assert.True(BarcodeValidator.Validate(BarcodeType.Itf, "12345678").IsValid);
        var odd = BarcodeValidator.Validate(BarcodeType.Itf, "12345");
        Assert.False(odd.IsValid);
        Assert.Contains("çift", odd.FirstError);
        var withCheck = BarcodeValidator.Validate(BarcodeType.Itf, "1234567", new BarcodeSettings { AppendCheckDigit = true });
        Assert.True(withCheck.IsValid);
        Assert.Equal("12345670", withCheck.EncoderData);
        Assert.False(BarcodeValidator.Validate(BarcodeType.Itf, "12A4").IsValid);
    }

    [Fact]
    public void TwoDimensional_AcceptTurkishText()
    {
        foreach (var type in new[] { BarcodeType.QrCode, BarcodeType.DataMatrix, BarcodeType.Pdf417 })
            Assert.True(BarcodeValidator.Validate(type, "Örnek Firma Şekerlik ğüşıöç").IsValid);
        Assert.False(BarcodeValidator.Validate(BarcodeType.QrCode, "").IsValid);
        Assert.False(BarcodeValidator.Validate(BarcodeType.QrCode, new string('A', 5000)).IsValid);
    }
}

public class Gs1Tests
{
    [Fact]
    public void Parse_ValidElementString_BuildsFnc1AndHri()
    {
        var r = Gs1.Parse("(01)08691234567890(10)ABC123(17)261231");
        Assert.True(r.IsValid, string.Join("; ", r.Errors));
        Assert.Equal(3, r.Elements.Count);
        Assert.Equal("(01)08691234567890(10)ABC123(17)261231", r.HumanReadable);
        // 01 ön tanımlı uzunluk → ayırıcı yok; 10 değişken → FNC1 ayırıcı; 17 son eleman.
        Assert.Equal("ñ0108691234567890" + "10ABC123ñ" + "17261231", r.BuildEncoderData('ñ'));
        Assert.Equal("]C10108691234567890" + "10ABC123\u001d" + "17261231", r.BuildDecodedForm("]C1"));
    }

    [Fact]
    public void Parse_LastVariableElement_HasNoTrailingSeparator()
    {
        var r = Gs1.Parse("(01)08691234567890(21)SERI001");
        Assert.True(r.IsValid);
        Assert.Equal("ñ0108691234567890" + "21SERI001", r.BuildEncoderData('ñ'));
    }

    [Theory]
    [InlineData("(01)08691234567891", "kontrol basamağı")]
    [InlineData("(01)0869123456789", "14 karakter")]
    [InlineData("(17)261332", "tarih")]
    [InlineData("(99999)ABC", "bilinen")]
    [InlineData("(10)ŞEKER", "kullanılamaz")]
    [InlineData("0108691234567890", "parantezli")]
    [InlineData("(10)A(10)B", "birden fazla")]
    public void Parse_Errors(string input, string expectedFragment)
    {
        var r = Gs1.Parse(input);
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Gs1_128_IsNotTreatedAsPlainCode128()
    {
        var gs1 = BarcodeValidator.Validate(BarcodeType.Gs1_128, "(01)08691234567890(10)ABC");
        Assert.True(gs1.IsValid);
        Assert.StartsWith("ñ", gs1.EncoderData);
        Assert.StartsWith("]C1", gs1.VerificationText);
        // Parantezsiz/AI'sız düz metin GS1-128 olarak kabul edilmez.
        Assert.False(BarcodeValidator.Validate(BarcodeType.Gs1_128, "AKEL001").IsValid);
    }
}
