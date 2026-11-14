using System.Diagnostics;
using System.Runtime.InteropServices;
using AkelEtiket.Editor.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace AkelEtiket.App.Mac.Services;

/// <summary>Avalonia arayüz iş parçacığı dağıtıcısı.</summary>
public sealed class AvaloniaDispatcher : IUiDispatcher
{
    public bool IsUiThread => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action) => Dispatcher.UIThread.Post(action, DispatcherPriority.Normal);
}

/// <summary>Render edilmiş BGRA pikselleri değişmez bir Avalonia görüntüsüne çevirir (her iş parçacığından güvenli).</summary>
public sealed class AvaloniaLayerImageFactory : ILayerImageFactory
{
    private static readonly Vector Dpi = new(96, 96);

    public object Create(byte[] bgra, int width, int height)
    {
        var handle = GCHandle.Alloc(bgra, GCHandleType.Pinned);
        try
        {
            // Pikseller kopyalanır; dizi çağıranda kalır. Alfa önceden çarpılmamıştır (WPF'teki Bgra32 ile aynı veri).
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Unpremul, handle.AddrOfPinnedObject(),
                new PixelSize(width, height), Dpi, width * 4);
        }
        finally
        {
            handle.Free();
        }
    }
}

/// <summary>Pano: kendi öğe biçimimiz + düz metin; görsel ve metin okuma.</summary>
public sealed class AvaloniaClipboardService(Func<TopLevel?> topLevel) : IClipboardService
{
    private static readonly DataFormat<string> ElementsFormat = DataFormat.CreateStringApplicationFormat("AkelEtiket.Elements");

    private IClipboard? Clipboard => topLevel()?.Clipboard;

    public void SetElements(string payloadJson, string plainText)
    {
        if (Clipboard is not { } clipboard) return;
        var item = new DataTransferItem();
        item.Set(ElementsFormat, payloadJson);
        if (!string.IsNullOrEmpty(plainText)) item.SetText(plainText);
        var data = new DataTransfer();
        data.Add(item);
        Try(() => UiWait.Run(() => clipboard.SetDataAsync(data)));
    }

    public string? GetElements() => Read(clipboard => clipboard.TryGetValueAsync(ElementsFormat));

    public bool ContainsElements() => GetElements() is not null;

    public byte[]? GetImagePng() => Read(async clipboard =>
    {
        using var bitmap = await clipboard.TryGetBitmapAsync().ConfigureAwait(true);
        if (bitmap is null) return null;
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    });

    public string? GetText() => Read(clipboard => clipboard.TryGetTextAsync());

    private T? Read<T>(Func<IClipboard, Task<T?>> read) where T : class
    {
        if (Clipboard is not { } clipboard) return null;
        T? result = null;
        Try(() => result = UiWait.Run(() => read(clipboard)));
        return result;
    }

    /// <summary>Pano başka bir uygulama tarafından kilitli ya da içerik okunamaz olabilir; işlem sessizce atlanır.</summary>
    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or IOException or TimeoutException or COMException)
        {
        }
    }
}

/// <summary>İşletim sistemi kabuğu yardımcıları (Finder / Gezgin, Çöp Sepeti).</summary>
public static class ShellHelper
{
    public static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var start = OperatingSystem.IsMacOS() ? new ProcessStartInfo("/usr/bin/open")
                : OperatingSystem.IsWindows() ? new ProcessStartInfo("explorer.exe")
                : new ProcessStartInfo("xdg-open");
            start.ArgumentList.Add(folder);
            start.UseShellExecute = false;
            Process.Start(start)?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            // Klasör açılamazsa sessizce geç.
        }
    }

    /// <summary>Dosyayı Çöp Sepeti'ne taşır; taşınamazsa kalıcı siler (Windows uygulamasıyla aynı davranış).</summary>
    public static void SendToTrash(string path)
    {
        if (OperatingSystem.IsMacOS() && TryMoveToMacTrash(path)) return;
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>~/.Trash altına, aynı ad varsa saat eklenerek taşınır (Finder'ın yaptığı gibi).</summary>
    private static bool TryMoveToMacTrash(string path)
    {
        try
        {
            if (!File.Exists(path)) return true;
            string trash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash");
            if (!Directory.Exists(trash)) return false;
            string name = Path.GetFileName(path);
            string target = Path.Combine(trash, name);
            if (File.Exists(target) || Directory.Exists(target))
                target = Path.Combine(trash, $"{Path.GetFileNameWithoutExtension(name)} {DateTime.Now:HH.mm.ss}{Path.GetExtension(name)}");
            File.Move(path, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
