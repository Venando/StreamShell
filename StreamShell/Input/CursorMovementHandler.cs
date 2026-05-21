using System.Buffers;

namespace StreamShell;

/// <summary>
/// Handles cursor movement within a text buffer, including character-level,
/// word-boundary, and vertical (visual line) navigation with sticky column tracking.
/// All methods operate on the shared <see cref="TextBuffer"/> and
/// <see cref="SelectionManager"/> but are independent of clipboard or undo logic.
/// Uses <see cref="ArrayPool{T}"/> for placeholder range computation to avoid
/// heap allocation on every cursor movement.
/// </summary>
internal class CursorMovementHandler
{
    private readonly TextBuffer _buffer;
    private readonly SelectionManager _selection;
    private readonly Func<int> _getRightMargin;
    private readonly Func<int> _getPrefixMargin;
    private readonly Func<int> _getWrappingRightMargin;
    private readonly Func<IReadOnlyList<Attachment>> _getAttachments;
    private readonly Func<bool> _getWordWrap;
    private int _stickyColumn = -1;

    // Reusable visual-line lists — cleared and repopulated per arrow press
    // to avoid allocating two List<T> on every up/down movement.
    private readonly List<string> _visualLinesCache = new();
    private readonly List<int> _visualOffsetsCache = new();

    public CursorMovementHandler(
        TextBuffer buffer,
        SelectionManager selection,
        Func<int> getRightMargin,
        Func<int>? getPrefixMargin = null,
        Func<int>? getWrappingRightMargin = null,
        Func<IReadOnlyList<Attachment>>? getAttachments = null,
        Func<bool>? getWordWrap = null)
    {
        _buffer = buffer;
        _selection = selection;
        _getRightMargin = getRightMargin;
        _getPrefixMargin = getPrefixMargin ?? (() => 2);
        _getWrappingRightMargin = getWrappingRightMargin ?? (() => 4);
        _getAttachments = getAttachments ?? (() => Array.Empty<Attachment>());
        _getWordWrap = getWordWrap ?? (() => false);
    }

    /// <summary>Resets sticky column tracking (e.g. when the user presses left/right or home/end).</summary>
    public void ResetStickyColumn() => _stickyColumn = -1;

    /// <summary>
    /// Computes placeholder ranges into a caller-provided buffer.
    /// Returns a span of populated ranges. Uses ArrayPool internally to avoid
    /// heap allocation — the caller MUST wrap the call in a try/finally that
    /// returns the pool buffer.
    /// </summary>
    private (int count, (int start, int end)[] poolBuffer) GetPlaceholderRanges()
    {
        var attachments = _getAttachments();
        int count = attachments.Count;
        if (count == 0)
            return (0, Array.Empty<(int, int)>());

        string currentInput = _buffer.CurrentInput;
        var buffer = ArrayPool<(int start, int end)>.Shared.Rent(count);
        int written = 0;

        foreach (var attachment in attachments)
        {
            string placeholder = attachment.Placeholder;
            if (string.IsNullOrEmpty(placeholder)) continue;
            int start = currentInput.IndexOf(placeholder, StringComparison.Ordinal);
            if (start >= 0)
            {
                buffer[written] = (start, start + placeholder.Length);
                written++;
            }
        }

        return (written, buffer);
    }

    private static void ReturnPlaceholderRanges((int start, int end)[] buffer)
    {
        if (buffer.Length > 0)
            ArrayPool<(int start, int end)>.Shared.Return(buffer);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Character-level movement
    // ══════════════════════════════════════════════════════════════════

    public void MoveCursorLeft(bool shift)
    {
        int cursor = _buffer.CursorPosition;
        if (cursor <= 0)
        {
            _selection.ForMovement(shift, cursor);
            return;
        }

        int target = cursor - 1;

        // Skip over placeholder if cursor is right after or inside it
        var (phCount, phBuffer) = GetPlaceholderRanges();
        try
        {
            for (int i = 0; i < phCount; i++)
            {
                var (start, end) = phBuffer[i];
                if (cursor > start && cursor <= end)
                {
                    target = start;
                    break;
                }
            }
        }
        finally
        {
            ReturnPlaceholderRanges(phBuffer);
        }

        _selection.ForMovement(shift, cursor);
        _buffer.MoveTo(target);

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
        int cursor = _buffer.CursorPosition;
        if (cursor >= _buffer.Length)
        {
            if (!shift) _selection.Clear();
            return;
        }

        int target = cursor + 1;

        // Skip over placeholder if cursor is at or inside it
        var (phCount, phBuffer) = GetPlaceholderRanges();
        try
        {
            for (int i = 0; i < phCount; i++)
            {
                var (start, end) = phBuffer[i];
                if (cursor >= start && cursor < end)
                {
                    target = end;
                    break;
                }
            }
        }
        finally
        {
            ReturnPlaceholderRanges(phBuffer);
        }

        int originalPos = cursor;
        _buffer.MoveTo(target);

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
    //  Visual-line-aware Home / End
    // ══════════════════════════════════════════════════════════════════

    public void MoveCursorHome(bool shift)
    {
        string input = _buffer.CurrentInput;
        int cursor = _buffer.CursorPosition;

        if (string.IsNullOrEmpty(input) || cursor == 0)
        {
            _selection.ForMovement(shift, cursor);
            return;
        }

        int width = GetEffectiveWidth();
        LineWrappingService.PopulateVisualLineData(input, width,
            _visualLinesCache, _visualOffsetsCache,
            prefixMargin: _getPrefixMargin(),
            rightMargin: _getWrappingRightMargin(),
            wordWrap: _getWordWrap());

        var (visLine, _) = GetVisualPosition(input, _visualLinesCache, _visualOffsetsCache);
        int targetPos = _visualOffsetsCache[visLine];

        if (targetPos != cursor)
        {
            _selection.ForMovement(shift, cursor);
            _buffer.MoveTo(targetPos);
        }
        else
        {
            // Already at visual-line start — do nothing (consistent with standard editors)
            _selection.ForMovement(shift, cursor);
        }
    }

    public void MoveCursorEnd(bool shift)
    {
        string input = _buffer.CurrentInput;
        int cursor = _buffer.CursorPosition;

        if (cursor >= _buffer.Length)
        {
            _selection.ForMovement(shift, cursor);
            return;
        }

        int width = GetEffectiveWidth();
        LineWrappingService.PopulateVisualLineData(input, width,
            _visualLinesCache, _visualOffsetsCache,
            prefixMargin: _getPrefixMargin(),
            rightMargin: _getWrappingRightMargin(),
            wordWrap: _getWordWrap());

        var (visLine, _) = GetVisualPosition(input, _visualLinesCache, _visualOffsetsCache);
        int lineEndOffset = _visualOffsetsCache[visLine] + _visualLinesCache[visLine].Length;

        if (lineEndOffset != cursor)
        {
            _selection.ForMovement(shift, cursor);
            _buffer.MoveTo(lineEndOffset);
        }
        else
        {
            // Already at visual-line end — do nothing (consistent with standard editors)
            _selection.ForMovement(shift, cursor);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Word-boundary movement (Ctrl+← / Ctrl+→)
    // ══════════════════════════════════════════════════════════════════

    public void MoveCursorWordLeft(bool shift)
    {
        int cursor = _buffer.CursorPosition;
        _selection.ForMovement(shift, cursor);

        if (cursor > 0)
        {
            int target = FindPreviousWordStart(_buffer.CurrentInput, cursor);

            // Skip over placeholder if cursor or target lands inside it
            var (phCount, phBuffer) = GetPlaceholderRanges();
            try
            {
                for (int i = 0; i < phCount; i++)
                {
                    var (start, end) = phBuffer[i];
                    // Cursor is inside or right after placeholder → jump to start
                    if (cursor > start && cursor <= end)
                    {
                        target = start;
                        break;
                    }
                    // Target landed inside or immediately after placeholder → jump to its start
                    if (target > start && target <= end)
                    {
                        target = start;
                        break;
                    }
                }
            }
            finally
            {
                ReturnPlaceholderRanges(phBuffer);
            }

            _buffer.MoveTo(target);
        }
    }

    public void MoveCursorWordRight(bool shift)
    {
        int cursor = _buffer.CursorPosition;
        _selection.ForMovement(shift, cursor);

        if (cursor < _buffer.Length)
        {
            int target = FindNextWordStart(_buffer.CurrentInput, cursor);

            // Skip over placeholder if cursor or target lands inside it
            var (phCount, phBuffer) = GetPlaceholderRanges();
            try
            {
                for (int i = 0; i < phCount; i++)
                {
                    var (start, end) = phBuffer[i];
                    // Cursor is at or inside placeholder → jump to end
                    if (cursor >= start && cursor < end)
                    {
                        target = end;
                        break;
                    }
                    // Target landed inside placeholder → jump past its end
                    if (target > start && target < end)
                    {
                        target = end;
                        break;
                    }
                }
            }
            finally
            {
                ReturnPlaceholderRanges(phBuffer);
            }

            _buffer.MoveTo(target);
        }
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
    private static bool IsSeparator(char c) => !IsWordChar(c) && !char.IsWhiteSpace(c);

    /// <summary>
    /// VS Code-style word navigation: stops at each token boundary.
    /// A "token" is a contiguous run of word chars, separators, or whitespace+next-token.
    /// </summary>
    private static int FindPreviousWordStart(string input, int pos)
    {
        if (pos <= 0) return 0;
        int i = pos - 1;

        if (IsWordChar(input[i]))
        {
            while (i >= 0 && IsWordChar(input[i])) i--;
            return i + 1;
        }

        if (IsSeparator(input[i]))
        {
            while (i >= 0 && IsSeparator(input[i])) i--;
            while (i >= 0 && char.IsWhiteSpace(input[i])) i--;
            return i + 1;
        }

        // whitespace
        while (i >= 0 && char.IsWhiteSpace(input[i])) i--;
        while (i >= 0 && IsSeparator(input[i])) i--;
        while (i >= 0 && char.IsWhiteSpace(input[i])) i--;
        return i + 1;
    }

    private static int FindNextWordStart(string input, int pos)
    {
        int len = input.Length;
        if (pos >= len) return len;
        int i = pos;

        if (char.IsWhiteSpace(input[i]))
        {
            while (i < len && char.IsWhiteSpace(input[i])) i++;
            if (i < len && IsSeparator(input[i]))
                while (i < len && IsSeparator(input[i])) i++;
            else if (i < len && IsWordChar(input[i]))
                while (i < len && IsWordChar(input[i])) i++;
            return i;
        }

        if (IsWordChar(input[i]))
        {
            while (i < len && IsWordChar(input[i])) i++;
            return i;
        }

        while (i < len && IsSeparator(input[i])) i++;
        return i;
    }

    // ══════════════════════════════════════════════════════════════════
    //  Vertical movement (↑ / ↓)
    // ══════════════════════════════════════════════════════════════════

    private int GetEffectiveWidth()
    {
        return Math.Max(10, _getRightMargin());
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
        LineWrappingService.PopulateVisualLineData(input, width,
            _visualLinesCache, _visualOffsetsCache,
            prefixMargin: _getPrefixMargin(),
            rightMargin: _getWrappingRightMargin(),
            wordWrap: _getWordWrap());
        var (visLine, visCol) = GetVisualPosition(input, _visualLinesCache, _visualOffsetsCache);

        if (visLine == 0)
        {
            _selection.ForMovement(shift, cursor);
            return;
        }

        int targetCol = _stickyColumn >= 0 ? _stickyColumn : visCol;
        _stickyColumn = targetCol;

        int clampedCol = Math.Min(targetCol, _visualLinesCache[visLine - 1].Length);
        int targetPos = _visualOffsetsCache[visLine - 1] + clampedCol;

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
        LineWrappingService.PopulateVisualLineData(input, width,
            _visualLinesCache, _visualOffsetsCache,
            prefixMargin: _getPrefixMargin(),
            rightMargin: _getWrappingRightMargin(),
            wordWrap: _getWordWrap());
        var (visLine, visCol) = GetVisualPosition(input, _visualLinesCache, _visualOffsetsCache);

        if (visLine >= _visualLinesCache.Count - 1)
        {
            _selection.ForMovement(shift, cursor);
            _buffer.MoveTo(_buffer.Length);
            return;
        }

        int targetCol = _stickyColumn >= 0 ? _stickyColumn : visCol;
        _stickyColumn = targetCol;

        int clampedCol = Math.Min(targetCol, _visualLinesCache[visLine + 1].Length);
        int targetPos = _visualOffsetsCache[visLine + 1] + clampedCol;

        _selection.ForMovement(shift, cursor);
        _buffer.MoveTo(targetPos);
    }
}
