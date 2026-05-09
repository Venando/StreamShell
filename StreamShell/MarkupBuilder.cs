using System.Text;
using Spectre.Console;

namespace StreamShell;

/// <summary>
/// Builds Spectre.Console markup strings for the input renderer.
/// Handles cursor highlighting, selection rendering, placeholder styling,
/// and Spectre markup escaping — all using <see cref="StringBuilder"/> and
/// <see cref="ReadOnlySpan{T}"/> to minimize string allocations.
///
/// Extracted from <see cref="ConsoleRenderer"/> to separate formatting concerns
/// from terminal write operations (SRP).
/// </summary>
internal class MarkupBuilder
{
    private readonly StringBuilder _sb = new(capacity: 256);
    private readonly StreamShellSettings _settings;

    /// <summary>
    /// Placeholder strings from current attachments, used to render them as
    /// underlined markup instead of plain escaped text.
    /// </summary>
    public IReadOnlyList<string>? PlaceholderStrings { get; set; }

    public MarkupBuilder(StreamShellSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Resets the internal StringBuilder for reuse, avoiding per-call allocation.
    /// </summary>
    public void Reset() => _sb.Clear();

    // ── Main pipeline ────────────────────────────────────────────────

    /// <summary>
    /// Builds Spectre markup for one visual line, rendering cursor and
    /// selection using the configured markup styles from settings.
    /// When selection is active, the cursor is hidden.
    /// Accepts <see cref="ReadOnlySpan{T}"/> for <paramref name="lineText"/>
    /// to avoid substring allocation on the hot render path.
    /// </summary>
    public string BuildLineMarkup(
        string input,
        int lineOffset,
        ReadOnlySpan<char> lineText,
        int cursorPosition,
        bool hasSelection,
        int selectionStart,
        int selectionLength)
    {
        // Reuse the same StringBuilder — clears instead of allocating a new one
        _sb.Clear();

        int lineStart = lineOffset;
        int lineEnd = lineOffset + lineText.Length;
        bool isCommandSlashLine = lineOffset == 0 && lineText.Length > 0 && lineText[0] == '/';
        string cmdSlashMarkup = _settings.CommandSlashMarkup;

        // Determine cursor column on this line (only when no selection)
        int cursorCol = -1;
        if (!hasSelection && cursorPosition >= lineStart && cursorPosition <= lineEnd)
            cursorCol = cursorPosition - lineStart;

        // Determine selection range on this visual line
        int selStartInLine = 0, selEndInLine = 0;
        bool selectionOnThisLine = hasSelection && selectionLength > 0
            && selectionStart < lineEnd && selectionStart + selectionLength > lineStart;

        if (selectionOnThisLine)
        {
            selStartInLine = Math.Max(0, selectionStart - lineStart);
            selEndInLine = Math.Min(lineText.Length, selectionStart + selectionLength - lineStart);
        }

        if (selectionOnThisLine)
        {
            // Before selection (span-based to avoid substring allocation)
            if (selStartInLine > 0)
                AppendEscapedChunk(lineText[..selStartInLine],
                    isCommandSlashLine, cmdSlashMarkup);

            // Selected text
            _sb.Append('[').Append(_settings.SelectionMarkup).Append(']');
            AppendMarkupEscaped(lineText.Slice(selStartInLine, selEndInLine - selStartInLine));
            _sb.Append("[/]");

            // After selection (span-based to avoid substring allocation)
            if (selEndInLine < lineText.Length)
                AppendEscapedChunk(lineText[selEndInLine..], isCommandSlashLine, cmdSlashMarkup);
        }
        else
        {
            AppendCursorHighlight(lineText, ref cursorCol, 0,
                isCommandSlashLine, cmdSlashMarkup);
            cursorCol = -1;
        }

        return _sb.ToString();
    }

    // ── Escaped chunk appending ──────────────────────────────────────

    /// <summary>
    /// Appends escaped text, wrapping a leading '/' in command markup if this
    /// is the first visual line and the character is at position 0.
    /// Placeholder patterns ([paste ...]) are rendered as underlined markup.
    /// </summary>
    private void AppendEscapedChunk(ReadOnlySpan<char> text, bool isCommandSlash, string cmdSlashMarkup)
    {
        if (isCommandSlash && text.Length > 0 && text[0] == '/')
        {
            _sb.Append('[').Append(cmdSlashMarkup).Append("]/[/]");
            if (text.Length > 1)
                AppendMarkupEscapedWithPlaceholder(text[1..]);
        }
        else
        {
            AppendMarkupEscapedWithPlaceholder(text);
        }
    }

    /// <summary>
    /// Appends Spectre-escaped text from a span to the StringBuilder, rendering
    /// known placeholder strings with italic underline styling.
    /// </summary>
    private void AppendMarkupEscapedWithPlaceholder(ReadOnlySpan<char> text)
    {
        var phStrings = PlaceholderStrings;
        if (phStrings is null || phStrings.Count == 0)
        {
            AppendMarkupEscaped(text);
            return;
        }

        int searchFrom = 0;

        while (searchFrom < text.Length)
        {
            ReadOnlySpan<char> remaining = text[searchFrom..];

            // Search for the earliest placeholder match (full or cursor-split fragment)
            int bestIdx = -1;
            int bestEnd = -1;

            foreach (string ph in phStrings)
            {
                // Try full placeholder string
                int idx = text[searchFrom..].IndexOf(ph, StringComparison.Ordinal);
                if (idx >= 0 && (bestIdx < 0 || idx < bestIdx))
                {
                    bestIdx = searchFrom + idx;
                    bestEnd = bestIdx + ph.Length;
                }

                // Try cursor-split fragment (missing opening [)
                if (ph.Length > 0 && ph[0] == '[')
                {
                    ReadOnlySpan<char> fragment = ph.AsSpan(1);
                    if (fragment.Length <= remaining.Length &&
                        remaining.StartsWith(fragment, StringComparison.Ordinal))
                    {
                        if (bestIdx < 0 || searchFrom < bestIdx)
                        {
                            bestIdx = searchFrom;
                            bestEnd = searchFrom + fragment.Length;
                        }
                    }
                }
            }

            if (bestIdx < 0)
                break;

            // Escape text before the placeholder
            if (bestIdx > searchFrom)
                AppendMarkupEscaped(text[searchFrom..bestIdx]);

            // Render placeholder as italic underline
            _sb.Append("[italic underline]");
            ReadOnlySpan<char> placeholderContent = text[bestIdx..bestEnd];
            for (int i = 0; i < placeholderContent.Length; i++)
            {
                char c = placeholderContent[i];
                if (c == '[' || c == ']')
                {
                    _sb.Append(c);
                    _sb.Append(c);  // [[ or ]]
                }
                else
                {
                    _sb.Append(c);
                }
            }
            _sb.Append("[/]");
            searchFrom = bestEnd;
        }

        if (searchFrom < text.Length)
            AppendMarkupEscaped(text[searchFrom..]);
    }

    // ── Spectre markup escaping ──────────────────────────────────────

    /// <summary>
    /// Appends Spectre-escaped text from a <see cref="ReadOnlySpan{T}"/> directly
    /// to the StringBuilder, escaping <c>[</c> as <c>[[</c> to match the behavior
    /// of <see cref="Markup.Escape(string)"/> without allocating intermediate strings.
    /// </summary>
    private void AppendMarkupEscaped(ReadOnlySpan<char> span)
    {
        int last = 0;
        for (int i = 0; i < span.Length; i++)
        {
            char c = span[i];
            if (c == '[')
            {
                if (i > last)
                    _sb.Append(span[last..i]);
                _sb.Append("[[");
                last = i + 1;
            }
            else if (c == ']')
            {
                if (i > last)
                    _sb.Append(span[last..i]);
                _sb.Append("]]");
                last = i + 1;
            }
        }
        if (last < span.Length)
            _sb.Append(span[last..]);
    }

    // ── Cursor highlight ─────────────────────────────────────────────

    /// <summary>
    /// Appends text with cursor highlight using the configured cursor markup style.
    /// The character at <paramref name="cursorCol"/> gets the style.
    /// Past-end cursor shows a highlighted space.
    /// Sets <paramref name="cursorCol"/> to -1 after placing.
    /// </summary>
    private void AppendCursorHighlight(
        ReadOnlySpan<char> rawText,
        ref int cursorCol,
        int segmentOffset,
        bool isCommandSlashLine,
        string cmdSlashMarkup)
    {
        if (cursorCol < 0)
        {
            AppendEscapedChunk(rawText, isCommandSlashLine, cmdSlashMarkup);
            return;
        }

        int localCol = cursorCol - segmentOffset;
        if (localCol < 0 || localCol > rawText.Length)
        {
            AppendEscapedChunk(rawText, isCommandSlashLine, cmdSlashMarkup);
            return;
        }

        // Before cursor (span-based to avoid substring allocation)
        if (localCol > 0)
            AppendEscapedChunk(rawText[..localCol], isCommandSlashLine, cmdSlashMarkup);

        string cursorStyle = _settings.CursorMarkup;

        if (localCol < rawText.Length)
        {
            char c = rawText[localCol];
            _sb.Append('[').Append(cursorStyle).Append(']');
            if (c == '[')
                _sb.Append("[[");  // Escape [ for Spectre markup
            else if (c == ']')
                _sb.Append("]]");  // Escape ] for Spectre markup
            else
                _sb.Append(c);     // No allocation: char appends directly
            _sb.Append("[/]");

            if (localCol + 1 < rawText.Length)
                AppendMarkupEscapedWithPlaceholder(rawText[(localCol + 1)..]);
        }
        else
        {
            _sb.Append("[").Append(cursorStyle).Append("] [/]"); // Past-end placeholder
        }

        cursorCol = -1;
    }
}
