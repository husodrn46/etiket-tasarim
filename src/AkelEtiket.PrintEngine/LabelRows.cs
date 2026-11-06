using AkelEtiket.Core.Imaging;

namespace AkelEtiket.PrintEngine;

/// <summary>
/// Yan yana rulonun bir satırı. <see cref="Copies"/> aynı satırın kaç kez basılacağıdır;
/// <see cref="CompletedLabels"/> son kopyası bu satırda olan etiket (sayfa) sayısıdır. Baskı raporu, sayaçlar ve
/// SSCC kayıtları satırları değil etiketleri sayar.
/// </summary>
public sealed record LabelRow(MonoBitmap Bitmap, int Copies, int CompletedLabels);

/// <summary>
/// Etiketleri (her biri kendi kopya sayısıyla) yan yana rulonun satırlarına dizer. Sıra korunur: bir etiketin
/// kopyaları bitmeden sonraki etiket başlamaz. Aynı etiketle dolu satırlar tek <see cref="LabelRow"/>'da toplanır
/// (TSPL'de PRINT 1,adet). Son satır eksik kalırsa boş sütunlar beyaz basılır.
/// </summary>
public sealed class LabelRowBuilder
{
    private readonly LabelMedia _media;
    private readonly double _dotsPerMm;
    private readonly List<MonoBitmap> _pending = [];
    private int _pendingCompleted;

    public LabelRowBuilder(LabelMedia media, double dotsPerMm)
    {
        ArgumentNullException.ThrowIfNull(media);
        _media = media;
        _dotsPerMm = double.IsFinite(dotsPerMm) && dotsPerMm > 0 ? dotsPerMm : 8.0;
    }

    public int Columns => Math.Max(1, _media.Columns);

    /// <summary>Basılmayı bekleyen yarım satır var mı? (Varken iş bölünmez.)</summary>
    public bool HasPending => _pending.Count > 0;

    /// <summary>Bir etiketi ekler; tamamlanan satırları döndürür.</summary>
    public IReadOnlyList<LabelRow> Add(MonoBitmap label, int copies)
    {
        ArgumentNullException.ThrowIfNull(label);
        int remaining = TsplCommandBuilder.ClampCopies(copies);
        int columns = Columns;
        var rows = new List<LabelRow>();

        if (_pending.Count > 0)
        {
            int take = Math.Min(columns - _pending.Count, remaining);
            for (int i = 0; i < take; i++) _pending.Add(label);
            remaining -= take;
            if (remaining == 0) _pendingCompleted++;
            if (_pending.Count == columns) rows.Add(TakePending());
        }

        int fullRows = remaining / columns;
        if (fullRows > 0)
        {
            remaining -= fullRows * columns;
            rows.Add(new LabelRow(Compose(Enumerable.Repeat(label, columns).ToList(), _media, _dotsPerMm), fullRows, remaining == 0 ? 1 : 0));
        }

        if (remaining > 0)
        {
            for (int i = 0; i < remaining; i++) _pending.Add(label);
            _pendingCompleted++;
        }
        return rows;
    }

    /// <summary>Yarım kalan satırı (boş sütunları beyaz) döndürür; yoksa null.</summary>
    public LabelRow? Flush() => _pending.Count == 0 ? null : TakePending();

    private LabelRow TakePending()
    {
        var row = new LabelRow(Compose(_pending, _media, _dotsPerMm), 1, _pendingCompleted);
        _pending.Clear();
        _pendingCompleted = 0;
        return row;
    }

    /// <summary>Etiket görüntülerini satır görüntüsüne yerleştirir (sol kenar payı + sütun aralığı).</summary>
    public static MonoBitmap Compose(IReadOnlyList<MonoBitmap> labels, LabelMedia media, double dotsPerMm)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(media);
        int height = Math.Max(1, labels.Count == 0 ? media.HeightDots(dotsPerMm) : labels.Max(l => l.Height));
        int width = Math.Max(media.PaperWidthDots(dotsPerMm), 1);
        var row = new MonoBitmap(width, height);
        for (int i = 0; i < labels.Count && i < Math.Max(1, media.Columns); i++)
        {
            int x = (int)Math.Round(media.ColumnLeftMm(i) * dotsPerMm, MidpointRounding.AwayFromZero);
            row.DrawBitmap(labels[i], x, 0);
        }
        return row;
    }

    /// <summary>Tüm etiketleri satırlara çevirir (yarım son satır dahil).</summary>
    public static IEnumerable<LabelRow> Rows(IEnumerable<LabelPage> pages, LabelMedia media, double dotsPerMm)
    {
        ArgumentNullException.ThrowIfNull(pages);
        var builder = new LabelRowBuilder(media, dotsPerMm);
        foreach (var page in pages)
            foreach (var row in builder.Add(page.Bitmap, page.Copies))
                yield return row;
        if (builder.Flush() is { } last) yield return last;
    }

    /// <summary>Sürücü yolu için: kopyaları açılmış sayfa dizisini satırlara böler (her satır en çok N sayfa).</summary>
    public static IReadOnlyList<int[]> Chunk(IReadOnlyList<int> sequence, int columns) =>
        sequence.Chunk(Math.Max(1, columns)).ToList();
}
