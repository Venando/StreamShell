using System.Text;

namespace StreamShell;

/// <summary>
/// Manages a mutable text buffer with a cursor position.
/// Provides insert, delete, backspace, and cursor movement operations
/// without selection or undo awareness.
/// Caches <see cref="CurrentInput"/> to avoid repeated <c>StringBuilder.ToString()</c>
/// allocations (called dozens of times per render tick across the codebase).
/// </summary>
internal class TextBuffer
{
    private readonly StringBuilder _buffer = new();
    private int _cursor;
    private string? _cachedInput;
    private bool _dirty = true;

    /// <summary>Gets the current buffer content. Cached to avoid repeated StringBuilder.ToString().</summary>
    public string CurrentInput
    {
        get
        {
            if (_dirty)
            {
                _cachedInput = _buffer.ToString();
                _dirty = false;
            }
            return _cachedInput!;
        }
    }

    public int CursorPosition => _cursor;
    public int Length => _buffer.Length;

    public char this[int index] => _buffer[index];

    private void MarkDirty() => _dirty = true;

    public void Insert(char c)
    {
        _buffer.Insert(_cursor, c);
        _cursor++;
        MarkDirty();
    }

    public void Insert(string text)
    {
        _buffer.Insert(_cursor, text);
        _cursor += text.Length;
        MarkDirty();
    }

    /// <summary>Removes <paramref name="length"/> chars starting at <paramref name="start"/> and moves cursor to <paramref name="start"/>.</summary>
    public void Remove(int start, int length)
    {
        _buffer.Remove(start, length);
        _cursor = start;
        MarkDirty();
    }

    public void Backspace()
    {
        if (_cursor > 0)
        {
            _buffer.Remove(_cursor - 1, 1);
            _cursor--;
            MarkDirty();
        }
    }

    public void Delete()
    {
        if (_cursor < _buffer.Length)
        {
            _buffer.Remove(_cursor, 1);
            MarkDirty();
        }
    }

    public void MoveTo(int position)
    {
        _cursor = Math.Clamp(position, 0, _buffer.Length);
    }

    /// <summary>Replaces the entire buffer content and cursor position atomically.</summary>
    public void SetContent(string text, int cursor)
    {
        _buffer.Clear();
        _buffer.Append(text);
        _cursor = Math.Clamp(cursor, 0, _buffer.Length);
        _cachedInput = text;
        _dirty = false;
    }

    public void Clear()
    {
        _buffer.Clear();
        _cursor = 0;
        _cachedInput = string.Empty;
        _dirty = false;
    }
}
