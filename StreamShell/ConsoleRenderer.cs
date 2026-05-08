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

    /// <summary>Like <see cref="ClearInputBlock"/> but clears enough lines to cover
    /// both the old and new block heights, preventing stale content when a
    /// line-count change makes the re-rendered block taller.</summary>
    public void ClearInputBlockForReRender(string? oldInput, string newInput)
    {
        if (oldInput is null)
            return;

        int oldOffset = GetBlockOffset(oldInput);
        int newOffset = GetBlockOffset(newInput);
        int clearOffset = Math.Max(oldOffset, newOffset);
        int bufferHeight = Console.BufferHeight;

        int newTop = Console.CursorTop - oldOffset;
        Console.CursorTop = Math.Max(0, Math.Min(newTop, bufferHeight - 1));
        ClearBlock(clearOffset);
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
        int width = Math.Max(1, Math.Min(effectiveMargin, Console.WindowWidth));

        var segments = input.Split('\n');
        bool isFirstOverallLine = true;
        int charOffset = 0;

        for (int segIdx = 0; segIdx < segments.Length; segIdx++)
        {
            string segment = segments[segIdx];
            var wrappedLines = WrapSegment(segment, width, segIdx == 0, segIdx == segments.Length - 1, isFirstOverallLine);

            for (int lineIdx = 0; lineIdx < wrappedLines.Count; lineIdx++)
            {
                string lineText = wrappedLines[lineIdx];

                string lineMarkup = BuildLineMarkup(
                    input, charOffset, lineText,
                    cursorPosition, hasSelection, selectionStart, selectionLength);

                Console.CursorLeft = 0;

                if (isFirstOverallLine && segments.Length == 1)
                    AnsiConsole.Markup($"[blue]> [/]{lineMarkup}");
                else if (isFirstOverallLine)
                    AnsiConsole.Markup($"[blue]> [/]{lineMarkup}");
                else
                    AnsiConsole.Markup($"  {lineMarkup}");

                if (!(segIdx == segments.Length - 1 && lineIdx == wrappedLines.Count - 1))
                    Console.WriteLine();

                charOffset += lineText.Length;
                isFirstOverallLine = false;
            }

            // Account for the \n character between segments
            charOffset++;
        }

        // Handle entirely empty input
        if (segments.Length == 1 && segments[0].Length == 0 && string.IsNullOrEmpty(input))
        {
            Console.CursorLeft = 0;
            string lineMarkup = BuildLineMarkup(
                input, 0, "",
                cursorPosition, hasSelection, selectionStart, selectionLength);
            AnsiConsole.Markup($"[blue]> [/]{lineMarkup}");
        }
    }

    /// <summary>
    /// Build the Spectre markup for one visual line, accounting for selection
    /// and cursor position.
    ///
    /// Cursor is displayed as a "virtual" inverted cell — the character at
    /// the cursor position gets [black on gray] markup. If the cursor is past
    /// the end of the input, a highlighted space is shown.
    ///
    /// Selection is rendered as [white on gray]...[/]. When selection is
    /// active the cursor is hidden.
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

        // Only show cursor when no selection is active
        int cursorCol = -1;
        if (!hasSelection)
        {
            bool cursorOnThisLine = cursorPosition >= lineStart && cursorPosition <= lineEnd;
            if (cursorOnThisLine)
                cursorCol = cursorPosition - lineStart;
        }

        // Determine selection range on this visual line
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

        var sb = new System.Text.StringBuilder();

        if (selectionOnThisLine)
        {
            // Render selection segments — no cursor when selection is active

            // Before selection
            if (selStartInLine > 0)
                sb.Append(Markup.Escape(lineText[..selStartInLine]));

            // Selected text
            {
                string selText = Markup.Escape(lineText[selStartInLine..selEndInLine]);
                sb.Append("[white on gray]");
                sb.Append(selText);
                sb.Append("[/]");
            }

            // After selection
            if (selEndInLine < lineText.Length)
                sb.Append(Markup.Escape(lineText[selEndInLine..]));
        }
        else
        {
            // No selection on this visual line — render full text
            // with cursor highlight if it falls on this line.
            // We work with raw lineText and escape each segment
            // individually to avoid index mismatches when '['
            // expands to multiple chars in its escaped form.
            AppendCursorHighlight(sb, lineText, ref cursorCol, /*segOffset*/0);
            cursorCol = -1;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Appends text to <paramref name="sb"/> with cursor highlight.
    /// Operates on raw <paramref name="rawText"/> and escapes each
    /// segment individually so that <see cref="Spectre.Console.Markup.Escape"/>
    /// expansion of '[' → "[[" doesn't misalign the cursor index.
    ///
    /// Cursor position is shown by wrapping a single character (or a
    /// space at end-of-text) in [black on gray]...[/].
    /// Sets <paramref name="cursorCol"/> to -1 when the cursor has been placed.
    /// </summary>
    private static void AppendCursorHighlight(
        System.Text.StringBuilder sb,
        string rawText,
        ref int cursorCol,
        int segmentOffset)
    {
        if (cursorCol < 0)
        {
            sb.Append(Markup.Escape(rawText));
            return;
        }

        int localCol = cursorCol - segmentOffset;
        if (localCol < 0 || localCol > rawText.Length)
        {
            sb.Append(Markup.Escape(rawText));
            return;
        }

        // Before cursor
        if (localCol > 0)
            sb.Append(Markup.Escape(rawText[..localCol]));

        if (localCol < rawText.Length)
        {
            // Cursor on a character — escape it then wrap in markup
            string escapedChar = Markup.Escape(rawText[localCol].ToString());
            sb.Append("[black on gray]");
            sb.Append(escapedChar);
            sb.Append("[/]");
            // After cursor
            if (localCol + 1 < rawText.Length)
                sb.Append(Markup.Escape(rawText[(localCol + 1)..]));
        }
        else
        {
            // Cursor past end of text → highlighted space placeholder
            sb.Append("[black on gray] [/]");
        }

        cursorCol = -1;
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
        bool anyLinesProduced = false;

        for (int segIndex = 0; segIndex < segments.Length; segIndex++)
        {
            string segment = segments[segIndex];
            bool isFirstSegment = segIndex == 0;
            bool isLastSegment = segIndex == segments.Length - 1;

            var wrapped = WrapSegment(segment, width, isFirstSegment, isLastSegment, !anyLinesProduced);
            lines.AddRange(wrapped);

            if (wrapped.Count > 0)
                anyLinesProduced = true;
        }

        // Ensure empty input always has at least one line
        if (lines.Count == 0)
            lines.Add("");

        return lines;
    }

    /// <summary>
    /// Wraps a single segment (no newline characters) into visual lines
    /// at the given terminal <paramref name="width"/>.
    /// </summary>
    private static List<string> WrapSegment(
        string segment,
        int width,
        bool isFirstSegment,
        bool isLastSegment,
        bool isFirstVisualLine)
    {
        var lines = new List<string>();

        // Short single segment that fits on one line
        if (isFirstSegment && isLastSegment && segment.Length + 4 < width)
        {
            lines.Add(segment);
            return lines;
        }

        int remaining = segment.Length;
        int pos = 0;

        while (remaining > 0)
        {
            int cap;
            if (isFirstSegment && isFirstVisualLine && lines.Count == 0)
            {
                // First visual line has "> " prefix
                cap = Math.Max(0, width - 4);
            }
            else if (isLastSegment && remaining <= width - 2)
            {
                cap = Math.Max(1, width - 2);
            }
            else
            {
                cap = Math.Max(1, width - 1);
            }

            int take = Math.Min(remaining, cap);
            lines.Add(segment.Substring(pos, take));
            pos += take;
            remaining -= take;
        }

        // Empty segment produces an empty visual line so the cursor
        // after a newline has somewhere to render (e.g. Shift+Enter).
        if (segment.Length == 0)
        {
            lines.Add("");
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
        int bufferHeight = Console.BufferHeight;

        for (int i = 0; i <= linesBelowSeparator; i++)
        {
            int top = startTop + i;
            if (top < 0 || top >= bufferHeight)
                continue;
            Console.SetCursorPosition(0, top);
            ClearLine();
        }

        startTop = Math.Max(0, Math.Min(startTop, bufferHeight - 1));
        Console.SetCursorPosition(0, startTop);
    }
}
