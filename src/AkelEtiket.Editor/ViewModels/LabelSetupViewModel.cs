using AkelEtiket.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

/// <summary>Hazır etiket ölçüsü.</summary>
public sealed record LabelPreset(string Name, double WidthMm, double HeightMm, double GapMm)
{
    public override string ToString() => Name;
}

/// <summary>Yeni etiket / etiket ölçüsü penceresi.</summary>
public sealed partial class LabelSetupViewModel : ObservableObject
{
    public LabelSetupViewModel(string title, LabelSetup? current, double maxPrintWidthMm, LabelFormatting? formatting = null)
    {
        Title = title; Formatting = new(formatting);
        Formatting.PropertyChanged += (_,_) => { OnPropertyChanged(nameof(IsValid)); OnPropertyChanged(nameof(Warning)); };
        MaxPrintWidthMm = maxPrintWidthMm;
        var source = current ?? new LabelSetup();
        WidthMm = source.WidthMm;
        HeightMm = source.HeightMm;
        GapMm = source.GapMm;
        GapOffsetMm = source.GapOffsetMm;
        Media = source.Media;
        SelectedPreset = Presets.FirstOrDefault(p => Math.Abs(p.WidthMm - WidthMm) < 0.01 && Math.Abs(p.HeightMm - HeightMm) < 0.01);
    }

    public DocumentFormattingViewModel Formatting { get; }
    public string Title { get; }
    public double MaxPrintWidthMm { get; }

    public IReadOnlyList<LabelPreset> Presets { get; } =
    [
        new(L10n.T("100 × 100 mm (varsayılan)"), 100, 100, 3),
        new("100 × 150 mm (kargo/koli)", 100, 150, 3),
        new("100 × 50 mm", 100, 50, 3),
        new("100 × 75 mm", 100, 75, 3),
        new("80 × 50 mm", 80, 50, 2),
        new("60 × 40 mm", 60, 40, 2),
        new(L10n.T("58 × 40 mm (raf)"), 58, 40, 2),
        new("50 × 30 mm", 50, 30, 2),
        new("40 × 30 mm", 40, 30, 2),
        new(L10n.T("4 × 6 inç (101,6 × 152,4 mm)"), 101.6, 152.4, 3),
    ];

    public IReadOnlyList<KeyValuePair<MediaType, string>> MediaTypes { get; } =
    [
        new(MediaType.Gap, L10n.T("Boşluklu etiket (gap)")),
        new(MediaType.BlackMark, L10n.T("Siyah işaretli (black mark)")),
        new(MediaType.Continuous, L10n.T("Sürekli rulo")),
    ];

    [ObservableProperty]
    public partial LabelPreset? SelectedPreset { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Warning), nameof(IsValid))]
    public partial double WidthMm { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Warning), nameof(IsValid))]
    public partial double HeightMm { get; set; }

    [ObservableProperty]
    public partial double GapMm { get; set; }

    [ObservableProperty]
    public partial double GapOffsetMm { get; set; }

    [ObservableProperty]
    public partial MediaType Media { get; set; }

    partial void OnSelectedPresetChanged(LabelPreset? value)
    {
        if (value is null) return;
        WidthMm = value.WidthMm;
        HeightMm = value.HeightMm;
        GapMm = value.GapMm;
    }

    public bool IsValid => Formatting.IsValid && WidthMm is >= LabelSetup.MinSizeMm and <= LabelSetup.MaxSizeMm &&
                           HeightMm is >= LabelSetup.MinSizeMm and <= LabelSetup.MaxSizeMm;

    public string? Warning
    {
        get
        {
            if (!Formatting.IsValid) return L10n.T("Biçim ayarlarını kontrol edin.");
            if (!IsValid) return L10n.T($"Genişlik ve yükseklik {LabelSetup.MinSizeMm:0}–{LabelSetup.MaxSizeMm:0} mm arasında olmalı.");
            if (WidthMm > MaxPrintWidthMm + 0.01)
                return L10n.T($"Etiket genişliği yazıcının en fazla baskı genişliğini ({MaxPrintWidthMm:0.#} mm) aşıyor; taşan kısım basılmaz.");
            return null;
        }
    }

    public void ApplyTo(LabelSetup target)
    {
        target.WidthMm = WidthMm;
        target.HeightMm = HeightMm;
        target.GapMm = GapMm;
        target.GapOffsetMm = GapOffsetMm;
        target.Media = Media;
    }
}
