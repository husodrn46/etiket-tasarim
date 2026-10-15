using System.Collections.ObjectModel;
using AkelEtiket.Core.Data;
using AkelEtiket.Core.Serialization;
using AkelEtiket.Editor.Printing;
using AkelEtiket.Rendering;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using L10n = AkelEtiket.Core.Localization.UiText;

namespace AkelEtiket.Editor.ViewModels;

/// <summary>Toplu önizlemede tek bir etiket.</summary>
public sealed partial class PreviewThumbnail : ObservableObject
{
    public PreviewThumbnail(int index, string caption)
    {
        Index = index;
        Caption = caption;
    }

    public int Index { get; }
    public string Caption { get; }

    [ObservableProperty]
    public partial object? Image { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial string? Problem { get; set; }
}

/// <summary>
/// Baskı önizlemesi: etiket, yazıcı çözünürlüğünde ve baskı kipinde (hatalar dahil) gösterilir.
/// Veri kaynağı varsa satırlar arasında gezilir ve tüm etiketler küçük resim olarak listelenir.
/// </summary>
public sealed partial class PreviewViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private CancellationTokenSource? _thumbnailCts;

    public PreviewViewModel(MainViewModel main)
    {
        _main = main;
        Plan = PrintPlanner.Plan(main.Session.Document, main.Data, main.Data.HasData && main.Session.Document.HasFieldPlaceholders, 1, 1, DateTime.Now);
        Index = 0;
        RenderCurrent();
        _ = BuildThumbnailsAsync();
    }

    public PrintPlan Plan { get; }

    public ObservableCollection<PreviewThumbnail> Thumbnails { get; } = [];
    public ObservableCollection<string> Issues { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    public partial int Index { get; set; }

    [ObservableProperty]
    public partial object? Image { get; private set; }

    [ObservableProperty]
    public partial int PixelWidth { get; private set; }

    [ObservableProperty]
    public partial int PixelHeight { get; private set; }

    [ObservableProperty]
    public partial string Caption { get; private set; } = "";

    [ObservableProperty]
    public partial string ThumbnailStatus { get; private set; } = "";

    public double WidthMm => _main.Session.Document.Label.WidthMm;
    public double HeightMm => _main.Session.Document.Label.HeightMm;
    public double DotsPerMm => _main.Settings.Printer.DotsPerMm;

    public int Count => Math.Max(1, Plan.Items.Count);

    public string PositionText => Plan.Items.Count <= 1 ? "" : $"{Index + 1} / {Plan.Items.Count}";

    public string SummaryText => Plan.UsesData
        ? L10n.T($"{Plan.Items.Count} farklı etiket, toplam {Plan.TotalLabels} adet") + (Plan.Skipped.Count > 0 ? L10n.T($" • {Plan.Skipped.Count} hatalı satır atlanacak") : "")
        : L10n.T("Tek etiket önizlemesi");

    partial void OnIndexChanged(int value) => RenderCurrent();

    [RelayCommand]
    private void Next()
    {
        if (Plan.Items.Count > 1) Index = (Index + 1) % Plan.Items.Count;
    }

    [RelayCommand]
    private void Previous()
    {
        if (Plan.Items.Count > 1) Index = (Index - 1 + Plan.Items.Count) % Plan.Items.Count;
    }

    [RelayCommand]
    private void Show(PreviewThumbnail? thumbnail)
    {
        if (thumbnail is not null) Index = thumbnail.Index;
    }

    private IFieldValueSource? FieldsFor(int index) =>
        Plan.Items.Count > 0 ? Plan.Items[Math.Clamp(index, 0, Plan.Items.Count - 1)].Fields : _main.Data.GetPreviewFields(DateTime.Now);

    private void RenderCurrent()
    {
        var mode = Plan.UsesData ? RenderMode.Print : RenderMode.Design;
        var result = _main.Session.RenderLabel(mode, FieldsFor(Index), DotsPerMm);
        Image = _main.Services.ImageFactory.Create(MonoImageExport.ToBgra32(result.Bitmap, null), result.Bitmap.Width, result.Bitmap.Height);
        PixelWidth = result.Bitmap.Width;
        PixelHeight = result.Bitmap.Height;
        Caption = Plan.Items.Count > 0 && Plan.Items[Math.Clamp(Index, 0, Plan.Items.Count - 1)].RowNumber is { } row
            ? L10n.T($"Satır {row} • {result.Bitmap.Width} × {result.Bitmap.Height} nokta (gerçek baskı çözünürlüğü)")
            : L10n.T($"{result.Bitmap.Width} × {result.Bitmap.Height} nokta (gerçek baskı çözünürlüğü)");
        Issues.Clear();
        foreach (var issue in result.Issues.Where(i => i.Severity >= RenderIssueSeverity.Warning).DistinctBy(i => i.ToString()))
            Issues.Add((issue.Severity == RenderIssueSeverity.Error ? L10n.T("HATA: ") : L10n.T("Uyarı: ")) + issue);
        if (Index == 0)
            foreach (var skipped in Plan.Skipped.Take(20)) Issues.Add(L10n.T("Atlanacak – ") + skipped);
    }

    /// <summary>Toplu önizleme küçük resimleri arka planda üretilir (en fazla 300).</summary>
    private async Task BuildThumbnailsAsync()
    {
        if (Plan.Items.Count <= 1) return;
        _thumbnailCts = new CancellationTokenSource();
        var token = _thumbnailCts.Token;
        int count = Math.Min(Plan.Items.Count, 300);
        for (int i = 0; i < count; i++)
        {
            var item = Plan.Items[i];
            Thumbnails.Add(new PreviewThumbnail(i, item.Describe() + (item.Copies > 1 ? $" × {item.Copies}" : "")));
        }
        ThumbnailStatus = count < Plan.Items.Count ? L10n.T($"İlk {count} etiket gösteriliyor.") : "";
        var document = ModelJson.Clone(_main.Session.Document);
        var assets = _main.Session.Assets;
        var factory = _main.Services.ImageFactory;
        await Task.Run(() =>
        {
            using var renderer = new LabelRenderer(assets);
            for (int i = 0; i < count && !token.IsCancellationRequested; i++)
            {
                var result = renderer.RenderLabel(document, new RenderOptions { DotsPerMm = 3, Fields = Plan.Items[i].Fields, Mode = RenderMode.Print, ImageResolver = _main.Session.ImageResolver });
                var image = factory.Create(MonoImageExport.ToBgra32(result.Bitmap, null), result.Bitmap.Width, result.Bitmap.Height);
                var thumbnail = Thumbnails[i];
                string? problem = result.Errors.Select(e => e.ToString()).FirstOrDefault();
                _main.Services.Dispatcher.Post(() =>
                {
                    thumbnail.Image = image;
                    thumbnail.HasError = problem is not null;
                    thumbnail.Problem = problem;
                });
            }
        }, token);
    }

    public void Close() => _thumbnailCts?.Cancel();
}
