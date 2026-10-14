using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Printing;
using AkelEtiket.PrintEngine;

namespace AkelEtiket.Tests;

/// <summary>Yazıcı/rulo profilleri: hazır AKEL XP-470B profili, kaydet/yeniden adlandır/sil, bozuk dosyanın korunması.</summary>
public sealed class PrinterProfileTests : IDisposable
{
    private readonly string _folder = Path.Combine(TestDataRoot.Root, "profil-" + Guid.NewGuid().ToString("N")[..8]);

    private string FilePath => Path.Combine(_folder, "yazici-profilleri.json");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void BuiltInProfile_HasNeutralDefaultsAndRequiresPrinterSelection()
    {
        var store = new PrinterProfileStore(FilePath);
        var akel = Assert.Single(store.All, p => p.IsBuiltIn);
        Assert.Equal(PrinterProfileStore.AkelProfileName, akel.Name);
        Assert.Equal("XP-470B başlangıç profili", akel.Name);
        Assert.Equal(PrintMethod.TsplRaw, akel.Method);
        Assert.Equal(MediaType.Gap, akel.Media);
        Assert.Equal(3.0, akel.GapMm);
        Assert.Equal(4, akel.Speed);
        Assert.Equal(8, akel.Density);
        Assert.False(akel.Rotate180);
        Assert.Empty(akel.PrinterName);
        Assert.Equal(TearMode.Unchanged, akel.TearMode);
        Assert.Equal(0, akel.OffsetXMm);
        Assert.Equal(0, akel.OffsetYMm);
        Assert.EndsWith("(hazır)", akel.ToString());

        // Hız 6 ve DIRECTION 0 yazıcıya aynen gider (kılavuzdaki 2–5 aralığına kırpılmaz).
        var settings = new LabelPrinterSettings { PrinterName = "barkod", Density = akel.Density, Speed = akel.Speed, Rotate180 = akel.Rotate180 };
        Assert.Equal(4, settings.EffectiveSpeed);
        Assert.Empty(settings.Validate());
        string setup = System.Text.Encoding.ASCII.GetString(TsplCommandBuilder.BuildSetup(new LabelMedia(100, 50, akel.GapMm, akel.GapOffsetMm, akel.Media), settings));
        Assert.Contains("SPEED 4\r\n", setup);
        Assert.Contains("DENSITY 8\r\n", setup);
        Assert.Contains("DIRECTION 1,0\r\n", setup);
        Assert.Contains("GAP 3 mm,0 mm\r\n", setup);
    }

    [Fact]
    public void BuiltInProfile_CannotBeOverwrittenRenamedOrDeleted_AndCopiesAreIndependent()
    {
        var store = new PrinterProfileStore(FilePath);
        var copy = store.Find(PrinterProfileStore.AkelProfileName)!;
        copy.Speed = 2;
        Assert.Equal(4, store.Find(PrinterProfileStore.AkelProfileName)!.Speed);
        Assert.Equal(4, PrinterProfileStore.AkelXp470B.Speed);

        Assert.Throws<InvalidOperationException>(() => store.Save(copy));
        Assert.Throws<InvalidOperationException>(() => store.Rename(PrinterProfileStore.AkelProfileName, "Başka"));
        Assert.Throws<InvalidOperationException>(() => store.Delete(PrinterProfileStore.AkelProfileName));
        Assert.True(PrinterProfileStore.IsBuiltInName(" xp-470b başlangıç profili "));
    }

    [Fact]
    public void UserProfiles_SaveRenameDelete_PersistAcrossInstances()
    {
        var store = new PrinterProfileStore(FilePath);
        store.Save(new PrinterProfile
        {
            Name = "XP-470B kalibre",
            PrinterName = "barkod",
            Density = 12,
            Speed = 4,
            Rotate180 = true,
            OffsetXMm = -0.75,
            OffsetYMm = 0.5,
            SingleJob = true,
            Media = MediaType.BlackMark,
            GapMm = 3,
            GapOffsetMm = 1,
            LabelWidthMm = 100,
            LabelHeightMm = 150,
            Notes = "Depo rulosu",
        });
        store.Save(new PrinterProfile { Name = "Ofis", Method = PrintMethod.WindowsDriver });
        store.LastUsed = "Ofis";

        var reloaded = new PrinterProfileStore(FilePath);
        Assert.Equal([PrinterProfileStore.AkelProfileName, "Ofis", "XP-470B kalibre"], reloaded.All.Select(p => p.Name));
        var saved = reloaded.Find("xp-470b KALIBRE")!;
        Assert.False(saved.IsBuiltIn);
        Assert.Equal(12, saved.Density);
        Assert.Equal(-0.75, saved.OffsetXMm);
        Assert.Equal(0.5, saved.OffsetYMm);
        Assert.True(saved.SingleJob);
        Assert.Equal(MediaType.BlackMark, saved.Media);
        Assert.Equal(1, saved.GapOffsetMm);
        Assert.Equal(150, saved.LabelHeightMm);
        Assert.Equal("Depo rulosu", saved.Notes);
        Assert.Equal("Ofis", reloaded.LastUsed);
        Assert.Equal(PrintMethod.WindowsDriver, reloaded.Find("Ofis")!.Method);

        // Aynı adla kaydetmek günceller (çoğaltmaz).
        saved.Speed = 3;
        reloaded.Save(saved);
        Assert.Single(reloaded.All, p => p.Name == "XP-470B kalibre");
        Assert.Equal(3, new PrinterProfileStore(FilePath).Find("XP-470B kalibre")!.Speed);

        reloaded.Rename("Ofis", "Ofis yazıcısı");
        Assert.Throws<InvalidOperationException>(() => reloaded.Rename("XP-470B kalibre", "OFIS yazıcısı"));
        Assert.Equal("Ofis yazıcısı", new PrinterProfileStore(FilePath).LastUsed);

        reloaded.Delete("Ofis yazıcısı");
        var final = new PrinterProfileStore(FilePath);
        Assert.Null(final.Find("Ofis yazıcısı"));
        Assert.Null(final.LastUsed);
        Assert.Equal(2, final.All.Count);
        Assert.Throws<ArgumentException>(() => final.Save(new PrinterProfile { Name = "  " }));
    }

    [Fact]
    public void CorruptProfileFile_IsBackedUp_NotSilentlyLost()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, "{ bozuk json");
        var store = new PrinterProfileStore(FilePath);
        Assert.Single(store.All);
        Assert.NotNull(store.LoadProblem);
        Assert.Equal("{ bozuk json", File.ReadAllText(FilePath + ".bozuk"));

        store.Save(new PrinterProfile { Name = "Yeni" });
        Assert.NotNull(new PrinterProfileStore(FilePath).Find("Yeni"));
        Assert.Equal("{ bozuk json", File.ReadAllText(FilePath + ".bozuk"));
    }
}
