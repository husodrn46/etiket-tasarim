using AkelEtiket.Rendering;

namespace AkelEtiket.Editor.Printing;

/// <summary>
/// Bir baskı işinde plan öğelerinin sonucunu izler: hangi öğe üretildi (gönderim sırasıyla), hangisi üretilemedi.
/// İş bitince yazıcı kuyruğuna giden etiket sayısından her öğenin durumu (gönderildi / hatalı / gönderilmedi) çıkarılır;
/// "kaldığı yerden devam" gönderilmemiş öğelerle yeni bir plan kurar.
/// </summary>
public sealed class PrintJobRecorder
{
    private readonly IReadOnlyList<PrintItem> _items;
    private readonly List<int> _produced = [];
    private readonly Dictionary<int, string> _failed = [];
    private readonly object _lock = new();

    public PrintJobRecorder(IReadOnlyList<PrintItem> items)
    {
        _items = items;
    }

    public IReadOnlyList<PrintItem> Items => _items;

    /// <summary>Öğe üretildi ve gönderim sırasına girdi.</summary>
    public void Produced(int index)
    {
        lock (_lock) _produced.Add(index);
    }

    /// <summary>Öğe üretilemedi (gönderilmeyecek).</summary>
    public void Failed(int index, string problem)
    {
        lock (_lock) _failed[index] = problem;
    }

    /// <summary>
    /// Durumları hesaplar: üretim sırasındaki ilk <paramref name="spooledPages"/> öğe kuyruğa gönderilmiştir
    /// (TSPL'de sayfalar sırayla yazılır, iş parçaları sırayla tamamlanır).
    /// </summary>
    public IReadOnlyList<PrintItemState> States(int spooledPages)
    {
        lock (_lock)
        {
            var states = Enumerable.Repeat(PrintItemState.NotSent, _items.Count).ToArray();
            foreach (var (index, _) in _failed) states[index] = PrintItemState.Failed;
            int sent = Math.Clamp(spooledPages, 0, _produced.Count);
            for (int i = 0; i < sent; i++) states[_produced[i]] = PrintItemState.Sent;
            return states;
        }
    }

    /// <summary>Geçmiş kaydı için öğeler.</summary>
    public List<PrintHistoryItem> ToHistory(int spooledPages)
    {
        var states = States(spooledPages);
        lock (_lock)
        {
            return _items.Select((item, i) => new PrintHistoryItem
            {
                Sequence = item.Sequence,
                Row = item.RowNumber,
                Copies = item.Copies,
                State = states[i],
                Problem = _failed.TryGetValue(i, out var p) ? p : null,
            }).ToList();
        }
    }

    /// <summary>Gönderilmemiş öğeler (plan sırasıyla) — "kaldığı yerden devam" planı.</summary>
    public IReadOnlyList<PrintItem> Remaining(int spooledPages)
    {
        var states = States(spooledPages);
        return _items.Where((_, i) => states[i] == PrintItemState.NotSent).ToList();
    }
}
