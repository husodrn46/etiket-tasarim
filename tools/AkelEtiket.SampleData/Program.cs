// AKEL Etiket örnek veri üretici (geliştirici aracı).
// Kullanım:
//   dotnet run --project tools/AkelEtiket.SampleData -- [samples klasörü]   → Excel/CSV örnekleri + örnek şablonlar
//   dotnet run --project tools/AkelEtiket.SampleData -- icon [klasör]       → uygulama ikonu
// Klasör verilmezse depo kökü (AkelEtiket.sln) bulunur.
using System.Text;
using AkelEtiket.SampleData;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

if (args.Length > 0 && args[0] == "icon")
{
    string assets = args.Length > 1
        ? Path.GetFullPath(args[1])
        : Path.Combine(Path.GetDirectoryName(RepoPaths.FindSamplesDirectory())!, "src", "AkelEtiket.App", "Assets");
    IconGenerator.Write(assets);
    return 0;
}

string samplesDirectory;
try
{
    samplesDirectory = args.Length > 0 ? Path.GetFullPath(args[0]) : RepoPaths.FindSamplesDirectory();
}
catch (DirectoryNotFoundException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
Directory.CreateDirectory(samplesDirectory);

var products = SampleProducts.Create();

string excelPath = Path.Combine(samplesDirectory, "Ornek-Urunler.xlsx");
string utf8Path = Path.Combine(samplesDirectory, "ornek-urunler-utf8.csv");
string ansiPath = Path.Combine(samplesDirectory, "ornek-urunler-1254.csv");
string faultyPath = Path.Combine(samplesDirectory, "ornek-hatali-veri.csv");

SampleWriters.WriteExcel(excelPath, products);
SampleWriters.WriteUtf8CommaCsv(utf8Path, products);
SampleWriters.Write1254SemicolonCsv(ansiPath, products);
SampleWriters.WriteFaultyCsv(faultyPath, products);

Console.WriteLine($"Örnek dosyalar yazıldı ({products.Count} ürün): {samplesDirectory}");
foreach (var path in new[] { excelPath, utf8Path, ansiPath, faultyPath })
    Console.WriteLine($"  {Path.GetFileName(path)} ({new FileInfo(path).Length:N0} bayt)");

Console.WriteLine("Örnek şablonlar:");
TemplateSamples.Write(samplesDirectory, null);
return 0;
