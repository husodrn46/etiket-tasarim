namespace AkelEtiket.Design.Tests;

/// <summary>Yapay zekâ araçlarında veri dosyaları: eski Excel (.xls) listelenir ve okunur.</summary>
public sealed class DataFileToolTests : IDisposable
{
    private readonly DesignTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static string XlsFixture()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AkelEtiket.sln")))
                return Path.Combine(dir.FullName, "tests", "AkelEtiket.DataImport.Tests", "Fixtures", "eski-excel.xls");
        }
        throw new DirectoryNotFoundException("AkelEtiket.sln bulunamadı.");
    }

    [Fact]
    public async Task ListFiles_ShowsEachDataFileOnce_AndPreviewReadsXls()
    {
        File.Copy(XlsFixture(), Path.Combine(_host.Extra, "eski-urunler.xls"));
        File.WriteAllText(Path.Combine(_host.Extra, "yeni.xlsx"), "x");
        File.WriteAllText(Path.Combine(_host.Extra, "liste.csv"), "Kod\nA1\n");

        var list = await _host.Call("list_files", new { kind = "data" });
        var paths = list["files"]!.AsArray().Select(f => f!["path"]!.GetValue<string>()).ToList();
        // .xls, .xlsx ve .csv ayrı ayrı ve birer kez listelenir (desenler çakışsa da).
        Assert.Equal(3, paths.Count);
        Assert.Single(paths, p => p.EndsWith("/eski-urunler.xls", StringComparison.Ordinal));
        Assert.Single(paths, p => p.EndsWith("/yeni.xlsx", StringComparison.Ordinal));

        string xls = paths.Single(p => p.EndsWith(".xls", StringComparison.Ordinal));
        var preview = await _host.Call("get_data_preview", new { path = xls });
        Assert.Equal("Ürünler", preview["sheet"]!.GetValue<string>());
        Assert.Equal(3, preview["row_count"]!.GetValue<int>());
        Assert.Contains("Ürün Adı", preview["columns"]!.AsArray().Select(c => c!.GetValue<string>()));
        var first = preview["untrusted_data"]!["rows"]![0]!;
        Assert.Equal(3, first["_row"]!.GetValue<int>());
        Assert.Equal("0012345678905", first["Barkod"]!.GetValue<string>());

        var prices = await _host.Call("get_data_preview", new { path = xls, sheet = "Fiyatlar" });
        Assert.Equal(2, prices["row_count"]!.GetValue<int>());
    }
}
