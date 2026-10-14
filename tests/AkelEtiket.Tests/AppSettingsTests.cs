using AkelEtiket.Editor.Services;
using AkelEtiket.PrintEngine;
using AkelEtiket.ProjectManager;

namespace AkelEtiket.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "akel-ayar-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    /// <summary>Regresyon: varsayılan ayarlar (pencere konumu dahil) JSON'a yazılabilmeli.</summary>
    [Fact]
    public void DefaultSettings_RoundTrip()
    {
        var store = new JsonSettingsStore<AppSettings>(Path.Combine(_dir, "ayarlar.json"));
        var settings = new AppSettings();
        settings.Printer.PrinterName = "barkod";
        settings.Printer.Method = PrintMethod.TsplRaw;
        settings.Printer.Density = 11;
        settings.Recent.Add(Path.Combine(_dir, "a.akelabel"));
        store.Save(settings);

        var loaded = store.Load();
        Assert.Null(store.LastLoadProblem);
        Assert.Equal("barkod", loaded.Printer.PrinterName);
        Assert.Equal(11, loaded.Printer.Density);
        Assert.Null(loaded.Window.Left);
        Assert.Single(loaded.Recent.Items);

        loaded.Window.Left = 120.5;
        store.Save(loaded);
        Assert.Equal(120.5, store.Load().Window.Left);
    }
}
