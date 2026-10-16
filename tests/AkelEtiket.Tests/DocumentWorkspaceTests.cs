using AkelEtiket.Core.Data;
using AkelEtiket.Core.Model;
using AkelEtiket.DataImport;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.ProjectManager;

namespace AkelEtiket.Tests;

[Collection("Yapay zekâ masaüstü")]
public sealed class DocumentWorkspaceTests : IDisposable
{
    private readonly string _root=Path.Combine(TestDataRoot.Root,"tabs-"+Guid.NewGuid().ToString("N"));
    public DocumentWorkspaceTests()=>Directory.CreateDirectory(_root);
    public void Dispose()=>Directory.Delete(_root,true);
    private MainViewModel Create(WorkspaceDialogs? dialogs=null) => new(new AppServices
    {
        Dialogs=dialogs??new(),Clipboard=new Clipboard(),Dispatcher=new ImmediateDispatcher(),ImageFactory=new FakeImageFactory(),
        SettingsStore=new JsonSettingsStore<AppSettings>(Path.Combine(_root,"settings.json")),
        InitialSettings=new() { FirstRunCompleted=true,OnboardingCompleted=true,TemplatesFolder=Path.Combine(_root,"templates") },
        RecoveryFolder=Path.Combine(_root,"recovery"),
    });
    private string SaveFile(string name)
    {
        var project=LabelProject.CreateBlank();project.Document.Name=name;project.Document.Elements.Add(new TextElement { Text=name });
        string path=Path.Combine(_root,name+ProjectFile.Extension);ProjectFile.Save(project,path);return path;
    }
    private static DataSourceTable Table(string source,string value) => new(source,DataSourceTable.KindCsv,null,1,["SKU"],[new DataRecord(2,[value],["SKU"])],[]);
    [Fact] public void SessionsKeepIndependentDataUndoSelectionAndZoom()
    {
        using var main=Create();string a=SaveFile("A"),b=SaveFile("B");Assert.True(main.OpenProject(a,true));var first=main.ActiveDocument!;
        main.Data.ApplyTable(Table("a.csv","001"));main.Data.Rows[0].Copies=7;main.Zoom=3;
        main.AddTextCommand.Execute(null);string? original=main.Session.PrimarySelection?.Id;
        Assert.True(main.OpenProject(b,true));var second=main.ActiveDocument!;Assert.Equal(2,main.Documents.Count);Assert.NotSame(first.Data,second.Data);Assert.False(main.Data.HasData);
        main.Data.ApplyTable(Table("b.csv","002"));main.Data.Rows[0].Copies=2;main.Zoom=1.1;
        main.ActiveDocument=first;Assert.Equal(3,main.Zoom);Assert.Equal(7,main.Data.Rows[0].Copies);Assert.Equal("001",FieldTemplate.Resolve("{SKU}",main.Session.Fields).Text);Assert.Equal(original,main.Session.PrimarySelection?.Id);Assert.True(main.Session.CanUndo);
        int bCount=second.Session.Document.Elements.Count;main.UndoCommand.Execute(null);Assert.Equal(bCount,second.Session.Document.Elements.Count);
        main.ActiveDocument=second;Assert.Equal(1.1,main.Zoom);Assert.Equal("002",FieldTemplate.Resolve("{SKU}",main.Session.Fields).Text);
    }
    [Fact] public void OpeningSamePathActivatesDirtyTabWithoutReloadOrPrompt()
    {
        var dialogs=new WorkspaceDialogs();using var main=Create(dialogs);string path=SaveFile("A");main.OpenProject(path,true);var first=main.ActiveDocument!;
        ((TextElement)main.Session.Document.Elements[0]).Text="Unsaved";main.OpenProject(SaveFile("B"),true);
        Assert.True(main.OpenProject(path,true));Assert.Same(first,main.ActiveDocument);Assert.Equal(2,main.Documents.Count);Assert.Equal("Unsaved",((TextElement)main.Session.Document.Elements[0]).Text);Assert.Empty(dialogs.SaveQuestions);
    }
    [Fact] public void CancelClosingDirtyTabPreservesEveryDocument()
    {
        var dialogs=new WorkspaceDialogs();using var main=Create(dialogs);main.OpenProject(SaveFile("A"),false);var first=main.ActiveDocument!;main.AddTextCommand.Execute(null);
        main.OpenProject(SaveFile("B"),false);var second=main.ActiveDocument!;dialogs.Choices.Enqueue(SaveChangesChoice.Cancel);
        main.CloseDocumentCommand.Execute(first);Assert.Equal(2,main.Documents.Count);Assert.Same(second,main.ActiveDocument);Assert.False(first.IsDisposed);Assert.True(first.IsDirty);
    }
    [Fact] public void CancelApplicationCloseKeepsAllRecoverySnapshotsAndTabs()
    {
        var dialogs=new WorkspaceDialogs();using var main=Create(dialogs);
        main.OpenProject(SaveFile("A"),false);main.AddTextCommand.Execute(null);main.OpenProject(SaveFile("B"),false);main.AddTextCommand.Execute(null);
        main.AutoRecoveryTick();Assert.Equal(2,Directory.GetFiles(Path.Combine(_root,"recovery"),"*"+ProjectFile.Extension).Length);
        var selected=main.ActiveDocument;dialogs.Choices.Enqueue(SaveChangesChoice.Discard);dialogs.Choices.Enqueue(SaveChangesChoice.Cancel);
        Assert.False(main.ConfirmCloseAllDocuments());Assert.Same(selected,main.ActiveDocument);Assert.Equal(2,main.Documents.Count);Assert.All(main.Documents,t=>Assert.False(t.IsDisposed));
        Assert.Equal(2,Directory.GetFiles(Path.Combine(_root,"recovery"),"*"+ProjectFile.Extension).Length);
    }
    [Fact] public void SaveAsCannotOverwriteAnotherOpenDocument()
    {
        var dialogs=new WorkspaceDialogs();using var main=Create(dialogs);string a=SaveFile("A"),b=SaveFile("B");main.OpenProject(a,false);var first=main.ActiveDocument!;main.AddTextCommand.Execute(null);main.OpenProject(b,false);
        main.ActiveDocument=first;dialogs.SavePaths.Enqueue(b);Assert.False(main.SaveAs());Assert.True(first.IsDirty);Assert.Equal("B",ProjectFile.Load(b).Project.Document.Name);Assert.Contains(dialogs.Messages,m=>m.Contains("başka",StringComparison.OrdinalIgnoreCase));
    }
    [Fact] public void SaveAllWritesEachDocumentAndRestoresActiveTab()
    {
        var dialogs=new WorkspaceDialogs();using var main=Create(dialogs);
        main.OpenProjectAsNew(ProjectFile.Load(SaveFile("A")).Project,"A");var first=main.ActiveDocument!;
        main.OpenProjectAsNew(ProjectFile.Load(SaveFile("B")).Project,"B");Assert.All(main.Documents,t=>Assert.True(t.IsDirty));
        main.ActiveDocument=first;dialogs.SavePaths.Enqueue(Path.Combine(_root,"Saved-A.akelabel"));dialogs.SavePaths.Enqueue(Path.Combine(_root,"Saved-B.akelabel"));
        main.SaveAllCommand.Execute(null);Assert.Same(first,main.ActiveDocument);Assert.All(main.Documents,t=>Assert.False(t.IsDirty));Assert.False(main.SaveAllCommand.CanExecute(null));
        Assert.Equal("A",ProjectFile.Load(Path.Combine(_root,"Saved-A.akelabel")).Project.Document.Name);Assert.Equal("B",ProjectFile.Load(Path.Combine(_root,"Saved-B.akelabel")).Project.Document.Name);
    }
    [Fact] public void CancellingSaveAllDoesNotCloseOrDiscardTheRemainingTab()
    {
        var dialogs=new WorkspaceDialogs();using var main=Create(dialogs);
        main.OpenProjectAsNew(ProjectFile.Load(SaveFile("A")).Project,"A");var first=main.ActiveDocument!;main.OpenProjectAsNew(ProjectFile.Load(SaveFile("B")).Project,"B");var second=main.ActiveDocument!;
        dialogs.SavePaths.Enqueue(Path.Combine(_root,"Saved-A.akelabel"));dialogs.SavePaths.Enqueue(null);main.SaveAllCommand.Execute(null);
        Assert.Equal(2,main.Documents.Count);Assert.Same(second,main.ActiveDocument);Assert.False(first.IsDirty);Assert.True(second.IsDirty);Assert.False(second.IsDisposed);
    }
    [Fact] public void PinnedTabsSurviveClosingOthers_AndLastCloseCreatesBlankWorkspace()
    {
        using var main=Create();main.OpenProject(SaveFile("A"),false);var first=main.ActiveDocument!;main.PinDocumentCommand.Execute(first);
        main.OpenProject(SaveFile("B"),false);var second=main.ActiveDocument!;main.OpenProject(SaveFile("C"),false);var third=main.ActiveDocument!;
        main.CloseOtherDocumentsCommand.Execute(second);Assert.Equal(2,main.Documents.Count);Assert.Contains(first,main.Documents);Assert.True(third.IsDisposed);
        main.CloseDocumentCommand.Execute(first);main.CloseDocumentCommand.Execute(second);Assert.Single(main.Documents);Assert.Null(main.Session.Project.FilePath);Assert.Empty(main.Session.Document.Elements);
    }
    [Fact] public void DuplicateCopiesArtworkAndRowsWithoutSharingMutableWorkspaceState()
    {
        using var main=Create();main.OpenProject(SaveFile("A"),false);var first=main.ActiveDocument!;main.Data.ApplyTable(Table("a.csv","001"));main.Data.Rows[0].Copies=4;
        main.DuplicateDocumentCommand.Execute(null);Assert.Equal(2,main.Documents.Count);Assert.Null(main.Session.Project.FilePath);Assert.True(main.Session.IsDirty);Assert.NotSame(first.Session.Assets,main.Session.Assets);
        ((TextElement)main.Session.Document.Elements[0]).Text="Changed copy";main.Data.Rows[0].Copies=9;
        Assert.Equal("A",((TextElement)first.Session.Document.Elements[0]).Text);Assert.Equal(4,first.Data.Rows[0].Copies);Assert.Equal("001",main.Data.Rows[0].Record.GetValue("SKU"));
    }
    [Fact] public async Task ImportFinishesOnItsOriginatingTabWithoutSwitchingOrChangingAnotherTab()
    {
        using var main=Create();main.OpenProject(SaveFile("A"),false);var first=main.ActiveDocument!;
        string csv=Path.Combine(_root,"large.csv");await File.WriteAllTextAsync(csv,"SKU,ProductName\n"+string.Join('\n',Enumerable.Range(0,20000).Select(i=>$"SKU-{i},Product {i}")));
        var import=main.LoadDataFileAsync(csv,null);main.OpenProject(SaveFile("B"),false);var second=main.ActiveDocument!;await import;
        Assert.Same(second,main.ActiveDocument);Assert.False(second.Data.HasData);Assert.Equal(20000,first.Data.Rows.Count);Assert.DoesNotContain("large.csv",main.StatusMessage);
    }
    [Fact] public async Task AiMutationTargetsOriginalInactiveTabBySessionId()
    {
        using var main=Create();main.OpenProject(SaveFile("A"),false);var first=main.ActiveDocument!;main.OpenProject(SaveFile("B"),false);var second=main.ActiveDocument!;
        Assert.Equal(2,main.Ai.LiveHost.GetOpenDocuments().Count);
        await main.Ai.LiveHost.MutateAsync(first.Session.SessionNumber,first.Session.Version,"Change A",(document,_)=>{ ((TextElement)document.Elements[0]).Text="AI changed A";return true; },default);
        Assert.Same(second,main.ActiveDocument);Assert.Equal("B",((TextElement)second.Session.Document.Elements[0]).Text);Assert.Equal("AI changed A",((TextElement)first.Session.Document.Elements[0]).Text);Assert.True(first.RecoveryDirty);
    }
    [Fact] public void SingleDocumentPreferenceRetainsSaveCancelBehavior()
    {
        var dialogs=new WorkspaceDialogs();using var main=Create(dialogs);main.Settings.Personalization.OpenDocumentsInTabs=false;
        main.OpenProject(SaveFile("A"),false);var first=main.ActiveDocument!;main.AddTextCommand.Execute(null);dialogs.Choices.Enqueue(SaveChangesChoice.Cancel);
        Assert.False(main.OpenProject(SaveFile("B"),true));Assert.Same(first,main.ActiveDocument);Assert.Single(main.Documents);
        dialogs.Choices.Enqueue(SaveChangesChoice.Discard);Assert.True(main.OpenProject(Path.Combine(_root,"B.akelabel"),true));Assert.Single(main.Documents);Assert.True(first.IsDisposed);
    }
    [Fact] public void RecoveryOpensValidOrphansAndPreservesUnreadableCopy()
    {
        using var main=Create();string recovery=Path.Combine(_root,"recovery");Directory.CreateDirectory(recovery);
        var project=LabelProject.CreateBlank();project.Document.Name="Recovered";project.Document.Elements.Add(new TextElement { Text="Unsaved work" });
        string valid=Path.Combine(recovery,"oturum-valid.akelabel"),invalid=Path.Combine(recovery,"oturum-invalid.akelabel");ProjectFile.Save(project,valid);File.WriteAllText(invalid,"damaged file");
        main.Initialize([]);Assert.Contains(main.Documents,t=>t.Name=="Recovered");Assert.True(main.Session.IsDirty);Assert.True(File.Exists(invalid));Assert.False(File.Exists(valid));
        main.AutoRecoveryTick();Assert.True(Directory.GetFiles(recovery,"*.akelabel").Length>=2);
    }
    [Fact] public void TabLimitKeepsCurrentWorkAndAllowsOpeningAfterClosing()
    {
        var dialogs=new WorkspaceDialogs();using var main=Create(dialogs);
        for (int i=0;i<MainViewModel.MaximumOpenDocuments;i++)main.OpenProjectAsNew(LabelProject.CreateBlank(),"New");
        var active=main.ActiveDocument;main.OpenProjectAsNew(LabelProject.CreateBlank(),"Extra");Assert.Equal(MainViewModel.MaximumOpenDocuments,main.Documents.Count);Assert.Same(active,main.ActiveDocument);Assert.Single(dialogs.Messages);
        main.CloseDocumentCommand.Execute(main.Documents[0]);main.OpenProjectAsNew(LabelProject.CreateBlank(),"Now fits");Assert.Equal(MainViewModel.MaximumOpenDocuments,main.Documents.Count);
    }
    [Fact] public void TabAndAnimationPreferencesRemainDraftUntilAppliedAndRoundTrip()
    {
        var settings=new AppSettings();var draft=new SettingsViewModel(settings) { OpenDocumentsInTabs=false,EnableAnimations=false };
        Assert.True(settings.Personalization.OpenDocumentsInTabs);Assert.True(settings.Personalization.EnableAnimations);draft.ApplyTo(settings);
        var copy=AkelEtiket.Core.Serialization.ModelJson.Clone(settings);Assert.False(copy.Personalization.OpenDocumentsInTabs);Assert.False(copy.Personalization.EnableAnimations);
    }
    private sealed class WorkspaceDialogs : IDialogService
    {
        public Queue<SaveChangesChoice> Choices { get; }=[];
        public Queue<string?> SavePaths { get; }=[];
        public List<string> SaveQuestions { get; }=[];
        public List<string> Messages { get; }=[];
        public string? ShowOpenFileDialog(string title,string filter,string? initialDirectory=null)=>null;
        public string? ShowSaveFileDialog(string title,string filter,string defaultFileName,string? initialDirectory=null)=>SavePaths.TryDequeue(out var p)?p:null;
        public void ShowMessage(string title,string message,MessageKind kind=MessageKind.Info)=>Messages.Add(message);
        public bool Confirm(string title,string message,string confirmText="Evet",string cancelText="Hayır")=>true;
        public SaveChangesChoice AskSaveChanges(string documentName) { SaveQuestions.Add(documentName);return Choices.TryDequeue(out var c)?c:SaveChangesChoice.Discard; }
        public string? Prompt(string title,string message,string defaultValue="")=>defaultValue;
        public bool ShowDialog(object viewModel)=>viewModel is LabelSetupViewModel;
    }
    private sealed class Clipboard : IClipboardService
    {
        public void SetElements(string payloadJson,string plainText) { }
        public string? GetElements()=>null;public bool ContainsElements()=>false;public byte[]? GetImagePng()=>null;public string? GetText()=>null;
    }
}
