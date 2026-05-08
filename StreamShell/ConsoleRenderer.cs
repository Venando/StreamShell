namespace StreamShell;

using Spectre.Console;

/// <summary>
/// Renders the StreamShell UI to the console using Spectre.Console markup.
/// Handles input block rendering (separator, input lines, hints) and
/// message display, with cursor and selection highlighting.
/// </summary>
internal class ConsoleRenderer : IRenderer
{
    private readonly StreamShellSettings _settings;

    /// <summary>Creates a renderer with default settings.</summary>
    public ConsoleRenderer() : this(new StreamShellSettings()) { }

    /// <summary>Creates a renderer with the specified settings.</summary>
    public ConsoleRenderer(StreamShellSettings settings)
    {
        _settings = settings;
        RightMargin = settings.GetEffectiveRightMargin();
    }

    public int RightMargin { get; set; } = Console.WindowWidth;

    private const int BlockOffsetBase = 7; // Known off-by-2 for some cases — see PROJECT-INDEX

    /// <summary>Total vertical space taken by the input block.</summary>
    public int GetBlockOffset(string input)
    {
        // BlockSeparator + BlankAfterInput + HintsSeparator + HintsLines = 9 estimated
        return BlockOffsetBase + GetInputLineCount(input);
    }

    /// <summary>Number of visual lines the input occupies.</summary>
    public int GetInputLineCount(string input) => LineWrappingService.GetInputLines(
        input, RightMargin, _settings.PrefixMargin, _settings.WrappingRightMargin).Count;

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
    internal void RenderInputLine(
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
            var wrappedLines = LineWrappingService.WrapSegment(segment, width, segIdx == 0,
                segIdx == segments.Length - 1, isFirstOverallLine,
                _settings.PrefixMargin, _settings.WrappingRightMargin);

            for (int lineIdx = 0; lineIdx < wrappedLines.Count; lineIdx++)
            {
                string lineText = wrappedLines[lineIdx];
                string lineMarkup = BuildLineMarkup(
                    input, charOffset, lineText,
                    cursorPosition, hasSelection, selectionStart, selectionLength);

                Console.CursorLeft = 0;

                string prefix = isFirstOverallLine
                    ? _settings.InputPrefix
                    : _settings.ContinuationPrefix;
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
            AnsiConsole.Markup($"{_settings.InputPrefix}{lineMarkup}");
        }
    }

    // ── Selection & Cursor Markup Building ───────────────────────────
    /// <summary>
    /// Builds Spectre markup for one visual line, rendering cursor and
    /// selection using the configured markup styles from settings.
    /// When selection is active, the cursor is hidden.
    /// </summary>
    private string BuildLineMarkup(
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
            sb.Append("[").Append(_settings.SelectionMarkup).Append("]")
              .Append(selText).Append("[/]");

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
    /// Appends text with cursor highlight using the configured cursor markup style.
    /// The character at <paramref name="cursorCol"/> gets the style.
    /// Past-end cursor shows a highlighted space.
    /// Sets <paramref name="cursorCol"/> to -1 after placing.
    /// </summary>
    private void AppendCursorHighlight(
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

        string cursorStyle = _settings.CursorMarkup;

        if (localCol < rawText.Length)
        {
            string escapedChar = Markup.Escape(rawText[localCol].ToString());
            sb.Append("[").Append(cursorStyle).Append("]")
              .Append(escapedChar).Append("[/]");

            if (localCol + 1 < rawText.Length)
                sb.Append(Markup.Escape(rawText[(localCol + 1)..]));
        }
        else
        {
            sb.Append("[").Append(cursorStyle).Append("] [/]"); // Past-end placeholder
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

    /// <summary>Gets the wrapped visual lines for the input at the given margin.</summary>
    internal static List<string> GetInputLines(string input, int margin,
        int prefixMargin = 2, int rightMargin = 4)
        => LineWrappingService.GetInputLines(input, margin, prefixMargin, rightMargin);

    /// <summary>
    /// Converts a character position in the raw input string to
    /// (visual line index, visual column) at the given <paramref name="margin"/>.
    /// </summary>
    public static (int line, int column) GetCursorVisualPosition(
        string input, int cursorPosition, int margin,
        int prefixMargin = 2, int rightMargin = 4)
        => LineWrappingService.GetCursorVisualPosition(input, cursorPosition, margin, prefixMargin, rightMargin);

    /// <summary>
    /// Gets both visual line text and the character offset of each line
    /// in the raw input. Offsets account for newline characters between segments.
    /// Needed for up/down cursor navigation.
    /// </summary>
    public static (List<string> lines, List<int> offsets) GetVisualLineData(string input, int margin,
        int prefixMargin = 2, int rightMargin = 4)
        => LineWrappingService.GetVisualLineData(input, margin, prefixMargin, rightMargin);

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
