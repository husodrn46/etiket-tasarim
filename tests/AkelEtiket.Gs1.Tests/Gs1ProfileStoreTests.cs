namespace AkelEtiket.Gs1.Tests;

public class Gs1ProfileStoreTests
{
    [Fact]
    public void MissingFile_ReturnsDefaultProfile()
    {
        using var temp = new TempFolder();
        var store = new Gs1ProfileStore(temp.Path);
        var profile = store.Load();
        Assert.Equal(Gs1CompanyProfile.DefaultGln, profile.Gln);
        Assert.Null(store.LastLoadProblem);
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        using var temp = new TempFolder();
        var store = new Gs1ProfileStore(temp.Path);
        var profile = TestData.VerifiedProfile(start: 1000, end: 1999, ext: 4);
        profile.CompanyName = "Örnek Firma Test";
        profile.GcpVerificationNote = "Üyelik belgesi 2026";
        profile.Notes = "Not: çğıöşü";
        profile.RegisteredGtins.Add(new RegisteredGtin
        {
            Gtin = "4006381333931", ProductCode = "TST-1", Description = "Tabak", PackagingLevel = PackagingLevel.Carton, Notes = "n",
        });
        store.Save(profile);

        var loaded = new Gs1ProfileStore(temp.Path).Load();
        Assert.Equal("Örnek Firma Test", loaded.CompanyName);
        Assert.Equal(TestData.TestGcp, loaded.GcpPrefix);
        Assert.True(loaded.GcpVerified);
        Assert.Equal(4, loaded.SsccExtensionDigit);
        Assert.Equal(1000, loaded.SerialStart);
        Assert.Equal(1999, loaded.SerialEnd);
        Assert.Equal("Üyelik belgesi 2026", loaded.GcpVerificationNote);
        Assert.Equal("Not: çğıöşü", loaded.Notes);
        var gtin = Assert.Single(loaded.RegisteredGtins);
        Assert.Equal(("4006381333931", "TST-1", "Tabak", PackagingLevel.Carton), (gtin.Gtin, gtin.ProductCode, gtin.Description, gtin.PackagingLevel));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));
        // Türkçe karakterler dosyada okunabilir kalır.
        Assert.Contains("çğıöşü", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void CorruptFile_IsBackedUp_NotSilentlyOverwritten()
    {
        using var temp = new TempFolder();
        var store = new Gs1ProfileStore(temp.Path);
        File.WriteAllText(store.FilePath, "{ bozuk json");
        var profile = store.Load();
        Assert.Equal(Gs1CompanyProfile.DefaultGln, profile.Gln);
        Assert.NotNull(store.LastLoadProblem);
        Assert.Contains("okunamadı", store.LastLoadProblem);
        Assert.Single(Directory.GetFiles(temp.Path, "firma-profili.bozuk-*.json"));
        Assert.Equal("{ bozuk json", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void Context_CachesProfile_AndRaisesChanged()
    {
        using var temp = new TempFolder();
        var context = new Gs1Context(temp.Path);
        int changed = 0;
        context.ProfileChanged += (_, _) => changed++;
        Assert.Same(context.Profile, context.Profile);
        var edit = context.Profile.Clone();
        edit.GcpPrefix = TestData.TestGcp;
        context.Save(edit);
        Assert.Equal(1, changed);
        Assert.Equal(TestData.TestGcp, context.Profile.GcpPrefix);
        Assert.Equal(TestData.TestGcp, new Gs1Context(temp.Path).Profile.GcpPrefix);
    }
}
