using Xunit;

/// <summary>Yalnızca Windows'ta çalışan test (yazdırma biriktiricisi, GDI); diğer sistemlerde atlanır.</summary>
internal sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Yalnızca Windows'ta çalışır.";
    }
}

/// <summary>Yalnızca macOS'ta çalışan test (Anahtar Zinciri).</summary>
internal sealed class MacFactAttribute : FactAttribute
{
    public MacFactAttribute()
    {
        if (!OperatingSystem.IsMacOS()) Skip = "Yalnızca macOS'ta çalışır.";
    }
}

/// <summary>Windows dışındaki sistemlerde çalışan test (desteklenmeyen platform davranışı).</summary>
internal sealed class NonWindowsFactAttribute : FactAttribute
{
    public NonWindowsFactAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "Windows dışındaki sistemlerde çalışır.";
    }
}
