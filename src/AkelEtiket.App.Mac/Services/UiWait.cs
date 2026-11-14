using Avalonia.Threading;

namespace AkelEtiket.App.Mac.Services;

/// <summary>
/// Görünüm modelleri diyalogları ve panoyu eşzamanlı çağırır (<c>IDialogService</c>, <c>IClipboardService</c>);
/// Avalonia'da bunların hepsi eşzamansızdır. Bu yardımcı, iş bitene kadar arayüz iş parçacığında iç içe bir ileti
/// döngüsü çalıştırır: pencere çizilmeye ve girdi almaya devam eder, çağıran kod ise sonucu bekler.
/// </summary>
internal static class UiWait
{
    public static T Run<T>(Func<Task<T>> start)
    {
        Dispatcher.UIThread.VerifyAccess();
        var task = start();
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            // Tamamlanma her iş parçacığından gelebilir; döngü arayüz iş parçacığından durdurulur.
            task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.UIThread.PushFrame(frame);
        }
        return task.GetAwaiter().GetResult();
    }

    public static void Run(Func<Task> start) => Run(async () =>
    {
        await start().ConfigureAwait(true);
        return true;
    });
}
