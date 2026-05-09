namespace StreamShell;

/// <summary>
/// Manages undo state for a text buffer with cursor and selection tracking.
/// Each snapshot records the full text, cursor position, and optional selection anchor.
/// Uses a ring buffer internally to bound memory usage and avoid array allocation
/// on eviction (the Stack-based approach allocated a full array copy per eviction).
/// Supports a configurable maximum depth to bound memory usage.
/// </summary>
internal class UndoManager
{
    private const int DefaultMaxDepth = 50;

    private readonly int _maxDepth;
    private readonly (string text, int cursor, int? selection)[] _ring;
    private int _head;  // Next write position
    private int _count; // Number of items currently in the ring

    /// <summary>Creates an undo manager with the specified maximum depth.</summary>
    public UndoManager(int maxDepth = DefaultMaxDepth)
    {
        _maxDepth = Math.Max(1, maxDepth);
        _ring = new (string, int, int?)[_maxDepth];
    }

    /// <summary>Number of snapshots currently stored.</summary>
    public int Count => _count;

    /// <summary>Records a new undo snapshot, evicting the oldest if at capacity.</summary>
    public void Snapshot(string text, int cursor, int? selectionAnchor)
    {
        _ring[_head] = (text, cursor, selectionAnchor);
        _head = (_head + 1) % _maxDepth;
        if (_count < _maxDepth)
            _count++;
    }

    /// <summary>Restores the most recent snapshot (LIFO). Returns false if the stack is empty.</summary>
    public bool TryUndo(out string text, out int cursor, out int? selectionAnchor)
    {
        if (_count == 0)
        {
            text = string.Empty;
            cursor = 0;
            selectionAnchor = null;
            return false;
        }

        // Head points to the next write position — the item before head is the most recent
        _head = (_head - 1 + _maxDepth) % _maxDepth;
        _count--;

        var snapshot = _ring[_head];
        text = snapshot.text;
        cursor = snapshot.cursor;
        selectionAnchor = snapshot.selection;
        return true;
    }

    /// <summary>Clears all stored snapshots.</summary>
    public void Clear()
    {
        _head = 0;
        _count = 0;
        // Clear ring references so the GC can collect old strings
        Array.Clear(_ring, 0, _maxDepth);
    }
}
