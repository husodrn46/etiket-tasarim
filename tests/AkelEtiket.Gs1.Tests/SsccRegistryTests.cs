using System.Collections.Concurrent;

namespace AkelEtiket.Gs1.Tests;

public class SsccRegistryTests
{
    private static int GcpLength => TestData.TestGcp.Length;

    [Fact]
    public void Allocate_RefusesUnverifiedPrefix_AndWritesNothing()
    {
        using var temp = new TempFolder();
        var registry = new SsccRegistry(temp.Path);
        var profile = new Gs1CompanyProfile { GcpPrefix = TestData.TestGcp };
        var ex = Assert.Throws<Gs1Exception>(() => registry.Allocate(profile, 1, "test"));
        Assert.Contains("doğrulanmadı", ex.Message);
        Assert.False(File.Exists(registry.LogPath));

        var noPrefix = new Gs1CompanyProfile();
        Assert.Throws<Gs1Exception>(() => registry.Allocate(noPrefix, 1, "test"));
    }

    [Fact]
    public void Allocate_IsSequential_ValidAndNeverUsesSerialZero()
    {
        using var temp = new TempFolder();
        var registry = new SsccRegistry(temp.Path);
        var numbers = registry.Allocate(TestData.VerifiedProfile(), 3, "test");
        Assert.Equal(3, numbers.Count);
        Assert.Equal(new long[] { 1, 2, 3 }, numbers.Select(n => TestData.SerialOf(n, GcpLength)));
        foreach (var n in numbers)
        {
            Assert.True(Gs1Identifiers.ValidateSscc(n).IsValid);
            Assert.StartsWith("0" + TestData.TestGcp, n);
        }
    }

    [Fact]
    public void Allocate_WritesLogBeforeReturning()
    {
        using var temp = new TempFolder();
        var registry = new SsccRegistry(temp.Path);
        string sscc = registry.Allocate(TestData.VerifiedProfile(), 1, "Koli 1")[0];
        string log = File.ReadAllText(registry.LogPath);
        Assert.Contains(sscc, log);
        Assert.Contains("\"durum\":\"Ayrildi\"", log);
        Assert.Contains("Koli 1", log);
        var entry = registry.GetEntry(sscc);
        Assert.NotNull(entry);
        Assert.Equal(SsccStatus.Allocated, entry!.Status);
    }

    [Fact]
    public void Numbering_PersistsAcrossInstances()
    {
        using var temp = new TempFolder();
        var profile = TestData.VerifiedProfile();
        var first = new SsccRegistry(temp.Path).Allocate(profile, 2, "a");
        var second = new SsccRegistry(temp.Path).Allocate(profile, 2, "b");
        Assert.Equal(new long[] { 3, 4 }, second.Select(n => TestData.SerialOf(n, GcpLength)));
        Assert.Empty(first.Intersect(second));
    }

    [Fact]
    public void DeletedCounter_ContinuesFromLog()
    {
        using var temp = new TempFolder();
        var profile = TestData.VerifiedProfile();
        var registry = new SsccRegistry(temp.Path);
        registry.Allocate(profile, 5, "a");
        File.Delete(registry.CounterPath);
        string next = new SsccRegistry(temp.Path).Allocate(profile, 1, "b")[0];
        Assert.Equal(6, TestData.SerialOf(next, GcpLength));
    }

    [Fact]
    public void CorruptCounter_ContinuesFromLog()
    {
        using var temp = new TempFolder();
        var profile = TestData.VerifiedProfile();
        var registry = new SsccRegistry(temp.Path);
        registry.Allocate(profile, 4, "a");
        File.WriteAllText(registry.CounterPath, "bozuk{{{");
        string next = registry.Allocate(profile, 1, "b")[0];
        Assert.Equal(5, TestData.SerialOf(next, GcpLength));
    }

    [Fact]
    public void CounterBehindLog_EvenWithMatchingSize_DoesNotReuse()
    {
        using var temp = new TempFolder();
        var profile = TestData.VerifiedProfile();
        var registry = new SsccRegistry(temp.Path);
        registry.Allocate(profile, 3, "a");
        // Sayaç elle geri alınmış (boyut alanı uyuşsa bile) — kaydın sonundaki numara daha büyük.
        string counter = File.ReadAllText(registry.CounterPath).Replace($"\"0|{TestData.TestGcp}\": 3", $"\"0|{TestData.TestGcp}\": 1");
        Assert.Contains($"\"0|{TestData.TestGcp}\": 1", counter);
        File.WriteAllText(registry.CounterPath, counter);
        string next = registry.Allocate(profile, 1, "b")[0];
        Assert.Equal(4, TestData.SerialOf(next, GcpLength));
    }

    [Fact]
    public void TruncatedLastLine_IsStillCounted_AndNextRecordStartsOnNewLine()
    {
        using var temp = new TempFolder();
        var profile = TestData.VerifiedProfile();
        var registry = new SsccRegistry(temp.Path);
        registry.Allocate(profile, 1, "a");
        // Yarıda kalmış yazım: 50 numaralı kayıt satır sonu olmadan kesilmiş.
        string orphan = Gs1Identifiers.BuildSscc(0, TestData.TestGcp, 50);
        File.AppendAllText(registry.LogPath, "{\"zaman\":\"2026-10-13T10:00:00+03:00\",\"sscc\":\"" + orphan + "\",\"durum\":\"Ayr");
        string next = registry.Allocate(profile, 1, "b")[0];
        Assert.Equal(51, TestData.SerialOf(next, GcpLength));
        var lines = File.ReadAllLines(registry.LogPath);
        Assert.Equal(3, lines.Length);
        Assert.Contains(next, lines[2]);
        Assert.NotNull(registry.GetEntry(next));
    }

    [Fact]
    public void VoidedNumbers_AreNeverReused()
    {
        using var temp = new TempFolder();
        var profile = TestData.VerifiedProfile();
        var registry = new SsccRegistry(temp.Path);
        var numbers = registry.Allocate(profile, 3, "a");
        registry.MarkVoided(numbers, "iptal");
        string next = registry.Allocate(profile, 1, "b")[0];
        Assert.Equal(4, TestData.SerialOf(next, GcpLength));
        Assert.All(numbers, n => Assert.Equal(SsccStatus.Voided, registry.GetEntry(n)!.Status));
    }

    [Fact]
    public void Range_IsRespected_AndExhaustionIsReported()
    {
        using var temp = new TempFolder();
        var profile = TestData.VerifiedProfile(start: 1000, end: 1002);
        var registry = new SsccRegistry(temp.Path);
        var numbers = registry.Allocate(profile, 2, "a");
        Assert.Equal(new long[] { 1000, 1001 }, numbers.Select(n => TestData.SerialOf(n, GcpLength)));

        long sizeBefore = new FileInfo(registry.LogPath).Length;
        var tooMany = Assert.Throws<Gs1Exception>(() => registry.Allocate(profile, 2, "b"));
        Assert.Contains("yalnızca 1", tooMany.Message);
        Assert.Equal(sizeBefore, new FileInfo(registry.LogPath).Length);

        registry.Allocate(profile, 1, "c");
        var full = Assert.Throws<Gs1Exception>(() => registry.Allocate(profile, 1, "d"));
        Assert.Contains("doldu", full.Message);
    }

    [Fact]
    public void DifferentPrefixOrExtension_HaveIndependentNumbering()
    {
        using var temp = new TempFolder();
        var registry = new SsccRegistry(temp.Path);
        registry.Allocate(TestData.VerifiedProfile(), 3, "a");
        string otherExt = registry.Allocate(TestData.VerifiedProfile(ext: 5), 1, "b")[0];
        Assert.Equal(1, TestData.SerialOf(otherExt, GcpLength));
        Assert.Equal('5', otherExt[0]);
        string other = registry.Allocate(TestData.VerifiedProfile(gcp: "86955555"), 1, "c")[0];
        Assert.StartsWith("086955555", other);
        string again = registry.Allocate(TestData.VerifiedProfile(), 1, "d")[0];
        Assert.Equal(4, TestData.SerialOf(again, GcpLength));
    }

    [Fact]
    public void ParallelAllocations_FromManyInstances_NeverCollide()
    {
        using var temp = new TempFolder();
        var profile = TestData.VerifiedProfile();
        var all = new ConcurrentBag<string>();
        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, worker =>
        {
            var registry = new SsccRegistry(temp.Path);
            for (int i = 0; i < 20; i++)
            {
                foreach (var n in registry.Allocate(profile, 1 + (i % 2), $"işçi {worker}"))
                    all.Add(n);
            }
        });
        var list = all.ToList();
        Assert.Equal(240, list.Count);
        Assert.Equal(list.Count, list.Distinct().Count());
        var serials = list.Select(n => TestData.SerialOf(n, GcpLength)).OrderBy(s => s).ToList();
        Assert.Equal(Enumerable.Range(1, 240).Select(i => (long)i), serials);
        Assert.Equal(240, File.ReadAllLines(new SsccRegistry(temp.Path).LogPath).Length);
    }

    [Fact]
    public void StatusRecords_AreAppended_NeverRewritten()
    {
        using var temp = new TempFolder();
        var profile = TestData.VerifiedProfile();
        var registry = new SsccRegistry(temp.Path);
        var numbers = registry.Allocate(profile, 3, "iş");
        string before = File.ReadAllText(registry.LogPath);
        registry.MarkSent(numbers.Take(2), "kuyruğa gitti");
        registry.MarkVoided(numbers.Skip(2), "iptal");
        string after = File.ReadAllText(registry.LogPath);
        Assert.StartsWith(before, after);
        Assert.Equal(6, File.ReadAllLines(registry.LogPath).Length);

        var recent = registry.GetRecent();
        Assert.Equal(3, recent.Count);
        Assert.Equal(SsccStatus.Voided, recent[0].Status);
        Assert.Equal(numbers[2], recent[0].Sscc);
        Assert.Equal(2, recent.Count(e => e.Status == SsccStatus.Sent));

        var summary = registry.GetSummary(profile);
        Assert.Equal((0, 2, 1), (summary.Allocated, summary.Sent, summary.Voided));
        Assert.Equal(3, summary.LastSerial);
        Assert.Equal(4, summary.NextSerial);
        Assert.Equal(registry.PeekNextSerial(profile), summary.NextSerial);
    }

    [Fact]
    public void Mark_RejectsInvalidSscc()
    {
        using var temp = new TempFolder();
        var registry = new SsccRegistry(temp.Path);
        Assert.Throws<ArgumentException>(() => registry.MarkSent(["123"], "x"));
    }

    [Fact]
    public void PeekNextSerial_DoesNotAllocate()
    {
        using var temp = new TempFolder();
        var profile = TestData.VerifiedProfile(start: 77);
        var registry = new SsccRegistry(temp.Path);
        Assert.Equal(77, registry.PeekNextSerial(profile));
        Assert.Equal(77, registry.PeekNextSerial(profile));
        Assert.False(File.Exists(registry.LogPath));
        Assert.Null(registry.PeekNextSerial(new Gs1CompanyProfile()));
    }
}
