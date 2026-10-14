using System.ComponentModel;
using System.Windows;
using AkelEtiket.Editor.ViewModels;

namespace AkelEtiket.App.Views;

/// <summary>Baskı penceresi.</summary>
public partial class PrintWindow : ThemedWindow
{
    private readonly PrintViewModel _vm;

    public PrintWindow(PrintViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_vm.IsBusy)
        {
            // Baskı sürerken pencere kapatılamaz; önce durdurulmalı.
            e.Cancel = !MessageWindow.Confirm(this, "Baskı sürüyor",
                "Baskı devam ediyor. Durdurup pencereyi kapatmak ister misiniz? (Kuyruğa gönderilmiş etiketler basılmaya devam edebilir.)",
                "Durdur ve kapat", "Bekle");
            if (!e.Cancel) _vm.CancelCommand.Execute(null);
        }
    }
}
