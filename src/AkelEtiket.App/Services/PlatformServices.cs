using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AkelEtiket.Editor.Services;

namespace AkelEtiket.App.Services;

/// <summary>WPF arayüz iş parçacığı dağıtıcısı.</summary>
public sealed class WpfDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public bool IsUiThread => dispatcher.CheckAccess();

    public void Post(Action action) => dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
}

/// <summary>Render edilmiş BGRA pikselleri dondurulmuş BitmapSource'a çevirir (her iş parçacığından güvenli).</summary>
public sealed class WpfLayerImageFactory : ILayerImageFactory
{
    public object Create(byte[] bgra, int width, int height)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}

/// <summary>Pano: kendi biçimimiz + düz metin; görsel ve metin okuma.</summary>
public sealed class WpfClipboardService : IClipboardService
{
    private const string Format = "AkelEtiket.Elements";

    public void SetElements(string payloadJson, string plainText)
    {
        try
        {
            var data = new DataObject();
            data.SetData(Format, payloadJson);
            if (!string.IsNullOrEmpty(plainText)) data.SetText(plainText);
            Clipboard.SetDataObject(data, copy: true);
        }
        catch (COMException)
        {
            // Pano başka bir uygulama tarafından kilitli olabilir.
        }
    }

    public string? GetElements()
    {
        try
        {
            return Clipboard.ContainsData(Format) ? Clipboard.GetData(Format) as string : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    public bool ContainsElements()
    {
        try
        {
            return Clipboard.ContainsData(Format);
        }
        catch (COMException)
        {
            return false;
        }
    }

    public byte[]? GetImagePng()
    {
        try
        {
            if (!Clipboard.ContainsImage()) return null;
            var image = Clipboard.GetImage();
            if (image is null) return null;
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    public string? GetText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (COMException)
        {
            return null;
        }
    }
}

/// <summary>Windows kabuğu yardımcıları.</summary>
public static class ShellHelper
{
    public static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            // Gezgin açılamazsa sessizce geç.
        }
    }

    /// <summary>Dosyayı Geri Dönüşüm Kutusu'na gönderir.</summary>
    public static void SendToRecycleBin(string path)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + '\0' + '\0',
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
        };
        int result = SHFileOperation(ref op);
        if (result != 0 && File.Exists(path)) File.Delete(path);
    }

    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
}

/// <summary>Koyu/açık tema geçişi ve Windows başlık çubuğu rengi.</summary>
public static class ThemeManager
{
    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    public static event EventHandler? ThemeChanged;

    public static void Apply(AppTheme theme)
    {
        Current = theme;
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var colors = new ResourceDictionary
        {
            Source = new Uri(theme == AppTheme.Dark ? "Themes/Colors.Dark.xaml" : "Themes/Colors.Light.xaml", UriKind.Relative),
        };
        if (dictionaries.Count > 0) dictionaries[0] = colors;
        else dictionaries.Add(colors);
        foreach (Window window in Application.Current.Windows) ApplyTitleBar(window);
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Windows 10/11 başlık çubuğunu temaya uydurur (desteklenmezse sessizce geçer).</summary>
    public static void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int dark = Current == AppTheme.Dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        if (Application.Current.TryFindResource("Color.Toolbar") is Color c)
        {
            int colorRef = c.R | (c.G << 8) | (c.B << 16);
            _ = DwmSetWindowAttribute(handle, DWMWA_CAPTION_COLOR, ref colorRef, sizeof(int));
        }
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
