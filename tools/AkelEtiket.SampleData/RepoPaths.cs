namespace AkelEtiket.SampleData;

internal static class RepoPaths
{
    /// <summary>Geçerli klasörden ve uygulama klasöründen yukarı çıkarak AkelEtiket.sln'yi arar.</summary>
    public static string FindSamplesDirectory()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "AkelEtiket.sln")))
                    return Path.Combine(dir.FullName, "samples");
            }
        }
        throw new DirectoryNotFoundException(
            "Depo kökü (AkelEtiket.sln) bulunamadı. samples klasörünün yolunu argüman olarak verin.");
    }
}
