namespace StreamShell;

/// <summary>
/// Manages undo state for a text buffer with cursor and selection tracking.
/// Each snapshot records the full text, cursor position, and optional selection anchor.
/// Supports a configurable maximum depth to bound memory usage.
/// </summary>
internal class UndoManager
{
    private const int DefaultMaxDepth = 50;

    private readonly int _maxDepth;
    private readonly Stack<(string text, int cursor, int? selection)> _stack = new();

    /// <summary>Creates an undo manager with the specified maximum depth.</summary>
    public UndoManager(int maxDepth = DefaultMaxDepth)
    {
        _maxDepth = Math.Max(1, maxDepth);
    }

    /// <summary>Number of snapshots currently stored.</summary>
    public int Count => _stack.Count;

    /// <summary>Records a new undo snapshot, evicting the oldest if at capacity.</summary>
    public void Snapshot(string text, int cursor, int? selectionAnchor)
    {
        if (_stack.Count >= _maxDepth)
            EvictOldest();

        _stack.Push((text, cursor, selectionAnchor));
    }

    /// <summary>Restores the most recent snapshot. Returns false if the stack is empty.</summary>
    public bool TryUndo(out string text, out int cursor, out int? selectionAnchor)
    {
        if (_stack.Count == 0)
        {
            text = string.Empty;
            cursor = 0;
            selectionAnchor = null;
            return false;
        }

        var snapshot = _stack.Pop();
        text = snapshot.text;
        cursor = snapshot.cursor;
        selectionAnchor = snapshot.selection;
        return true;
    }

    /// <summary>Clears all stored snapshots.</summary>
    public void Clear() => _stack.Clear();

    /// <summary>Removes the oldest snapshot (bottom of the stack).</summary>
    private void EvictOldest()
    {
        if (_stack.Count == 0)
            return;

        var items = new (string, int, int?)[_stack.Count];
        _stack.CopyTo(items, 0);
        _stack.Clear();

        // Skip the last (oldest) item, re-push newer ones
        for (int i = items.Length - 2; i >= 0; i--)
            _stack.Push(items[i]);
    }
}
