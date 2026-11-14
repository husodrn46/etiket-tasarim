using System.Globalization;
using AkelEtiket.App.Mac.Services;
using AkelEtiket.Core.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

namespace AkelEtiket.App.Mac.Controls;

/// <summary>
/// Sayı giriş kutusu: ondalık virgül (12,5) ve nokta (12.5) kabul eder; Enter veya odak kaybında uygular;
/// ↑/↓ ve fare tekerleği adım değiştirir (Shift ×10, komut tuşu ×0,1); Esc geri alır.
/// Değer kaynağa yalnızca uygulandığında yazılır; yazarken her tuşta model değişmez.
/// </summary>
public class NumberBox : TextBox
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<NumberBox, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<NumberBox, double>(nameof(Minimum), double.MinValue);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<NumberBox, double>(nameof(Maximum), double.MaxValue);

    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<NumberBox, double>(nameof(Step), 1.0);

    public static readonly StyledProperty<int> DecimalsProperty =
        AvaloniaProperty.Register<NumberBox, int>(nameof(Decimals), 2);

    protected override Type StyleKeyOverride => typeof(TextBox);

    public NumberBox()
    {
        HorizontalContentAlignment = HorizontalAlignment.Right;
        UpdateText();
        LostFocus += (_, _) => Commit();
        GotFocus += (_, _) => OnFocused();
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public int Decimals
    {
        get => GetValue(DecimalsProperty);
        set => SetValue(DecimalsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Kullanıcı yazarken kaynaktan gelen değer metni ezmez.
        if (change.Property == ValueProperty && !IsKeyboardFocusWithin) UpdateText();
        else if (change.Property == DecimalsProperty) UpdateText();
    }

    private void UpdateText() => Text = Format(Value);

    private string Format(double value)
    {
        string pattern = Decimals <= 0 ? "0" : "0." + new string('#', Decimals);
        return value.ToString(pattern, CultureInfo.CurrentCulture);
    }

    public static bool TryParse(string? text, out double value) => DisplayNumber.TryParse(text ?? "", CultureInfo.CurrentCulture, out value);

    /// <summary>Yazılan metni değere çevirip kaynağa yazar; geçersiz metin eski değere döner.</summary>
    public void Commit()
    {
        if (TryParse(Text, out double parsed)) SetValueAndPush(parsed);
        UpdateText();
    }

    private void SetValueAndPush(double value) =>
        SetCurrentValue(ValueProperty, Math.Clamp(Math.Round(value, Math.Max(0, Decimals)), Minimum, Maximum));

    private void Increment(double direction, KeyModifiers modifiers)
    {
        double factor = modifiers.HasFlag(KeyModifiers.Shift) ? 10 : modifiers.HasFlag(PlatformKeys.Command) ? 0.1 : 1;
        double current = TryParse(Text, out double parsed) ? parsed : Value;
        SetValueAndPush(current + direction * Step * factor);
        UpdateText();
        SelectAll();
    }

    private bool _justFocused;
    private bool _focusingClick;

    private void OnFocused()
    {
        SelectAll();
        // Odak tıklamayla geldiyse aynı tıklamanın basma olayı hemen ardından gelir (bkz. OnPointerPressed).
        _justFocused = true;
        Dispatcher.UIThread.Post(() => _justFocused = false);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        // Kutuyu odaklayan tıklama yalnızca odaklar ve tümünü seçer; imleci tıklanan yere taşımaz
        // (yazılan değer eskisinin yerine geçsin). Odaktaki kutuda sonraki tıklamalar imleci normal biçimde taşır.
        if (_justFocused)
        {
            _justFocused = false;
            _focusingClick = true;
            SelectAll();
            e.Handled = true;
            return;
        }
        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_focusingClick)
        {
            // Odaklayan tıklamanın bırakılması da seçimi bozmamalı.
            _focusingClick = false;
            e.Handled = true;
            return;
        }
        base.OnPointerReleased(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Commit();
                SelectAll();
                e.Handled = true;
                return;
            case Key.Escape:
                UpdateText();
                SelectAll();
                e.Handled = true;
                return;
            case Key.Up:
                Increment(1, e.KeyModifiers);
                e.Handled = true;
                return;
            case Key.Down:
                Increment(-1, e.KeyModifiers);
                e.Handled = true;
                return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (IsKeyboardFocusWithin && e.Delta.Y != 0)
        {
            Increment(e.Delta.Y > 0 ? 1 : -1, e.KeyModifiers);
            e.Handled = true;
            return;
        }
        base.OnPointerWheelChanged(e);
    }
}
