using System.Globalization;
using AkelEtiket.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

public sealed partial class DocumentFormattingViewModel : ObservableObject
{
    public DocumentFormattingViewModel(LabelFormatting? current)
    {
        Enabled = current is not null;
        var value = current ?? new LabelFormatting { CultureName = L10n.Language == "en" ? "en-US" : "tr-TR" };
        CultureName=value.CultureName; InputCultureName=value.InputCultureName; DateFormat=value.DateFormat;
        NumberFormat=value.NumberFormat; CurrencyCode=value.CurrencyCode;
        PropertyChanged += (_,_) => { if (!_notifying) { _notifying=true; OnPropertyChanged(nameof(IsValid)); OnPropertyChanged(nameof(Preview)); _notifying=false; } };
    }
    private bool _notifying;
    public IReadOnlyList<string> Cultures { get; } = ["tr-TR","en-US","en-GB","de-DE","fr-FR","ja-JP","es-ES","ar-SA"];
    public IReadOnlyList<string> DateFormats { get; } = ["yyyy-MM-dd","dd.MM.yyyy","dd/MM/yyyy","MM/dd/yyyy","dd MMMM yyyy"];
    public IReadOnlyList<string> NumberFormats { get; } = ["N2","N0","0.00","0.###"];
    public IReadOnlyList<string> Currencies { get; } = ["","TRY","USD","EUR","GBP","JPY","CAD","AUD","CHF"];
    [ObservableProperty] public partial bool Enabled { get; set; }
    [ObservableProperty] public partial string CultureName { get; set; }
    [ObservableProperty] public partial string InputCultureName { get; set; }
    [ObservableProperty] public partial string DateFormat { get; set; }
    [ObservableProperty] public partial string NumberFormat { get; set; }
    [ObservableProperty] public partial string CurrencyCode { get; set; }
    public bool IsValid => !Enabled || Model().IsValid();
    public string Preview
    {
        get
        {
            if (!Enabled) return L10n.T("Eski biçim davranışı korunur.");
            if (!IsValid) return L10n.T("Biçim ayarlarını kontrol edin.");
            var model=Model();var culture=model.OutputCulture();
            return new DateTime(2026,10,15).ToString(DateFormat,culture)+" · "+1234.56m.ToString(NumberFormat,culture)+" · "+1234.56m.ToString(CurrencyCode.Length==0?NumberFormat:"C",culture);
        }
    }
    private LabelFormatting Model() => new() { CultureName=CultureName, InputCultureName=InputCultureName, DateFormat=DateFormat, NumberFormat=NumberFormat, CurrencyCode=CurrencyCode };
    public LabelFormatting? ToModel() => Enabled ? IsValid ? Model() : throw new InvalidOperationException(L10n.T("Biçim ayarlarını kontrol edin.")) : null;
}
