namespace AkelEtiket.Gs1.Tests;

public class SsccPrintSessionTests
{
    [Fact]
    public void Finish_MarksFirstNSent_RestVoided()
    {
        using var temp = new TempFolder();
        var registry = new SsccRegistry(temp.Path);
        var session = new SsccPrintSession(registry, TestData.VerifiedProfile(), "Baskı: Koli");
        var numbers = Enumerable.Range(1, 5).Select(i => session.Allocate($"Etiket {i}")).ToList();
        session.Finish(3, "kuyruğa gönderildi", "gönderilmedi");

        Assert.Equal(numbers.Take(3), session.Sent);
        Assert.Equal(numbers.Skip(3), session.Voided);
        Assert.Empty(session.Pending);
        Assert.All(numbers.Take(3), n => Assert.Equal(SsccStatus.Sent, registry.GetEntry(n)!.Status));
        Assert.All(numbers.Skip(3), n => Assert.Equal(SsccStatus.Voided, registry.GetEntry(n)!.Status));
        Assert.Contains("Baskı: Koli", registry.GetEntry(numbers[0])!.Note);
    }

    [Fact]
    public void VoidOne_IsImmediate_AndExcludedFromFinish()
    {
        using var temp = new TempFolder();
        var registry = new SsccRegistry(temp.Path);
        var session = new SsccPrintSession(registry, TestData.VerifiedProfile(), "İş");
        string a = session.Allocate("1");
        string b = session.Allocate("2");
        session.VoidOne(a, "etiket üretilemedi");
        Assert.Equal(SsccStatus.Voided, registry.GetEntry(a)!.Status);
        session.Finish(1, "gönderildi", "gönderilmedi");
        Assert.Equal(SsccStatus.Sent, registry.GetEntry(b)!.Status);
        Assert.Equal(SsccStatus.Voided, registry.GetEntry(a)!.Status);
    }

    [Fact]
    public void Finish_IsIdempotent_AndSessionThenRefusesAllocations()
    {
        using var temp = new TempFolder();
        var registry = new SsccRegistry(temp.Path);
        var session = new SsccPrintSession(registry, TestData.VerifiedProfile(), "İş");
        session.Allocate("1");
        session.Finish(1, "ok", "yok");
        int lines = File.ReadAllLines(registry.LogPath).Length;
        session.Finish(0, "ok", "yok");
        Assert.Equal(lines, File.ReadAllLines(registry.LogPath).Length);
        Assert.Throws<InvalidOperationException>(() => session.Allocate("2"));
    }

    [Fact]
    public void Finish_WithMoreThanPending_IsClamped()
    {
        using var temp = new TempFolder();
        var registry = new SsccRegistry(temp.Path);
        var session = new SsccPrintSession(registry, TestData.VerifiedProfile(), "İş");
        string a = session.Allocate("1");
        session.Finish(10, "ok", "yok");
        Assert.Equal([a], session.Sent);
    }

    [Fact]
    public void Context_BeginPrint_RequiresVerifiedPrefix()
    {
        using var temp = new TempFolder();
        var context = new Gs1Context(temp.Path);
        Assert.Throws<Gs1Exception>(() => context.BeginPrint("İş"));
        context.Save(TestData.VerifiedProfile());
        var session = context.BeginPrint("İş");
        string sscc = session.Allocate("1");
        Assert.True(Gs1Identifiers.ValidateSscc(sscc).IsValid);
    }
}
