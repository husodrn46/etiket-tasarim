using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace AkelEtiket.App.Services;

/// <summary>Short, user-triggered UI feedback. Label geometry and printing never use animations.</summary>
public static class Motion
{
    private static readonly List<WeakReference<UIElement>> Animated = [];
    public static bool PreferenceEnabled { get; private set; } = true;
    public static bool IsEnabled => PreferenceEnabled && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
    static Motion() => SystemParameters.StaticPropertyChanged += OnSystemChanged;
    public static void Configure(bool enabled)
    {
        PreferenceEnabled=enabled;
        if(!IsEnabled) ClearAnimations();
    }
    private static void OnSystemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if(!IsEnabled) ClearAnimations();
    }
    private static void ClearAnimations()
    {
        foreach(var reference in Animated.ToArray())
            if(reference.TryGetTarget(out var element))
            {
                if(element.Dispatcher.CheckAccess()) element.BeginAnimation(UIElement.OpacityProperty,null);
                else element.Dispatcher.BeginInvoke(()=>element.BeginAnimation(UIElement.OpacityProperty,null));
            }
        Animated.Clear();
    }
    public static void Pulse(UIElement element)
    {
        if(!IsEnabled || !element.IsVisible) { element.BeginAnimation(UIElement.OpacityProperty,null); return; }
        Animate(element,.86,1,140);
    }
    private static void Animate(UIElement element,double from,double to,int milliseconds)
    {
        Animated.RemoveAll(r=>!r.TryGetTarget(out _));
        if(!Animated.Any(r=>r.TryGetTarget(out var target)&&ReferenceEquals(target,element))) Animated.Add(new(element));
        element.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(from,to,TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut }, FillBehavior=FillBehavior.Stop,
        },HandoffBehavior.SnapshotAndReplace);
    }
    public static readonly DependencyProperty InteractiveProperty=DependencyProperty.RegisterAttached("Interactive",typeof(bool),typeof(Motion),new PropertyMetadata(false,OnInteractiveChanged));
    public static bool GetInteractive(DependencyObject value)=>(bool)value.GetValue(InteractiveProperty);
    public static void SetInteractive(DependencyObject value,bool enabled)=>value.SetValue(InteractiveProperty,enabled);
    private static void OnInteractiveChanged(DependencyObject d,DependencyPropertyChangedEventArgs e)
    {
        if(d is not ButtonBase button) return;
        if((bool)e.OldValue) { button.MouseEnter-=OnEnter;button.PreviewMouseLeftButtonDown-=OnPress;button.PreviewMouseLeftButtonUp-=OnRelease; }
        if((bool)e.NewValue) { button.MouseEnter+=OnEnter;button.PreviewMouseLeftButtonDown+=OnPress;button.PreviewMouseLeftButtonUp+=OnRelease; }
    }
    private static void OnEnter(object sender,MouseEventArgs e) { if(sender is UIElement element) Pulse(element); }
    private static void OnPress(object sender,MouseButtonEventArgs e)
    { if(sender is UIElement element && IsEnabled) Animate(element,1,.76,83); }
    private static void OnRelease(object sender,MouseButtonEventArgs e) { if(sender is UIElement element) Pulse(element); }
    public static readonly DependencyProperty RevealProperty=DependencyProperty.RegisterAttached("Reveal",typeof(bool),typeof(Motion),new PropertyMetadata(false,OnRevealChanged));
    public static bool GetReveal(DependencyObject value)=>(bool)value.GetValue(RevealProperty);
    public static void SetReveal(DependencyObject value,bool enabled)=>value.SetValue(RevealProperty,enabled);
    private static void OnRevealChanged(DependencyObject d,DependencyPropertyChangedEventArgs e)
    {
        if(d is not FrameworkElement element)return;
        if((bool)e.OldValue) { element.Loaded-=OnLoaded;element.IsVisibleChanged-=OnVisible; }
        if((bool)e.NewValue) { element.Loaded+=OnLoaded;element.IsVisibleChanged+=OnVisible; }
    }
    private static void OnLoaded(object sender,RoutedEventArgs e) { if(sender is UIElement element)Pulse(element); }
    private static void OnVisible(object sender,DependencyPropertyChangedEventArgs e) { if((bool)e.NewValue && sender is UIElement element)Pulse(element); }
}
