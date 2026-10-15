using System.ComponentModel;
using System.Globalization;
using AkelEtiket.Core.Model;
using AkelEtiket.Editor.Services;
using AkelEtiket.Editor.Templates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

/// <summary>
/// Kalibrasyon yardımcısı: (1) sensör kalibrasyonu (GAPDETECT/BLINEDETECT), (2) cetvelli kalibrasyon etiketini normal,
/// onaylı baskı yoluyla basma, (3) baskı yönünü kontrol etme, (4) basılan etikette ölçülen kaymadan ofseti hesaplayıp
/// ayarlara ve profile kaydetme. Bu pencere kendiliğinden hiçbir şey göndermez; her gönderim kullanıcı onayı ister.
/// </summary>
public sealed partial class CalibrationViewModel : ObservableObject
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private readonly PrintViewModel _print;
    private readonly IDialogService _dialogs;

    public CalibrationViewModel(PrintViewModel print, IDialogService dialogs)
    {
        _print = print;
        _dialogs = dialogs;
        MeasuredLeftMm = ExpectedMm;
        MeasuredTopMm = ExpectedMm;
        _print.PropertyChanged += OnPrintPropertyChanged;
    }

    /// <summary>Çerçevenin etiket kenarından beklenen uzaklığı (mm).</summary>
    public double ExpectedMm => CalibrationLabelFactory.FrameInsetMm;

    public string PrinterText => _print.SelectedPrinter is { } p
        ? $"{p.Name} · {(_print.IsTspl ? L10n.T("TSPL doğrudan") : L10n.T("Windows sürücüsü"))}"
        : L10n.T("Yazıcı seçilmedi");

    public string LabelText
    {
        get
        {
            var label = _print.Label;
            string media = label.Media switch
            {
                MediaType.BlackMark => L10n.T($"siyah işaretli ({label.GapMm:0.##} mm)"),
                MediaType.Continuous => L10n.T("sürekli rulo"),
                _ => L10n.T($"boşluklu (boşluk {label.GapMm:0.##} mm)"),
            };
            return string.Create(Turkish, $"{label.WidthMm:0.##} × {label.HeightMm:0.##} mm · {media}");
        }
    }

    public bool IsTspl => _print.IsTspl;

    /// <summary>Sensör kalibrasyonu bu medyada anlamlı mı (TSPL ve boşluklu/siyah işaretli rulo)?</summary>
    public bool CanDetectSensor => _print.IsTspl && _print.Label.Media != MediaType.Continuous;

    public string SensorText => _print.Label.Media switch
    {
        MediaType.Continuous => L10n.T("Sürekli ruloda etiket arası olmadığı için sensör kalibrasyonu gerekmez."),
        MediaType.BlackMark =>
            L10n.T("BLINEDETECT: yazıcı birkaç etiket ilerleterek arkadaki siyah işaretleri ölçer ve etiket boyunu hafızasına yazar. ") +
            L10n.T("Rulo değiştirdiğinizde, baskı her etikette aynı yerden başlamıyorsa ya da yazıcı kırmızı ışıkla duruyorsa yapın."),
        _ =>
            L10n.T("GAPDETECT: yazıcı birkaç etiket ilerleterek etiketler arasındaki boşluğu ölçer ve etiket boyunu hafızasına yazar. ") +
            L10n.T("Rulo değiştirdiğinizde, baskı her etikette aynı yerden başlamıyorsa (etiketler kayarak ya da iki etikete bölünerek ") +
            L10n.T("basılıyorsa) veya yazıcı kırmızı ışıkla duruyorsa yapın. Komut birkaç boş etiket harcayabilir."),
    };

    public string DirectionText => _print.Rotate180
        ? L10n.T("Şu an: 180° döndürülmüş (TSPL DIRECTION 0 — sipariş sistemiyle aynı).")
        : L10n.T("Şu an: normal yön (TSPL DIRECTION 1).");

    public double CurrentOffsetXMm => _print.OffsetXMm;
    public double CurrentOffsetYMm => _print.OffsetYMm;

    /// <summary>Etiketin sol kenarından çerçevenin dış çizgisine ölçülen uzaklık (mm).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewOffsetXMm), nameof(ShiftText))]
    public partial double MeasuredLeftMm { get; set; }

    /// <summary>Etiketin üst kenarından ("▲ ÜST" tarafı) çerçevenin dış çizgisine ölçülen uzaklık (mm).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewOffsetYMm), nameof(ShiftText))]
    public partial double MeasuredTopMm { get; set; }

    public double NewOffsetXMm => CalibrationLabelFactory.CorrectedOffset(_print.OffsetXMm, MeasuredLeftMm);
    public double NewOffsetYMm => CalibrationLabelFactory.CorrectedOffset(_print.OffsetYMm, MeasuredTopMm);

    /// <summary>Ölçümün anlamı ve hesaplanan yeni ofset.</summary>
    public string ShiftText
    {
        get
        {
            double dx = MeasuredLeftMm - ExpectedMm, dy = MeasuredTopMm - ExpectedMm;
            if (Math.Abs(dx) < 0.01 && Math.Abs(dy) < 0.01)
                return L10n.T("Çerçeve beklenen yerde: ofset değişmez.");
            string h = Math.Abs(dx) < 0.01 ? L10n.T("yatayda kayma yok") : L10n.T($"içerik {Math.Abs(dx):0.##} mm {(dx > 0 ? L10n.T("sağa") : "sola")}");
            string v = Math.Abs(dy) < 0.01 ? L10n.T("dikeyde kayma yok") : L10n.T($"{Math.Abs(dy):0.##} mm {(dy > 0 ? L10n.T("aşağı") : L10n.T("yukarı"))} kaymış");
            return L10n.T($"{h}, {v} → yeni ofset sağ {NewOffsetXMm:0.###} mm, aşağı {NewOffsetYMm:0.###} mm");
        }
    }

    /// <summary>Kaydedilecek profil hakkında bilgi.</summary>
    public string TargetProfileText => _print.SelectedProfile is { IsBuiltIn: false } p
        ? L10n.T($"Ofsetler \"{p.Name}\" profiline de kaydedilir.")
        : L10n.T("Hazır profil değiştirilemez: uygularken yeni bir profil adı sorulur (vazgeçerseniz ofsetler yalnızca yazıcı ayarlarına kaydedilir).");

    [ObservableProperty]
    public partial string Status { get; private set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DetectSensorCommand), nameof(PrintLabelCommand), nameof(FlipDirectionCommand), nameof(ApplyCommand))]
    public partial bool IsWorking { get; private set; }

    private bool CanWork() => !IsWorking && !_print.IsBusy;

    /// <summary>Sensör kalibrasyonu (onay ister).</summary>
    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task DetectSensor()
    {
        if (!CanDetectSensor) return;
        IsWorking = true;
        try
        {
            bool? sent = await _print.DetectMediaAsync();
            if (sent is null) return;
            Status = sent == true
                ? L10n.T("Sensör kalibrasyonu komutu yazıcı kuyruğuna gönderildi. Yazıcı birkaç etiket ilerletip durmalı; ardından kalibrasyon etiketini basın.")
                : L10n.T("Sensör kalibrasyonu komutu gönderilemedi; baskı penceresindeki rapora bakın.");
        }
        finally
        {
            IsWorking = false;
        }
    }

    /// <summary>Cetvelli kalibrasyon etiketini mevcut ayarlarla basar (onay ister).</summary>
    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task PrintLabel()
    {
        if (_print.SelectedPrinter is not { } printer)
        {
            _dialogs.ShowMessage(L10n.T("Kalibrasyon etiketi"), L10n.T("Lütfen baskı penceresinde bir yazıcı seçin."), MessageKind.Warning);
            return;
        }
        var label = _print.Label;
        if (!_dialogs.Confirm(L10n.T("Kalibrasyon etiketi"),
                L10n.T($"\"{printer.Name}\" yazıcısına 1 adet {label.WidthMm:0.##} × {label.HeightMm:0.##} mm kalibrasyon etiketi gönderilecek ") +
                L10n.T("(penceredeki yoğunluk, hız, yön ve ofset ayarlarıyla).\n\nGönderilsin mi?"), L10n.T("Yazdır"), L10n.T("Vazgeç")))
            return;
        IsWorking = true;
        try
        {
            bool ok = await _print.PrintCalibrationLabelAsync();
            Status = ok
                ? L10n.T("Kalibrasyon etiketi yazıcı kuyruğuna gönderildi. Etiketi alın: önce ▲ ÜST işaretinin yönünü kontrol edin, sonra sol ve üst kenardan çerçeveye olan uzaklığı ölçün.")
                : L10n.T("Kalibrasyon etiketi gönderilemedi; baskı penceresindeki rapora bakın.");
            NotifyOffsets();
        }
        finally
        {
            IsWorking = false;
        }
    }

    /// <summary>"▲ ÜST" işareti ters çıktıysa baskı yönünü değiştirir (ofsetler yeni yönde yeniden ölçülmeli).</summary>
    [RelayCommand(CanExecute = nameof(CanWork))]
    private void FlipDirection()
    {
        _print.Rotate180 = !_print.Rotate180;
        _print.SaveCurrentSettings();
        MeasuredLeftMm = ExpectedMm;
        MeasuredTopMm = ExpectedMm;
        Status = L10n.T("Baskı yönü değiştirildi. ") + DirectionText + L10n.T(" Yön değişince kayma da değişir: kalibrasyon etiketini yeniden basıp ölçün.");
        OnPropertyChanged(nameof(DirectionText));
    }

    /// <summary>Ölçülen kaymaya göre ofsetleri uygular ve profile kaydeder.</summary>
    [RelayCommand(CanExecute = nameof(CanWork))]
    private void Apply()
    {
        double maxX = _print.Label.WidthMm / 2, maxY = _print.Label.HeightMm / 2;
        if (MeasuredLeftMm < 0 || MeasuredTopMm < 0 || MeasuredLeftMm > maxX || MeasuredTopMm > maxY)
        {
            _dialogs.ShowMessage(L10n.T("Kalibrasyon"), L10n.T("Ölçülen uzaklık etiketin yarısından büyük olamaz. Etiketin kenarından çerçevenin dış çizgisine kadar ölçün."), MessageKind.Warning);
            return;
        }
        Status = _print.StoreCalibration(NewOffsetXMm, NewOffsetYMm);
        // Ofset artık güncel: aynı ölçüm ikinci kez uygulanmasın.
        MeasuredLeftMm = ExpectedMm;
        MeasuredTopMm = ExpectedMm;
        NotifyOffsets();
        OnPropertyChanged(nameof(TargetProfileText));
    }

    /// <summary>Pencere kapanırken çağrılır (olay aboneliği bırakılır).</summary>
    public void Close() => _print.PropertyChanged -= OnPrintPropertyChanged;

    private void NotifyOffsets()
    {
        OnPropertyChanged(nameof(CurrentOffsetXMm));
        OnPropertyChanged(nameof(CurrentOffsetYMm));
        OnPropertyChanged(nameof(NewOffsetXMm));
        OnPropertyChanged(nameof(NewOffsetYMm));
        OnPropertyChanged(nameof(ShiftText));
    }

    private void OnPrintPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PrintViewModel.IsBusy):
                DetectSensorCommand.NotifyCanExecuteChanged();
                PrintLabelCommand.NotifyCanExecuteChanged();
                FlipDirectionCommand.NotifyCanExecuteChanged();
                ApplyCommand.NotifyCanExecuteChanged();
                break;
            case nameof(PrintViewModel.OffsetXMm) or nameof(PrintViewModel.OffsetYMm):
                NotifyOffsets();
                break;
            case nameof(PrintViewModel.Rotate180):
                OnPropertyChanged(nameof(DirectionText));
                break;
            case nameof(PrintViewModel.SelectedPrinter) or nameof(PrintViewModel.Method):
                OnPropertyChanged(nameof(PrinterText));
                OnPropertyChanged(nameof(IsTspl));
                OnPropertyChanged(nameof(CanDetectSensor));
                break;
            case nameof(PrintViewModel.SelectedProfile):
                OnPropertyChanged(nameof(TargetProfileText));
                break;
        }
    }
}
