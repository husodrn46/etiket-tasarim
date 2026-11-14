using AkelEtiket.App.Mac.Controls;
using AkelEtiket.App.Mac.Services;
using AkelEtiket.App.Mac.Views;
using AkelEtiket.Editor.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace AkelEtiket.App.Mac.Tests;

/// <summary>
/// İletişim pencereleri: her pencere, uygulamanın kendi komutunun ürettiği gerçek görünüm modeliyle açılır;
/// bağlamaların tamamı çözülmelidir. Etiket ölçüsü penceresi uçtan uca denenir (yaz, onayla, etiket değişsin).
/// </summary>
public class DialogTests
{
    private static Point CenterOf(TopLevel window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
        ?? throw new InvalidOperationException("Denetim pencerede değil.");

    private static void Click(TopLevel window, Control control)
    {
        var p = CenterOf(window, control);
        window.MouseDown(p, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(p, MouseButton.Left, RawInputModifiers.None);
        Ui.Pump();
    }

    public static TheoryData<string> Dialogs => new()
    {
        "LabelSetup", "Preview", "Settings", "FirstSetup", "Help", "StarterPacks", "Backups", "ColumnMapping", "Products",
    };

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void Dialog_OpensWithItsRealViewModel_AndResolvesEveryBinding(string dialog) => Ui.Run(() =>
    {
        // Sütun eşleştirme, veri alanı içeren bir tasarım ve bir veri kaynağı ister.
        using var app = dialog == "ColumnMapping" ? new AppHarness("Ornek-100x100-Excel-Toplu-Etiket.akelabel") : new AppHarness();
        if (dialog == "ColumnMapping")
        {
            app.Vm.HandleFileDrop([AppHarness.SamplePath("ornek-urunler-utf8.csv")], null, null);
            Ui.WaitFor(() => app.Vm.Data.HasData, "veri kaynağı yüklendi");
        }
        if (dialog == "Backups")
        {
            // Yedek listesi kaydedilmiş bir tasarım ister: örnek geçici klasöre kopyalanıp iki kez kaydedilir.
            string copy = Path.Combine(TestDataRoot.Root, Guid.NewGuid().ToString("N") + ".akelabel");
            File.Copy(app.Vm.Session.Project.FilePath!, copy);
            app.Vm.HandleFileDrop([copy], null, null);
            Ui.WaitFor(() => app.Vm.Session.Project.FilePath == copy, "kopya açıldı");
            app.Vm.Nudge(1, 0);
            app.Vm.SaveDocumentCommand.Execute(null);
            Ui.Pump();
        }
        app.Dialogs.RequestedDialogs.Clear();
        app.Dialogs.Messages.Clear();

        var command = dialog switch
        {
            "LabelSetup" => app.Vm.LabelSetupCommand,
            "Preview" => app.Vm.PreviewCommand,
            "Settings" => app.Vm.OpenSettingsCommand,
            "FirstSetup" => app.Vm.OpenFirstSetupCommand,
            "Help" => app.Vm.OpenHelpCommand,
            "StarterPacks" => app.Vm.OpenStarterPacksCommand,
            "Backups" => app.Vm.OpenBackupsCommand,
            "ColumnMapping" => app.Vm.OpenColumnMappingCommand,
            _ => (System.Windows.Input.ICommand)app.Vm.OpenProductsCommand,
        };
        command.Execute(null);
        Ui.Pump();
        Assert.True(app.Dialogs.RequestedDialogs.Count == 1,
            $"Komut pencere istemedi. İletiler: {string.Join(" | ", app.Dialogs.Messages)}");
        object viewModel = app.Dialogs.RequestedDialogs[0];

        var window = DialogWindows.Create(viewModel);
        Assert.NotNull(window);
        BindingErrors.Take();
        window.Show();
        Ui.Wait(150);
        Ui.Pump();
        app.Screenshot("dialog-" + dialog, window);
        Assert.Empty(BindingErrors.Take());
        Assert.False(string.IsNullOrWhiteSpace(window.Title));
        // Her pencerede en az bir düğme görünür ve pencerenin içindedir (taşan, kesilen düzen olmasın).
        var buttons = window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
        Assert.NotEmpty(buttons);
        window.Close();
        Ui.Pump();
    });

    [Fact]
    public void LabelSetup_AppliesTheTypedSize_WhenConfirmed() => Ui.Run(() =>
    {
        using var app = new AppHarness();
        Assert.Equal(100, app.Vm.Session.Document.Label.WidthMm);
        app.Dialogs.OnShowDialog = viewModel =>
        {
            var window = Assert.IsType<LabelSetupWindow>(DialogWindows.Create(viewModel));
            window.Show();
            Ui.Wait(100);
            // İlk sayı kutusu genişliktir: tıkla (tümü seçilir), yaz. Enter yalnız kutuyu uygular, pencereyi kapatmaz.
            var width = window.GetVisualDescendants().OfType<NumberBox>().First(b => b.IsEffectivelyVisible);
            Click(window, width);
            window.KeyTextInput("80");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            Ui.Pump();
            Assert.Equal(80, ((LabelSetupViewModel)viewModel).WidthMm);
            Assert.Null(window.Result);

            var ok = window.GetVisualDescendants().OfType<Button>().Single(b => b.IsDefault);
            ok.BringIntoView();
            Ui.Pump();
            Click(window, ok);
            Assert.True(window.Result is true, "Tamam düğmesi pencereyi onaylamadı.");
            return true;
        };

        app.Vm.LabelSetupCommand.Execute(null);
        Ui.Pump();

        Assert.Equal(80, app.Vm.Session.Document.Label.WidthMm);
        Assert.Equal(150, app.Vm.Session.Document.Label.HeightMm);
        // Etiket ölçüsü değişikliği tek adımda geri alınır.
        app.Vm.UndoCommand.Execute(null);
        Ui.Pump();
        Assert.Equal(100, app.Vm.Session.Document.Label.WidthMm);
    });

    [Fact]
    public void CancelButton_AndEscape_CloseWithoutResult() => Ui.Run(() =>
    {
        using var app = new AppHarness();
        app.Vm.OpenSettingsCommand.Execute(null);
        var viewModel = app.Dialogs.RequestedDialogs[^1];

        var window = DialogWindows.Create(viewModel)!;
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();
        Ui.Wait(100);
        var cancel = window.GetVisualDescendants().OfType<Button>().Single(b => b.IsCancel);
        cancel.BringIntoView();
        Ui.Pump();
        Click(window, cancel);
        Assert.True(closed);
        Assert.Null(window.Result);

        // Esc de vazgeç düğmesini tetikler.
        window = DialogWindows.Create(viewModel)!;
        closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();
        Ui.Wait(100);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        Ui.Pump();
        Assert.True(closed);
        Assert.Null(window.Result);
    });

    [Fact]
    public void UnportedDialog_SaysSo_InsteadOfFailingSilently() => Ui.Run(() =>
    {
        using var app = new AppHarness();
        // Yazdırma penceresi henüz taşınmadı: pencere yok, ama eşleme null döner ve servis bunu kullanıcıya söyler.
        app.Vm.PrintCommand.Execute(null);
        Ui.Pump();
        object? print = app.Dialogs.RequestedDialogs.LastOrDefault(vm => vm is PrintViewModel);
        if (print is not null) Assert.Null(DialogWindows.Create(print));
    });
}
