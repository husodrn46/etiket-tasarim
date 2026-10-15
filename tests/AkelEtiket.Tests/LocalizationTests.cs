using System.Globalization;
using System.Resources;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AkelEtiket.Core;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Localization;
using AkelEtiket.Core.Model;
using AkelEtiket.Core.Serialization;
using AkelEtiket.DataImport;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.Templates;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;

namespace AkelEtiket.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData(null, "tr-TR", "tr")]
    [InlineData("auto", "en-GB", "en")]
    [InlineData("auto", "de-DE", "en")]
    [InlineData("auto", "ja-JP", "en")]
    [InlineData("en", "tr-TR", "en")]
    [InlineData("tr", "en-US", "tr")]
    public void LanguageResolution(string? preference, string system, string expected) =>
        Assert.Equal(expected, UiText.ResolveLanguage(preference, CultureInfo.GetCultureInfo(system)));

    [Fact]
    public void OlderSettingsKeepTurkish_NewSettingsUseSystem_AndSelectionRoundTrips()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"firstRunCompleted\":true}", ModelJson.FileOptions)!;
        old.Normalize(); Assert.Equal("tr", old.UiLanguage);
        var fresh = new AppSettings(); fresh.Normalize(); Assert.Equal("auto", fresh.UiLanguage);
        fresh.UiLanguage = "en";
        var saved = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(fresh, ModelJson.FileOptions), ModelJson.FileOptions)!;
        saved.Normalize(); Assert.Equal("en", saved.UiLanguage);
        var draft = new SettingsViewModel(saved); draft.UiLanguage = "tr";
        Assert.Equal("en", saved.UiLanguage); draft.ApplyTo(saved); Assert.Equal("tr", saved.UiLanguage);
    }

    [Theory]
    [InlineData("en-US", "Save", "Copied 7 elements.")]
    [InlineData("tr-TR", "Kaydet", "7 öğe kopyalandı.")]
    public void ExplicitUiTranslations_PreserveInterpolatedValues(string language, string save, string copied)
    {
        using var scope = new CultureScope(language);
        Assert.Equal(save, UiText.T("Kaydet"));
        int count = 7; Assert.Equal(copied, UiText.T($"{count} öğe kopyalandı."));
        string customerName = "Kaydet {0} IŞIK 東京";
        Assert.Contains(customerName, UiText.T($"Açıldı: {customerName}"));
        string column = "Ürün Kodu";
        Assert.Contains("{Ürün Kodu}", UiText.T($"{{{column}}} alanına bağlı metin eklendi."));
    }

    [Fact]
    public void CatalogsHaveSameKeys_ValidCompositeFormats_AndNoMissingXamlKeys()
    {
        string trFile = EditorTestHelpers.FindRepoFile("src", "AkelEtiket.Core", "Localization", "UiStrings.resx");
        var tr = Read(trFile); var en = Read(trFile.Replace(".resx", ".en.resx"));
        Assert.True(tr.Count > 1200); Assert.Equal(tr.Keys.Order(), en.Keys.Order());
        foreach (var (key, value) in tr)
        {
            Assert.False(string.IsNullOrWhiteSpace(en[key]), key);
            Assert.Equal(NumericHoles(value), NumericHoles(en[key]));
            if (Regex.IsMatch(value, @"\{\d+(?:[:},])"))
            {
                var sourceFormat = System.Text.CompositeFormat.Parse(value);
                var targetFormat = System.Text.CompositeFormat.Parse(en[key]);
                Assert.Equal(sourceFormat.MinimumArgumentCount, targetFormat.MinimumArgumentCount);
                Assert.NotNull(string.Format(CultureInfo.InvariantCulture, targetFormat, Enumerable.Repeat<object>(7, targetFormat.MinimumArgumentCount).ToArray()));
            }
        }
        string views = Path.GetDirectoryName(EditorTestHelpers.FindRepoFile("src", "AkelEtiket.App", "Views", "MainWindow.xaml"))!;
        foreach (string file in Directory.EnumerateFiles(views, "*.xaml"))
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\{loc:Text (T_[a-f0-9]+)\}"))
                Assert.True(tr.ContainsKey(match.Groups[1].Value), file);
        var resources = new ResourceManager("AkelEtiket.Core.Localization.UiStrings", typeof(UiText).Assembly);
        Assert.Equal("Save", resources.GetString(UiText.Key("Kaydet"), CultureInfo.GetCultureInfo("en")));
    }

    [Theory]
    [InlineData("en-US", "12.5", 12.5)]
    [InlineData("en-US", "12,5", 12.5)]
    [InlineData("tr-TR", "12,5", 12.5)]
    [InlineData("tr-TR", "12.5", 12.5)]
    [InlineData("de-DE", "12,5", 12.5)]
    [InlineData("fr-FR", " 12,5 ", 12.5)]
    public void MeasurementsUseRegionAndAcceptEitherDecimalSeparator(string region, string input, double expected)
    { Assert.True(DisplayNumber.TryParse(input, CultureInfo.GetCultureInfo(region), out var actual)); Assert.Equal(expected, actual); }

    [Theory]
    [InlineData("1,234.5")][InlineData("1.234,5")][InlineData("NaN")][InlineData("Infinity")][InlineData("1 234")][InlineData("")]
    public void MeasurementsRejectGroupingAndNonFiniteValues(string input) =>
        Assert.False(DisplayNumber.TryParse(input, CultureInfo.GetCultureInfo("en-US"), out _));

    [Fact]
    public void SavedLabelsAndRawBarcodeDataStayIdenticalAcrossUiLanguages()
    {
        var project = SampleTemplates.CreateBatchLabel(null);
        project.Document.Elements.OfType<TextElement>().First().Text = "Kaydet 東京 {ProductName}";
        var before = JsonSerializer.Serialize(project.Document, ModelJson.FileOptions);
        string tr, en;
        using (new CultureScope("tr")) tr = Fields();
        using (new CultureScope("en")) en = Fields();
        Assert.Equal(tr, en); Assert.Contains("00012345", en); Assert.Contains("12.50", en);
        Assert.Equal(before, JsonSerializer.Serialize(project.Document, ModelJson.FileOptions));
        static string Fields()
        {
            var source = new CompositeFieldSource(new RecordFieldSource(["Barcode", "Price"], ["00012345", "12.50"]), new BuiltInFieldSource(9, new(2026,10,15,13,45,0)));
            return FieldTemplate.Resolve("{Barcode}|{Price}|{TARIH}|{SAAT}|{SIRA:0000}", source).Text;
        }
    }

    [Fact]
    public async Task EnglishExamplesRender_KeepCompanyText_AndUseEnglishColumns()
    {
        using var scope = new CultureScope("en");
        var company = new CompanyProfile { Name = "Örnek Ürün", Address = "Renk: Beyaz", Website = "https://example.org" };
        var samples = InternationalSamples.CreateAll(null, company, "en"); Assert.Equal(8, samples.Count);
        foreach (var (name, project) in samples)
        {
            Assert.DoesNotContain("Etiketi", name);
            if (project.Document.Elements.OfType<TextElement>().Any(t => t.Name is "Company" or "Footer"))
                Assert.Contains(project.Document.Elements.OfType<TextElement>(), t => t.Name == "Company" && t.Text == company.Name || t.Name == "Footer" && t.Text.Contains(company.Address));
            using var renderer = new LabelRenderer(project.Assets);
            var result = renderer.RenderLabel(project.Document, new RenderOptions { DotsPerMm = 8 });
            Assert.False(result.HasErrors, name + ": " + string.Join("; ", result.Errors));
        }
        var batch = samples[1].Project;
        Assert.Contains(batch.Document.Elements.OfType<TextElement>(), t => t.Text == "{Price}");
        Assert.Contains(batch.Document.Elements.OfType<BarcodeElement>(), b => b.Content == "{Barcode}");
        var imported = await DataImporter.ImportAsync(EditorTestHelpers.FindRepoFile("samples", "sample-products.csv"));
        var data = new DataSourceViewModel(); data.ApplyTable(imported);
        Assert.Equal("Quantity", data.CopiesColumn); Assert.Equal(2, data.Rows[1].Copies);
    }

    private static Dictionary<string,string> Read(string path) => XDocument.Load(path).Root!.Elements("data").ToDictionary(d => (string)d.Attribute("name")!, d => d.Element("value")!.Value);
    private static string[] NumericHoles(string value) => Regex.Matches(value, @"\{(\d+)(?:,[+-]?\d+)?(?::[^{}]+)?\}").Select(m => m.Groups[1].Value).Order().ToArray();
    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;
        public CultureScope(string language) => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }
}
