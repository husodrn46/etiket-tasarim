using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace AkelEtiket.App.Mac.Services;

/// <summary>Kısayollarda platformun komut tuşu: macOS'ta ⌘, diğer sistemlerde Ctrl.</summary>
internal static class PlatformKeys
{
    public static KeyModifiers Command => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    public static KeyGesture Gesture(Key key, KeyModifiers extra = KeyModifiers.None) => new(key, Command | extra);
}

/// <summary>Sürükleyip bırakılan dosyaların yerel yolları.</summary>
internal static class DroppedFiles
{
    public static string[] LocalPaths(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.Select(item => item.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
}
