using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AkelEtiket.App.Services;
using AkelEtiket.App.Views;
using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.ViewModels;
using AkelEtiket.ProjectManager;

internal static class Program
{
    private static readonly List<object> Checks=[];
    private static string _output="";
    private static void Check(bool condition,string name)
    {
        Checks.Add(new { name,passed=condition });
        Console.WriteLine($"{(condition?"PASS":"FAIL")} {name}");
        if(!condition) throw new InvalidOperationException(name);
    }
    [STAThread] private static int Main(string[] args)
    {
        _output=Path.GetFullPath(args.FirstOrDefault()??"windows-smoke-results");Directory.CreateDirectory(_output);
        string root=Path.Combine(Path.GetTempPath(),"LabelHarborSmoke-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("ETIKET_VERI",root);
        CultureInfo.DefaultThreadCurrentUICulture=CultureInfo.GetCultureInfo("en");
        var app=new AkelEtiket.App.App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        bool success=false;
        try
        {
            var dialogs=new Dialogs();var settings=new AppSettings { FirstRunCompleted=true,OnboardingCompleted=true,TemplatesFolder=Path.Combine(root,"templates"),Theme=AppTheme.Light };
            using var vm=new MainViewModel(new AppServices { Dialogs=dialogs,Clipboard=new ClipboardStub(),Dispatcher=new WpfDispatcher(Dispatcher.CurrentDispatcher),ImageFactory=new WpfLayerImageFactory(),SettingsStore=new JsonSettingsStore<AppSettings>(Path.Combine(root,"settings.json")),InitialSettings=settings,RecoveryFolder=Path.Combine(root,"recovery") });
            var window=new MainWindow(vm) { Width=1440,Height=900,WindowState=WindowState.Normal };
            app.MainWindow=window;window.Show();Pump();
            var tabs=(ListBox)window.FindName("DocumentTabs");
            string a=Save(root,"Retail label"),b=Save(root,"Warehouse label");vm.OpenProject(a,true);Pump();var first=vm.ActiveDocument!;
            vm.AddTextCommand.Execute(null);vm.Zoom=2.5;vm.OpenProject(b,true);Pump();var second=vm.ActiveDocument!;
            Check(tabs.Items.Count==2 && ReferenceEquals(tabs.SelectedItem,second),"Document strip selects the active independent session");
            tabs.SelectedItem=first;Pump();Check(ReferenceEquals(vm.Session,first.Session)&&Math.Abs(vm.Zoom-2.5)<.001,"Tab selection restores its document and zoom");
            dialogs.Choice=SaveChangesChoice.Cancel;vm.CloseDocumentCommand.Execute(first);Pump();Check(vm.Documents.Count==2&&!first.IsDisposed,"Cancel close preserves every tab");
            vm.PinDocumentCommand.Execute(first);vm.DuplicateDocumentCommand.Execute(null);Pump();Check(vm.Documents.Count==3&&vm.Session.IsDirty,"Duplicate opens an independent unsaved label");
            Capture(window,"light-tabs-1440.png");
            CheckVisibleBounds(window,tabs,"Tabs remain within the client area");
            var focus=(Button)window.FindName("ToggleFocusMode");Invoke(focus);Pump();Check(vm.IsFocusMode&&((Grid)window.FindName("MainGrid")).ColumnDefinitions[4].ActualWidth==0,"Focus mode hides the inspector");
            Capture(window,"light-focus-1440.png");Invoke(focus);Pump();
            vm.Settings.Theme=AppTheme.Dark;ThemeManager.Configure(vm.Settings);Pump();Capture(window,"dark-tabs-1440.png");
            window.Width=1080;window.Height=720;vm.Settings.Personalization.UiScale=1.25;ThemeManager.Configure(vm.Settings);Pump();
            CheckVisibleBounds(window,tabs,"Scaled document strip remains within the client area");Capture(window,"dark-compact-scale125.png");
            vm.Settings.Personalization.EnableAnimations=false;ThemeManager.Configure(vm.Settings);Motion.Pulse(tabs);Check(!Motion.IsEnabled&&!tabs.HasAnimatedProperties,"Reduced motion resolves immediately without animation clocks");
            var preferences=new SettingsWindow(new SettingsViewModel(vm.Settings)) { Owner=window };preferences.Show();Pump();Capture(preferences,"settings-english.png");preferences.Close();
            vm.Settings.Personalization.UiScale=1;vm.Settings.Personalization.EnableAnimations=true;ThemeManager.Configure(vm.Settings);window.Width=1440;window.Height=900;Pump();
            Motion.Pulse(tabs);bool allowed=Motion.IsEnabled;Check(!allowed||tabs.HasAnimatedProperties,"Allowed motion creates a short feedback clock");
            Motion.Configure(false);Check(!tabs.HasAnimatedProperties,"Disabling motion clears running clocks");
            Capture(window,"dark-capture200.png",2);
            dialogs.Choice=SaveChangesChoice.Discard;window.Close();Check(!window.IsVisible,"Close resolves all document decisions and closes the native window");
            Checks.Add(new { name="Capture environment",dpi=VisualTreeHelper.GetDpi(tabs).PixelsPerInchX,capture200="Raster capture scale only; not a physical monitor DPI test",clientAnimation=SystemParameters.ClientAreaAnimation,highContrast=SystemParameters.HighContrast });
            success=true;return 0;
        }
        catch(Exception ex) { File.WriteAllText(Path.Combine(_output,"error.txt"),ex.ToString());Console.Error.WriteLine(ex);return 1; }
        finally
        {
            File.WriteAllText(Path.Combine(_output,"results.json"),JsonSerializer.Serialize(new { success,checks=Checks,physicalPrinterTested=false,installerTested=false },new JsonSerializerOptions { WriteIndented=true }));
            app.Shutdown();
            try{Directory.Delete(root,true);}catch(IOException){}
        }
    }
    private static string Save(string root,string name)
    {
        var project=LabelProject.CreateBlank(100,60);project.Document.Name=name;
        project.Document.Elements.Add(new TextElement { Text="LabelHarbor",X=7,Y=8,Width=80,Height=12 });
        project.Document.Elements.Add(new TextElement { Text="Offline label & barcode designer",X=7,Y=25,Width=85,Height=9 });
        string path=Path.Combine(root,name+ProjectFile.Extension);ProjectFile.Save(project,path);return path;
    }
    private static void Pump()
    {
        var frame=new DispatcherFrame();var timer=new DispatcherTimer(DispatcherPriority.Background) { Interval=TimeSpan.FromMilliseconds(230) };
        timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
    }
    private static void Invoke(Button button)
    {
        var peer=UIElementAutomationPeer.CreatePeerForElement(button)??new ButtonAutomationPeer(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
    }
    private static void CheckVisibleBounds(Window window,FrameworkElement element,string name)
    {
        var point=element.TranslatePoint(new Point(),window);
        Check(element.ActualWidth>100&&point.X>=0&&point.X+element.ActualWidth<=window.ActualWidth+1,name);
    }
    private static void Capture(Window window,string name,double scale=1)
    {
        window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth*scale),(int)Math.Ceiling(window.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(window);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(_output,name));encoder.Save(file);
    }
    private sealed class Dialogs : IDialogService
    {
        public SaveChangesChoice Choice { get; set; }=SaveChangesChoice.Cancel;
        public string? ShowOpenFileDialog(string title,string filter,string? initialDirectory=null)=>null;
        public string? ShowSaveFileDialog(string title,string filter,string defaultFileName,string? initialDirectory=null)=>null;
        public void ShowMessage(string title,string message,MessageKind kind=MessageKind.Info)=>throw new InvalidOperationException(title+": "+message);
        public bool Confirm(string title,string message,string confirmText="Evet",string cancelText="Hayır")=>false;
        public SaveChangesChoice AskSaveChanges(string documentName)=>Choice;
        public string? Prompt(string title,string message,string defaultValue="")=>null;
        public bool ShowDialog(object viewModel)=>false;
    }
    private sealed class ClipboardStub : IClipboardService
    {
        public void SetElements(string payloadJson,string plainText){}
        public string? GetElements()=>null;
        public bool ContainsElements()=>false;
        public byte[]? GetImagePng()=>null;
        public string? GetText()=>null;
    }
}
