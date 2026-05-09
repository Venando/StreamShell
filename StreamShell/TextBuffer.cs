using System.Text;

namespace StreamShell;

/// <summary>
/// Manages a mutable text buffer with a cursor position.
/// Provides insert, delete, backspace, and cursor movement operations
/// without selection or undo awareness.
/// </summary>
internal class TextBuffer
{
    private readonly StringBuilder _buffer = new();
    private int _cursor;

    /// <summary>
    /// Cached string snapshot of the buffer contents.
    /// Invalidated on every mutation to avoid allocating a new string
    /// each time <see cref="CurrentInput"/> is read (called many times
    /// per render tick by ProcessOneTick, EnsureProperPanel,
    /// CursorMovementHandler, StateDiffersFromRender, etc.).
    /// </summary>
    private string? _cached;

    /// <summary>
    /// Returns the current buffer content as a string.
    /// The result is cached until the next mutation, avoiding repeated
    /// <c>StringBuilder.ToString()</c> allocations.
    /// </summary>
    public string CurrentInput => _cached ??= _buffer.ToString();

    public int CursorPosition => _cursor;
    public int Length => _buffer.Length;

    public char this[int index] => _buffer[index];

    private void InvalidateCache() => _cached = null;

    public void Insert(char c)
    {
        InvalidateCache();
        _buffer.Insert(_cursor, c);
        _cursor++;
    }

    public void Insert(string text)
    {
        InvalidateCache();
        _buffer.Insert(_cursor, text);
        _cursor += text.Length;
    }

    /// <summary>Removes <paramref name="length"/> chars starting at <paramref name="start"/> and moves cursor to <paramref name="start"/>.</summary>
    public void Remove(int start, int length)
    {
        InvalidateCache();
        _buffer.Remove(start, length);
        _cursor = start;
    }

    public void Backspace()
    {
        if (_cursor > 0)
        {
            InvalidateCache();
            _buffer.Remove(_cursor - 1, 1);
            _cursor--;
        }
    }

    public void Delete()
    {
        if (_cursor < _buffer.Length)
        {
            InvalidateCache();
            _buffer.Remove(_cursor, 1);
        }
    }

    public void MoveTo(int position)
    {
        _cursor = Math.Clamp(position, 0, _buffer.Length);
    }

    /// <summary>Replaces the entire buffer content and cursor position atomically.</summary>
    public void SetContent(string text, int cursor)
    {
        InvalidateCache();
        _buffer.Clear();
        _buffer.Append(text);
        _cursor = Math.Clamp(cursor, 0, _buffer.Length);
    }

    public void Clear()
    {
        InvalidateCache();
        _buffer.Clear();
        _cursor = 0;
    }
}
