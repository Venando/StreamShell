using System.Buffers;
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
    private readonly IClipboardService _clipboard;

    /// <summary>Attachments collected during input (e.g. large pastes). Shared with the owner.</summary>
    public List<Attachment> Attachments { get; set; } = new();

    /// <summary>Counter for attachment placeholders, reset on each submit.</summary>
    private int _attachmentCounter;

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
        _clipboard = new ClipboardService();
    }

    /// <summary>Creates the handler with an explicit clipboard service (for testing).</summary>
    internal ClipboardHandler(
        TextBuffer buffer,
        SelectionManager selection,
        StringBuilder tempInput,
        Action snapshot,
        Func<int> getLargePasteThreshold,
        Func<int> getLargePasteLineThreshold,
        IClipboardService clipboard)
        : this(buffer, selection, tempInput, snapshot, getLargePasteThreshold, getLargePasteLineThreshold)
    {
        _clipboard = clipboard;
    }

    // ══════════════════════════════════════════════════════════════════
    //  Clipboard Operations
    // ══════════════════════════════════════════════════════════════════

    /// <summary>Returns the selected text (or full buffer if no selection), with attachment
    /// placeholders resolved to their actual content so that copy/cut captures the
    /// original pasted data rather than the inline placeholder string.</summary>
    private string GetClipboardText()
    {
        int cursor = _buffer.CursorPosition;
        int textStart;
        string text;

        if (_selection.IsActiveAt(cursor))
        {
            text = _selection.SelectedText(cursor, _buffer.CurrentInput);
            textStart = _selection.SelectionStart(cursor);
        }
        else
        {
            text = _buffer.CurrentInput;
            textStart = 0;
        }

        return ResolvePlaceholdersInText(text, textStart);
    }

    /// <summary>
    /// Replaces attachment placeholder strings within <paramref name="text"/>
    /// with the actual attachment content. <paramref name="textStart"/> is the
    /// start position of <paramref name="text"/> within the full buffer, used
    /// to locate overlapping placeholders.
    /// </summary>
    private string ResolvePlaceholdersInText(string text, int textStart)
    {
        if (Attachments.Count == 0 || string.IsNullOrEmpty(text))
            return text;

        string fullInput = _buffer.CurrentInput;
        int textEnd = textStart + text.Length;

        // Collect attachments whose placeholder overlaps [textStart, textEnd)
        // Store as (bufferPos, placeholder, content) for replacement.
        var overlapping = new List<(int pos, string placeholder, string content)>();

        foreach (var attachment in Attachments)
        {
            string ph = attachment.Placeholder;
            if (string.IsNullOrEmpty(ph))
                continue;

            int phPos = fullInput.IndexOf(ph, StringComparison.Ordinal);
            if (phPos < 0)
                continue;

            int phEnd = phPos + ph.Length;

            // Overlap check: placeholder range intersects [textStart, textEnd)
            if (phPos < textEnd && phEnd > textStart)
            {
                overlapping.Add((phPos, ph, attachment.Content));
            }
        }

        if (overlapping.Count == 0)
            return text;

        // Sort descending by buffer position so replacements don't shift earlier indices
        overlapping.Sort((a, b) => b.pos.CompareTo(a.pos));

        string resolved = text;
        int offset = 0;

        foreach (var (pos, placeholder, content) in overlapping)
        {
            int localStart = pos - textStart + offset;
            int localEnd = localStart + placeholder.Length;

            // Clip replacement to the bounds of the resolved string
            int clipStart = Math.Max(0, localStart);
            int clipEnd = Math.Min(resolved.Length, localEnd);

            if (clipStart < clipEnd)
            {
                resolved = string.Concat(
                    resolved.AsSpan(0, clipStart),
                    content.AsSpan(),
                    resolved.AsSpan(clipEnd));
                offset += content.Length - (clipEnd - clipStart);
            }
        }

        return resolved;
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

        CleanupOrphanedAttachments();
    }

    /// <summary>Attempts clipboard copy. Silently ignores platform errors (e.g. WSL).</summary>
    private void TryClipboardCopy(string text)
    {
        try
        {
            _clipboard.Copy(text);
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
            text = _clipboard.Paste();
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
        // Count newlines without allocating a string array from Split
        int lineCount = 1;
        foreach (char c in text)
        {
            if (c == '\n') lineCount++;
        }

        if (text.Length > _getLargePasteThreshold() || lineCount > _getLargePasteLineThreshold())
        {
            string placeholder = GeneratePlaceholder(lineCount, _attachmentCounter + 1);
            var attachment = new Attachment(text, AttachmentType.PlainText, lineCount, ++_attachmentCounter, placeholder);
            Attachments.Add(attachment);
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

    /// <summary>Generates the placeholder text for a paste attachment.</summary>
    internal static string GeneratePlaceholder(int lineCount, int counter)
    {
        string lines = lineCount > 1 ? $"{lineCount} lines" : "1 line";
        return $"[paste #{counter}, {lines}]";
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
    /// <summary>Removes attachments whose placeholders no longer exist intact in the buffer.
    /// Uses reverse iteration and span-based searching to avoid string allocations.</summary>
    public void CleanupOrphanedAttachments()
    {
        string currentInput = _buffer.CurrentInput;
        ReadOnlySpan<char> inputSpan = currentInput.AsSpan();
        for (int i = Attachments.Count - 1; i >= 0; i--)
        {
            string placeholder = Attachments[i].Placeholder;
            if (string.IsNullOrEmpty(placeholder) || inputSpan.IndexOf(placeholder, StringComparison.Ordinal) < 0)
                Attachments.RemoveAt(i);
        }
    }

    /// <summary>
    /// Checks if any attachment's placeholder would be affected by an operation
    /// at the given buffer range (insertion point with length 0, or deletion range
    /// with length &gt; 0). If an overlap is found, the placeholder is removed from
    /// the buffer and its attachment is removed from the list.
    /// Uses <see cref="ArrayPool{T}"/> for the overlap tracking list to avoid
    /// per-call heap allocation on every Insert/Backspace/Delete keystroke.
    /// </summary>
    /// <param name="affectedStart">Start position of the affected buffer range.</param>
    /// <param name="affectedLength">Length of the affected range (0 for insertions).</param>
    /// <returns>True if at least one placeholder was preemptively removed.</returns>
    public bool RemovePlaceholderAffectedBy(int affectedStart, int affectedLength)
    {
        // Phase 1: compute all overlaps on the ORIGINAL buffer content
        string originalInput = _buffer.CurrentInput;
        ReadOnlySpan<char> inputSpan = originalInput.AsSpan();

        // Rent from ArrayPool to avoid List<(int,int)> allocation on every keystroke.
        int maxOverlaps = Math.Min(Attachments.Count, 16);
        (int index, int length)[]? rentedBuffer = null;
        Span<(int index, int length)> overlapBuffer = maxOverlaps <= 8
            ? stackalloc (int, int)[8]
            : (rentedBuffer = ArrayPool<(int index, int length)>.Shared.Rent(maxOverlaps));

        int overlapCount = 0;

        try
        {
            // Reverse-iterate attachments to avoid allocating Attachments.ToList()
            for (int i = Attachments.Count - 1; i >= 0; i--)
            {
                string placeholder = Attachments[i].Placeholder;
                if (string.IsNullOrEmpty(placeholder)) continue;

                int placeholderIndex = inputSpan.IndexOf(placeholder, StringComparison.Ordinal);
                if (placeholderIndex == -1) continue;

                int placeholderEnd = placeholderIndex + placeholder.Length;
                int affectedEnd = affectedStart + affectedLength;

                if (affectedStart < placeholderEnd && affectedEnd > placeholderIndex)
                {
                    if (overlapCount < overlapBuffer.Length)
                        overlapBuffer[overlapCount++] = (placeholderIndex, placeholder.Length);
                    Attachments.RemoveAt(i);
                }
            }

            // Phase 2: remove right-to-left so indices stay valid
            // Sort descending by index using span to avoid comparer allocation
            if (overlapCount > 1)
            {
                Span<(int index, int length)> toSort = overlapBuffer[..overlapCount];
                // Simple insertion sort for small lists
                for (int i = 1; i < toSort.Length; i++)
                {
                    var key = toSort[i];
                    int j = i - 1;
                    while (j >= 0 && toSort[j].index < key.index)
                    {
                        toSort[j + 1] = toSort[j];
                        j--;
                    }
                    toSort[j + 1] = key;
                }
            }

            for (int i = 0; i < overlapCount; i++)
            {
                _buffer.Remove(overlapBuffer[i].index, overlapBuffer[i].length);
            }

            return overlapCount > 0;
        }
        finally
        {
            if (rentedBuffer is not null)
                ArrayPool<(int index, int length)>.Shared.Return(rentedBuffer);
        }
    }

    /// <summary>Resets the attachment counter (called on each submit).</summary>
    public void ResetCounter() => _attachmentCounter = 0;
}
