namespace AkelEtiket.Editor.Services;

/// <summary>Geri al yığınındaki bir kayıt: değişiklikten önceki durum ve değişikliğin açıklaması.</summary>
public sealed record UndoEntry(string State, string? Description, DateTime Time);

/// <summary>
/// Anlık görüntü (snapshot) tabanlı geri al / ileri al. Her kayıt belgenin JSON hâlidir (görseller
/// kimlikle başvurulur, bu yüzden kayıtlar küçüktür). Ardışık aynı tür değişiklikler (ör. bir metin
/// kutusuna yazmak) birleştirilir. Kayıtlar isteğe bağlı bir açıklama taşır (ör. "Yapay zekâ önerisi: ...").
/// </summary>
public sealed class UndoManager
{
    private readonly LinkedList<UndoEntry> _undo = new();
    private readonly Stack<UndoEntry> _redo = new();
    private string? _lastCoalesceKey;
    private DateTime _lastChangeAt;

    public UndoManager(int capacity = 200) => Capacity = Math.Max(10, capacity);

    public int Capacity { get; }

    /// <summary>Aynı anahtarlı değişikliklerin birleştirileceği süre.</summary>
    public TimeSpan CoalesceWindow { get; set; } = TimeSpan.FromSeconds(1.5);

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;

    /// <summary>Geri alınacak değişikliğin açıklaması (yoksa null).</summary>
    public string? UndoDescription => _undo.Last?.Value.Description;

    /// <summary>İleri alınacak değişikliğin açıklaması (yoksa null).</summary>
    public string? RedoDescription => _redo.Count > 0 ? _redo.Peek().Description : null;

    /// <summary>Geri al kayıtları, en yenisi sonda.</summary>
    public IReadOnlyList<UndoEntry> UndoEntries => _undo.ToList();

    public event EventHandler? StateChanged;

    /// <summary>
    /// Değişiklikten ÖNCEKİ durumu kaydeder. <paramref name="coalesceKey"/> bir önceki kayıtla aynıysa
    /// ve süre penceresi içindeyse yeni kayıt açılmaz.
    /// </summary>
    public void Record(string stateBefore, string? coalesceKey = null, DateTime? now = null, string? description = null)
    {
        var time = now ?? DateTime.UtcNow;
        bool coalesce = coalesceKey is not null && coalesceKey == _lastCoalesceKey && time - _lastChangeAt <= CoalesceWindow && _undo.Count > 0;
        _lastCoalesceKey = coalesceKey;
        _lastChangeAt = time;
        _redo.Clear();
        if (!coalesce)
        {
            if (_undo.Last?.Value.State == stateBefore) return;
            _undo.AddLast(new UndoEntry(stateBefore, description, time));
            while (_undo.Count > Capacity) _undo.RemoveFirst();
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Birleştirmeyi keser (ör. odak başka alana geçince).</summary>
    public void BreakCoalescing() => _lastCoalesceKey = null;

    /// <summary>Geri alır: mevcut durumu ileri al yığınına koyar, önceki durumu döndürür.</summary>
    public string? Undo(string currentState)
    {
        if (_undo.Count == 0) return null;
        var previous = _undo.Last!.Value;
        _undo.RemoveLast();
        _redo.Push(previous with { State = currentState });
        _lastCoalesceKey = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return previous.State;
    }

    public string? Redo(string currentState)
    {
        if (_redo.Count == 0) return null;
        var next = _redo.Pop();
        _undo.AddLast(next with { State = currentState });
        _lastCoalesceKey = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return next.State;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _lastCoalesceKey = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
