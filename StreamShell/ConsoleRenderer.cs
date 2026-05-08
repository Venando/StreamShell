namespace StreamShell;

using Spectre.Console;

internal class ConsoleRenderer
{
    public int RightMargin { get; set; } = Console.WindowWidth;

    /// <summary>Get the total vertical space taken by the input block.</summary>
    public int GetBlockOffset(string input) => 7 + GetInputLineCount(input);

    /// <summary>Get the number of visual lines the input occupies.</summary>
    public int GetInputLineCount(string input) => GetInputLines(input, RightMargin).Count;

    public static void RenderMessage(string markup)
    {
        try
        {
            AnsiConsole.MarkupLine(markup);
        }
        catch (InvalidOperationException)
        {
            AnsiConsole.MarkupLine(Markup.Escape(markup));
        }
    }

    public void ClearInputLine()
    {
        Console.CursorLeft = 0;
        ClearLine();
    }

    public void ClearInputBlock(string? lastInput)
    {
        if (lastInput is null)
            return;

        int blockOffset = GetBlockOffset(lastInput);
        Console.CursorTop -= blockOffset;
        ClearBlock(blockOffset);
    }

    // ── Render with cursor and selection ──────────────────────────────

    public static void RenderInputBlock(
        string input,
        IReadOnlyList<string> hints,
        int cursorPosition,
        bool hasSelection,
        int selectionStart,
        int selectionLength,
        int margin)
    {
        RenderSeparatorLine();
        RenderInputLine(input, cursorPosition, hasSelection, selectionStart, selectionLength, margin);
        Console.WriteLine();

        RenderHintsBlock(hints);
    }

    public static void OverwriteInputBlock(
        string input,
        IReadOnlyList<string> hints,
        int blockOffset,
        int cursorPosition,
        bool hasSelection,
        int selectionStart,
        int selectionLength,
        int margin)
    {
        Console.CursorTop -= blockOffset - 1;

        RenderInputLine(input, cursorPosition, hasSelection, selectionStart, selectionLength, margin);
        Console.Write("\x1b[K");
        Console.WriteLine();

        RenderHintsBlock(hints);
    }

    // ── Input Line Rendering ──────────────────────────────────────────

    internal static void RenderInputLine(
        string input,
        int cursorPosition,
        bool hasSelection,
        int selectionStart,
        int selectionLength,
        int margin)
    {
        int effectiveMargin = Math.Max(10, margin);

        var lines = GetInputLines(input, effectiveMargin);
        int absOffset = 0; // character position in raw input where current visual line starts

        for (int i = 0; i < lines.Count; i++)
        {
            string lineText = lines[i];
            int lineEnd = absOffset + lineText.Length;

            // Build the Spectre.Console markup for this visual line
            string lineMarkup = BuildLineMarkup(
                input, absOffset, lineText,
                cursorPosition, hasSelection, selectionStart, selectionLength);

            // Set cursor to column 0
            Console.CursorLeft = 0;

            if (lines.Count == 1)
            {
                // Single-line input
                AnsiConsole.Markup($"[blue]> [/]{lineMarkup}");
            }
            else if (i == 0)
            {
                // First line of multi-line input
                AnsiConsole.Markup($"[blue]> [/]{lineMarkup}");
            }
            else
            {
                // Continuation line (no prompt)
                Console.Write(lineMarkup);
            }

            // Move to next visual line
            if (i < lines.Count - 1)
                Console.WriteLine();

            absOffset = lineEnd;
        }
    }

    /// <summary>
    /// Build the Spectre markup for one visual line, accounting for selection
    /// and cursor position. The cursor is rendered as [white]|[/] at its column.
    /// The selection is rendered as [white on gray]...[/].
    /// </summary>
    private static string BuildLineMarkup(
        string input,
        int lineOffset,
        string lineText,
        int cursorPosition,
        bool hasSelection,
        int selectionStart,
        int selectionLength)
    {
        int lineStart = lineOffset;
        int lineEnd = lineOffset + lineText.Length;

        // Determine cursor column within this line (or outside it)
        bool cursorOnThisLine = cursorPosition >= lineStart && cursorPosition <= lineEnd;
        int cursorCol = cursorOnThisLine ? cursorPosition - lineStart : -1;

        // Determine selection range on this line
        bool selectionOnThisLine = false;
        int selStartInLine = 0;
        int selEndInLine = 0;

        if (hasSelection && selectionLength > 0)
        {
            int selAbsStart = selectionStart;
            int selAbsEnd = selectionStart + selectionLength;

            if (selAbsStart < lineEnd && selAbsEnd > lineStart)
            {
                selectionOnThisLine = true;
                selStartInLine = Math.Max(0, selAbsStart - lineStart);
                selEndInLine = Math.Min(lineText.Length, selAbsEnd - lineStart);
            }
        }

        // Build segments: text is split into runs where selection boundaries and
        // cursor column are segment boundaries.
        var sb = new System.Text.StringBuilder();

        if (selectionOnThisLine)
        {
            // Segment 1: before selection
            if (selStartInLine > 0)
            {
                string beforeSel = Markup.Escape(lineText[..selStartInLine]);
                InsertCursorIntoSegment(sb, beforeSel, cursorCol, 0);
                cursorCol = -1; // cursor already rendered
                sb.Append(beforeSel);
            }

            // Segment 2: selected text
            {
                string selText = Markup.Escape(lineText[selStartInLine..selEndInLine]);
                sb.Append("[white on gray]");
                InsertCursorIntoSegment(sb, selText, cursorCol, 0);
                cursorCol = -1;
                sb.Append(selText);
                sb.Append("[/]");
            }

            // Segment 3: after selection
            if (selEndInLine < lineText.Length)
            {
                string afterSel = Markup.Escape(lineText[selEndInLine..]);
                InsertCursorIntoSegment(sb, afterSel, cursorCol, 0);
                cursorCol = -1;
                sb.Append(afterSel);
            }

            // Cursor not yet rendered and on this line but outside selection?
            if (cursorCol >= 0)
            {
                InsertCursorIntoSegment(sb, "", cursorCol, 0);
            }
        }
        else
        {
            // No selection on this line — just render the text with cursor
            string escaped = Markup.Escape(lineText);
            InsertCursorIntoSegment(sb, escaped, cursorCol, 0);
            sb.Append(escaped);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Inserts [white]|[/] cursor marker into the segment at <paramref name="cursorCol"/>
    /// if cursorColumn is within [0, segmentLength]. The segment text and cursorCol are
    /// relative to the start of this segment (segmentOffset accounts for earlier segments).
    /// </summary>
    private static void InsertCursorIntoSegment(
        System.Text.StringBuilder sb,
        string segmentText,
        int cursorCol,
        int segmentOffset)
    {
        if (cursorCol < 0)
            return;

        int localCol = cursorCol - segmentOffset;
        if (localCol >= 0 && localCol <= segmentText.Length)
        {
            // Insert cursor at localCol within segmentText
            sb.Append(segmentText[..localCol]);
            sb.Append("[white]|[/]");
            sb.Append(segmentText[localCol..]);
        }
    }

    // ── Hints Block ───────────────────────────────────────────────────

    private static void RenderHintsBlock(IReadOnlyList<string> hints)
    {
        if (HasHints(hints))
            RenderSeparatorLine();
        else
            Console.WriteLine();

        int maxWidth = Console.WindowWidth - 1;
        for (int i = 0; i < CommandPalette.MaxHeight; i++)
        {
            Console.CursorLeft = 0;
            ClearLine();
            string hint = hints[i];
            if (!string.IsNullOrEmpty(hint))
            {
                string safeHint = TruncateToVisualWidth(hint, maxWidth);

                try
                {
                    AnsiConsole.Markup(safeHint);
                }
                catch (InvalidOperationException)
                {
                    AnsiConsole.Markup(Markup.Escape(safeHint));
                }
            }
            if (i < CommandPalette.MaxHeight - 1)
                Console.WriteLine();
        }
    }

    // ── Line Wrapping ─────────────────────────────────────────────────

    internal static List<string> GetInputLines(string input, int margin)
    {
        int width = Math.Max(1, Math.Min(margin, Console.WindowWidth));
        var lines = new List<string>();

        if (string.IsNullOrEmpty(input))
            return new List<string> { "" };

        var segments = input.Split('\n');

        for (int segIndex = 0; segIndex < segments.Length; segIndex++)
        {
            string segment = segments[segIndex];
            bool isFirstSegment = segIndex == 0;
            bool isLastSegment = segIndex == segments.Length - 1;
            bool singleSegment = segments.Length == 1;

            if (singleSegment && segment.Length + 4 < width)
            {
                lines.Add(segment);
                continue;
            }

            int remaining = segment.Length;
            int pos = 0;

            while (remaining > 0)
            {
                int cap;
                if (isFirstSegment && lines.Count == 0)
                {
                    // First visual line has "> " prefix
                    cap = Math.Max(0, width - 4);
                }
                else if (isLastSegment && remaining <= width - 2)
                {
                    cap = width - 2;
                    if (remaining == width - 1)
                        cap = width - 2;
                }
                else
                {
                    cap = width - 1;
                }

                int take = Math.Min(remaining, cap);
                lines.Add(segment.Substring(pos, take));
                pos += take;
                remaining -= take;
            }

            // Empty segment between newlines
            if (segment.Length == 0 && (!isLastSegment || segments.Length > 1))
            {
                lines.Add("");
            }
        }

        return lines;
    }

    /// <summary>
    /// Converts a character position in the raw input string to
    /// (visual line index, visual column) for the wrapping at <paramref name="margin"/>.
    /// </summary>
    public static (int line, int column) GetCursorVisualPosition(
        string input, int cursorPosition, int margin)
    {
        var visualLines = GetInputLines(input, margin);
        int accumulated = 0;

        for (int i = 0; i < visualLines.Count; i++)
        {
            int lineLen = visualLines[i].Length;
            if (accumulated + lineLen > cursorPosition)
                return (i, cursorPosition - accumulated);

            accumulated += lineLen;
        }

        // At or past the end of the last line
        if (visualLines.Count == 0)
            return (0, 0);

        return (visualLines.Count - 1, visualLines[^1].Length);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static string TruncateToVisualWidth(string text, int maxWidth)
    {
        int visualWidth = 0;
        int i = 0;

        while (i < text.Length)
        {
            if (text[i] == '[')
            {
                int close = text.IndexOf(']', i);
                if (close > i)
                {
                    i = close + 1;
                    continue;
                }
            }

            visualWidth++;
            if (visualWidth > maxWidth)
                return text[..i];

            i++;
        }

        return text;
    }

    private static bool HasHints(IReadOnlyList<string> hints) => hints.Any(h => !string.IsNullOrEmpty(h));

    private static void RenderSeparatorLine()
    {
        Console.WriteLine(new string('─', Console.WindowWidth - 1));
    }

    private static void ClearLine()
    {
        Console.Write("\x1b[K");
    }

    private static void ClearBlock(int linesBelowSeparator)
    {
        int startTop = Console.CursorTop;
        for (int i = 0; i <= linesBelowSeparator; i++)
        {
            Console.SetCursorPosition(0, startTop + i);
            ClearLine();
        }
        Console.SetCursorPosition(0, startTop);
    }
}
