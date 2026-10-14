namespace AkelEtiket.Gs1.Tests;

public class Gs1CompanyProfileTests
{
    [Fact]
    public void Default_HasNoCompanyIdentifiers_AndIsNotReadyForSscc()
    {
        var profile = new Gs1CompanyProfile();
        Assert.Empty(profile.Gln);
        Assert.Empty(profile.CompanyName);
        Assert.Equal("", profile.GcpPrefix);
        Assert.False(profile.GcpVerified);
        Assert.False(profile.IsReadyForSscc);
        Assert.Contains(profile.GetSsccBlockers(), b => b.Contains("girilmedi"));
    }

    [Fact]
    public void Prefix_IsNeverInferredFromGln()
    {
        var profile = new Gs1CompanyProfile();
        profile.Normalize();
        _ = profile.Validate();
        _ = profile.GetSsccBlockers();
        _ = profile.GetSerialRange();
        Assert.Equal("", profile.GcpPrefix);
        Assert.Null(profile.GetSerialRange());
        Assert.Contains(profile.Validate(), i => i.Field == nameof(Gs1CompanyProfile.GcpPrefix) && i.Message.Contains("GLN'den tahmin edilmez"));
    }

    [Fact]
    public void UnverifiedPrefix_BlocksSscc()
    {
        var profile = new Gs1CompanyProfile { GcpPrefix = TestData.TestGcp };
        var blockers = profile.GetSsccBlockers();
        Assert.Single(blockers);
        Assert.Contains("doğrulanmadı", blockers[0]);
    }

    [Fact]
    public void VerifiedPrefix_IsReady_AndRangeStartsAtOne()
    {
        var profile = TestData.VerifiedProfile();
        Assert.True(profile.IsReadyForSscc);
        var range = profile.GetSerialRange();
        Assert.Equal((1L, 999_999_999L), range);
    }

    [Fact]
    public void Normalize_ClearsVerifiedFlag_ForInvalidPrefix()
    {
        var profile = new Gs1CompanyProfile { GcpPrefix = "12AB", GcpVerified = true };
        profile.Normalize();
        Assert.False(profile.GcpVerified);
    }

    [Fact]
    public void Hint_WhenGlnDoesNotStartWithPrefix_ButNeverAutoFills()
    {
        var profile = TestData.VerifiedProfile();
        var issues = profile.Validate();
        var hint = Assert.Single(issues, i => i.Severity == Gs1IssueSeverity.Hint && i.Field == nameof(Gs1CompanyProfile.Gln));
        Assert.Contains("hata değildir", hint.Message);
        Assert.Equal("1234567890128", profile.Gln);
        Assert.Equal(TestData.TestGcp, profile.GcpPrefix);

        profile.Gln = Gs1Identifiers.AppendCheckDigit(TestData.TestGcp + "00001");
        Assert.DoesNotContain(profile.Validate(), i => i.Severity == Gs1IssueSeverity.Hint && i.Field == nameof(Gs1CompanyProfile.Gln));
    }

    [Fact]
    public void InvalidGln_IsAnError()
    {
        var profile = new Gs1CompanyProfile { Gln = "1234567890120" };
        Assert.Contains(profile.Validate(), i => i.Severity == Gs1IssueSeverity.Error && i.Field == nameof(Gs1CompanyProfile.Gln));
    }

    [Fact]
    public void SerialRange_Errors()
    {
        var profile = TestData.VerifiedProfile(start: 500, end: 100);
        Assert.Contains(profile.GetSsccBlockers(), b => b.Contains("büyük olamaz"));
        profile = TestData.VerifiedProfile(start: 0);
        Assert.Contains(profile.GetSsccBlockers(), b => b.Contains("Seri başlangıcı"));
        profile = TestData.VerifiedProfile(end: 1_000_000_000);
        Assert.Contains(profile.GetSsccBlockers(), b => b.Contains("Seri sonu"));
    }

    [Fact]
    public void GtinRegistry_Validation()
    {
        var profile = TestData.VerifiedProfile();
        profile.RegisteredGtins.Add(new RegisteredGtin { Gtin = "4006381333931", Description = "Örnek A" });
        profile.RegisteredGtins.Add(new RegisteredGtin { Gtin = "04006381333931", Description = "Aynısı 14 hane" });
        profile.RegisteredGtins.Add(new RegisteredGtin { Gtin = "4006381333932", Description = "Hatalı" });
        profile.RegisteredGtins.Add(new RegisteredGtin { Gtin = "1234567890128", Description = "GLN yazılmış" });
        var issues = profile.Validate();
        Assert.Contains(issues, i => i.Severity == Gs1IssueSeverity.Error && i.Message.Contains("birden fazla"));
        Assert.Contains(issues, i => i.Severity == Gs1IssueSeverity.Error && i.Message.Contains("kontrol basamağı"));
        Assert.Contains(issues, i => i.Severity == Gs1IssueSeverity.Warning && i.Message.Contains("GLN ile aynı"));
        // Önekle başlamayan GTIN için yalnızca ipucu.
        Assert.Contains(issues, i => i.Severity == Gs1IssueSeverity.Hint && i.Message.Contains("Örnek A"));
    }

    [Fact]
    public void GtinUsesPrefix_HandlesGtin14IndicatorDigit()
    {
        string gtin13 = Gs1Identifiers.AppendCheckDigit(TestData.TestGcp + "00001");
        Assert.True(Gs1CompanyProfile.GtinUsesPrefix(gtin13, TestData.TestGcp));
        string gtin14 = Gs1Identifiers.AppendCheckDigit("1" + TestData.TestGcp + "00001");
        Assert.True(Gs1CompanyProfile.GtinUsesPrefix(gtin14, TestData.TestGcp));
        Assert.False(Gs1CompanyProfile.GtinUsesPrefix("4006381333931", TestData.TestGcp));
    }

    [Fact]
    public void Clone_IsDeep()
    {
        var profile = TestData.VerifiedProfile();
        profile.RegisteredGtins.Add(new RegisteredGtin { Gtin = "4006381333931" });
        var copy = profile.Clone();
        copy.RegisteredGtins[0].Gtin = "73513537";
        copy.GcpPrefix = "869999";
        Assert.Equal("4006381333931", profile.RegisteredGtins[0].Gtin);
        Assert.Equal(TestData.TestGcp, profile.GcpPrefix);
    }
}
