namespace AkelEtiket.PrintEngine.Tests;

/// <summary>
/// Kuyruk bekleme döngüsü, sahte kuyrukla (yazıcıya hiçbir şey gönderilmez). İşin ilk turda kuyrukta görünmesi v1'de
/// "TimeSpan overflowed" hatası veriyordu (13.10.2026, sensör kalibrasyonunda kullanıcıda görüldü).
/// </summary>
public class SpoolWaitTests
{
    private static PrinterStatusSnapshot Ready() => new() { PrinterName = "barkod", Printer = new PrinterInfo { Name = "barkod" } };

    /// <summary>Her çağrıda sıradaki iş durumu; null = iş kuyruktan çıktı. Son durum tekrar eder.</summary>
    private static Func<JobSnapshot?> Queue(params JobStatusFlags?[] states)
    {
        int i = 0;
        return () => states[Math.Min(i++, states.Length - 1)] is { } status ? new JobSnapshot { JobId = 7, Status = status } : null;
    }

    [Fact]
    public async Task JobVisibleOnFirstPoll_ThenLeavingQueue_IsDelivered()
    {
        var result = await SpoolerMonitor.WaitForJobAsync(
            Queue(JobStatusFlags.Spooling, JobStatusFlags.Printing, null), Ready, TimeSpan.FromSeconds(10), null, CancellationToken.None);

        Assert.Equal(SpoolOutcome.LeftQueue, result.Outcome);
        Assert.True(result.Delivered);
    }

    [Fact]
    public async Task JobMarkedPrinted_IsReportedAsPrinted()
    {
        var result = await SpoolerMonitor.WaitForJobAsync(
            Queue(JobStatusFlags.Printing, JobStatusFlags.Printed), Ready, TimeSpan.FromSeconds(10), null, CancellationToken.None);

        Assert.Equal(SpoolOutcome.Printed, result.Outcome);
    }

    [Fact]
    public async Task OfflinePrinter_IsReportedOnceItPersists()
    {
        var offline = new PrinterStatusSnapshot
        {
            PrinterName = "barkod",
            Printer = new PrinterInfo { Name = "barkod", Status = PrinterStatusFlags.Offline },
        };

        var result = await SpoolerMonitor.WaitForJobAsync(
            Queue(JobStatusFlags.Spooling), () => offline, TimeSpan.FromSeconds(10), null, CancellationToken.None);

        Assert.Equal(SpoolOutcome.Offline, result.Outcome);
        Assert.False(result.Delivered);
    }

    [Fact]
    public async Task JobStuckInQueue_TimesOut()
    {
        var result = await SpoolerMonitor.WaitForJobAsync(
            Queue(JobStatusFlags.Spooling), Ready, TimeSpan.FromMilliseconds(600), null, CancellationToken.None);

        Assert.Equal(SpoolOutcome.TimedOut, result.Outcome);
    }
}
