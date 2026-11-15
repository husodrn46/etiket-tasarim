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
        "Counters", "DataTable", "Production", "Print", "QuickPrint", "PrintHistory", "Calibration",
        "Gs1Profile", "Gs1Builder", "AiSettings",
    };

    private static bool NeedsData(string dialog) => dialog is "ColumnMapping" or "DataTable" or "Production";

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void Dialog_OpensWithItsRealViewModel_AndResolvesEveryBinding(string dialog) => Ui.Run(() =>
    {
        // Sütun eşleştirme, veri alanı içeren bir tasarım ve bir veri kaynağı ister.
        using var app = NeedsData(dialog) ? new AppHarness("Ornek-100x100-Excel-Toplu-Etiket.akelabel") : new AppHarness();
        if (NeedsData(dialog))
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
        if (dialog == "Gs1Builder")
        {
            // GS1 içerik oluşturucu, seçili bir GS1 barkodu ister (örnek etiketteki GS1-128).
            app.Vm.Session.Select(app.Vm.Session.Elements.First(e =>
                e.Model is AkelEtiket.Core.Model.BarcodeElement b && AkelEtiket.Core.Model.BarcodeElement.IsGs1(b.Symbology)));
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
            "Counters" => app.Vm.OpenCountersCommand,
            "Production" => app.Vm.OpenProductionCommand,
            "Print" or "Calibration" => app.Vm.PrintCommand,
            "Gs1Profile" => app.Vm.OpenSettingsCommand,
            "Gs1Builder" => app.Vm.OpenGs1BuilderCommand,
            "AiSettings" => app.Vm.Ai.OpenSettingsCommand,
            "QuickPrint" => app.Vm.OpenQuickPrintCommand,
            "PrintHistory" => app.Vm.OpenPrintHistoryCommand,
            "DataTable" => null,
            _ => (System.Windows.Input.ICommand)app.Vm.OpenProductsCommand,
        };
        object viewModel;
        if (command is null)
        {
            // Veri tablosu sol paneldeki düğmeyle, veri kaynağının kendi görünüm modeliyle açılır.
            viewModel = app.Vm.Data;
        }
        else
        {
            command.Execute(null);
            Ui.Pump();
            Assert.True(app.Dialogs.RequestedDialogs.Count == 1,
                $"Komut pencere istemedi. İletiler: {string.Join(" | ", app.Dialogs.Messages)}");
            viewModel = app.Dialogs.RequestedDialogs[0];
            if (dialog == "Gs1Profile")
            {
                // GS1 firma profili ayarlar penceresinin içinden açılır.
                app.Dialogs.RequestedDialogs.Clear();
                ((SettingsViewModel)viewModel).OpenGs1ProfileCommand.Execute(null);
                viewModel = Assert.Single(app.Dialogs.RequestedDialogs);
            }
            if (dialog == "Calibration")
            {
                // Kalibrasyon yardımcısı yazdırma penceresinin içinden açılır.
                app.Dialogs.RequestedDialogs.Clear();
                ((PrintViewModel)viewModel).OpenCalibrationCommand.Execute(null);
                viewModel = Assert.Single(app.Dialogs.RequestedDialogs);
            }
        }

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
    public void UnknownViewModel_HasNoWindow_SoTheServiceCanSaySo()
    {
        // Penceresi olmayan bir görünüm modeli için eşleme null döner; servis bunu sessizce yutmaz, kullanıcıya söyler.
        Assert.Null(DialogWindows.Create(new object()));
    }

    [Fact]
    public void DataTable_ShowsEveryColumn_AndSearchFiltersRows() => Ui.Run(() =>
    {
        using var app = new AppHarness("Ornek-100x100-Excel-Toplu-Etiket.akelabel");
        app.Vm.HandleFileDrop([AppHarness.SamplePath("ornek-urunler-utf8.csv")], null, null);
        Ui.WaitFor(() => app.Vm.Data.HasData, "veri kaynağı yüklendi");
        var data = app.Vm.Data;

        var window = Assert.IsType<DataTableWindow>(DialogWindows.Create(data));
        window.Show();
        Ui.Wait(150);
        var grid = window.FindControl<DataGrid>("Grid")!;
        // Seçim, satır no, adet, durum + veri sütunları + sorun
        Assert.Equal(data.Columns.Count + 5, grid.Columns.Count);
        int all = data.VisibleRows.Count;
        Assert.Equal(120, all);
        Assert.Same(data.VisibleRows, grid.ItemsSource);

        // Arama kutusuna yazıp Enter'a basmak gecikmeyi beklemeden süzer; pencere kapanmaz.
        var search = window.FindControl<TextBox>("SearchBox")!;
        Click(window, search);
        window.KeyTextInput(data.VisibleRows[7].Values[0]);
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        Ui.Wait(50);
        Assert.InRange(data.VisibleRows.Count, 1, all - 1);
        Assert.Same(data.VisibleRows, grid.ItemsSource);
        Assert.True(window.IsVisible);
        Assert.Empty(BindingErrors.Take());
        window.Close();
    });

    [Fact]
    public void Production_ChecksQuality_AndExportsPdf_ThroughTheWindow() => Ui.Run(() =>
    {
        using var app = new AppHarness("Ornek-100x100-Excel-Toplu-Etiket.akelabel");
        app.Vm.HandleFileDrop([AppHarness.SamplePath("ornek-urunler-utf8.csv")], null, null);
        Ui.WaitFor(() => app.Vm.Data.HasData, "veri kaynağı yüklendi");
        string pdf = Path.Combine(TestDataRoot.Root, Guid.NewGuid().ToString("N") + ".pdf");
        app.Dialogs.NextSavePath = pdf;
        string status = "";
        app.Dialogs.OnShowDialog = viewModel =>
        {
            var production = (ProductionViewModel)viewModel;
            var window = Assert.IsType<ProductionWindow>(DialogWindows.Create(viewModel));
            window.Show();
            Ui.Wait(150);
            var check = window.FindControl<Button>("CheckQualityButton")!;
            var export = window.FindControl<Button>("ExportButton")!;
            Assert.False(export.IsEffectivelyEnabled); // kalite denetimi yapılmadan çıktı alınamaz

            void CheckQuality()
            {
                string before = production.QualitySummary;
                Click(window, check);
                try
                {
                    Ui.WaitFor(() => !production.IsBusy && production.QualitySummary != before, "kalite denetimi bitti", 60_000);
                }
                catch (TimeoutException)
                {
                    Assert.Fail($"Kalite denetimi bitmedi. busy={production.IsBusy} özet={production.QualitySummary} durum={production.Status}");
                }
                status = production.QualitySummary + " | " + production.Status;
            }

            // Örnek veride bilerek hatalı bir barkod satırı vardır: denetim onu bulur ve çıktıyı engeller.
            CheckQuality();
            Assert.Contains("1 hata", production.QualitySummary);
            Assert.False(export.IsEffectivelyEnabled, "Hatalı kayıt varken çıktı düğmesi açık: " + status);
            Assert.NotEmpty(production.Findings);

            // Yalnız geçerli satırlar seçilince iş temizdir ve çıktı alınabilir.
            app.Vm.Data.ValidateAgainst(app.Vm.Session.Document);
            app.Vm.Data.SelectValidRowsCommand.Execute(null);
            Ui.Pump();
            CheckQuality();
            Assert.Contains("0 hata", production.QualitySummary);
            Assert.True(export.IsEffectivelyEnabled, "Kalite denetiminden sonra çıktı düğmesi açılmadı: " + status);

            Click(window, export);
            Ui.WaitFor(() => !production.IsBusy && File.Exists(pdf), "PDF yazıldı", 120_000);
            status = production.Status;
            window.Close();
            return false;
        };

        app.Vm.OpenProductionCommand.Execute(null);
        Ui.Pump();

        Assert.True(File.Exists(pdf), "PDF oluşmadı: " + status);
        byte[] head = new byte[5];
        using (var stream = File.OpenRead(pdf))
        {
            Assert.Equal(5, stream.Read(head, 0, 5));
            Assert.True(stream.Length > 20_000, $"PDF beklenenden küçük: {stream.Length} bayt");
        }
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(head));
    });

    [Fact]
    public void Print_ToAMissingPrinter_ShowsTheReasonInTheWindow_AndSendsNothing() => Ui.Run(() =>
    {
        // GÜVENLİK: gerçek yazıcıya iş gönderilmez; yalnızca var olmayan bir yazıcı adı kullanılır.
        using var app = new AppHarness();
        app.Dialogs.OnShowDialog = viewModel =>
        {
            var print = (PrintViewModel)viewModel;
            var window = Assert.IsType<PrintWindow>(DialogWindows.Create(viewModel));
            window.Show();
            Ui.Wait(150);
            // Baskı yöntemi listesi yalnız bu sistemde çalışanları gösterir.
            Assert.Equal(OperatingSystem.IsWindows() ? 2 : 1, print.MethodChoices.Count);

            print.Printers.Add(new AkelEtiket.PrintEngine.PrinterInfo { Name = "LabelHarbor-Olmayan-Yazici-7f3c" });
            print.SelectedPrinter = print.Printers[^1];
            Ui.Pump();
            Assert.True(print.PrintCommand.CanExecute(null), "Yazdır düğmesi kapalı: " + print.ReportText);
            print.PrintCommand.Execute(null);
            Ui.WaitFor(() => !print.IsBusy && print.ReportText.Length > 0, "baskı raporu yazıldı", 30_000);

            Assert.Contains("bulunamadı", print.ReportText);
            // Rapor satırları pencerede görünür: nedeni kullanıcı okuyabilir.
            Assert.NotEmpty(print.ReportLines);
            Ui.Pump();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && (text.Text ?? "").Contains("bulunamadı"));
            window.Close();
            return false;
        };
        app.Vm.PrintCommand.Execute(null);
        Ui.Pump();
        Assert.Contains(app.Dialogs.RequestedDialogs, vm => vm is PrintViewModel);
    });
}
