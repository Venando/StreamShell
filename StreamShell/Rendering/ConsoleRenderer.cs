namespace StreamShell;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
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

    /// <summary>
    /// Reusable StringBuilder for separator line rendering.
    /// Pooled on the renderer to avoid per-render GC pressure.
    /// </summary>
    private readonly System.Text.StringBuilder _separatorSb = new(capacity: 512);

    // Cached separator lines — invalidated when config or width changes.
    private string? _cachedTopSepLine;
    private string? _cachedBottomSepLine;
    private SeparatorConfig? _cachedTopSepConfig;
    private SeparatorConfig? _cachedBottomSepConfig;
    private int _cachedSepWidth;

    /// <summary>
    /// Reserve one column from terminal width for separator rendering.
    /// Prevents wrapping caused by the cursor position at the rightmost column.
    /// </summary>
    private const int TerminalWidthMargin = 0;

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
    private int _panelLineCount = CommandPalette.DefaultMaxHeight;

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

    /// <summary>When false, suppresses the bottom separator between input and hints.</summary>
    public bool ShowBottomSeparator { get; set; } = true;

    /// <summary>When false, the input field and top separator are not rendered.
    /// Only the panel's hint lines display. Default: true.</summary>
    public bool ShowUserField { get; set; } = true;

    /// <summary>Updates the panel line count used for block offset calculation.</summary>
    public void SetPanelLineCount(int count) => _panelLineCount = count;

    /// <summary>Total vertical space taken by the input block using the current panel line count.
    /// Always includes separator + input lines even when <see cref="ShowUserField"/> is false —
    /// the rendering methods emit empty lines instead so the block height stays stable.</summary>
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
        input, RightMargin, _settings.PrefixMargin, _settings.WrappingRightMargin, _settings.WordWrap);

    // ── Message History ──────────────────────────────────────────────
    private readonly List<string> _messageHistory = new();
    private const int MessageHistoryCapacity = 50;

    /// <summary>Maximum number of messages to replay when the block grows. Default: 10.</summary>
    public int MessageBufferReplayCount { get; set; } = 10;

    /// <summary>
    /// Re-emits the last <paramref name="count"/> messages from the message history.
    /// Used after console width decreases so messages are re-wrapped correctly.
    /// </summary>
    public void ReplayMessages(int count)
    {
        if (_messageHistory.Count == 0)
            return;

        int startIndex = Math.Max(0, _messageHistory.Count - count);
        for (int i = startIndex; i < _messageHistory.Count; i++)
        {
            try
            {
                AnsiConsole.MarkupLine(_messageHistory[i]);
            }
            catch (InvalidOperationException)
            {
                AnsiConsole.MarkupLine(Markup.Escape(_messageHistory[i]));
            }
        }
    }

    // ── Message Display ──────────────────────────────────────────────
    public void RenderMessage(string markup)
    {
        try
        {
            // Render markup without automatic newline, then clear to
            // end of line to prevent separator/ghost characters from
            // leaking into the message area (critical for scroll-region
            // mode where old input block content can bleed through).
            AnsiConsole.Markup(markup);
            _terminal.Write("\x1b[K");
            _terminal.WriteLine();
        }
        catch (InvalidOperationException)
        {
            AnsiConsole.Markup(Markup.Escape(markup));
            _terminal.Write("\x1b[K");
            _terminal.WriteLine();
        }

        _messageHistory.Add(markup);
        if (_messageHistory.Count > MessageHistoryCapacity)
            _messageHistory.RemoveRange(0, _messageHistory.Count - MessageHistoryCapacity);
    }

    public void RetrieveMessagesFromHistory(int count, Action<Span<string>> callback)
    {
        Span<string> totalSpan = CollectionsMarshal.AsSpan(_messageHistory);
        count = Math.Min(_messageHistory.Count, count);
        callback?.Invoke(totalSpan[^count..]);
        _messageHistory.RemoveRange(_messageHistory.Count - count, count);
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
        if (ShowUserField)
        {
            RenderTopSeparator();
            RenderInputLine(input, cursorPosition, hasSelection, selectionStart, selectionLength, margin);
            _terminal.WriteLine();
        }
        else
        {
            // Emit empty lines in place of separator + input to keep block height stable
            _terminal.WriteLine();
            int inputLines = GetInputLineCount(input);
            for (int i = 0; i < inputLines; i++)
                _terminal.WriteLine();
        }
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

        if (ShowUserField)
        {
            RenderInputLine(input, cursorPosition, hasSelection, selectionStart, selectionLength, margin);
            _terminal.Write("\x1b[K");
            _terminal.WriteLine();
        }
        else
        {
            // Emit empty lines in place of input to keep block height stable
            int inputLines = GetInputLineCount(input);
            for (int i = 0; i < inputLines; i++)
            {
                _terminal.Write("\x1b[K");
                _terminal.WriteLine();
            }
        }
        RenderHintsBlock(hints);
    }

    // ── Full-Block Overwrite (no clear-first — eliminates flicker) ────
    /// <summary>
    /// Overwrites the entire input block in-place without clearing first.
    /// Positions the cursor at the top of the old block then renders the
    /// full block (top separator, input lines, hints).
    /// Each visual line has \x1b[K appended to clear stale content, so no
    /// separate clear-before-render step is needed — this eliminates the
    /// visible flicker that occurred when rendering 2+ lines.
    /// </summary>
    public void OverwriteFullBlock(
        string input,
        IReadOnlyList<string> hints,
        int oldBlockOffset,
        int cursorPosition,
        bool hasSelection,
        int selectionStart,
        int selectionLength,
        int margin)
    {
        int bufferHeight = _terminal.BufferHeight;
        int newTop = _terminal.CursorTop - oldBlockOffset;
        _terminal.CursorLeft = 0;
        _terminal.CursorTop = Math.Max(0, Math.Min(newTop, bufferHeight - 1));

        if (ShowUserField)
        {
            RenderTopSeparator();
            RenderInputLine(input, cursorPosition, hasSelection, selectionStart, selectionLength, margin);
            _terminal.Write("\x1b[K");
            _terminal.WriteLine();
        }
        else
        {
            // Emit empty lines in place of separator + input to keep block height stable
            _terminal.Write("\x1b[K");
            _terminal.WriteLine();
            int inputLines = GetInputLineCount(input);
            for (int i = 0; i < inputLines; i++)
            {
                _terminal.Write("\x1b[K");
                _terminal.WriteLine();
            }
        }
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
            string lineMarkup = _markupBuilder.BuildLineMarkup(
                input, 0, "",
                cursorPosition, hasSelection, selectionStart, selectionLength);
            AnsiConsole.Markup(_settings.InputPrefix);
            AnsiConsole.Markup(lineMarkup);
            _terminal.Write("\x1b[K");
            return;
        }

        RenderInputLines(input, cursorPosition, hasSelection, selectionStart, selectionLength,
            width);
    }

    /// <summary>
    /// Enumerates newline-delimited segments using spans and renders each
    /// segment's wrapped visual lines. Extracted from <see cref="RenderInputLine"/>
    /// to keep segment iteration separate from single-line prep (SRP).
    /// Inlines the per-segment wrapping to avoid per-line substring allocation
    /// (the span slices reference the original input string directly).
    /// </summary>
    private void RenderInputLines(
        string input,
        int cursorPosition,
        bool hasSelection,
        int selectionStart,
        int selectionLength,
        int width)
    {
        ReadOnlySpan<char> inputSpan = input.AsSpan();
        bool isFirstOverallLine = true;
        int charOffset = 0;
        int segIdx = 0;
        int segStart = 0;
        int totalMargin = _settings.PrefixMargin + _settings.WrappingRightMargin;
        bool wordWrap = _settings.WordWrap;

        while (segStart <= inputSpan.Length)
        {
            int nl = segStart < inputSpan.Length
                ? inputSpan[segStart..].IndexOf('\n')
                : -1;
            int segEnd = nl >= 0 ? segStart + nl : inputSpan.Length;
            int nextSegStart = nl >= 0 ? segEnd + 1 : inputSpan.Length + 1;

            bool isLastSegment = nextSegStart > inputSpan.Length;
            bool isFirstSegment = segIdx == 0;
            ReadOnlySpan<char> segment = inputSpan[segStart..segEnd];

            int remaining = segment.Length;
            int pos = 0;
            int lineIdx = 0;
            while (remaining > 0)
            {
                int takeCap = Math.Max(
                    isFirstSegment && isFirstOverallLine && lineIdx == 0 ? 0 : 1,
                    width - totalMargin);
                int skipAfter = 0;
                int take = LineWrappingService.FindWordBreak(segment, pos, takeCap, wordWrap, out skipAfter);

                ReadOnlySpan<char> visualLine = segment.Slice(pos, take);

                RenderSingleVisualLine(
                    input, charOffset, visualLine,
                    cursorPosition, hasSelection, selectionStart, selectionLength,
                    isFirstOverallLine);

                if (!(isLastSegment && remaining == take + skipAfter))
                    _terminal.WriteLine();

                charOffset += take + skipAfter;
                remaining -= take + skipAfter;
                pos += take + skipAfter;
                lineIdx++;
                isFirstOverallLine = false;
            }

            if (segment.Length == 0)
            {
                RenderSingleVisualLine(
                    input, charOffset, ReadOnlySpan<char>.Empty,
                    cursorPosition, hasSelection, selectionStart, selectionLength,
                    isFirstOverallLine);

                if (!isLastSegment)
                    _terminal.WriteLine();

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
    /// Accepts <see cref="ReadOnlySpan{T}"/> for <paramref name="lineText"/>
    /// to avoid substring allocation on the hot render path.
    /// </summary>
    private void RenderSingleVisualLine(
        string input,
        int charOffset,
        ReadOnlySpan<char> lineText,
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

        string lineMarkup = _markupBuilder.BuildLineMarkup(
            input, charOffset, lineText,
            cursorPosition, hasSelection, selectionStart, selectionLength);

        // Two separate Markup calls avoid the prefix + lineMarkup string
        // concatenation (one allocation per visual line per render tick).
        AnsiConsole.Markup(prefix);
        AnsiConsole.Markup(lineMarkup);
        // Clear remainder of this line for overwrite-in-place rendering.
        // This eliminates flicker by avoiding a separate clear-before-render step.
        _terminal.Write("\x1b[K");
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

        if (ShowBottomSeparator && hasNonEmptyHint)
            RenderBottomSeparator();
        else
        {
            // Clear the old bottom separator line first, then advance
            _terminal.CursorLeft = 0;
            ClearLine();
            _terminal.WriteLine();
        }

        int maxWidth = _terminal.WindowWidth - TerminalWidthMargin;
        for (int i = 0; i < hints.Count; i++)
        {
            _terminal.CursorLeft = 0;
            ClearLine();
            string hint = hints[i];
            if (!string.IsNullOrEmpty(hint))
            {
                string displayHint = GetTruncatedString(hint, maxWidth);
                try
                {
                    AnsiConsole.Markup(displayHint);
                }
                catch (InvalidOperationException)
                {
                    AnsiConsole.Markup(Markup.Escape(displayHint));
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
    /// <summary>
    /// <summary>
    /// Returns <paramref name="text"/> truncated to at most <paramref name="maxWidth"/>
    /// visible characters (Spectre markup tags are stripped during counting).
    /// When truncation would leave unclosed markup tags, appends <c>[/]</c> closers
    /// so the returned string is always valid Spectre markup.
    /// Returns the original string if it fits within <paramref name="maxWidth"/>.
    /// </summary>
    public static string GetTruncatedString(string text, int maxWidth)
    {
        int visualWidth = 0;
        int tagDepth = 0;
        int i = 0;

        while (i < text.Length)
        {
            // ── Escaped bracket [[ → literal '[' ────────────────────────
            if (i + 1 < text.Length && text[i] == '[' && text[i + 1] == '[')
            {
                visualWidth++;
                if (visualWidth > maxWidth)
                    return BuildTruncated(text, i, tagDepth);
                i += 2;
                continue;
            }

            // ── Escaped bracket ]] → literal ']' ────────────────────────
            if (i + 1 < text.Length && text[i] == ']' && text[i + 1] == ']')
            {
                visualWidth++;
                if (visualWidth > maxWidth)
                    return BuildTruncated(text, i, tagDepth);
                i += 2;
                continue;
            }

            // ── Markup tag ──────────────────────────────────────────────
            if (text[i] == '[')
            {
                int close = FindTagCloseBracket(text, i);
                if (close > i)
                {
                    ReadOnlySpan<char> tagContent = text.AsSpan(i + 1, close - i - 1).Trim();
                    if (tagContent.Length > 0 && tagContent[0] == '/')
                        tagDepth--;
                    else
                        tagDepth++;
                    i = close + 1;
                    continue;
                }
            }

            // ── Regular visible character ───────────────────────────────
            visualWidth++;
            if (visualWidth > maxWidth)
                return BuildTruncated(text, i, tagDepth);

            i++;
        }

        return text; // fits within maxWidth
    }

    /// <summary>
    /// Finds the closing <c>]</c> for a tag starting at <paramref name="openBracket"/>.
    /// Skips over <c>]]</c> escaped-bracket pairs (which do not close a tag).
    /// </summary>
    private static int FindTagCloseBracket(string text, int openBracket)
    {
        int pos = openBracket + 1;
        while (pos < text.Length)
        {
            int close = text.IndexOf(']', pos);
            if (close < 0)
                return -1;
            // ] followed by ] → escaped literal ']', skip both and keep looking.
            if (close + 1 < text.Length && text[close + 1] == ']')
            {
                pos = close + 2;
                continue;
            }
            return close;
        }
        return -1;
    }

    /// <summary>Builds the truncated result with closing tags for unclosed markup.</summary>
    private static string BuildTruncated(string text, int cutIndex, int tagDepth)
    {
        string truncated = text[..cutIndex];
        for (int j = 0; j < tagDepth; j++)
            truncated += "[/]";
        return truncated;
    }

    /// <summary>Renders the top separator (between message feed and input block).</summary>
    private void RenderTopSeparator()
        => RenderSeparator(TopSeparator, ref _cachedTopSepLine, ref _cachedTopSepConfig);

    /// <summary>Renders the bottom separator (between input line and hints block).</summary>
    private void RenderBottomSeparator()
        => RenderSeparator(BottomSeparator, ref _cachedBottomSepLine, ref _cachedBottomSepConfig);

    /// <summary>Renders a separator line, caching the built string when config and width are stable.</summary>
    private void RenderSeparator(SeparatorConfig config, ref string? cachedLine, ref SeparatorConfig? cachedConfig)
    {
        int width = _terminal.WindowWidth - TerminalWidthMargin;
        if (cachedLine == null || _cachedSepWidth != width || cachedConfig != config)
        {
            cachedLine = BuildSeparatorLine(config, width);
            cachedConfig = config;
            _cachedSepWidth = width;
        }
        AnsiConsole.Markup(cachedLine);
        _terminal.Write("\x1b[K");
        _terminal.WriteLine();
    }

    /// <summary>
    /// Counts visible characters in a markup string without allocating.
    /// Walks the span directly, stripping Spectre markup tags ([...])
    /// to compute the display length. No string copies or Markup.Remove calls.
    /// </summary>
    private static int GetVisualLength(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty) return 0;
        int len = 0;
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '[')
            {
                int close = text.Slice(i + 1).IndexOf(']');
                if (close >= 0) { i += close + 2; continue; }
            }
            len++;
            i++;
        }
        return len;
    }

    /// <summary>Builds the separator string from the given config and available width.</summary>
    private string BuildSeparatorLine(SeparatorConfig config, int width)
    {
        string left = config.LeftText ?? string.Empty;
        string right = config.RightText ?? string.Empty;
        char fill = config.RepeatedChar;

        // Measure display length without allocating (span-based, no Markup.Remove)
        int leftLen = GetVisualLength(left.AsSpan());
        int rightLen = GetVisualLength(right.AsSpan());

        int fillCount = width - leftLen - rightLen;
        if (fillCount < 0) fillCount = 0;

        if (fillCount == 0)
        {
            return string.IsNullOrEmpty(left) ? string.Empty
                : string.IsNullOrEmpty(right) ? left : left + right;
        }

        // Reuse the pooled StringBuilder to avoid per-render allocation.
        var sb = _separatorSb;
        sb.Clear();
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

    // ── Scroll Region ────────────────────────────────────────────
    /// <summary>
    /// Sets the terminal's scroll region (DECSTBM) to exclude the input block area,
    /// so message rendering scrolls only within the message area while the input
    /// block stays visually locked at the bottom.
    /// After messages are rendered, call <see cref="ResetScrollRegion"/>.
    /// </summary>
    /// <param name="inputBlockHeight">
    /// Total vertical space taken by the input block
    /// (separator + input lines + blank + hints).
    /// </param>
    public void SetMessageScrollRegion(int inputBlockHeight)
    {
        int terminalHeight = _terminal.BufferHeight;
        // Reserve bottom lines for input block + 1 buffer line to prevent
        // boundary issues when the message area is exactly the top portion.
        int scrollBottom = terminalHeight - inputBlockHeight - 1;
        if (scrollBottom < 1)
            return; // terminal too small for effective scroll region

        // DECSTBM: \x1b[top;bottomr where top=0, bottom is last scrollable line
        _terminal.Write($"\x1b[0;{scrollBottom}r");
    }

    /// <summary>Resets the scroll region back to the full terminal (default).</summary>
    public void ResetScrollRegion()
    {
        _terminal.Write("\x1b[r");
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

        ClearLinesBelowCursor(-delta);
    }

    /// <summary>Clears <paramref name="count"/> lines below the new block that were
    /// part of the old block but not re-filled (block shrank). These are uncleared
    /// gaps between the new block bottom and the old clear area end.</summary>
    public void ClearLinesBelowCursor(int count)
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
