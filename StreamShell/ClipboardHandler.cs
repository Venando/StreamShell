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
            var attachment = new Attachment(text, AttachmentType.PlainText, lineCount);
            Attachments.Add(attachment);
            _buffer.Insert(GeneratePlaceholder(attachment));
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

    /// <summary>Generates the placeholder text that will be inserted into the buffer for an attachment.</summary>
    internal static string GeneratePlaceholder(Attachment attachment)
    {
        if (attachment.LineCount > 1)
        {
            return $"[paste {attachment.LineCount} lines]";
        }
        else
        {
            return $"[paste {1} line]";
        }
    }

    /// <summary>
    /// Checks if any attachment's placeholder would be affected by an operation
    /// at the given buffer range (insertion point with length 0, or deletion range
    /// with length &gt; 0). If an overlap is found, the placeholder is removed from
    /// the buffer and its attachment is removed from the list.
    /// </summary>
    /// <param name="affectedStart">Start position of the affected buffer range.</param>
    /// <param name="affectedLength">Length of the affected range (0 for insertions).</param>
    /// <returns>True if at least one placeholder was preemptively removed.</returns>
    public bool RemovePlaceholderAffectedBy(int affectedStart, int affectedLength)
    {
        bool anyRemoved = false;

        foreach (var attachment in Attachments.ToList())
        {
            string placeholder = GeneratePlaceholder(attachment);
            int placeholderIndex = _buffer.CurrentInput.IndexOf(placeholder, StringComparison.Ordinal);
            if (placeholderIndex == -1) continue;

            int placeholderEnd = placeholderIndex + placeholder.Length;
            int affectedEnd = affectedStart + affectedLength;

            if (affectedStart < placeholderEnd && affectedEnd > placeholderIndex)
            {
                _buffer.Remove(placeholderIndex, placeholder.Length);
                Attachments.Remove(attachment);
                anyRemoved = true;
            }
        }

        return anyRemoved;
    }
}
