using System.IO;
using System.Windows;
using AkelEtiket.App.Services;
using AkelEtiket.Editor.ViewModels;

namespace AkelEtiket.App.Views;

/// <summary>Yardım penceresi.</summary>
public partial class HelpWindow : ThemedWindow
{
    private readonly HelpViewModel _vm;

    public HelpWindow(HelpViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
    }

    private void OnOpenDocs(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(_vm.DocumentsFolder)) ShellHelper.OpenFolder(_vm.DocumentsFolder);
        else MessageWindow.Show(this, "Yardım", "Belgeler klasörü bulunamadı (geliştirme sürümünde belgeler depodaki docs klasöründedir).",
            Editor.Services.MessageKind.Info);
    }
}
