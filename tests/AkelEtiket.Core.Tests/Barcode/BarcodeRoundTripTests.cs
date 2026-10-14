using AkelEtiket.BarcodeEngine;
using AkelEtiket.Core.Imaging;
using AkelEtiket.Core.Model;

namespace AkelEtiket.Core.Tests.Barcode;

/// <summary>
/// Kabul testi: barkod üret → yazıcı çözünürlüğünde (8 nokta/mm) 1-bit çiz → bağımsız çözücüyle oku → içerik eşleşsin.
/// </summary>
public class BarcodeRoundTripTests
{
    private const double Dpmm = 8.0;

    public static TheoryData<BarcodeType, string, double, double> Cases => new()
    {
        { BarcodeType.Ean13, "8691234567890", 50, 25 },
        { BarcodeType.Ean13, "869123456789", 38, 22 },
        { BarcodeType.Ean8, "96385074", 30, 20 },
        { BarcodeType.UpcA, "036000291452", 45, 25 },
        { BarcodeType.Code128, "AKEL001", 40, 15 },
        { BarcodeType.Code128, "TST-2026/10 abc", 70, 15 },
        { BarcodeType.Code128, "8691234567890123", 60, 15 },
        { BarcodeType.Gs1_128, "(01)08691234567890(10)ABC123(17)261231", 95, 20 },
        // Kontrol karakteri 102 (FNC1 değeri) olan durum: okuyucunun sona eklediği GS doğru ele alınmalı.
        { BarcodeType.Gs1_128, "(01)08699242111623(10)H0E4GZ(17)281004", 100, 20 },
        { BarcodeType.Code39, "AKEL-001", 60, 15 },
        { BarcodeType.Itf, "12345678", 45, 15 },
        { BarcodeType.Itf14, "18691234567897", 80, 25 },
        { BarcodeType.QrCode, "https://example.com", 25, 25 },
        { BarcodeType.QrCode, "Örnek Firma – Şekerlik ğüşıöç İ", 30, 30 },
        { BarcodeType.DataMatrix, "AKEL001 8691234567890", 20, 20 },
        { BarcodeType.DataMatrix, "Çay Tabağı ğüşıöç", 20, 20 },
        { BarcodeType.Pdf417, "Örnek Firma TST001 8691234567890", 80, 25 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void EncodedBarcode_DecodesBackToSameContent(BarcodeType type, string content, double widthMm, double heightMm)
    {
        var (bitmap, encode, layout) = Render(type, content, widthMm, heightMm, BarcodeSettings.Default);
        Assert.True(layout.Fits, layout.Problem);
        Assert.True(layout.ModuleDots >= 1);

        var verification = BarcodeVerifier.Verify(bitmap, type, encode.Validation.VerificationText);
        Assert.True(verification.Success, $"{type} '{content}': {verification.Message}");
    }

    [Theory]
    [InlineData(BarcodeType.Ean13, "8691234567890")]
    [InlineData(BarcodeType.Code128, "AKEL001")]
    [InlineData(BarcodeType.QrCode, "AKEL")]
    [InlineData(BarcodeType.DataMatrix, "AKEL001")]
    public void RotatedBarcode_StillDecodes(BarcodeType type, string content)
    {
        var (bitmap, encode, _) = Render(type, content, 50, 25, BarcodeSettings.Default);
        var rotated = bitmap.RotateQuarterTurns(1);
        Assert.True(BarcodeVerifier.Verify(rotated, type, encode.Validation.VerificationText).Success);
    }

    [Fact]
    public void Code39AndItf_WithCheckDigitAndRatio_Decode()
    {
        foreach (var ratio in new[] { 2.0, 2.5, 3.0 })
        {
            var settings = new BarcodeSettings { AppendCheckDigit = true, WideToNarrowRatio = ratio };
            var (b1, e1, _) = Render(BarcodeType.Code39, "AKEL 39", 70, 15, settings);
            Assert.True(BarcodeVerifier.Verify(b1, BarcodeType.Code39, e1.Validation.VerificationText).Success, $"Code39 oran {ratio}");
            var (b2, e2, _) = Render(BarcodeType.Itf, "1234567", 50, 15, settings);
            Assert.True(BarcodeVerifier.Verify(b2, BarcodeType.Itf, e2.Validation.VerificationText).Success, $"ITF oran {ratio}");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(8)]
    public void Pdf417_ErrorCorrectionLevels(int level)
    {
        var settings = new BarcodeSettings { Pdf417ErrorCorrection = level };
        var (bmp, enc, layout) = Render(BarcodeType.Pdf417, "AKEL 123456", 90, 30, settings);
        Assert.True(layout.Fits, layout.Problem);
        Assert.True(BarcodeVerifier.Verify(bmp, BarcodeType.Pdf417, enc.Validation.VerificationText).Success);
    }

    [Theory]
    [InlineData(QrErrorCorrectionLevel.L)]
    [InlineData(QrErrorCorrectionLevel.M)]
    [InlineData(QrErrorCorrectionLevel.Q)]
    [InlineData(QrErrorCorrectionLevel.H)]
    public void Qr_ErrorCorrectionLevels_ChangeSymbolSizeAndDecode(QrErrorCorrectionLevel level)
    {
        var settings = new BarcodeSettings { QrErrorCorrection = level };
        var encode = BarcodeEncoder.Encode(BarcodeType.QrCode, "Örnek Firma https://example.com", settings);
        Assert.True(encode.Success);
        var (bmp, enc, _) = Render(BarcodeType.QrCode, "Örnek Firma https://example.com", 35, 35, settings);
        Assert.True(BarcodeVerifier.Verify(bmp, BarcodeType.QrCode, enc.Validation.VerificationText).Success);
        var low = (MatrixSymbol)BarcodeEncoder.Encode(BarcodeType.QrCode, "Örnek Firma https://example.com",
            new BarcodeSettings { QrErrorCorrection = QrErrorCorrectionLevel.L }).Symbol!;
        Assert.True(((MatrixSymbol)encode.Symbol!).Width >= low.Width);
    }

    [Fact]
    public void Verification_DetectsMismatchAndDamage()
    {
        var (bitmap, _, _) = Render(BarcodeType.Code128, "AKEL001", 40, 15, BarcodeSettings.Default);
        var mismatch = BarcodeVerifier.Verify(bitmap, BarcodeType.Code128, "AKEL002");
        Assert.False(mismatch.Success);
        Assert.Equal("AKEL001", mismatch.DecodedText);

        var damaged = bitmap.Clone();
        damaged.Fill(false);
        var failed = BarcodeVerifier.Verify(damaged, BarcodeType.Code128, "AKEL001");
        Assert.False(failed.Success);
        Assert.Null(failed.DecodedText);
    }

    [Fact]
    public void Layout_UsesIntegerModules_AndStaysInsideBox()
    {
        var encode = BarcodeEncoder.Encode(BarcodeType.Ean13, "8691234567890");
        int boxW = 400, boxH = 200; // 50 × 25 mm @ 8 nokta/mm
        var layout = BarcodeLayoutEngine.Layout(encode.Symbol!, boxW, boxH, new BarcodeLayoutOptions { DotsPerMm = Dpmm });
        Assert.True(layout.Fits);
        // 95 modül + 2 × 11 sessiz bölge = 117 → 400/117 = 3 nokta
        Assert.Equal(3, layout.ModuleDots);
        Assert.All(layout.Bars, r =>
        {
            Assert.True(r.X >= 0 && r.Y >= 0 && r.Right <= boxW && r.Bottom <= boxH);
            Assert.Equal(0, r.Width % 3);
        });
        Assert.Equal(13, layout.Texts.Count); // EAN-13: her hane ayrı yerleştirilir
        // 2 nokta (0,25 mm) %76 büyütmeye denk gelir → uyarı verilmeli
        var narrow = BarcodeLayoutEngine.Layout(encode.Symbol!, 240, 160, new BarcodeLayoutOptions { DotsPerMm = Dpmm });
        Assert.Equal(2, narrow.ModuleDots);
        Assert.Contains(narrow.Warnings, w => w.Contains("%80"));
    }

    [Fact]
    public void Layout_TooSmall_ReportsProblem()
    {
        var encode = BarcodeEncoder.Encode(BarcodeType.Code128, "AKEL001-UZUN-DEGER");
        var layout = BarcodeLayoutEngine.Layout(encode.Symbol!, 60, 100, new BarcodeLayoutOptions { DotsPerMm = Dpmm });
        Assert.False(layout.Fits);
        Assert.Contains("sığmıyor", layout.Problem);
        Assert.True(layout.MinimumWidthDots > 60);
    }

    [Fact]
    public void RequiredWidth_MatchesLayoutAtThatModule()
    {
        var encode = BarcodeEncoder.Encode(BarcodeType.Code128, "AKEL001");
        var options = new BarcodeLayoutOptions { DotsPerMm = Dpmm };
        int required = BarcodeLayoutEngine.GetRequiredWidthDots(encode.Symbol!, 3, options);
        var layout = BarcodeLayoutEngine.Layout(encode.Symbol!, required, 120, options);
        Assert.Equal(3, layout.ModuleDots);
        Assert.Equal(required, layout.SymbolArea.Width);
    }

    [Fact]
    public void RandomGeneratedValues_AreValidAndDecodable()
    {
        var random = new Random(2026);
        foreach (var info in BarcodeCatalog.All)
        {
            for (int i = 0; i < 5; i++)
            {
                string value = BarcodeValueGenerator.GenerateRandom(info.Type, random);
                var (bmp, enc, layout) = Render(info.Type, value, info.Is2D ? 40 : 100, info.Is2D ? 40 : 25, BarcodeSettings.Default);
                Assert.True(enc.Success, $"{info.DisplayName} '{value}': {enc.Error}");
                Assert.True(layout.Fits, $"{info.DisplayName} '{value}': {layout.Problem}");
                var v = BarcodeVerifier.Verify(bmp, info.Type, enc.Validation.VerificationText);
                Assert.True(v.Success, $"{info.DisplayName} '{value}': {v.Message}");
            }
        }
    }

    [Fact]
    public void CatalogExamples_AreAllValid()
    {
        foreach (var info in BarcodeCatalog.All)
            Assert.True(BarcodeValidator.Validate(info.Type, info.Example).IsValid, info.DisplayName);
    }

    [Fact]
    public void GenerateGtin_ProducesValidEan13()
    {
        string ean = BarcodeValueGenerator.GenerateGtin(BarcodeType.Ean13, "8691234", 42);
        Assert.Equal(13, ean.Length);
        Assert.StartsWith("869123400042", ean);
        Assert.True(CheckDigits.IsValidGs1(ean));
        Assert.Throws<ArgumentOutOfRangeException>(() => BarcodeValueGenerator.GenerateGtin(BarcodeType.Ean13, "8691234", 123456));
    }

    internal static (MonoBitmap Bitmap, BarcodeEncodeResult Encode, BarcodeLayout Layout) Render(
        BarcodeType type, string content, double widthMm, double heightMm, BarcodeSettings settings)
    {
        var encode = BarcodeEncoder.Encode(type, content, settings);
        Assert.True(encode.Success, $"{type} '{content}': {encode.Error}");
        int w = Units.MmToDotsRounded(widthMm, Dpmm), h = Units.MmToDotsRounded(heightMm, Dpmm);
        var layout = BarcodeLayoutEngine.Layout(encode.Symbol!, w, h, new BarcodeLayoutOptions { DotsPerMm = Dpmm });
        var bitmap = new MonoBitmap(w, h);
        foreach (var bar in layout.Bars) bitmap.FillRect(bar.X, bar.Y, bar.Width, bar.Height, true);
        return (bitmap, encode, layout);
    }
}
