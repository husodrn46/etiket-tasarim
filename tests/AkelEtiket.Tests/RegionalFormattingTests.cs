using System.Globalization;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.Core.Serialization;
using AkelEtiket.ProjectManager;
using AkelEtiket.Rendering;
using AkelEtiket.Editor.Services;
namespace AkelEtiket.Tests;

public sealed class RegionalFormattingTests
{
    [Theory]
    [InlineData("en-US","USD","1,234.56")][InlineData("de-DE","EUR","1.234,56")][InlineData("tr-TR","TRY","1.234,56")]
    public void ExplicitFormatsUseDocumentCultureWithoutChangingRawData(string culture,string currency,string expected)
    {
        var raw=new RecordFieldSource(["Price","Barcode","Date"],["1234.56","00012345","10/15/2026"]);
        var config=new LabelFormatting { CultureName=culture,InputCultureName="en-US",CurrencyCode=currency,DateFormat="yyyy-MM-dd" };
        var source=new RegionalFieldSource(raw,config);
        Assert.Equal(expected,FieldTemplate.Resolve("{Price:NUMBER}",source).Text);
        Assert.Contains(currency,FieldTemplate.Resolve("{Price:CURRENCY}",source).Text);
        Assert.Equal("1234.56|00012345",FieldTemplate.Resolve("{Price}|{Barcode}",source).Text);
        Assert.Equal("2026-10-15",FieldTemplate.Resolve("{Date:DATE}",source).Text);
        Assert.Equal("2026-11-14",FieldTemplate.Resolve("{Date+30:DATE}",source).Text);
    }
    [Theory][InlineData("en-US","01/02/2026","2026-01-02")][InlineData("en-GB","01/02/2026","2026-02-01")]
    public void InputDateRegionIsExplicit(string input,string value,string expected)
    {
        var raw=new RecordFieldSource(["Date"],[value]);var source=new RegionalFieldSource(raw,new() { InputCultureName=input });
        Assert.Equal(expected,FieldTemplate.Resolve("{Date:DATE}",source).Text);
    }
    [Fact] public void BuiltInDateUsesTypedValue_AndLocaleNeverTouchesBarcode()
    {
        var config=new LabelFormatting { CultureName="de-DE",InputCultureName="en-US",CurrencyCode="EUR",DateFormat="yyyy-MM-dd" };
        var fields=new CompositeFieldSource(new RecordFieldSource(["Price","Barcode"],["12.50","00012345"]),new BuiltInFieldSource(1,new(2026,1,2)));
        var project=LabelProject.CreateBlank();project.Document.Formatting=config;
        project.Document.Elements.Add(new TextElement { Text="{TARIH} {Price:CURRENCY}",Width=90,Height=15 });
        project.Document.Elements.Add(new BarcodeElement { Content="{Barcode}",Symbology=BarcodeType.Code128,Y=20,Width=70,Height=25 });
        using var renderer=new LabelRenderer(project.Assets);var output=renderer.RenderLabel(project.Document,new() { Fields=fields,Mode=RenderMode.Print });
        Assert.False(output.HasErrors,string.Join("; ",output.Errors));Assert.Contains("2026-01-02",output.Elements[0].ResolvedContent);Assert.Equal("00012345",output.Elements[1].ResolvedContent);
    }
    [Fact] public void FormattingIsUndoableAndPersistsWithVersionThree_WhileLegacyStaysVersionOne()
    {
        using var session=EditorTestHelpers.CreateSession();Assert.Equal(1,session.Document.GetRequiredFormatVersion());
        using(session.BeginTransaction("Format"))session.Document.Formatting=new() { CultureName="en-GB",CurrencyCode="GBP" };
        Assert.True(session.IsDirty);Assert.Equal(3,session.Document.GetRequiredFormatVersion());session.Undo();Assert.Null(session.Document.Formatting);session.Redo();Assert.Equal("GBP",session.Document.Formatting!.CurrencyCode);
        var copy=ModelJson.DeserializeDocument(ModelJson.Serialize(session.Document));Assert.Equal("en-GB",copy.Formatting!.CultureName);
    }
    [Theory][InlineData("12,34","en-US")][InlineData("1.234,50","en-US")][InlineData("NaN","en-US")]
    public void InvalidNumberFormattingBlocksTextProduction(string value,string input)
    {
        var project=LabelProject.CreateBlank();project.Document.Formatting=new() { InputCultureName=input };
        project.Document.Elements.Add(new TextElement { Text="{Price:NUMBER}" });
        using var renderer=new LabelRenderer(project.Assets);var result=renderer.RenderLabel(project.Document,new() { Fields=new RecordFieldSource(["Price"],[value]),Mode=RenderMode.Print });
        Assert.True(result.HasErrors);
    }
    [Fact] public void ProjectSaveReloadAndPreviewCacheUseSameFormatting()
    {
        string path=Path.Combine(TestDataRoot.Root,"regional-"+Guid.NewGuid().ToString("N")+ProjectFile.Extension);
        try
        {
            var project=LabelProject.CreateBlank();var text=new TextElement { Text="{Price:CURRENCY}",Width=90,Height=20 };project.Document.Elements.Add(text);
            var fields=new RecordFieldSource(["Price"],["1234.56"]);
            using var session=EditorTestHelpers.CreateSession(project);session.SetFields(fields);EditorTestHelpers.WaitForRenders(session);string? before=session.Elements[0].RenderedKey;
            session.Document.Formatting=new() { CultureName="de-DE",InputCultureName="en-US",CurrencyCode="EUR" };
            EditorTestHelpers.WaitForRenders(session);Assert.NotEqual(before,session.Elements[0].RenderedKey);Assert.Contains("EUR",session.Elements[0].RenderResult!.ResolvedContent);
            ProjectFile.Save(project,path);var loaded=ProjectFile.Load(path);Assert.Empty(loaded.Warnings);Assert.Equal(3,loaded.Project.Document.FormatVersion);Assert.Equal("de-DE",loaded.Project.Document.Formatting!.CultureName);
            using var renderer=new LabelRenderer(loaded.Project.Assets);var output=renderer.RenderLabel(loaded.Project.Document,new() { Fields=fields,DotsPerMm=session.DotsPerMm,Mode=RenderMode.Print });
            Assert.False(output.HasErrors);Assert.Equal(session.Elements[0].RenderResult!.ResolvedContent,output.Elements[0].ResolvedContent);
            Assert.Equal(session.Elements[0].RenderResult!.Layer!.Ink.Data,output.Elements[0].Layer!.Ink.Data);
        }
        finally { if(File.Exists(path))File.Delete(path); }
    }
    [Fact] public void InvalidInputRegionDoesNotFallBackToAmbiguousDate()
    {
        var raw=new RecordFieldSource(["Date"],["31/01/2026"]);var source=new RegionalFieldSource(raw,new() { InputCultureName="en-US" });
        Assert.False(source.TryGetDate("Date",out _,out var exists));Assert.True(exists);
    }
    [Fact] public void ExistingFormatsStayTurkishWithoutOptIn_RegardlessOfUiLanguage()
    {
        var old=CultureInfo.CurrentUICulture;try { CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo("en");Assert.Equal("12,50",FieldTemplate.Resolve("{Price:0.00}",new RecordFieldSource(["Price"],["12.50"])).Text); }finally{CultureInfo.CurrentUICulture=old;}
    }
}
