using System.Text;

namespace StreamShell;

/// <summary>
/// Handles clipboard operations (copy, cut, paste), large-paste detection,
/// temp input buffering, and attachment tracking. Works on the shared
/// <see cref="TextBuffer"/> and <see cref="SelectionManager"/> but is
/// independent of cursor movement or undo orchestration.
/// </summary>
internal class ClipboardHandler
{
    private readonly TextBuffer _buffer;
    private readonly SelectionManager _selection;
    private readonly StringBuilder _tempInput;
    private readonly Action _snapshot;
    private readonly Func<int> _getLargePasteThreshold;
    private readonly Func<int> _getLargePasteLineThreshold;

    /// <summary>Attachments collected during input (e.g. large pastes). Shared with the owner.</summary>
    public List<Attachment> Attachments { get; set; } = new();

    public ClipboardHandler(
        TextBuffer buffer,
        SelectionManager selection,
        StringBuilder tempInput,
        Action snapshot,
        Func<int> getLargePasteThreshold,
        Func<int> getLargePasteLineThreshold)
    {
        _buffer = buffer;
        _selection = selection;
        _tempInput = tempInput;
        _snapshot = snapshot;
        _getLargePasteThreshold = getLargePasteThreshold;
        _getLargePasteLineThreshold = getLargePasteLineThreshold;
    }

    // ══════════════════════════════════════════════════════════════════
    //  Clipboard Operations
    // ══════════════════════════════════════════════════════════════════

    /// <summary>Returns the selected text, or the full buffer if no selection is active.</summary>
    private string GetClipboardText()
    {
        int cursor = _buffer.CursorPosition;
        return _selection.IsActiveAt(cursor)
            ? _selection.SelectedText(cursor, _buffer.CurrentInput)
            : _buffer.CurrentInput;
    }

    public void CopyToClipboard()
    {
        TryClipboardCopy(GetClipboardText());
    }

    public void CutToClipboard()
    {
        TryClipboardCopy(GetClipboardText());

        int cursor = _buffer.CursorPosition;
        if (_selection.IsActiveAt(cursor))
        {
            _buffer.Remove(_selection.SelectionStart(cursor), _selection.SelectionLength(cursor));
            _selection.Clear();
        }
        else
        {
            _buffer.Clear();
        }
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

    public void PasteFromClipboard()
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

        int cursor = _buffer.CursorPosition;
        if (_selection.IsActiveAt(cursor))
        {
            _snapshot();
            _buffer.Remove(_selection.SelectionStart(cursor), _selection.SelectionLength(cursor));
            _selection.Clear();
        }

        InsertPastedText(text);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Temp Buffer / Paste Handling
    // ══════════════════════════════════════════════════════════════════

    /// <summary>Flushes non-control input accumulated in the temp buffer into the main buffer.</summary>
    public void FlushTempInput()
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

        if (text.Length > _getLargePasteThreshold() || lineCount > _getLargePasteLineThreshold())
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

    /// <summary>Buffers a single character for later flush (non-control key input).</summary>
    public void BufferCharacter(char c)
    {
        _tempInput.Append(c);
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
