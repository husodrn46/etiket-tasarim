using System.Collections.ObjectModel;
using AkelEtiket.Editor.Services;
using AkelEtiket.PrintEngine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

/// <summary>Kaydedilene kadar mevcut ayarlardan bağımsız ilk kurulum taslağı.</summary>
public sealed partial class FirstSetupViewModel : ObservableObject
{
    public FirstSetupViewModel(AppSettings settings, IDialogService dialogs)
    {
        Profile = new SettingsViewModel(settings) { Dialogs = dialogs };
        PrinterName = settings.Printer.PrinterName ?? "";
        PrinterMessage = L10n.T("Yazıcıyı daha sonra da seçebilirsiniz.");
        Profile.PropertyChanged += (_, _) => NotifyValidation();
    }

    public SettingsViewModel Profile { get; }
    public ObservableCollection<string> Printers { get; } = [];
    public IReadOnlyList<LabelPreset> Presets { get; } = new LabelSetupViewModel("", null, 300).Presets;
    [ObservableProperty] public partial LabelPreset? SelectedPreset { get; set; }
    partial void OnSelectedPresetChanged(LabelPreset? value)
    {
        if (value is null) return;
        Profile.DefaultWidthMm = value.WidthMm; Profile.DefaultHeightMm = value.HeightMm;
    }
    public IReadOnlyList<string> Steps { get; } = [L10n.T("Firma bilgileri"), L10n.T("Yazıcı ve ölçü"), L10n.T("Başlamaya hazırsınız")];
    [ObservableProperty] public partial int StepIndex { get; set; }
    [ObservableProperty] public partial string PrinterName { get; set; }
    [ObservableProperty] public partial string? PrinterMessage { get; set; }
    [ObservableProperty] public partial bool OpenPreview { get; set; } = true;
    [ObservableProperty] public partial bool AddTemplates { get; set; } = true;
    public bool IsCompanyStep => StepIndex == 0;
    public bool IsPrinterStep => StepIndex == 1;
    public bool IsFinishStep => StepIndex == 2;
    public bool IsNotFinishStep => !IsFinishStep;
    public string StepTitle => Steps[StepIndex];
    public string Summary => $"{(string.IsNullOrWhiteSpace(Profile.CompanyName) ? L10n.T("Firma adı daha sonra girilebilir") : Profile.CompanyName.Trim())}\n" +
        $"{Profile.DefaultWidthMm:0.##} × {Profile.DefaultHeightMm:0.##} mm\n" +
        (string.IsNullOrWhiteSpace(PrinterName) ? L10n.T("Yazıcı baskı sırasında seçilecek.") : L10n.T("Yazıcı: ") + PrinterName);
    public string? ValidationProblem => !double.IsFinite(Profile.MaxPrintWidthMm) || Profile.MaxPrintWidthMm is < 20 or > 300
        ? L10n.T("Yazıcının en fazla baskı genişliği 20–300 mm arasında olmalı.")
        : !double.IsFinite(Profile.DefaultWidthMm) || !double.IsFinite(Profile.DefaultHeightMm) ||
        Profile.DefaultWidthMm is < 5 or > 1000 || Profile.DefaultHeightMm is < 5 or > 1000
        ? L10n.T("Etiket genişliği ve yüksekliği 5–1000 mm arasında olmalı.")
        : Profile.DefaultWidthMm > Profile.MaxPrintWidthMm ? L10n.T("Etiket genişliği yazıcının baskı genişliğini aşıyor. Ölçüyü veya yazıcı genişliğini düzeltin.") : null;

    partial void OnStepIndexChanged(int value)
    {
        if (value < 0 || value >= Steps.Count) { StepIndex = Math.Clamp(value, 0, Steps.Count - 1); return; }
        OnPropertyChanged(nameof(IsCompanyStep)); OnPropertyChanged(nameof(IsPrinterStep));
        OnPropertyChanged(nameof(IsFinishStep)); OnPropertyChanged(nameof(IsNotFinishStep)); OnPropertyChanged(nameof(StepTitle));
        NextCommand.NotifyCanExecuteChanged(); PreviousCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(Summary));
    }
    partial void OnPrinterNameChanged(string value) => OnPropertyChanged(nameof(Summary));
    private void NotifyValidation()
    {
        OnPropertyChanged(nameof(ValidationProblem)); OnPropertyChanged(nameof(Summary));
        NextCommand.NotifyCanExecuteChanged();
    }
    private bool CanNext() => StepIndex < 2 && (StepIndex == 0 || ValidationProblem is null);
    private bool CanPrevious() => StepIndex > 0;
    [RelayCommand(CanExecute = nameof(CanNext))] private void Next() => StepIndex++;
    [RelayCommand(CanExecute = nameof(CanPrevious))] private void Previous() => StepIndex--;
    [RelayCommand]
    private void RefreshPrinters()
    {
        Printers.Clear();
        if (PrintingServices.ReadPrinters(out var printers, out string? error))
            foreach (var printer in printers) Printers.Add(printer.Name);
        PrinterMessage = error ?? (Printers.Count == 0 ? L10n.T("Yazıcı bulunamadı. Bu adımı boş bırakıp daha sonra seçebilirsiniz.") : null);
    }

    public void ApplyTo(AppSettings settings)
    {
        if (ValidationProblem is { } problem) throw new InvalidOperationException(problem);
        Profile.ApplyTo(settings);
        settings.Printer.PrinterName = (PrinterName ?? "").Trim();
        settings.OnboardingCompleted = true;
    }
}
