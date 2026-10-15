using AkelEtiket.Editor.Templates;
using AkelEtiket.Rendering;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

public sealed partial class StarterPacksViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    public StarterPacksViewModel(MainViewModel main) { _main=main;SelectedPack=Packs[0]; }
    public IReadOnlyList<StarterPack> Packs { get; } = StarterPacks.All;
    [ObservableProperty] public partial StarterPack? SelectedPack { get; set; }
    [ObservableProperty] public partial byte[]? PreviewPng { get; private set; }
    [ObservableProperty] public partial string? Message { get; private set; }
    public string SampleFolder => Path.Combine(AppContext.BaseDirectory,"Örnekler","packs",SelectedPack?.Id ?? "");
    partial void OnSelectedPackChanged(StarterPack? value)
    {
        Message=null;PreviewPng=null;OnPropertyChanged(nameof(SampleFolder));
        if(value is null)return;
        var project=StarterPacks.Create(value.Id,_main.Settings.Company)[0].Project;
        using var renderer=new LabelRenderer(project.Assets);
        var result=renderer.RenderLabel(project.Document,new RenderOptions { DotsPerMm=3,Fields=StarterPacks.ExampleFields });
        PreviewPng=MonoImageExport.EncodePng(result.Bitmap,3*25.4);
    }
    [RelayCommand] private void OpenSamples()
    {
        if(Directory.Exists(SampleFolder))_main.Services.OpenFolder?.Invoke(SampleFolder);
        else Message=L10n.T("Geliştirme sürümünde örnekler: samples/packs/")+(SelectedPack?.Id ?? "");
    }
    [RelayCommand] private void Install()
    {
        if(SelectedPack is null)return;
        try
        {
            int count=0;
            foreach(var(name,project)in StarterPacks.Create(SelectedPack.Id,_main.Settings.Company))
            {
                string candidate=name;
                for(int i=2;File.Exists(_main.Templates.Library.GetPathForName(candidate));i++)candidate=$"{name} ({i})";
                using var renderer=new LabelRenderer(project.Assets);
                var preview=renderer.RenderLabel(project.Document,new RenderOptions { DotsPerMm=3,Fields=StarterPacks.ExampleFields });
                _main.Templates.Library.SaveAsTemplate(project,candidate,MonoImageExport.EncodePng(preview.Bitmap,3*25.4),overwrite:false);count++;
            }
            _main.Templates.Refresh();Message=L10n.T($"{count} şablon eklendi. Mevcut dosyalar korundu.");
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ProjectManager.ProjectFileException) { Message=ex.Message; }
    }
}

public sealed partial class MainViewModel
{
    [RelayCommand] private void OpenStarterPacks() => Dialogs.ShowDialog(new StarterPacksViewModel(this));
}
