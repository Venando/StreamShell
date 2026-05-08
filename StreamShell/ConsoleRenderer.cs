namespace StreamShell;

using Spectre.Console;

/// <summary>
/// Renders the StreamShell UI to the console using Spectre.Console markup.
/// Handles input block rendering (separator, input lines, hints) and
/// message display, with cursor and selection highlighting.
/// </summary>
internal class ConsoleRenderer : IRenderer
{
    public int RightMargin { get; set; } = Console.WindowWidth;

    /// <summary>Total vertical space taken by the input block.</summary>
    public int GetBlockOffset(string input)
    {
        // Fixed parts = BlockSeparator + BlankAfterInput + HintsSeparator + HintsLines = 9
        // Known offset: 7 + inputLineCount (off by 2 — investigation documented in PROJECT-INDEX)
        return 7 + GetInputLineCount(input);
    }

    /// <summary>Number of visual lines the input occupies.</summary>
    public int GetInputLineCount(string input) => GetInputLines(input, RightMargin).Count;

    // ── Message Display ──────────────────────────────────────────────
    public void RenderMessage(string markup)
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

    // ── Block Clearing ───────────────────────────────────────────────
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
        int bufferHeight = Console.BufferHeight;
        int newTop = Console.CursorTop - blockOffset;
        Console.CursorTop = Math.Max(0, Math.Min(newTop, bufferHeight - 1));
        ClearBlock(blockOffset);
    }

    /// <summary>Clears enough lines to cover both old and new block heights,
    /// preventing stale content when the re-rendered block is taller.</summary>
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

    // ── Full Block Render ────────────────────────────────────────────
    public void RenderInputBlock(
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

    // ── Overwrite-Only Render ────────────────────────────────────────
    public void OverwriteInputBlock(
        string input,
        IReadOnlyList<string> hints,
        int blockOffset,
        int cursorPosition,
        bool hasSelection,
        int selectionStart,
        int selectionLength,
        int margin)
    {
        int bufferHeight = Console.BufferHeight;
        int newTop = Console.CursorTop - (blockOffset - 1);
        Console.CursorTop = Math.Max(0, Math.Min(newTop, bufferHeight - 1));

        RenderInputLine(input, cursorPosition, hasSelection, selectionStart, selectionLength, margin);
        Console.Write("\x1b[K");
        Console.WriteLine();
        RenderHintsBlock(hints);
    }

    // ── Input Line Rendering ─────────────────────────────────────────
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
            var wrappedLines = WrapSegment(segment, width, segIdx == 0,
                segIdx == segments.Length - 1, isFirstOverallLine);

            for (int lineIdx = 0; lineIdx < wrappedLines.Count; lineIdx++)
            {
                string lineText = wrappedLines[lineIdx];
                string lineMarkup = BuildLineMarkup(
                    input, charOffset, lineText,
                    cursorPosition, hasSelection, selectionStart, selectionLength);

                Console.CursorLeft = 0;
                string prefix = isFirstOverallLine ? "[blue]> [/]" : "  ";
                AnsiConsole.Markup(prefix + lineMarkup);

                if (!(segIdx == segments.Length - 1 && lineIdx == wrappedLines.Count - 1))
                    Console.WriteLine();

                charOffset += lineText.Length;
                isFirstOverallLine = false;
            }

            charOffset++; // Account for the \n between segments
        }

        // Handle entirely empty input (single empty segment)
        if (segments.Length == 1 && segments[0].Length == 0 && string.IsNullOrEmpty(input))
        {
            Console.CursorLeft = 0;
            string lineMarkup = BuildLineMarkup(
                input, 0, "",
                cursorPosition, hasSelection, selectionStart, selectionLength);
            AnsiConsole.Markup($"[blue]> [/]{lineMarkup}");
        }
    }

    // ── Selection & Cursor Markup Building ───────────────────────────
    /// <summary>
    /// Builds Spectre markup for one visual line, rendering cursor as
    /// [black on gray] and selection as [white on gray]. When selection
    /// is active, the cursor is hidden.
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

        var sb = new System.Text.StringBuilder();

        if (selectionOnThisLine)
        {
            // Before selection
            if (selStartInLine > 0)
                sb.Append(Markup.Escape(lineText[..selStartInLine]));

            // Selected text
            string selText = Markup.Escape(lineText[selStartInLine..selEndInLine]);
            sb.Append("[white on gray]").Append(selText).Append("[/]");

            // After selection
            if (selEndInLine < lineText.Length)
                sb.Append(Markup.Escape(lineText[selEndInLine..]));
        }
        else
        {
            AppendCursorHighlight(sb, lineText, ref cursorCol, 0);
            cursorCol = -1;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Appends text with cursor highlight. The character at <paramref name="cursorCol"/>
    /// gets [black on gray] markup. Past-end cursor shows a highlighted space.
    /// Sets <paramref name="cursorCol"/> to -1 after placing.
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
            string escapedChar = Markup.Escape(rawText[localCol].ToString());
            sb.Append("[black on gray]").Append(escapedChar).Append("[/]");

            if (localCol + 1 < rawText.Length)
                sb.Append(Markup.Escape(rawText[(localCol + 1)..]));
        }
        else
        {
            sb.Append("[black on gray] [/]"); // Past-end placeholder
        }

        cursorCol = -1;
    }

    // ── Hints Block ───────────────────────────────────────────────────
    private static void RenderHintsBlock(IReadOnlyList<string> hints)
    {
        if (hints.Any(h => !string.IsNullOrEmpty(h)))
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
        var (lines, _) = GetVisualLineData(input, margin);
        return lines;
    }

    /// <summary>
    /// Wraps a single segment (no newline characters) into visual lines
    /// at the given terminal <paramref name="width"/>.
    /// </summary>
    internal static List<string> WrapSegment(
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
            // First visual line has "> " prefix (2 chars), continuation gets "  " (2 chars)
            // Both use cap = width - 4 for consistent right margin
            int cap = Math.Max(isFirstSegment && isFirstVisualLine && lines.Count == 0 ? 0 : 1, width - 4);
            int take = Math.Min(remaining, cap);
            lines.Add(segment.Substring(pos, take));
            pos += take;
            remaining -= take;
        }

        // Empty segment produces an empty visual line so the cursor
        // after a newline has somewhere to render (e.g. Shift+Enter).
        if (segment.Length == 0)
            lines.Add("");

        return lines;
    }

    /// <summary>
    /// Converts a character position in the raw input string to
    /// (visual line index, visual column) at the given <paramref name="margin"/>.
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

        if (visualLines.Count == 0)
            return (0, 0);

        return (visualLines.Count - 1, visualLines[^1].Length);
    }

    /// <summary>
    /// Gets both visual line text and the character offset of each line
    /// in the raw input. Offsets account for newline characters between segments.
    /// Needed for up/down cursor navigation.
    /// </summary>
    public static (List<string> lines, List<int> offsets) GetVisualLineData(string input, int margin)
    {
        int width = Math.Max(1, Math.Min(margin, Console.WindowWidth));
        var lines = new List<string>();
        var offsets = new List<int>();

        if (string.IsNullOrEmpty(input))
        {
            lines.Add("");
            offsets.Add(0);
            return (lines, offsets);
        }

        var segments = input.Split('\n');
        bool anyLinesProduced = false;
        int charOffset = 0;

        for (int segIdx = 0; segIdx < segments.Length; segIdx++)
        {
            string segment = segments[segIdx];
            bool isFirstSegment = segIdx == 0;
            bool isLastSegment = segIdx == segments.Length - 1;

            var wrapped = WrapSegment(segment, width, isFirstSegment, isLastSegment, !anyLinesProduced);

            for (int lineIdx = 0; lineIdx < wrapped.Count; lineIdx++)
            {
                offsets.Add(charOffset);
                lines.Add(wrapped[lineIdx]);
                charOffset += wrapped[lineIdx].Length;
            }

            if (wrapped.Count > 0)
                anyLinesProduced = true;

            charOffset++; // Account for \n between segments
        }

        if (lines.Count == 0)
        {
            lines.Add("");
            offsets.Add(0);
        }

        return (lines, offsets);
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

    private static void RenderSeparatorLine()
    {
        Console.WriteLine(new string('─', Console.WindowWidth - 1));
    }

    private static void ClearLine() => Console.Write("\x1b[K");

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
