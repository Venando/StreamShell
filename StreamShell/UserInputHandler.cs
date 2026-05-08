using System.Text;

namespace StreamShell;

/// <summary>
/// Handles keyboard input processing — reads key events, dispatches them
/// to buffer, selection, clipboard, and undo state managers.
/// Does NOT handle rendering or terminal cursor positioning.
/// </summary>
internal class UserInputHandler : IInputHandler
{
    private readonly TextBuffer _buffer = new();
    private readonly SelectionManager _selection = new();
    private readonly UndoManager _undo = new();
    private readonly StringBuilder _tempInput = new();

    public string CurrentInput => _buffer.CurrentInput;
    public List<Attachment> Attachments { get; private set; } = new();
    public int LargePasteThreshold { get; set; } = 100;
    public int LargePasteLineThreshold { get; set; } = 4;
    public bool QuitRequested { get; set; }

    // ── Cursor & Selection (delegated) ──────────────────────────────
    public int CursorPosition => _buffer.CursorPosition;
    public bool HasSelection => _selection.IsActiveAt(_buffer.CursorPosition);

    public bool TryGetSelection(out int start, out int length)
        => _selection.TryGetSelection(_buffer.CursorPosition, out start, out length);

    // ── Undo (delegated to UndoManager) ─────────────────────────────

    // ── Right Margin & Vertical Navigation ──────────────────────────
    public int RightMargin { get; set; } = Console.WindowWidth;
    private int _stickyColumn = -1;

    // ── Main Processing Loop ────────────────────────────────────────
    public string? ProcessInput()
    {
        string? submitted = null;

        while (Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true);
            bool ctrl = key.Modifiers.HasFlag(ConsoleModifiers.Control);
            bool shift = key.Modifiers.HasFlag(ConsoleModifiers.Shift);
            bool alt = key.Modifiers.HasFlag(ConsoleModifiers.Alt);

            if (HandleEnter(key, ctrl, shift, alt, ref submitted))
                break;
            if (HandleControlKey(key, ctrl, shift))
                continue;
            if (HandleNavigationKey(key, ctrl, alt, shift))
                continue;
            if (HandleEditingKey(key, ctrl))
                continue;

            // Unhandled non-control characters → buffer for flush
            if (key.KeyChar != '\0')
                _tempInput.Append(key.KeyChar);
        }

        // Flush buffered input (e.g. from Ctrl+V pastes or fast typing)
        if (_tempInput.Length > 0)
        {
            Snapshot();
            FlushTempInput();
        }

        return submitted;
    }

    // ── Key Dispatch: Enter ──────────────────────────────────────────
    /// <summary>Handles Enter. Returns true if the outer while should continue or break.</summary>
    private bool HandleEnter(ConsoleKeyInfo key, bool ctrl, bool shift, bool alt, ref string? submitted)
    {
        if (key.Key != ConsoleKey.Enter)
            return false;

        if (!shift && !ctrl && !alt)
        {
            while (Console.KeyAvailable)
            {
                _tempInput.Append('\n');
                return true; // keep processing buffered keys
            }

            if (_tempInput.Length == 0 && _buffer.Length > 0)
            {
                submitted = _buffer.CurrentInput;
                ResetState();
                return true; // break outer while
            }
        }

        // Shift+Enter / Ctrl+Enter / Alt+Enter → literal newline
        _tempInput.Append('\n');
        return true;
    }

    // ── Key Dispatch: Control Keys ───────────────────────────────────
    private bool HandleControlKey(ConsoleKeyInfo key, bool ctrl, bool shift)
    {
        if (!ctrl)
            return false;

        switch (key.Key)
        {
            case ConsoleKey.D:
                QuitRequested = true;
                return true;

            case ConsoleKey.C when !shift:
                CopyToClipboard();
                return true;

            case ConsoleKey.X:
                Snapshot();
                CutToClipboard();
                return true;

            case ConsoleKey.V:
                Snapshot();
                PasteFromClipboard();
                return true;

            case ConsoleKey.Z:
                Undo();
                return true;
        }

        return false;
    }

    // ── Key Dispatch: Navigation ─────────────────────────────────────
    private bool HandleNavigationKey(ConsoleKeyInfo key, bool ctrl, bool alt, bool shift)
    {
        if (alt)
            return false;

        // Word jumps (Ctrl+arrows)
        if (ctrl)
        {
            switch (key.Key)
            {
                case ConsoleKey.LeftArrow:
                    _stickyColumn = -1;
                    MoveCursorWordLeft(shift);
                    return true;
                case ConsoleKey.RightArrow:
                    _stickyColumn = -1;
                    MoveCursorWordRight(shift);
                    return true;
            }
            return false;
        }

        // Plain navigation keys
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                MoveCursorUp(shift);
                return true;
            case ConsoleKey.DownArrow:
                MoveCursorDown(shift);
                return true;
            case ConsoleKey.LeftArrow:
                _stickyColumn = -1;
                MoveCursorLeft(shift);
                return true;
            case ConsoleKey.RightArrow:
                _stickyColumn = -1;
                MoveCursorRight(shift);
                return true;
            case ConsoleKey.Home:
                _stickyColumn = -1;
                MoveCursorHome(shift);
                return true;
            case ConsoleKey.End:
                _stickyColumn = -1;
                MoveCursorEnd(shift);
                return true;
        }

        return false;
    }

    // ── Key Dispatch: Editing ────────────────────────────────────────
    private bool HandleEditingKey(ConsoleKeyInfo key, bool ctrl)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                ResetState();
                return true;

            case ConsoleKey.Backspace:
                if (!_selection.IsActiveAt(_buffer.CursorPosition) && _buffer.CursorPosition == 0)
                    return true; // nothing to delete
                Snapshot();
                HandleBackspace();
                return true;

            case ConsoleKey.Delete when !ctrl:
                if (_selection.IsActiveAt(_buffer.CursorPosition) || _buffer.CursorPosition < _buffer.Length)
                {
                    Snapshot();
                    HandleDelete();
                }
                return true;
        }

        // Printable characters
        if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
        {
            InsertCharacter(key.KeyChar);
            return true;
        }

        return false;
    }

    private void InsertCharacter(char c)
    {
        Snapshot();

        if (_selection.IsActiveAt(_buffer.CursorPosition))
            RemoveSelectedText();

        if (_buffer.CursorPosition < _buffer.Length || _buffer.Length == 0)
            _buffer.Insert(c);
        else
            _tempInput.Append(c);
    }

    // ── Selection Handling ──────────────────────────────────────────
    private void HandleBackspace()
    {
        if (!RemoveSelectedText())
            _buffer.Backspace();
    }

    private void HandleDelete()
    {
        if (!RemoveSelectedText())
            _buffer.Delete();
    }

    /// <summary>If selection is active, removes it and returns true. Otherwise returns false.</summary>
    private bool RemoveSelectedText()
    {
        int cursor = _buffer.CursorPosition;
        if (!_selection.IsActiveAt(cursor))
            return false;

        _buffer.Remove(_selection.SelectionStart(cursor), _selection.SelectionLength(cursor));
        _selection.Clear();
        return true;
    }

    // ── Clipboard Operations ────────────────────────────────────────
    /// <summary>Returns the selected text, or the full buffer if no selection is active.</summary>
    private string GetClipboardText()
    {
        int cursor = _buffer.CursorPosition;
        return _selection.IsActiveAt(cursor)
            ? _selection.SelectedText(cursor, _buffer.CurrentInput)
            : _buffer.CurrentInput;
    }

    private void CopyToClipboard()
    {
        TryClipboardCopy(GetClipboardText());
    }

    private void CutToClipboard()
    {
        TryClipboardCopy(GetClipboardText());

        if (!RemoveSelectedText())
            _buffer.Clear();
    }

    /// <summary>Attempts clipboard copy. Silently ignores platform errors (e.g. WSL).</summary>
    private static void TryClipboardCopy(string text)
    {
        try
        {
            ClipboardService.Copy(text);
        }
        catch
        {
            // Clipboard not available on this platform (e.g. WSL)
        }
    }

    private void PasteFromClipboard()
    {
        string? text;
        try
        {
            text = ClipboardService.Paste();
        }
        catch
        {
            return;
        }

        if (string.IsNullOrEmpty(text))
            return;

        if (_selection.IsActiveAt(_buffer.CursorPosition))
            RemoveSelectedText();

        InsertPastedText(text);
    }

    // ── Temp Buffer / Paste Handling ─────────────────────────────────
    private void FlushTempInput()
    {
        if (_tempInput.Length == 0)
            return;

        string text = _tempInput.ToString();
        _tempInput.Clear();
        InsertPastedText(text);
    }

    private void InsertPastedText(string text)
    {
        int lineCount = text.Split('\n').Length;

        if (text.Length > LargePasteThreshold || lineCount > LargePasteLineThreshold)
        {
            string name = GenerateName(text);
            Attachments.Add(new Attachment(text, AttachmentType.PlainText, lineCount));
            string placeholder = $"[paste {lineCount} lines: {name}]";
            _buffer.Insert(placeholder);
        }
        else
        {
            _buffer.Insert(text);
        }
    }

    // ── Undo (delegated to UndoManager) ────────────────────────────────
    private void Snapshot()
    {
        _undo.Snapshot(_buffer.CurrentInput, _buffer.CursorPosition, _selection.GetAnchor());
    }

    private void Undo()
    {
        if (!_undo.TryUndo(out string text, out int cursor, out int? selection))
            return;

        _buffer.SetContent(text, cursor);

        if (selection.HasValue)
            _selection.ForMovement(shift: true, cursor); // re-anchor
        else
            _selection.Clear();
    }

    // ── Cursor Movement Helpers ─────────────────────────────────────
    private void MoveCursorLeft(bool shift)
    {
        if (_buffer.CursorPosition <= 0)
        {
            _selection.ForMovement(shift, _buffer.CursorPosition);
            return;
        }

        _selection.ForMovement(shift, _buffer.CursorPosition);
        _buffer.MoveTo(_buffer.CursorPosition - 1);

        // When selecting with Shift, skip newline characters so the
        // first selected character is visible content, not a structural
        // line break.
        if (shift)
        {
            while (_buffer.CursorPosition > 0 && _buffer[_buffer.CursorPosition] == '\n')
                _buffer.MoveTo(_buffer.CursorPosition - 1);
        }
    }

    private void MoveCursorRight(bool shift)
    {
        if (_buffer.CursorPosition >= _buffer.Length)
        {
            if (!shift) _selection.Clear();
            return;
        }

        int originalPos = _buffer.CursorPosition;
        _buffer.MoveTo(_buffer.CursorPosition + 1);

        // When selecting with Shift, skip newline characters so the
        // first selected character is visible content, not a structural
        // line break.
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

    private void MoveCursorHome(bool shift)
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

    private void MoveCursorEnd(bool shift)
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

    // ── Vertical Movement (↑/↓) ─────────────────────────────────────
    private int GetEffectiveWidth()
    {
        int effectiveMargin = Math.Max(10, RightMargin);
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

    private void MoveCursorUp(bool shift)
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

    private void MoveCursorDown(bool shift)
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

    // ── Word-Boundary Movement (Ctrl+←/→) ──────────────────────────
    private void MoveCursorWordLeft(bool shift)
    {
        _selection.ForMovement(shift, _buffer.CursorPosition);

        if (_buffer.CursorPosition > 0)
            _buffer.MoveTo(FindPreviousWordStart(_buffer.CurrentInput, _buffer.CursorPosition));
    }

    private void MoveCursorWordRight(bool shift)
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

    // ── Reset ───────────────────────────────────────────────────────
    public void Reset()
    {
        ResetState();
        Attachments.Clear();
    }

    private void ResetState()
    {
        _buffer.Clear();
        _tempInput.Clear();
        _selection.Reset();
        _stickyColumn = -1;
        _undo.Clear();
    }

    private static string GenerateName(string content)
    {
        int newlineIndex = content.IndexOf('\n');
        string firstLine = newlineIndex > 0 ? content[..newlineIndex] : content;
        string trimmed = firstLine.TrimEnd();
        string result = trimmed.Length > 15 ? trimmed[..15] : trimmed;
        return result + "...";
    }
}
