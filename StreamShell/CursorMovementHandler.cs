namespace StreamShell;

/// <summary>
/// Handles cursor movement within a text buffer, including character-level,
/// word-boundary, and vertical (visual line) navigation with sticky column tracking.
/// All methods operate on the shared <see cref="TextBuffer"/> and
/// <see cref="SelectionManager"/> but are independent of clipboard or undo logic.
/// </summary>
internal class CursorMovementHandler
{
    private readonly TextBuffer _buffer;
    private readonly SelectionManager _selection;
    private readonly Func<int> _getRightMargin;
    private int _stickyColumn = -1;

    public CursorMovementHandler(TextBuffer buffer, SelectionManager selection, Func<int> getRightMargin)
    {
        _buffer = buffer;
        _selection = selection;
        _getRightMargin = getRightMargin;
    }

    /// <summary>Resets sticky column tracking (e.g. when the user presses left/right or home/end).</summary>
    public void ResetStickyColumn() => _stickyColumn = -1;

    // ══════════════════════════════════════════════════════════════════
    //  Character-level movement
    // ══════════════════════════════════════════════════════════════════

    public void MoveCursorLeft(bool shift)
    {
        if (_buffer.CursorPosition <= 0)
        {
            _selection.ForMovement(shift, _buffer.CursorPosition);
            return;
        }

        _selection.ForMovement(shift, _buffer.CursorPosition);
        _buffer.MoveTo(_buffer.CursorPosition - 1);

        // When selecting with Shift, skip newline characters so the
        // first selected character is visible content, not a structural line break.
        if (shift)
        {
            while (_buffer.CursorPosition > 0 && _buffer[_buffer.CursorPosition] == '\n')
                _buffer.MoveTo(_buffer.CursorPosition - 1);
        }
    }

    public void MoveCursorRight(bool shift)
    {
        if (_buffer.CursorPosition >= _buffer.Length)
        {
            if (!shift) _selection.Clear();
            return;
        }

        int originalPos = _buffer.CursorPosition;
        _buffer.MoveTo(_buffer.CursorPosition + 1);

        // When selecting with Shift, skip newline characters so the
        // first selected character is visible content, not a structural line break.
        if (shift)
        {
            while (_buffer.CursorPosition < _buffer.Length && _buffer[_buffer.CursorPosition] == '\n')
                _buffer.MoveTo(_buffer.CursorPosition + 1);

            // Anchor at the last visible character before the newline
            if (!_selection.HasAnchor)
            {
                int anchor = originalPos;
                while (anchor > 0 && _buffer[anchor - 1] == '\n')
                    anchor--;
                _selection.SetAnchor(anchor);
            }
        }
        else
        {
            _selection.Clear();
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Line-based movement (Home / End)
    // ══════════════════════════════════════════════════════════════════

    public void MoveCursorHome(bool shift)
    {
        string input = _buffer.CurrentInput;
        int cursor = _buffer.CursorPosition;
        int lineStart = cursor > 0
            ? input.LastIndexOf('\n', cursor - 1) + 1
            : 0;

        if (lineStart != cursor)
        {
            _selection.ForMovement(shift, cursor);
            _buffer.MoveTo(lineStart);
            return;
        }

        // Already at line start — act like Left arrow
        MoveCursorLeft(shift);
    }

    public void MoveCursorEnd(bool shift)
    {
        string input = _buffer.CurrentInput;
        int cursor = _buffer.CursorPosition;
        int nextNewline = input.IndexOf('\n', cursor);
        int lineEnd = nextNewline >= 0 ? nextNewline : _buffer.Length;

        if (lineEnd != cursor)
        {
            _selection.ForMovement(shift, cursor);
            _buffer.MoveTo(lineEnd);
            return;
        }

        // Already at line end — act like Right arrow
        MoveCursorRight(shift);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Word-boundary movement (Ctrl+← / Ctrl+→)
    // ══════════════════════════════════════════════════════════════════

    public void MoveCursorWordLeft(bool shift)
    {
        _selection.ForMovement(shift, _buffer.CursorPosition);

        if (_buffer.CursorPosition > 0)
            _buffer.MoveTo(FindPreviousWordStart(_buffer.CurrentInput, _buffer.CursorPosition));
    }

    public void MoveCursorWordRight(bool shift)
    {
        _selection.ForMovement(shift, _buffer.CursorPosition);

        if (_buffer.CursorPosition < _buffer.Length)
            _buffer.MoveTo(FindNextWordStart(_buffer.CurrentInput, _buffer.CursorPosition));
    }

    private static int FindPreviousWordStart(string input, int pos)
    {
        if (pos <= 0) return 0;
        int i = pos - 1;
        while (i >= 0 && char.IsWhiteSpace(input[i])) i--;
        while (i >= 0 && !char.IsWhiteSpace(input[i])) i--;
        return i + 1;
    }

    private static int FindNextWordStart(string input, int pos)
    {
        int len = input.Length;
        if (pos >= len) return len;
        int i = pos;
        while (i < len && !char.IsWhiteSpace(input[i])) i++;
        while (i < len && char.IsWhiteSpace(input[i])) i++;
        return i;
    }

    // ══════════════════════════════════════════════════════════════════
    //  Vertical movement (↑ / ↓)
    // ══════════════════════════════════════════════════════════════════

    private int GetEffectiveWidth()
    {
        int effectiveMargin = Math.Max(10, _getRightMargin());
        return Math.Max(1, Math.Min(effectiveMargin, Console.WindowWidth));
    }

    private (int visLine, int visCol) GetVisualPosition(string input, List<string> visualLines, List<int> offsets)
    {
        int cursor = _buffer.CursorPosition;
        for (int i = visualLines.Count - 1; i >= 0; i--)
        {
            if (offsets[i] <= cursor)
            {
                int col = cursor - offsets[i];
                if (col <= visualLines[i].Length)
                    return (i, col);
            }
        }
        return (visualLines.Count - 1, visualLines[^1].Length);
    }

    public void MoveCursorUp(bool shift)
    {
        int cursor = _buffer.CursorPosition;
        if (cursor <= 0)
        {
            _selection.ForMovement(shift, cursor);
            return;
        }

        string input = _buffer.CurrentInput;
        int width = GetEffectiveWidth();
        var (visualLines, offsets) = LineWrappingService.GetVisualLineData(input, width);
        var (visLine, visCol) = GetVisualPosition(input, visualLines, offsets);

        if (visLine == 0)
        {
            _selection.ForMovement(shift, cursor);
            return;
        }

        int targetCol = _stickyColumn >= 0 ? _stickyColumn : visCol;
        _stickyColumn = targetCol;

        string prevLineText = visualLines[visLine - 1];
        int clampedCol = Math.Min(targetCol, prevLineText.Length);
        int targetPos = offsets[visLine - 1] + clampedCol;

        _selection.ForMovement(shift, cursor);
        _buffer.MoveTo(targetPos);
    }

    public void MoveCursorDown(bool shift)
    {
        int cursor = _buffer.CursorPosition;
        if (cursor >= _buffer.Length)
        {
            _selection.ForMovement(shift, cursor);
            return;
        }

        string input = _buffer.CurrentInput;
        int width = GetEffectiveWidth();
        var (visualLines, offsets) = LineWrappingService.GetVisualLineData(input, width);
        var (visLine, visCol) = GetVisualPosition(input, visualLines, offsets);

        if (visLine >= visualLines.Count - 1)
        {
            _selection.ForMovement(shift, cursor);
            _buffer.MoveTo(_buffer.Length);
            return;
        }

        int targetCol = _stickyColumn >= 0 ? _stickyColumn : visCol;
        _stickyColumn = targetCol;

        string nextLineText = visualLines[visLine + 1];
        int clampedCol = Math.Min(targetCol, nextLineText.Length);
        int targetPos = offsets[visLine + 1] + clampedCol;

        _selection.ForMovement(shift, cursor);
        _buffer.MoveTo(targetPos);
    }
}
