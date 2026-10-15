using System.Globalization;
using AkelEtiket.Core.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace AkelEtiket.App.Controls;

/// <summary>
/// Sayı giriş kutusu: Türkçe ondalık (12,5) ve nokta (12.5) kabul eder; Enter veya odak kaybında uygular;
/// ↑/↓ ve fare tekerleği adım değiştirir (Shift ×10, Ctrl ×0,1); Esc geri alır.
/// </summary>
public class NumberBox : TextBox
{


    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(NumberBox),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged)
        {
            DefaultUpdateSourceTrigger = UpdateSourceTrigger.Explicit,
        });

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(NumberBox), new PropertyMetadata(double.MinValue));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(NumberBox), new PropertyMetadata(double.MaxValue));
    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(nameof(Step), typeof(double), typeof(NumberBox), new PropertyMetadata(1.0));
    public static readonly DependencyProperty DecimalsProperty = DependencyProperty.Register(nameof(Decimals), typeof(int), typeof(NumberBox), new PropertyMetadata(2, OnFormatChanged));

    public NumberBox()
    {
        HorizontalContentAlignment = HorizontalAlignment.Right;
        UpdateText();
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public int Decimals
    {
        get => (int)GetValue(DecimalsProperty);
        set => SetValue(DecimalsProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (NumberBox)d;
        if (!box.IsKeyboardFocusWithin) box.UpdateText();
    }

    private static void OnFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((NumberBox)d).UpdateText();

    private void UpdateText() => Text = Format(Value);

    private string Format(double value)
    {
        string pattern = Decimals <= 0 ? "0" : "0." + new string('#', Decimals);
        return value.ToString(pattern, CultureInfo.CurrentCulture);
    }

    public static bool TryParse(string text, out double value) => DisplayNumber.TryParse(text, CultureInfo.CurrentCulture, out value);

    private void Commit()
    {
        if (TryParse(Text, out double parsed))
        {
            SetValueAndPush(parsed);
        }
        UpdateText();
    }

    private void SetValueAndPush(double value)
    {
        value = Math.Clamp(Math.Round(value, Math.Max(0, Decimals)), Minimum, Maximum);
        Value = value;
        GetBindingExpression(ValueProperty)?.UpdateSource();
        // Model değeri sıkıştırmış olabilir: kaynaktan geri oku.
        GetBindingExpression(ValueProperty)?.UpdateTarget();
    }

    private void Increment(double direction)
    {
        double factor = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 0.1 : 1;
        double current = TryParse(Text, out double parsed) ? parsed : Value;
        SetValueAndPush(current + direction * Step * factor);
        UpdateText();
        SelectAll();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        Commit();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        SelectAll();
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!IsKeyboardFocusWithin)
        {
            Focus();
            e.Handled = true;
        }
        base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Commit();
                SelectAll();
                e.Handled = true;
                break;
            case Key.Escape:
                UpdateText();
                SelectAll();
                e.Handled = true;
                break;
            case Key.Up:
                Increment(1);
                e.Handled = true;
                break;
            case Key.Down:
                Increment(-1);
                e.Handled = true;
                break;
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (IsKeyboardFocusWithin)
        {
            Increment(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
            return;
        }
        base.OnMouseWheel(e);
    }
}
