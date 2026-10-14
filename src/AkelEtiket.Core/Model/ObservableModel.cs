using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AkelEtiket.Core.Model;

/// <summary>
/// Değişiklik bildiren model tabanı. Arayüz doğrudan modele bağlanabilir;
/// editör bu bildirimlerle yeniden çizim ve geri al kaydı yapar.
/// </summary>
public abstract class ObservableModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>NaN/sonsuz değerleri reddedip aralığa sıkıştırır.</summary>
    protected static double Clamp(double value, double min, double max, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return fallback;
        return Math.Clamp(value, min, max);
    }
}
