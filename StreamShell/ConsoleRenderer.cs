namespace StreamShell;

using Spectre.Console;

/// <summary>
/// Renders the StreamShell UI to the console using Spectre.Console markup.
/// Handles input block rendering (separator, input lines, hints) and
/// message display, with cursor and selection highlighting.
/// </summary>
internal class ConsoleRenderer : IRenderer
{
    private readonly ITerminal _terminal;
    private readonly StreamShellSettings _settings;

    /// <summary>Creates a renderer with default settings and the real terminal.</summary>
    public ConsoleRenderer() : this(new StreamShellSettings(), new SystemTerminal()) { }

    /// <summary>Creates a renderer with the specified settings and the real terminal.</summary>
    public ConsoleRenderer(StreamShellSettings settings) : this(settings, new SystemTerminal()) { }

    /// <summary>Creates a renderer with explicit settings and terminal (for testing).</summary>
    internal ConsoleRenderer(StreamShellSettings settings, ITerminal terminal)
    {
        _settings = settings;
        _terminal = terminal;
        _markupBuilder = new MarkupBuilder(settings);
        RightMargin = terminal.WindowWidth;
    }

    private readonly MarkupBuilder _markupBuilder;

    public int RightMargin { get; set; }

    // Line count of the current bottom panel (set by host). Defaults to CommandPalette's size.
    private int _panelLineCount = CommandPalette.MaxHeight;

    /// <summary>Top separator configuration (between message feed and input block).</summary>
    public SeparatorConfig TopSeparator { get; set; } = SeparatorConfig.Default;

    /// <summary>Bottom separator configuration (between input line and hints block).</summary>
    public SeparatorConfig BottomSeparator { get; set; } = SeparatorConfig.Default;

    /// <summary>
    /// Placeholder strings from current attachments, delegated to the MarkupBuilder
    /// for placeholder-aware markup escaping.
    /// </summary>
    public IReadOnlyList<string>? PlaceholderStrings
    {
        get => _markupBuilder.PlaceholderStrings;
        set => _markupBuilder.PlaceholderStrings = value;
    }

    /// <summary>Updates the panel line count used for block offset calculation.</summary>
    public void SetPanelLineCount(int count) => _panelLineCount = count;

    /// <summary>Total vertical space taken by the input block using the current panel line count.</summary>
    public int GetBlockOffset(string input)
    {
        // 1 (separator) + BlankAfterInput + HintsSep + _panelLineCount = 1 + 1 + 1 + _panelLineCount = 3 + _panelLineCount
        // Plus input line count
        return (1 + _panelLineCount) + GetInputLineCount(input);
    }

    /// <summary>Total vertical space taken by the input block with a given panel line count.</summary>
    private int GetBlockOffset(string input, int panelLineCount)
    {
        return (1 + panelLineCount) + LineWrappingService.GetInputLineCount(
            input, _terminal.WindowWidth, 2, 4);
    }

    /// <summary>Number of visual lines the input occupies.</summary>
    public int GetInputLineCount(string input) => LineWrappingService.GetInputLineCount(
        input, RightMargin, _settings.PrefixMargin, _settings.WrappingRightMargin);

    // ── Message History ──────────────────────────────────────────────
    private readonly List<string> _messageHistory = new();
    private const int MessageHistoryCapacity = 50;

    /// <summary>Maximum number of messages to replay when the block grows. Default: 10.</summary>
    public int MessageBufferReplayCount { get; set; } = 10;

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

        _messageHistory.Add(markup);
        if (_messageHistory.Count > MessageHistoryCapacity)
            _messageHistory.RemoveRange(0, _messageHistory.Count - MessageHistoryCapacity);
    }

    // ── Block Clearing ───────────────────────────────────────────────
    public void ClearInputLine()
    {
        _terminal.CursorLeft = 0;
        ClearLine();
    }

    public void ClearInputBlock(string? lastInput)
    {
        if (lastInput is null)
            return;

        int blockOffset = GetBlockOffset(lastInput);
        int bufferHeight = _terminal.BufferHeight;
        int newTop = _terminal.CursorTop - blockOffset;
        _terminal.CursorTop = Math.Max(0, Math.Min(newTop, bufferHeight - 1));
        ClearBlock(blockOffset);
    }

    /// <summary>Clears enough lines to cover both old and new block heights,
    /// preventing stale content when the re-rendered block is taller.</summary>
    public void ClearInputBlockForReRender(string? oldInput, string newInput, int oldPanelLineCount)
    {
        if (oldInput is null)
            return;

        int oldOffset = GetBlockOffset(oldInput, oldPanelLineCount);
        int newOffset = GetBlockOffset(newInput);
        int clearOffset = Math.Max(oldOffset, newOffset);
        int bufferHeight = _terminal.BufferHeight;

        int newTop = _terminal.CursorTop - oldOffset;
        _terminal.CursorTop = Math.Max(0, Math.Min(newTop, bufferHeight - 1));
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
        RenderTopSeparator();
        RenderInputLine(input, cursorPosition, hasSelection, selectionStart, selectionLength, margin);
        _terminal.WriteLine();
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
        int bufferHeight = _terminal.BufferHeight;
        int newTop = _terminal.CursorTop - (blockOffset - 1);
        _terminal.CursorTop = Math.Max(0, Math.Min(newTop, bufferHeight - 1));

        RenderInputLine(input, cursorPosition, hasSelection, selectionStart, selectionLength, margin);
        _terminal.Write("\x1b[K");
        _terminal.WriteLine();
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
        int width = Math.Max(1, Math.Min(effectiveMargin, _terminal.WindowWidth));

        // Handle empty input early: render a single line with just the prefix
        if (string.IsNullOrEmpty(input))
        {
            _terminal.CursorLeft = 0;
            _markupBuilder.Reset();
            string lineMarkup = _markupBuilder.BuildLineMarkup(
                input, 0, "",
                cursorPosition, hasSelection, selectionStart, selectionLength);
            AnsiConsole.Markup(_settings.InputPrefix);
            AnsiConsole.Markup(lineMarkup);
            return;
        }

        // Enumerate newline-delimited segments using spans to avoid
        // allocating a string array via Split('\n') on every render tick.
        ReadOnlySpan<char> inputSpan = input.AsSpan();
        bool isFirstOverallLine = true;
        int charOffset = 0;
        int segIdx = 0;
        int segStart = 0;

        while (segStart <= inputSpan.Length)
        {
            // Find the next newline (or end of string for the last segment)
            int nl = segStart < inputSpan.Length
                ? inputSpan[segStart..].IndexOf('\n')
                : -1;
            int segEnd = nl >= 0 ? segStart + nl : inputSpan.Length;
            int nextSegStart = nl >= 0 ? segEnd + 1 : inputSpan.Length + 1;

            bool isLastSegment = nextSegStart > inputSpan.Length;
            ReadOnlySpan<char> segment = inputSpan[segStart..segEnd];

            var wrappedLines = LineWrappingService.WrapSegment(
                segment, width, segIdx == 0,
                isLastSegment, isFirstOverallLine,
                _settings.PrefixMargin, _settings.WrappingRightMargin);

            for (int lineIdx = 0; lineIdx < wrappedLines.Count; lineIdx++)
            {
                RenderSingleVisualLine(
                    input, charOffset, wrappedLines[lineIdx],
                    cursorPosition, hasSelection, selectionStart, selectionLength,
                    isFirstOverallLine);

                // Don't write a newline after the very last visual line
                if (!(isLastSegment && lineIdx == wrappedLines.Count - 1))
                    _terminal.WriteLine();

                charOffset += wrappedLines[lineIdx].Length;
                isFirstOverallLine = false;
            }

            charOffset++; // Account for the \n between segments
            segStart = nextSegStart;
            segIdx++;
        }
    }

    /// <summary>
    /// Renders a single visual line of the input block: clears to column 0,
    /// writes the prefix (input or continuation), and the markup for this line.
    /// </summary>
    private void RenderSingleVisualLine(
        string input,
        int charOffset,
        string lineText,
        int cursorPosition,
        bool hasSelection,
        int selectionStart,
        int selectionLength,
        bool isFirstOverallLine)
    {
        _terminal.CursorLeft = 0;

        string prefix = isFirstOverallLine
            ? _settings.InputPrefix
            : _settings.ContinuationPrefix;

        _markupBuilder.Reset();
        string lineMarkup = _markupBuilder.BuildLineMarkup(
            input, charOffset, lineText,
            cursorPosition, hasSelection, selectionStart, selectionLength);

        // Two separate Markup calls avoid the prefix + lineMarkup string
        // concatenation (one allocation per visual line per render tick).
        AnsiConsole.Markup(prefix);
        AnsiConsole.Markup(lineMarkup);
    }



    // ── Hints Block ───────────────────────────────────────────────────
    private void RenderHintsBlock(IReadOnlyList<string> hints)
    {
        bool hasNonEmptyHint = false;
        for (int h = 0; h < hints.Count; h++)
        {
            if (!string.IsNullOrEmpty(hints[h]))
            {
                hasNonEmptyHint = true;
                break;
            }
        }

        if (hasNonEmptyHint)
            RenderBottomSeparator();
        else
        {
            // Clear the old bottom separator line first, then advance
            _terminal.CursorLeft = 0;
            ClearLine();
            _terminal.WriteLine();
        }

        int maxWidth = _terminal.WindowWidth - 1;
        for (int i = 0; i < hints.Count; i++)
        {
            _terminal.CursorLeft = 0;
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
            if (i < hints.Count - 1)
                _terminal.WriteLine();
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

    /// <summary>Renders the top separator (between message feed and input block).</summary>
    private void RenderTopSeparator()
    {
        int width = _terminal.WindowWidth - 1;
        AnsiConsole.MarkupLine(BuildSeparatorLine(TopSeparator, width));
    }

    /// <summary>Renders the bottom separator (between input line and hints block).</summary>
    private void RenderBottomSeparator()
    {
        int width = _terminal.WindowWidth - 1;
        AnsiConsole.MarkupLine(BuildSeparatorLine(BottomSeparator, width));
    }

    /// <summary>Builds the separator string from the given config and available width.</summary>
    private static string BuildSeparatorLine(SeparatorConfig config, int width)
    {
        string left = config.LeftText ?? string.Empty;
        string right = config.RightText ?? string.Empty;
        char fill = config.RepeatedChar;

        // Measure display length (strip markup)
        int leftLen = string.IsNullOrEmpty(left) ? 0 : Markup.Remove(left).Length;
        int rightLen = string.IsNullOrEmpty(right) ? 0 : Markup.Remove(right).Length;

        int fillCount = width - leftLen - rightLen;
        if (fillCount < 0) fillCount = 0;

        if (fillCount == 0)
        {
            return string.IsNullOrEmpty(left) ? string.Empty
                : string.IsNullOrEmpty(right) ? left : left + right;
        }

        // Build with StringBuilder::Append(char, int) to avoid
        // allocating a large repeated-char string on every render.
        var sb = new System.Text.StringBuilder(capacity: left.Length + fillCount + right.Length);
        sb.Append(left);

        string? fillMarkup = config.RepeatedCharMarkup;
        if (string.IsNullOrEmpty(fillMarkup))
        {
            sb.Append(fill, fillCount);
        }
        else
        {
            sb.Append('[').Append(fillMarkup).Append(']');
            sb.Append(fill, fillCount);
            sb.Append("[/]");
        }

        sb.Append(right);
        return sb.ToString();
    }

    /// <summary>
    /// After the input block was re-rendered and the block shrunk
    /// (<paramref name="newBlockOffset"/> &lt; <paramref name="oldBlockOffset"/>),
    /// clears excess lines below the new (smaller) block that were
    /// cleared but not re-filled. The growing case is handled naturally
    /// since the larger block fills all cleared lines.
    /// </summary>
    public void HandleBlockHeightChange(int oldBlockOffset, int newBlockOffset)
    {
        int delta = newBlockOffset - oldBlockOffset;
        if (delta >= 0)
            return;

        ClearLinesBelow(-delta);
    }

    /// <summary>Clears <paramref name="count"/> lines below the new block that were
    /// part of the old block but not re-filled (block shrank). These are uncleared
    /// gaps between the new block bottom and the old clear area end.</summary>
    private void ClearLinesBelow(int count)
    {
        if (count <= 0)
            return;

        // Save cursor position first — SetCursorPosition + ClearLine moves the cursor
        int savedTop = _terminal.CursorTop;
        int bufferHeight = _terminal.BufferHeight;

        for (int i = 1; i <= count; i++)
        {
            int top = savedTop + i;
            if (top >= bufferHeight)
                break;
            _terminal.SetCursorPosition(0, top);
            ClearLine();
        }

        // Restore cursor to saved position (block bottom)
        _terminal.CursorTop = Math.Max(0, Math.Min(savedTop, bufferHeight - 1));
    }

    private void ClearLine() => _terminal.Write("\x1b[K");

    private void ClearBlock(int linesBelowSeparator)
    {
        int startTop = _terminal.CursorTop;
        int bufferHeight = _terminal.BufferHeight;

        for (int i = 0; i <= linesBelowSeparator; i++)
        {
            int top = startTop + i;
            if (top < 0 || top >= bufferHeight)
                continue;
            _terminal.SetCursorPosition(0, top);
            ClearLine();
        }

        startTop = Math.Max(0, Math.Min(startTop, bufferHeight - 1));
        _terminal.SetCursorPosition(0, startTop);
    }
}
