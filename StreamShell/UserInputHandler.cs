using System.Text;

namespace StreamShell;

internal class UserInputHandler
{
    private readonly StringBuilder _currentInput = new();
    private readonly StringBuilder _tempInput = new();
    public string CurrentInput => _currentInput.ToString();
    public List<Attachment> Attachments { get; private set; } = new();
    public int LargePasteThreshold { get; internal set; } = 100;
    public int LargePasteLineThreshold { get; internal set; } = 4;
    /// <summary>Set to true when Ctrl+D is pressed while reading input.</summary>
    public bool QuitRequested { get; set; }

    // ── Cursor & Selection State ──────────────────────────────────────
    private int _cursorPosition;
    /// <summary>null when no selection is active; otherwise one end of the selection (the other is _cursorPosition).</summary>
    private int? _selectionAnchor;

    public int CursorPosition => _cursorPosition;
    public bool HasSelection => _selectionAnchor.HasValue && _selectionAnchor.Value != _cursorPosition;
    private int SelectionStart => Math.Min(_cursorPosition, _selectionAnchor ?? _cursorPosition);
    private int SelectionEnd => Math.Max(_cursorPosition, _selectionAnchor ?? _cursorPosition);
    private int SelectionLength => SelectionEnd - SelectionStart;
    public string SelectedText => HasSelection ? _currentInput.ToString(SelectionStart, SelectionLength) : "";

    /// <summary>Returns the selected range as a span when a selection exists.</summary>
    public bool TryGetSelection(out int start, out int length)
    {
        if (HasSelection)
        {
            start = SelectionStart;
            length = SelectionLength;
            return true;
        }
        start = 0;
        length = 0;
        return false;
    }

    // ── Undo Stack ────────────────────────────────────────────────────
    private readonly Stack<(string text, int cursor, int? selection)> _undoStack = new();
    private const int MaxUndoDepth = 50;

    private void Snapshot()
    {
        // Trim oldest entries if at capacity
        if (_undoStack.Count >= MaxUndoDepth)
        {
            var items = _undoStack.ToArray();
            _undoStack.Clear();
            // Keep the (MaxUndoDepth - 1) most recent entries
            for (int i = items.Length - (MaxUndoDepth - 1); i < items.Length; i++)
                _undoStack.Push(items[i]);
        }
        _undoStack.Push((_currentInput.ToString(), _cursorPosition, _selectionAnchor));
    }

    private void Undo()
    {
        if (_undoStack.Count == 0)
            return;
        var (text, cursor, selection) = _undoStack.Pop();
        _currentInput.Clear();
        _currentInput.Append(text);
        _cursorPosition = cursor;
        _selectionAnchor = selection;
    }

    // ── Right Margin ──────────────────────────────────────────────────
    public int RightMargin { get; set; } = Console.WindowWidth;

    // ── Selection Operations ──────────────────────────────────────────
    private void DeleteSelection()
    {
        if (!HasSelection)
            return;
        int start = SelectionStart;
        int len = SelectionLength;
        _currentInput.Remove(start, len);
        _cursorPosition = start;
        _selectionAnchor = null;
    }

    private void HandleBackspace()
    {
        if (HasSelection)
        {
            DeleteSelection();
        }
        else if (_cursorPosition > 0)
        {
            _currentInput.Remove(_cursorPosition - 1, 1);
            _cursorPosition--;
        }
    }

    private void HandleDelete()
    {
        if (HasSelection)
        {
            DeleteSelection();
        }
        else if (_cursorPosition < _currentInput.Length)
        {
            _currentInput.Remove(_cursorPosition, 1);
        }
    }

    // ── Clipboard Operations ──────────────────────────────────────────
    private void CopyToClipboard()
    {
        try
        {
            if (HasSelection)
                ClipboardService.Copy(SelectedText);
            else
                ClipboardService.Copy(_currentInput.ToString());
        }
        catch
        {
            // Clipboard not available on this platform (e.g. WSL)
        }
    }

    private void CutToClipboard()
    {
        try
        {
            if (HasSelection)
            {
                ClipboardService.Copy(SelectedText);
                DeleteSelection();
            }
            else
            {
                ClipboardService.Copy(_currentInput.ToString());
                _currentInput.Clear();
                _cursorPosition = 0;
            }
        }
        catch
        {
            // Clipboard not available; still perform the cut
            if (HasSelection)
                DeleteSelection();
            else
            {
                _currentInput.Clear();
                _cursorPosition = 0;
            }
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

        // Replace selection if active
        if (HasSelection)
            DeleteSelection();

        int lineCount = text.Split('\n').Length;

        if (text.Length > LargePasteThreshold || lineCount > LargePasteLineThreshold)
        {
            string name = GenerateName(text);
            Attachments.Add(new Attachment(text, AttachmentType.PlainText, lineCount));
            string placeholder = $"[paste {lineCount} lines: {name}]";
            _currentInput.Insert(_cursorPosition, placeholder);
            _cursorPosition += placeholder.Length;
        }
        else
        {
            _currentInput.Insert(_cursorPosition, text);
            _cursorPosition += text.Length;
        }
    }

    // ── Temp Buffer Flush ─────────────────────────────────────────────
    private void FlushTempInput()
    {
        if (_tempInput.Length == 0)
            return;

        string text = _tempInput.ToString();
        _tempInput.Clear();

        // Insert at cursor position, replacing any selection
        int lineCount = text.Split('\n').Length;

        if (text.Length > LargePasteThreshold || lineCount > LargePasteLineThreshold)
        {
            string name = GenerateName(text);
            Attachments.Add(new Attachment(text, AttachmentType.PlainText, lineCount));
            string placeholder = $"[paste {lineCount} lines: {name}]";
            _currentInput.Insert(_cursorPosition, placeholder);
            _cursorPosition += placeholder.Length;
        }
        else
        {
            _currentInput.Insert(_cursorPosition, text);
            _cursorPosition += text.Length;
        }
    }

    // ── Main Processing Loop ──────────────────────────────────────────
    public string? ProcessInput()
    {
        string? submitted = null;

        while (Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true);
            bool ctrl = key.Modifiers.HasFlag(ConsoleModifiers.Control);
            bool shift = key.Modifiers.HasFlag(ConsoleModifiers.Shift);
            bool alt = key.Modifiers.HasFlag(ConsoleModifiers.Alt);

            // ── Submit (Enter without Shift/queued keys) ──────────────
            if (key.Key == ConsoleKey.Enter && !shift && !ctrl && !alt)
            {
                if (Console.KeyAvailable)
                {
                    _tempInput.Append('\n');
                    continue;
                }

                if (_tempInput.Length == 0 && _currentInput.Length > 0)
                {
                    submitted = _currentInput.ToString();
                    ResetState();
                    break;
                }

                // No text to submit — add a newline
                _tempInput.Append('\n');
                continue;
            }

            // ── Enter with Shift or modifiers → newline ──────────────
            if (key.Key == ConsoleKey.Enter)
            {
                _tempInput.Append('\n');
                continue;
            }

            // ── Quit (Ctrl+D) ─────────────────────────────────────────
            if (key.Key is ConsoleKey.D && ctrl)
            {
                QuitRequested = true;
                return null;
            }

            // ── Copy (Ctrl+C) ─────────────────────────────────────────
            if (key.Key is ConsoleKey.C && ctrl && !shift)
            {
                CopyToClipboard();
                continue;
            }

            // ── Cut (Ctrl+X) ──────────────────────────────────────────
            if (key.Key is ConsoleKey.X && ctrl)
            {
                Snapshot();
                CutToClipboard();
                continue;
            }

            // ── Paste (Ctrl+V) ────────────────────────────────────────
            if (key.Key is ConsoleKey.V && ctrl)
            {
                Snapshot();
                PasteFromClipboard();
                continue;
            }

            // ── Undo (Ctrl+Z) ─────────────────────────────────────────
            if (key.Key is ConsoleKey.Z && ctrl)
            {
                Undo();
                continue;
            }

            // ── Escape (clear all) ────────────────────────────────────
            if (key.Key == ConsoleKey.Escape)
            {
                ResetState();
                continue;
            }

            // ── Backspace ─────────────────────────────────────────────
            if (key.Key == ConsoleKey.Backspace)
            {
                if (!HasSelection && _cursorPosition == 0)
                    continue; // nothing to delete
                Snapshot();
                HandleBackspace();
                continue;
            }

            // ── Delete ────────────────────────────────────────────────
            if (key.Key == ConsoleKey.Delete && !ctrl)
            {
                if (HasSelection || _cursorPosition < _currentInput.Length)
                {
                    Snapshot();
                    HandleDelete();
                }
                continue;
            }

            // ── Navigation (arrow keys, Home, End) ────────────────────
            if (!ctrl && !alt)
            {
                if (key.Key == ConsoleKey.LeftArrow)
                {
                    MoveCursorLeft(shift);
                    continue;
                }
                if (key.Key == ConsoleKey.RightArrow)
                {
                    MoveCursorRight(shift);
                    continue;
                }
                if (key.Key == ConsoleKey.Home)
                {
                    MoveCursorHome(shift);
                    continue;
                }
                if (key.Key == ConsoleKey.End)
                {
                    MoveCursorEnd(shift);
                    continue;
                }
            }

            // ── Ctrl+←/→ Word Jump ────────────────────────────────────
            if (ctrl && !alt)
            {
                if (key.Key == ConsoleKey.LeftArrow)
                {
                    MoveCursorWordLeft(shift);
                    continue;
                }
                if (key.Key == ConsoleKey.RightArrow)
                {
                    MoveCursorWordRight(shift);
                    continue;
                }
            }

            // ── Regular character (printable) ─────────────────────────
            if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
            {
                // If there's an active selection, capture the snapshot
                // and delete it before inserting characters
                if (HasSelection)
                {
                    Snapshot();
                    DeleteSelection();
                }
                else
                {
                    Snapshot();
                }

                // Handle single insert with cursor advancement
                // vs. buffering for large pastes
                if (_cursorPosition < _currentInput.Length
                    || _currentInput.Length == 0)
                {
                    _currentInput.Insert(_cursorPosition, key.KeyChar);
                    _cursorPosition++;
                }
                else
                {
                    _tempInput.Append(key.KeyChar);
                }
                continue;
            }

            // ── Ignored keys fall through to tempInput ────────────────
            if (key.KeyChar != '\0')
                _tempInput.Append(key.KeyChar);
        }

        // Flush buffered input
        if (_tempInput.Length > 0)
        {
            Snapshot();
            FlushTempInput();
        }

        return submitted;
    }

    // ── Cursor Movement Helpers ───────────────────────────────────────
    private void MoveCursorLeft(bool shift)
    {
        if (_cursorPosition <= 0)
        {
            if (!shift) _selectionAnchor = null;
            return;
        }

        if (!shift)
            _selectionAnchor = null;
        else if (!_selectionAnchor.HasValue)
            _selectionAnchor = _cursorPosition;

        _cursorPosition--;

        // When selecting with Shift, skip newline characters so the
        // first selected character is visible content, not a structural
        // line break.
        if (shift)
        {
            while (_cursorPosition > 0 && _currentInput[_cursorPosition] == '\n')
                _cursorPosition--;
        }
    }

    private void MoveCursorRight(bool shift)
    {
        if (_cursorPosition >= _currentInput.Length)
        {
            if (!shift) _selectionAnchor = null;
            return;
        }

        if (!shift)
            _selectionAnchor = null;
        else if (!_selectionAnchor.HasValue)
            _selectionAnchor = _cursorPosition;

        _cursorPosition++;
    }

    private void MoveCursorHome(bool shift)
    {
        string input = _currentInput.ToString();
        int lineStart = _cursorPosition > 0
            ? input.LastIndexOf('\n', _cursorPosition - 1) + 1
            : 0;

        if (lineStart != _cursorPosition)
        {
            // At line start → move there
            if (!shift)
                _selectionAnchor = null;
            else if (!_selectionAnchor.HasValue)
                _selectionAnchor = _cursorPosition;
            _cursorPosition = lineStart;
            return;
        }

        // Already at line start — act like Left arrow:
        // move one character left, wrapping to previous line.
        if (_cursorPosition <= 0)
        {
            if (!shift) _selectionAnchor = null;
            return;
        }
        if (!shift)
            _selectionAnchor = null;
        else if (!_selectionAnchor.HasValue)
            _selectionAnchor = _cursorPosition;
        _cursorPosition--;
    }

    private void MoveCursorEnd(bool shift)
    {
        string input = _currentInput.ToString();
        int nextNewline = input.IndexOf('\n', _cursorPosition);
        int lineEnd = nextNewline >= 0 ? nextNewline : _currentInput.Length;

        if (lineEnd != _cursorPosition)
        {
            // At line end → move there
            if (!shift)
                _selectionAnchor = null;
            else if (!_selectionAnchor.HasValue)
                _selectionAnchor = _cursorPosition;
            _cursorPosition = lineEnd;
            return;
        }

        // Already at line end — act like Right arrow:
        // move one character right, wrapping to next line.
        if (_cursorPosition >= _currentInput.Length)
        {
            if (!shift) _selectionAnchor = null;
            return;
        }
        if (!shift)
            _selectionAnchor = null;
        else if (!_selectionAnchor.HasValue)
            _selectionAnchor = _cursorPosition;
        _cursorPosition++;
    }

    // ── Word-Boundary Movement (Ctrl+←/→) ────────────────────────────

    private void MoveCursorWordLeft(bool shift)
    {
        if (_cursorPosition <= 0)
        {
            if (!shift) _selectionAnchor = null;
            return;
        }

        if (!shift)
            _selectionAnchor = null;
        else if (!_selectionAnchor.HasValue)
            _selectionAnchor = _cursorPosition;

        _cursorPosition = FindPreviousWordStart(_currentInput.ToString(), _cursorPosition);
    }

    private void MoveCursorWordRight(bool shift)
    {
        if (_cursorPosition >= _currentInput.Length)
        {
            if (!shift) _selectionAnchor = null;
            return;
        }

        if (!shift)
            _selectionAnchor = null;
        else if (!_selectionAnchor.HasValue)
            _selectionAnchor = _cursorPosition;

        _cursorPosition = FindNextWordStart(_currentInput.ToString(), _cursorPosition);
    }

    private static int FindPreviousWordStart(string input, int pos)
    {
        if (pos <= 0) return 0;
        int i = pos - 1;
        // Skip any trailing whitespace
        while (i >= 0 && char.IsWhiteSpace(input[i])) i--;
        // Skip the word
        while (i >= 0 && !char.IsWhiteSpace(input[i])) i--;
        return i + 1;
    }

    private static int FindNextWordStart(string input, int pos)
    {
        int len = input.Length;
        if (pos >= len) return len;
        int i = pos;
        // Skip current word
        while (i < len && !char.IsWhiteSpace(input[i])) i++;
        // Skip whitespace to find the next word
        while (i < len && char.IsWhiteSpace(input[i])) i++;
        return i;
    }

    // ── Reset ─────────────────────────────────────────────────────────
    public void Reset()
    {
        ResetState();
        Attachments.Clear();
    }

    private void ResetState()
    {
        _currentInput.Clear();
        _tempInput.Clear();
        _cursorPosition = 0;
        _selectionAnchor = null;
        _undoStack.Clear();
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
