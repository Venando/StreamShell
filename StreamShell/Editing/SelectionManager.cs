namespace StreamShell;

/// <summary>
/// Manages text selection state anchored at a fixed cursor position.
/// Selection spans from the anchor to the current cursor position (provided
/// by the caller on each query, avoiding stale-state bugs).
/// </summary>
internal class SelectionManager
{
    private int? _anchor;

    public bool HasAnchor => _anchor.HasValue;

    /// <summary>True when a non-collapsed selection is active at the given cursor.</summary>
    public bool IsActiveAt(int cursor) => _anchor.HasValue && _anchor.Value != cursor;

    public int SelectionStart(int cursor) => Math.Min(_anchor ?? cursor, cursor);
    public int SelectionEnd(int cursor) => Math.Max(_anchor ?? cursor, cursor);
    public int SelectionLength(int cursor) => SelectionEnd(cursor) - SelectionStart(cursor);

    public bool TryGetSelection(int cursor, out int start, out int length)
    {
        if (IsActiveAt(cursor))
        {
            start = SelectionStart(cursor);
            length = SelectionLength(cursor);
            return true;
        }
        start = 0;
        length = 0;
        return false;
    }

    public string SelectedText(int cursor, string input)
    {
        return IsActiveAt(cursor) ? input[SelectionStart(cursor)..SelectionEnd(cursor)] : "";
    }

    /// <summary>
    /// When <paramref name="shift"/> is held, establishes the anchor at
    /// <paramref name="cursor"/> if not already set. Without shift, clears selection.
    /// </summary>
    public void ForMovement(bool shift, int cursor)
    {
        if (!shift)
            _anchor = null;
        else if (!_anchor.HasValue)
            _anchor = cursor;
    }

    public int? GetAnchor() => _anchor;

    /// <summary>Forces the anchor to an explicit position, overriding any existing selection.</summary>
    public void SetAnchor(int position) => _anchor = position;

    public void Clear() => _anchor = null;

    public void Reset() => _anchor = null;
}
