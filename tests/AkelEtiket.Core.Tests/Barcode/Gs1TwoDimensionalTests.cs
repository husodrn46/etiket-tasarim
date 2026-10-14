using AkelEtiket.BarcodeEngine;
using AkelEtiket.Core.Model;

namespace AkelEtiket.Core.Tests.Barcode;

/// <summary>
/// GS1 DataMatrix ve GS1 QR: kodla → 8 nokta/mm 1-bit çiz → bağımsız çözücüyle oku. Sembolde başta FNC1 bulunmalı
/// (okuyucu "]d2" / "]Q3" bildirir), AI ayırıcıları yalnızca değişken uzunluklu AI'lardan sonra GS olarak gelmeli.
/// </summary>
public class Gs1TwoDimensionalTests
{
    private const char Gs = '\u001d';

    private static string Sscc(string body17) => body17 + CheckDigits.Gs1Mod10(body17);

    public static TheoryData<BarcodeType, string, string> RoundTripCases()
    {
        var data = new TheoryData<BarcodeType, string, string>();
        foreach (var type in new[] { BarcodeType.Gs1DataMatrix, BarcodeType.Gs1QrCode })
        {
            // Sabit uzunluklu (01)(17) + sonda değişken (10): ayırıcı gerekmez.
            data.Add(type, "(01)08691234567890(17)281231(10)ABC123", "0108691234567890" + "17281231" + "10ABC123");
            // Değişken (10) ortada: (21)'den önce GS.
            data.Add(type, "(01)08691234567890(10)LOT7(21)SN-0001x", "0108691234567890" + "10LOT7" + Gs + "21SN-0001x");
            // SSCC (yalnız rakam).
            string sscc = Sscc("31234567000000001");
            data.Add(type, $"(00){sscc}", "00" + sscc);
            // Birden çok değişken AI, küçük harf ve % (GS1 karakter kümesinde).
            data.Add(type, "(02)08691234567890(37)12(10)a%b(400)PO-77", "0208691234567890" + "3712" + Gs + "10a%b" + Gs + "400PO-77");
        }
        // QR alfasayısal kip: değerdeki % işareti "%%" olarak kaçışlanmalı (yoksa okuyucu onu ayırıcı sanar).
        data.Add(BarcodeType.Gs1QrCode, "(01)08691234567890(10)AB%C", "0108691234567890" + "10AB%C");
        // QR sayısal kip.
        data.Add(BarcodeType.Gs1QrCode, "(01)08691234567890(17)281231", "0108691234567890" + "17281231");
        return data;
    }

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void Gs1TwoDimensional_RoundTrip_WithFnc1AndSeparators(BarcodeType type, string content, string expectedElementString)
    {
        var (bitmap, encode, layout) = BarcodeRoundTripTests.Render(type, content, 30, 30, BarcodeSettings.Default);
        Assert.True(layout.Fits, layout.Problem);
        Assert.Equal(expectedElementString, encode.Validation.VerificationText);
        Assert.Equal(content, encode.Validation.HumanReadableText);

        var verification = BarcodeVerifier.Verify(bitmap, type, encode.Validation.VerificationText);
        Assert.True(verification.Success, $"{type} '{content}': {verification.Message}");
        Assert.Equal(type == BarcodeType.Gs1DataMatrix ? "]d2" : "]Q3", verification.SymbologyIdentifier);
        Assert.Equal(expectedElementString, verification.DecodedText);
        Assert.False(verification.DecodedText!.EndsWith(Gs), "Son öğeden sonra ayırıcı olmamalı.");
    }

    [Theory]
    [InlineData(BarcodeType.Gs1DataMatrix, BarcodeType.DataMatrix)]
    [InlineData(BarcodeType.Gs1QrCode, BarcodeType.QrCode)]
    public void PlainSymbolWithSameText_IsNotAcceptedAsGs1(BarcodeType gs1Type, BarcodeType plainType)
    {
        string elementString = "0108691234567890" + "10LOT7" + Gs + "21SN1";
        var (bitmap, _, _) = BarcodeRoundTripTests.Render(plainType, elementString, 30, 30, BarcodeSettings.Default);
        var verification = BarcodeVerifier.Verify(bitmap, gs1Type, elementString);
        Assert.False(verification.Success);
        Assert.Contains("GS1 işareti", verification.Message);
    }

    [Theory]
    [InlineData(BarcodeType.Gs1DataMatrix)]
    [InlineData(BarcodeType.Gs1QrCode)]
    public void RotatedGs1Symbol_StillDecodes(BarcodeType type)
    {
        var (bitmap, encode, _) = BarcodeRoundTripTests.Render(type, "(01)08691234567890(10)LOT7(21)SN1", 30, 30, BarcodeSettings.Default);
        var verification = BarcodeVerifier.Verify(bitmap.RotateQuarterTurns(1), type, encode.Validation.VerificationText);
        Assert.True(verification.Success, verification.Message);
    }

    [Fact]
    public void Gs1DataMatrix_RectangleShape_Decodes()
    {
        var settings = BarcodeSettings.Default with { DataMatrixShape = DataMatrixShape.Rectangle };
        var (bitmap, encode, _) = BarcodeRoundTripTests.Render(BarcodeType.Gs1DataMatrix, "(01)08691234567890(10)LOT7", 40, 20, settings);
        var symbol = Assert.IsType<MatrixSymbol>(encode.Symbol);
        Assert.NotEqual(symbol.Width, symbol.Height);
        Assert.True(BarcodeVerifier.Verify(bitmap, BarcodeType.Gs1DataMatrix, encode.Validation.VerificationText).Success);
    }

    [Theory]
    [InlineData(BarcodeType.Gs1DataMatrix, QrErrorCorrectionLevel.M)]
    [InlineData(BarcodeType.Gs1QrCode, QrErrorCorrectionLevel.L)]
    [InlineData(BarcodeType.Gs1QrCode, QrErrorCorrectionLevel.H)]
    public void Gs1TwoDimensional_ModulesAreWholeDots(BarcodeType type, QrErrorCorrectionLevel level)
    {
        var settings = BarcodeSettings.Default with { QrErrorCorrection = level };
        var (_, _, layout) = BarcodeRoundTripTests.Render(type, "(01)08691234567890(17)281231", 25, 25, settings);
        Assert.True(layout.ModuleDots >= 1);
        Assert.All(layout.Bars, bar =>
        {
            Assert.Equal(0, bar.Width % layout.ModuleDots);
            Assert.Equal(0, bar.Height % layout.ModuleDots);
        });
    }

    [Theory]
    [InlineData(BarcodeType.Gs1DataMatrix, "08691234567890", "parantezli AI")]
    [InlineData(BarcodeType.Gs1QrCode, "08691234567890", "parantezli AI")]
    [InlineData(BarcodeType.Gs1DataMatrix, "(01)08691234567891", "08691234567890")]
    [InlineData(BarcodeType.Gs1QrCode, "(01)08691234567891", "kontrol basamağı hatalı")]
    [InlineData(BarcodeType.Gs1DataMatrix, "(17)281332", "tarih")]
    [InlineData(BarcodeType.Gs1QrCode, "(11)260230", "tarih")]
    [InlineData(BarcodeType.Gs1DataMatrix, "(10)ÇAY", "kullanılamaz")]
    [InlineData(BarcodeType.Gs1QrCode, "(10)A B", "kullanılamaz")]
    [InlineData(BarcodeType.Gs1DataMatrix, "(999)X", "bilinen bir GS1")]
    [InlineData(BarcodeType.Gs1QrCode, "", "boş")]
    public void InvalidContent_IsRejectedWithReason(BarcodeType type, string content, string expectedFragment)
    {
        var validation = BarcodeValidator.Validate(type, content);
        Assert.False(validation.IsValid);
        Assert.Contains(expectedFragment, string.Join(" ", validation.Errors));
        Assert.False(BarcodeEncoder.Encode(type, content).Success);
    }

    [Fact]
    public void ErrorMessages_NameTheSymbology()
    {
        Assert.Contains("GS1 DataMatrix", BarcodeValidator.Validate(BarcodeType.Gs1DataMatrix, "123").FirstError);
        Assert.Contains("GS1 QR", BarcodeValidator.Validate(BarcodeType.Gs1QrCode, "123").FirstError);
        Assert.Contains("GS1-128", BarcodeValidator.Validate(BarcodeType.Gs1_128, "123").FirstError);
    }

    [Fact]
    public void TwoDimensional_AllowsMoreThanGs1_128Limit()
    {
        string content = "(01)08691234567890(10)ABCDEFGHIJ1234567890(21)SERIAL12345678901234(400)SIPARIS-2026-000123";
        Assert.False(BarcodeValidator.Validate(BarcodeType.Gs1_128, content).IsValid);
        foreach (var type in new[] { BarcodeType.Gs1DataMatrix, BarcodeType.Gs1QrCode })
        {
            var (bitmap, encode, layout) = BarcodeRoundTripTests.Render(type, content, 40, 40, BarcodeSettings.Default);
            Assert.True(layout.Fits, layout.Problem);
            Assert.True(BarcodeVerifier.Verify(bitmap, type, encode.Validation.VerificationText).Success, type.ToString());
        }
    }

    [Fact]
    public void DataMatrixCodewords_UseFnc1DigitPairsAndAsciiValues()
    {
        string codewords = Gs1DataMatrixEncoder.BuildCodewords("0108" + "10A" + Gs + "21x");
        int[] values = codewords.Select(c => (int)c).ToArray();
        // FNC1, "01", "08", "10", 'A'+1, FNC1, "21", 'x'+1
        Assert.Equal([232, 131, 138, 140, 66, 232, 151, 121], values);
    }

    [Fact]
    public void DataMatrixPadding_FollowsIso16022()
    {
        string padded = Gs1DataMatrixEncoder.AddPadding("è\u0083", 6);
        int[] values = padded.Select(c => (int)c).ToArray();
        // 3. konum ilk dolgu 129; sonrakiler 253 durumlu karıştırma: 4 → 220, 5 → 115, 6 → 265-254 = 11.
        Assert.Equal([232, 131, 129, 220, 115, 11], values);
    }

    [Fact]
    public void Catalog_ListsGs1TwoDimensionalTypesAsTwoDimensional()
    {
        Assert.True(BarcodeCatalog.Get(BarcodeType.Gs1DataMatrix).Is2D);
        Assert.True(BarcodeCatalog.Get(BarcodeType.Gs1QrCode).Is2D);
        Assert.True(BarcodeElement.IsTwoDimensional(BarcodeType.Gs1DataMatrix));
        Assert.True(BarcodeElement.IsGs1(BarcodeType.Gs1QrCode));
        Assert.False(BarcodeElement.IsGs1(BarcodeType.QrCode));
        Assert.Equal("GS1 DataMatrix", new BarcodeElement { Symbology = BarcodeType.Gs1DataMatrix }.KindName);
    }

    [Theory]
    [InlineData(BarcodeType.Gs1DataMatrix, 2)]
    [InlineData(BarcodeType.Gs1QrCode, 2)]
    [InlineData(BarcodeType.Gs1_128, 1)]
    [InlineData(BarcodeType.DataMatrix, 1)]
    public void DocumentsUsingGs1TwoDimensional_RequireFormatVersion2(BarcodeType type, int expected)
    {
        var doc = LabelDocument.CreateBlank();
        doc.Elements.Add(new BarcodeElement { Symbology = type, Content = "(01)08691234567890" });
        Assert.Equal(expected, doc.GetRequiredFormatVersion());
    }

    [Fact]
    public void VerificationText_ForDataMatrix_StripsOnlyTheLeadingFnc1()
    {
        // Okuyucunun başa koyduğu GS dışında içerikteki GS'ler korunmalı.
        var (bitmap, encode, _) = BarcodeRoundTripTests.Render(BarcodeType.Gs1DataMatrix, "(10)A(21)B", 20, 20, BarcodeSettings.Default);
        var verification = BarcodeVerifier.Verify(bitmap, BarcodeType.Gs1DataMatrix, encode.Validation.VerificationText);
        Assert.True(verification.Success, verification.Message);
        Assert.Equal("10A" + Gs + "21B", verification.DecodedText);
    }

    [Fact]
    public void DamagedSymbol_FailsVerification()
    {
        var (bitmap, encode, layout) = BarcodeRoundTripTests.Render(BarcodeType.Gs1DataMatrix, "(01)08691234567890", 20, 20, BarcodeSettings.Default);
        var area = layout.SymbolArea;
        var damaged = bitmap.Clone();
        damaged.FillRect(area.X, area.Y, area.Width / 2, area.Height, false);
        Assert.False(BarcodeVerifier.Verify(damaged, BarcodeType.Gs1DataMatrix, encode.Validation.VerificationText).Success);
    }
}
