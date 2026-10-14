using AkelEtiket.Core.Model;

namespace AkelEtiket.BarcodeEngine;

/// <summary>Kodlanmış barkodun yazıcıdan bağımsız (modül cinsinden) tarifi.</summary>
public abstract class BarcodeSymbol
{
    protected BarcodeSymbol(BarcodeType type, BarcodeValidationResult validation)
    {
        Type = type;
        Validation = validation;
    }

    public BarcodeType Type { get; }
    public BarcodeValidationResult Validation { get; }
    public string HumanReadableText => Validation.HumanReadableText;
    public string VerificationText => Validation.VerificationText;
}

/// <summary>Tek bir çubuk veya boşluk.</summary>
/// <param name="IsBar">true = siyah çubuk</param>
/// <param name="Modules">Modül sayısı (geniş/dar türlerde dar = 1)</param>
/// <param name="IsWide">Code 39/ITF geniş eleman</param>
/// <param name="StartModule">Kodlayıcı desenindeki başlangıç modülü</param>
public readonly record struct BarRun(bool IsBar, int Modules, bool IsWide, int StartModule);

/// <summary>EAN/UPC tarzı okunabilir metin parçası; konumlar modül cinsinden (0 = ilk çubuk modülü).</summary>
public sealed record HriSegment(string Text, double StartModule, double EndModule, bool Small);

/// <summary>Tek boyutlu barkod.</summary>
public sealed class LinearSymbol : BarcodeSymbol
{
    internal LinearSymbol(
        BarcodeType type,
        BarcodeValidationResult validation,
        bool[] modules,
        bool usesWideNarrow,
        double wideToNarrowRatio,
        int quietZoneModules,
        IReadOnlyList<(int Start, int End)> extendedRanges,
        IReadOnlyList<HriSegment> hriSegments,
        bool bearerBars)
        : base(type, validation)
    {
        Modules = modules;
        UsesWideNarrow = usesWideNarrow;
        WideToNarrowRatio = wideToNarrowRatio;
        QuietZoneModules = quietZoneModules;
        ExtendedRanges = extendedRanges;
        HriSegments = hriSegments;
        HasBearerBars = bearerBars;
        Runs = BuildRuns(modules, usesWideNarrow);
    }

    /// <summary>Kodlayıcının ürettiği modül deseni (true = çubuk).</summary>
    public bool[] Modules { get; }

    public IReadOnlyList<BarRun> Runs { get; }

    /// <summary>Code 39 ve ITF: elemanlar dar/geniş olarak yeniden ölçeklenir.</summary>
    public bool UsesWideNarrow { get; }

    public double WideToNarrowRatio { get; }

    /// <summary>Her iki yandaki asgari sessiz bölge (dar modül cinsinden).</summary>
    public int QuietZoneModules { get; }

    /// <summary>EAN/UPC: sessiz bölge zorunlu ve ilk/son hane sessiz bölgede yazılır.</summary>
    public bool IsUpcEanFamily => Type is BarcodeType.Ean13 or BarcodeType.Ean8 or BarcodeType.UpcA;

    /// <summary>Aşağı uzatılan koruma çubuklarının modül aralıkları [Start, End).</summary>
    public IReadOnlyList<(int Start, int End)> ExtendedRanges { get; }

    /// <summary>Boşsa metin ortalanarak tek parça yazılır.</summary>
    public IReadOnlyList<HriSegment> HriSegments { get; }

    public bool HasBearerBars { get; }

    /// <summary>Dar modül genişliği verildiğinde geniş eleman genişliği (nokta).</summary>
    public int GetWideDots(int narrowDots) =>
        Math.Max(narrowDots + 1, (int)Math.Round(narrowDots * WideToNarrowRatio, MidpointRounding.AwayFromZero));

    /// <summary>Çubuk alanının (sessiz bölgeler hariç) nokta genişliği.</summary>
    public int GetSymbolWidthDots(int narrowDots)
    {
        int width = 0;
        int wide = GetWideDots(narrowDots);
        foreach (var run in Runs)
            width += run.IsWide ? wide : run.Modules * narrowDots;
        return width;
    }

    public bool IsExtended(int startModule)
    {
        foreach (var (start, end) in ExtendedRanges)
            if (startModule >= start && startModule < end) return true;
        return false;
    }

    private static List<BarRun> BuildRuns(bool[] modules, bool wideNarrow)
    {
        var runs = new List<BarRun>();
        int i = 0;
        while (i < modules.Length)
        {
            bool value = modules[i];
            int start = i;
            while (i < modules.Length && modules[i] == value) i++;
            int length = i - start;
            runs.Add(wideNarrow
                ? new BarRun(value, 1, length > 1, start)
                : new BarRun(value, length, false, start));
        }
        // Baştaki/sondaki boşlukları at (desen çubukla başlayıp bitmeli).
        while (runs.Count > 0 && !runs[0].IsBar) runs.RemoveAt(0);
        while (runs.Count > 0 && !runs[^1].IsBar) runs.RemoveAt(runs.Count - 1);
        return runs;
    }
}

/// <summary>İki boyutlu barkod (QR, Data Matrix, PDF417).</summary>
public sealed class MatrixSymbol : BarcodeSymbol
{
    private readonly bool[] _cells;

    internal MatrixSymbol(BarcodeType type, BarcodeValidationResult validation, int width, int height, bool[] cells, int quietZoneModules)
        : base(type, validation)
    {
        Width = width;
        Height = height;
        _cells = cells;
        QuietZoneModules = quietZoneModules;
    }

    /// <summary>Modül sütunu sayısı.</summary>
    public int Width { get; }

    /// <summary>Modül satırı sayısı (PDF417'de satır yüksekliği dahil).</summary>
    public int Height { get; }

    public int QuietZoneModules { get; }

    public bool this[int x, int y] => _cells[y * Width + x];
}
