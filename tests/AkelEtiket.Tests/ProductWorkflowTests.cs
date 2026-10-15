using System.Globalization;
using System.Text.Json;
using AkelEtiket.Core;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.Core.Serialization;
using AkelEtiket.DataImport;
using AkelEtiket.Editor.Printing;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.Templates;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;

namespace AkelEtiket.Tests;

[Collection("Yapay zekâ masaüstü")]
public sealed class ProductWorkflowTests : IDisposable
{
    private readonly string _root=Path.Combine(TestDataRoot.Root,"product-"+Guid.NewGuid().ToString("N"));
    public ProductWorkflowTests()=>Directory.CreateDirectory(_root);
    public void Dispose()=>Directory.Delete(_root,true);
    private MainViewModel Main(ScriptedDialogs? dialogs=null)
    {
        var store=new JsonSettingsStore<AppSettings>(Path.Combine(_root,"settings.json"));
        if(!File.Exists(store.FilePath))store.Save(new AppSettings { FirstRunCompleted=true,OnboardingCompleted=true,TemplatesFolder=Path.Combine(_root,"templates") });
        return new(new AppServices { Dialogs=dialogs??new(), Clipboard=new FakeClipboard(),Dispatcher=new ImmediateDispatcher(),ImageFactory=new FakeImageFactory(),SettingsStore=store,RecoveryFolder=Path.Combine(_root,"recovery") });
    }
    private sealed class FakeClipboard : IClipboardService
    {
        public void SetElements(string json,string text) { }
        public string? GetElements()=>null;
        public bool ContainsElements()=>false;
        public byte[]? GetImagePng()=>null;
        public string? GetText()=>null;
    }
    [Fact] public void PersonalizationDefaultsAndMalformedValuesAreSafe()
    {
        var settings=new AppSettings { Personalization=new() { UiScale=double.NaN,Accent="unknown",DefaultFontFamily="",ActivePanel="broken" } };
        settings.Normalize();Assert.Equal(1,settings.Personalization.UiScale);Assert.Equal("red",settings.Personalization.Accent);Assert.Equal("Arial",settings.Personalization.DefaultFontFamily);
        var vm=new SettingsViewModel(settings){ Accent="teal",UiScale=1.25,DefaultFontFamily="Courier New",RememberPanelLayout=false };
        Assert.Equal("red",settings.Personalization.Accent);vm.ApplyTo(settings);
        var saved=ModelJson.Clone(settings);Assert.Equal("teal",saved.Personalization.Accent);Assert.Equal(1.25,saved.Personalization.UiScale);Assert.False(saved.Personalization.RememberPanelLayout);
    }
    [Fact] public void DefaultFontOnlyAffectsNewElements_AndLayoutPersists()
    {
        using(var main=Main())
        {
            main.AddTextCommand.Execute(null);var original=Assert.IsType<TextElement>(main.Session.PrimarySelection!.Model);string font=original.FontFamily;
            main.Settings.Personalization.DefaultFontFamily="Courier New";main.AddTextCommand.Execute(null);
            Assert.Equal("Courier New",Assert.IsType<TextElement>(main.Session.PrimarySelection!.Model).FontFamily);Assert.Equal(font,original.FontFamily);
            main.ActivePanel=LeftPanel.Data;main.IsPanelOpen=false;main.IsAiPanelOpen=true;main.SaveSettings();
        }
        using var restored=Main();Assert.Equal(LeftPanel.Data,restored.ActivePanel);Assert.False(restored.IsPanelOpen);Assert.True(restored.IsAiPanelOpen);
    }
    [Fact] public void StartupTemplateOpensAsNewCopy_AndCommandLineWins()
    {
        string first=Path.Combine(_root,"startup.akelabel"),second=Path.Combine(_root,"explicit.akelabel");
        var template=SampleTemplates.CreateAssetLabel();template.Document.Name="Startup";ProjectFile.Save(template,first);
        var other=SampleTemplates.CreateShelfLabel();other.Document.Name="Explicit";ProjectFile.Save(other,second);
        using(var main=Main()){main.Settings.Personalization.StartupTemplatePath=first;main.SaveSettings();main.Initialize([]);Assert.Equal("Startup",main.Session.Document.Name);Assert.Null(main.Session.Project.FilePath);}
        using(var main=Main()){main.Initialize([second]);Assert.Equal("Explicit",main.Session.Document.Name);}
        Assert.Equal("Startup",ProjectFile.Load(first).Project.Document.Name);
    }
    [Fact] public void TemplateFavoritesPersist_FilterAndPrioritizeQuickPrint()
    {
        using(var main=Main())
        {
            main.Templates.Library.SaveAsTemplate(SampleTemplates.CreateShelfLabel(),"Shelf",null,false);
            main.Templates.Library.SaveAsTemplate(SampleTemplates.CreateAssetLabel(),"Asset",null,false);main.Templates.Refresh();
            main.Templates.SelectedItem=main.Templates.Items.Single(i=>i.Name=="Shelf");main.Templates.ToggleFavoriteCommand.Execute(null);
            main.Templates.OnlyFavorites=true;Assert.Equal("Shelf",Assert.Single(main.Templates.Items).Name);
            var quick=new QuickPrintViewModel(main,new ProductsViewModel(new Design.Data.ProductCatalog(Path.Combine(_root,"products")),main.Dialogs));
            Assert.StartsWith("★ ",quick.Templates[0].Name);
        }
        using var restored=Main();restored.Templates.Refresh();restored.Templates.OnlyFavorites=true;Assert.Equal("Shelf",Assert.Single(restored.Templates.Items).Name);
    }
    [Fact] public void FavoriteJobsKeepSnapshotsAndUseExistingReprintRequest()
    {
        var store=new PrintHistoryStore(Path.Combine(_root,"history"));string? snapshot=store.SaveSnapshot(SampleTemplates.CreateAssetLabel());Assert.NotNull(snapshot);
        var entry=new PrintHistoryEntry { Id="job-1",DocumentName="Asset",SnapshotFile=snapshot };store.Append(entry);
        List<string> persisted=[];var vm=new PrintHistoryViewModel(store,[],ids=>{persisted=ids;return true;});
        vm.ToggleFavoriteCommand.Execute(null);Assert.Equal("job-1",Assert.Single(persisted));
        File.SetLastWriteTimeUtc(store.SnapshotPath(entry)!,DateTime.UtcNow.AddDays(-400));Assert.Equal(0,store.PruneSnapshots(TimeSpan.FromDays(365)));
        vm.OnlyFavorites=true;Assert.Single(vm.Entries);Assert.False(vm.OpenJobCommand.CanExecute(new PrintHistoryEntry()));vm.OpenJobCommand.Execute(entry);Assert.NotNull(vm.Request);Assert.False(vm.Request!.ResumeOnly);
        vm.ToggleFavoriteCommand.Execute(null);Assert.Empty(vm.Entries);
    }
    [Theory][InlineData("UrunAdi","ProductName")][InlineData("Fiyat","Price")][InlineData("UrunKodu","SKU")][InlineData("Adet","Quantity")]
    public void ColumnSuggestionsUseMeaningfulAliases(string field,string source)=>Assert.Equal(source,ColumnSuggestions.Suggest(field,[source]));
    [Fact] public void ColumnSuggestionsRejectAmbiguityAndPreserveExactHeaders()
    {
        Assert.Null(ColumnSuggestions.Suggest("Barkod",["EAN","GTIN"]));
        Assert.Equal("Ürün Kodu",ColumnSuggestions.Suggest("Ürün Kodu",["Ürün Kodu","UrunKodu"]));
        Assert.Equal("UrunKodu",ColumnSuggestions.Suggest("UrunKodu",["Ürün Kodu","UrunKodu"]));
        Assert.Null(ColumnSuggestions.Suggest("Price",["Barcode"]));
    }
    [Fact] public void ImportProfilesRoundTripWithoutRowsPaths_AndMissingColumnsRequireReview()
    {
        var doc=LabelDocument.CreateBlank();doc.Elements.Add(new TextElement { Text="{UrunAdi} {Fiyat}" });
        var table=new DataSourceTable("private-source.csv",DataSourceTable.KindCsv,null,1,["ProductName","Price"],[new DataRecord(2,["Sensitive product","12.50"],["ProductName","Price"])],[]);
        var profile=new ImportMappingProfile { Name="Supplier schema",Delimiter=';',EncodingName="utf-8",Mappings=new() { ["UrunAdi"]="ProductName",["Fiyat"]="Cost" } };
        string json=JsonSerializer.Serialize(profile,ModelJson.FileOptions);Assert.DoesNotContain("Sensitive product",json);Assert.DoesNotContain("private-source",json);
        var copy=ModelJson.Clone(profile);Assert.Equal(';',copy.ToImportOptions().Delimiter);
        var vm=new ColumnMappingViewModel(doc,table);vm.ConfigureProfiles([copy],new ScriptedDialogs(),_=>true,copy.Id);
        Assert.Null(vm.Rows.Single(r=>r.Field=="Fiyat").SelectedColumn);Assert.False(vm.IsValid);Assert.Contains("1",vm.ProfileMessage);
    }
    [Fact] public void FailedFavoriteSettingsSaveLeavesPreferencesUnchanged()
    {
        var dialogs=new ScriptedDialogs();using var main=Main(dialogs);
        main.Templates.Library.SaveAsTemplate(SampleTemplates.CreateAssetLabel(),"Asset",null,false);main.Templates.Refresh();main.Templates.SelectedItem=main.Templates.Items[0];
        string path=Path.Combine(_root,"settings.json");File.Delete(path);Directory.CreateDirectory(path);
        main.Templates.ToggleFavoriteCommand.Execute(null);
        Assert.Empty(main.Settings.FavoriteTemplatePaths);Assert.Single(dialogs.Messages);
        Directory.Delete(path);
    }
    [Fact] public void SavedImportProfileContainsSchemaOnly_AndCanBeReused()
    {
        var doc=LabelDocument.CreateBlank();doc.Elements.Add(new TextElement { Text="{UrunAdi} {Fiyat}" });
        var table=new DataSourceTable("private.csv",DataSourceTable.KindCsv,null,1,["ProductName","Price"],[new DataRecord(2,["Private item","9.75"],["ProductName","Price"])],[]);
        ImportMappingProfile? saved=null;var vm=new ColumnMappingViewModel(doc,table);vm.ConfigureProfiles([],new ScriptedDialogs { PromptAnswer="Supplier" },p=>{saved=p;return true;});vm.SaveProfileCommand.Execute(null);
        Assert.NotNull(saved);Assert.Equal("ProductName",saved.Mappings["UrunAdi"]);Assert.DoesNotContain("Private item",JsonSerializer.Serialize(saved));
        var mapped=vm.CreateMappedDocument();var reuse=new ColumnMappingViewModel(mapped,table);reuse.ConfigureProfiles([ModelJson.Clone(saved)],new ScriptedDialogs(),_=>true,saved.Id);Assert.True(reuse.IsValid);
        var settings=new AppSettings { ImportProfiles=[saved] };settings.Normalize();Assert.Equal("Supplier",ModelJson.Clone(settings).ImportProfiles[0].Name);
    }
    [Theory][InlineData("Windows-1254 (Türkçe)",1254)][InlineData("Windows-1254 (Turkish)",1254)][InlineData("UTF-16 LE",1200)][InlineData("utf-16BE",1201)][InlineData("UTF-32 LE",12000)]
    public void ImportProfileAndReprintEncodingSurviveLanguageChanges(string name,int codePage)
    {
        Assert.Equal(codePage,ImportChoices.ResolveEncoding(name)!.CodePage);
        Assert.Equal(codePage,new ImportMappingProfile { EncodingName=name }.ToImportOptions().Encoding!.CodePage);
    }
    [Fact] public async Task RawExcelBooleansAndGeneratedHeadersStayStableAcrossLanguages()
    {
        string path=Path.Combine(_root,"booleans.xlsx");using(var workbook=new ClosedXML.Excel.XLWorkbook())
        { var sheet=workbook.AddWorksheet("Data");sheet.Cell(1,1).Value="Item";sheet.Cell(2,1).Value="Demo";sheet.Cell(2,2).Value=true;sheet.Cell(2,3).Value=false;workbook.SaveAs(path); }
        var old=CultureInfo.CurrentUICulture;try
        {
            CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo("tr");var tr=await DataImporter.ImportAsync(path);
            CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo("en");var en=await DataImporter.ImportAsync(path);
            Assert.Equal(tr.Columns,en.Columns);Assert.Contains("Sütun 2",en.Columns);Assert.Equal(tr.Rows[0].Values,en.Rows[0].Values);Assert.Equal("DOĞRU",en.Rows[0].Values[1]);Assert.Equal("YANLIŞ",en.Rows[0].Values[2]);
            Assert.Contains(en.Issues,i=>i.Message.Contains("header",StringComparison.OrdinalIgnoreCase));
        }
        finally { CultureInfo.CurrentUICulture=old; }
    }
    [Fact] public void PortableBackupExcludesMachineWorkflowPreferences()
    {
        using var main=Main();main.Settings.Personalization.StartupTemplatePath="private-path.akelabel";main.Settings.FavoriteTemplatePaths.Add("private.akelabel");main.Settings.FavoritePrintJobIds.Add("private-job");main.Settings.ImportProfiles.Add(new() { Name="Private supplier",Mappings=new() { ["Internal"]="SupplierColumn" } });
        string path=Path.Combine(_root,"company.etiketyedek");WorkspaceBackup.Export(path,main.Settings,main.Templates.Library,new());var content=WorkspaceBackup.Read(path);
        Assert.Null(content.Settings.Personalization.StartupTemplatePath);Assert.Empty(content.Settings.FavoriteTemplatePaths);Assert.Empty(content.Settings.FavoritePrintJobIds);Assert.Empty(content.Settings.ImportProfiles);
    }
    [Theory][InlineData("tr")][InlineData("en")]
    public async Task EveryStarterPackRendersWithItsCsv_WithoutGs1OrFileOverwrite(string language)
    {
        var old=CultureInfo.CurrentUICulture;CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo(language);
        try
        {
            using var main=Main();var vm=new StarterPacksViewModel(main);
            foreach(var pack in StarterPacks.All)
            {
                var table=await DataImporter.ImportAsync(EditorTestHelpers.FindRepoFile("samples","packs",pack.Id,"sample.csv"));
                foreach(var(name,project)in StarterPacks.Create(pack.Id,new CompanyProfile { Name="Acme",Address="Demo address" }))
                {
                    using var renderer=new LabelRenderer(project.Assets);
                    var result=renderer.RenderLabel(project.Document,new RenderOptions { DotsPerMm=8,Mode=RenderMode.Print,Fields=table.Rows[0].ToFieldSource(table.Columns) });
                    Assert.False(result.HasErrors,name+": "+string.Join("; ",result.Errors));
                    Assert.DoesNotContain(project.Document.Elements.OfType<BarcodeElement>(),b=>BarcodeElement.IsGs1(b.Symbology));
                }
                vm.SelectedPack=pack;vm.InstallCommand.Execute(null);
            }
            Assert.Equal(8,main.Templates.Library.List().Count);vm.InstallCommand.Execute(null);Assert.Equal(10,main.Templates.Library.List().Count);
        }
        finally { CultureInfo.CurrentUICulture=old; }
    }
}
