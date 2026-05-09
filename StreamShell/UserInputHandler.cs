using System.Collections.Concurrent;
using System.Text;

namespace StreamShell;

/// <summary>
/// Handles keyboard input processing — reads key events, dispatches them
/// to buffer, selection, cursor movement, clipboard, and undo state managers.
/// Does NOT handle rendering or terminal cursor positioning.
/// </summary>
internal class UserInputHandler : IInputHandler
{
    private readonly TextBuffer _buffer = new();
    private readonly SelectionManager _selection = new();
    private readonly UndoManager _undo = new();
    private readonly StringBuilder _tempInput = new();
    private readonly CursorMovementHandler _cursorMovement;
    private readonly ClipboardHandler _clipboard;
    private readonly ConcurrentDictionary<string, SavedInputState> _savedInputs = new();
    private long _saveCounter;

    /// <summary>Snapshot of the input field at a point in time.</summary>
    private sealed record SavedInputState(
        string Text,
        int CursorPosition,
        List<Attachment> Attachments);

    public UserInputHandler()
    {
        _clipboard = new ClipboardHandler(
            _buffer, _selection, _tempInput,
            () => Snapshot(),
            () => LargePasteThreshold,
            () => LargePasteLineThreshold);

        _cursorMovement = new CursorMovementHandler(
            _buffer, _selection, () => RightMargin, () => Attachments);

        LargePasteThreshold = 300;
        LargePasteLineThreshold = 4;
        try
        {
            int width = Console.WindowWidth;
            RightMargin = width > 0 ? width : 80;
        }
        catch (IOException)
        {
            RightMargin = 80; // fallback for test/headless environments
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  IInputHandler Implementation
    // ══════════════════════════════════════════════════════════════════

    public string CurrentInput => _buffer.CurrentInput;
    public List<Attachment> Attachments
    {
        get => _clipboard.Attachments;
        private set => _clipboard.Attachments = value;
    }

    public int LargePasteThreshold { get; set; }
    public int LargePasteLineThreshold { get; set; }
    public bool QuitRequested { get; set; }

    public int CursorPosition => _buffer.CursorPosition;
    public bool HasSelection => _selection.IsActiveAt(_buffer.CursorPosition);

    public bool TryGetSelection(out int start, out int length)
        => _selection.TryGetSelection(_buffer.CursorPosition, out start, out length);

    public int RightMargin { get; set; }

    /// <summary>Optional callback for Tab autocomplete. Takes the current input
    /// and returns the completed input, or null/empty if no completion is possible.</summary>
    public Func<string, string?>? AutoCompleteProvider { get; set; }

    /// <summary>
    /// If set, called for each key before normal processing begins.
    /// Return true to mark the key as handled and skip further processing.
    /// Used by ConsoleAppHost to route Up/Down to panel hint selection.
    /// </summary>
    public Func<ConsoleKeyInfo, bool>? KeyInterceptor { get; set; }

    // ══════════════════════════════════════════════════════════════════
    //  Main Processing Loop
    // ══════════════════════════════════════════════════════════════════

    public string? ProcessInput()
    {
        string? submitted = null;

        while (Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true);

            // Give the interceptor first crack at the key (e.g. hint navigation)
            if (KeyInterceptor?.Invoke(key) == true)
                continue;
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
                _clipboard.BufferCharacter(key.KeyChar);
        }

        // Flush buffered input (e.g. from Ctrl+V pastes or fast typing)
        if (_tempInput.Length > 0)
        {
            Snapshot();
            _clipboard.FlushTempInput();
        }

        return submitted;
    }

    // ══════════════════════════════════════════════════════════════════
    //  Key Dispatch: Enter
    // ══════════════════════════════════════════════════════════════════

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
                return false; // keep processing buffered keys
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

    // ══════════════════════════════════════════════════════════════════
    //  Key Dispatch: Control Keys
    // ══════════════════════════════════════════════════════════════════

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
                _clipboard.CopyToClipboard();
                return true;

            case ConsoleKey.X:
                Snapshot();
                _clipboard.CutToClipboard();
                return true;

            case ConsoleKey.V:
                Snapshot();
                _clipboard.PasteFromClipboard();
                return true;

            case ConsoleKey.Z:
                Undo();
                return true;

            case ConsoleKey.A:
                Snapshot();
                _selection.SetAnchor(0);
                _buffer.MoveTo(_buffer.Length);
                return true;
        }

        return false;
    }

    // ══════════════════════════════════════════════════════════════════
    //  Key Dispatch: Navigation
    // ══════════════════════════════════════════════════════════════════

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
                    _cursorMovement.ResetStickyColumn();
                    _cursorMovement.MoveCursorWordLeft(shift);
                    return true;
                case ConsoleKey.RightArrow:
                    _cursorMovement.ResetStickyColumn();
                    _cursorMovement.MoveCursorWordRight(shift);
                    return true;
            }
            return false;
        }

        // Plain navigation keys
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                _cursorMovement.MoveCursorUp(shift);
                return true;
            case ConsoleKey.DownArrow:
                _cursorMovement.MoveCursorDown(shift);
                return true;
            case ConsoleKey.LeftArrow:
                _cursorMovement.ResetStickyColumn();
                _cursorMovement.MoveCursorLeft(shift);
                return true;
            case ConsoleKey.RightArrow:
                _cursorMovement.ResetStickyColumn();
                _cursorMovement.MoveCursorRight(shift);
                return true;
            case ConsoleKey.Home:
                _cursorMovement.ResetStickyColumn();
                _cursorMovement.MoveCursorHome(shift);
                return true;
            case ConsoleKey.End:
                _cursorMovement.ResetStickyColumn();
                _cursorMovement.MoveCursorEnd(shift);
                return true;
        }

        return false;
    }

    // ══════════════════════════════════════════════════════════════════
    //  Key Dispatch: Editing
    // ══════════════════════════════════════════════════════════════════

    private bool HandleEditingKey(ConsoleKeyInfo key, bool ctrl)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                ResetState();
                return true;

            case ConsoleKey.Tab:
                PerformAutoComplete();
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
        {
            RemoveSelectedText();
            _clipboard.CleanupOrphanedAttachments();
        }
        else if (_clipboard.RemovePlaceholderAffectedBy(_buffer.CursorPosition, 0))
        {
            // Placeholder was preemptively removed, cursor is at its start position.
            // Insert the character at this position.
            _buffer.Insert(c);
            return;
        }

        if (_buffer.CursorPosition < _buffer.Length || _buffer.Length == 0)
            _buffer.Insert(c);
        else
            _tempInput.Append(c);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Selection Handling
    // ══════════════════════════════════════════════════════════════════

    private void HandleBackspace()
    {
        if (_selection.IsActiveAt(_buffer.CursorPosition))
        {
            if (_clipboard.RemovePlaceholderAffectedBy(
                    _selection.SelectionStart(_buffer.CursorPosition),
                    _selection.SelectionLength(_buffer.CursorPosition)))
            {
                _selection.Clear();
            }
            else
            {
                RemoveSelectedText();
            }
        }
        else if (!_clipboard.RemovePlaceholderAffectedBy(_buffer.CursorPosition - 1, 1))
        {
            _buffer.Backspace();
        }
    }

    private void HandleDelete()
    {
        if (_selection.IsActiveAt(_buffer.CursorPosition))
        {
            if (_clipboard.RemovePlaceholderAffectedBy(
                    _selection.SelectionStart(_buffer.CursorPosition),
                    _selection.SelectionLength(_buffer.CursorPosition)))
            {
                _selection.Clear();
            }
            else
            {
                RemoveSelectedText();
            }
        }
        else if (!_clipboard.RemovePlaceholderAffectedBy(_buffer.CursorPosition, 1))
        {
            _buffer.Delete();
        }
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

    // ══════════════════════════════════════════════════════════════════
    //  Undo
    // ══════════════════════════════════════════════════════════════════

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

    // ══════════════════════════════════════════════════════════════════
    //  Auto-Complete (Tab)
    // ══════════════════════════════════════════════════════════════════

    private void PerformAutoComplete()
    {
        if (AutoCompleteProvider == null)
            return;

        string current = _buffer.CurrentInput;
        string? completed = AutoCompleteProvider(current);

        if (string.IsNullOrEmpty(completed) || completed == current)
            return;

        Snapshot();
        _buffer.SetContent(completed, completed.Length);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Reset
    // ══════════════════════════════════════════════════════════════════

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
        _cursorMovement.ResetStickyColumn();
        _undo.Clear();
        _clipboard.ResetCounter();
    }

    // ══════════════════════════════════════════════════════════════════
    //  Save / Load / Remove Input Field State
    // ══════════════════════════════════════════════════════════════════

    /// <summary>Saves the current input field state and returns a unique ID.</summary>
    public string SaveInputField()
    {
        string id = Interlocked.Increment(ref _saveCounter).ToString();
        var saved = new SavedInputState(
            _buffer.CurrentInput,
            _buffer.CursorPosition,
            Attachments.Select(a => a with { }).ToList());
        _savedInputs[id] = saved;
        return id;
    }

    /// <summary>Restores input field state by ID. Returns false if the ID is unknown.</summary>
    public bool LoadInputField(string id)
    {
        if (!_savedInputs.TryGetValue(id, out var state))
            return false;

        var attachments = state.Attachments.Select(a => a with { }).ToList();

        Reset();
        _buffer.SetContent(state.Text, state.CursorPosition);
        Attachments = attachments;

        return true;
    }

    /// <summary>Removes a single saved input field by ID. Returns false if the ID is unknown.</summary>
    public bool RemoveSavedInputField(string id)
        => _savedInputs.TryRemove(id, out _);

    /// <summary>Removes all saved input field states.</summary>
    public void RemoveAllSavedInputFields()
        => _savedInputs.Clear();

    /// <summary>
    /// Replaces the input buffer with the given text, resets cursor to end,
    /// clears selection and undo history. Attachments are not affected.
    /// </summary>
    public void SetInputFieldContent(string text)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        _buffer.SetContent(text, text.Length);
        _selection.Clear();
        _undo.Clear();
    }

    /// <summary>Returns the IDs of all currently saved input field states.</summary>
    public IReadOnlyList<string> GetSavedInputFieldIds()
        => _savedInputs.Keys.ToList();
}
