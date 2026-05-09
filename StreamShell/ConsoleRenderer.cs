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
    }

    public int RightMargin { get; set; } = Console.WindowWidth;

    // Line count of the current bottom panel (set by host). Defaults to CommandPalette's size.
    private int _panelLineCount = CommandPalette.MaxHeight;

    /// <summary>Top separator configuration (between message feed and input block).</summary>
    public SeparatorConfig TopSeparator { get; set; } = SeparatorConfig.Default;

    /// <summary>Bottom separator configuration (between input line and hints block).</summary>
    public SeparatorConfig BottomSeparator { get; set; } = SeparatorConfig.Default;

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
    private static int GetBlockOffset(string input, int panelLineCount)
    {
        return (1 + panelLineCount) + LineWrappingService.GetInputLines(
            input, Console.WindowWidth, 2, 4).Count;
    }

    /// <summary>Number of visual lines the input occupies.</summary>
    public int GetInputLineCount(string input) => LineWrappingService.GetInputLines(
        input, RightMargin, _settings.PrefixMargin, _settings.WrappingRightMargin).Count;

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
    public void ClearInputBlockForReRender(string? oldInput, string newInput, int oldPanelLineCount)
    {
        if (oldInput is null)
            return;

        int oldOffset = GetBlockOffset(oldInput, oldPanelLineCount);
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
        RenderTopSeparator();
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

        var sb = new System.Text.StringBuilder();

        if (selectionOnThisLine)
        {
            // Before selection — wrap leading / if at position 0
            if (selStartInLine > 0)
                AppendEscapedChunk(sb, lineText[..selStartInLine],
                    isCommandSlashLine, cmdSlashMarkup);

            // Selected text
            string selText = Markup.Escape(lineText[selStartInLine..selEndInLine]);
            sb.Append("[").Append(_settings.SelectionMarkup).Append("]")
              .Append(selText).Append("[/]");

            // After selection (not at position 0, no command slash)
            if (selEndInLine < lineText.Length)
                sb.Append(EscapeWithPlaceholderStyling(lineText[selEndInLine..]));
        }
        else
        {
            AppendCursorHighlight(sb, lineText, ref cursorCol, 0,
                isCommandSlashLine, cmdSlashMarkup);
            cursorCol = -1;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Appends escaped text, wrapping a leading '/' in command markup if this
    /// is the first visual line and the character is at position 0.
    /// Placeholder patterns (<c>[paste ...]</c>) are rendered as underlined markup.
    /// </summary>
    private void AppendEscapedChunk(System.Text.StringBuilder sb,
        string text, bool isCommandSlash, string cmdSlashMarkup)
    {
        if (isCommandSlash && text.Length > 0 && text[0] == '/')
        {
            sb.Append("[").Append(cmdSlashMarkup).Append("]/[/]");
            if (text.Length > 1)
                sb.Append(EscapeWithPlaceholderStyling(text[1..]));
        }
        else
        {
            sb.Append(EscapeWithPlaceholderStyling(text));
        }
    }

    /// <summary>
    /// Escapes text for Spectre markup, but renders known placeholder patterns
    /// (<c>[paste N lines: name...]</c>) with underline styling instead of
    /// escaping them as plain text.
    /// </summary>
    /// <summary>
    /// Placeholder strings from current attachments, used to render them as
    /// underlined markup instead of plain escaped text.
    /// </summary>
    public IReadOnlyList<string>? PlaceholderStrings { get; set; }

    /// <summary>
    /// Escapes text for Spectre markup, but renders known placeholder strings
    /// (from <see cref="PlaceholderStrings"/>) with italic underline styling.
    /// Also handles cursor-split fragments where the opening <c>[</c> was
    /// consumed by the cursor character highlight.
    /// </summary>
    private string EscapeWithPlaceholderStyling(string text)
    {
        var phStrings = PlaceholderStrings;
        if (phStrings is null || phStrings.Count == 0)
            return Markup.Escape(text);

        var sb = new System.Text.StringBuilder();
        int searchFrom = 0;

        while (searchFrom < text.Length)
        {
            // Search for the earliest placeholder match (full or cursor-split fragment)
            int bestIdx = -1;
            int bestEnd = -1;
            string bestMatch = null!;

            foreach (string ph in phStrings)
            {
                // Try full placeholder string
                int idx = text.IndexOf(ph, searchFrom, StringComparison.Ordinal);
                if (idx >= 0 && (bestIdx < 0 || idx < bestIdx))
                {
                    bestIdx = idx;
                    bestEnd = idx + ph.Length;
                    bestMatch = ph;
                }

                // Try cursor-split fragment (missing opening [)
                if (ph.Length > 0 && ph[0] == '[')
                {
                    string fragment = ph[1..];
                    if (fragment.Length + searchFrom <= text.Length &&
                        text.AsSpan(searchFrom).StartsWith(fragment, StringComparison.Ordinal))
                    {
                        if (bestIdx < 0 || searchFrom < bestIdx)
                        {
                            bestIdx = searchFrom;
                            bestEnd = searchFrom + fragment.Length;
                            bestMatch = fragment;
                        }
                    }
                }
            }

            if (bestIdx < 0)
                break;

            // Escape text before the placeholder
            if (bestIdx > searchFrom)
                sb.Append(Markup.Escape(text[searchFrom..bestIdx]));

            // Render placeholder as italic underline (escape brackets for Spectre)
            string content = text[bestIdx..bestEnd];
            string escaped = content.Replace("[", "[[").Replace("]", "]]");
            sb.Append("[italic underline]").Append(escaped).Append("[/]");
            searchFrom = bestEnd;
        }

        if (searchFrom < text.Length)
            sb.Append(Markup.Escape(text[searchFrom..]));

        return sb.Length > 0 ? sb.ToString() : Markup.Escape(text);
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
        int segmentOffset,
        bool isCommandSlashLine,
        string cmdSlashMarkup)
    {
        if (cursorCol < 0)
        {
            AppendEscapedChunk(sb, rawText, isCommandSlashLine, cmdSlashMarkup);
            return;
        }

        int localCol = cursorCol - segmentOffset;
        if (localCol < 0 || localCol > rawText.Length)
        {
            AppendEscapedChunk(sb, rawText, isCommandSlashLine, cmdSlashMarkup);
            return;
        }

        // Before cursor
        if (localCol > 0)
            AppendEscapedChunk(sb, rawText[..localCol], isCommandSlashLine, cmdSlashMarkup);

        string cursorStyle = _settings.CursorMarkup;

        if (localCol < rawText.Length)
        {
            string escapedChar = Markup.Escape(rawText[localCol].ToString());
            sb.Append("[").Append(cursorStyle).Append("]")
              .Append(escapedChar).Append("[/]");

            if (localCol + 1 < rawText.Length)
                sb.Append(EscapeWithPlaceholderStyling(rawText[(localCol + 1)..]));
        }
        else
        {
            sb.Append("[").Append(cursorStyle).Append("] [/]"); // Past-end placeholder
        }

        cursorCol = -1;
    }

    // ── Hints Block ───────────────────────────────────────────────────
    private void RenderHintsBlock(IReadOnlyList<string> hints)
    {
        if (hints.Any(h => !string.IsNullOrEmpty(h)))
            RenderBottomSeparator();
        else
        {
            // Clear the old bottom separator line first, then advance
            Console.CursorLeft = 0;
            ClearLine();
            Console.WriteLine();
        }

        int maxWidth = Console.WindowWidth - 1;
        for (int i = 0; i < hints.Count; i++)
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
            if (i < hints.Count - 1)
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

    /// <summary>Renders the separator line using the current config.</summary>
    /// <summary>Renders the top separator (between message feed and input block).</summary>
    private void RenderTopSeparator()
    {
        int width = Console.WindowWidth - 1;
        AnsiConsole.MarkupLine(BuildSeparatorLine(TopSeparator, width));
    }

    /// <summary>Renders the bottom separator (between input line and hints block).</summary>
    private void RenderBottomSeparator()
    {
        int width = Console.WindowWidth - 1;
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

        string fillStr = string.IsNullOrEmpty(config.RepeatedCharMarkup)
            ? new string(fill, fillCount)
            : $"[{config.RepeatedCharMarkup}]{new string(fill, fillCount)}[/]";

        if (!string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right))
            return left + fillStr + right;
        if (!string.IsNullOrEmpty(left))
            return left + fillStr;
        if (!string.IsNullOrEmpty(right))
            return fillStr + right;
        return fillStr;
    }

    /// <summary>
    /// After the input block was re-rendered and its visual height changed,
    /// cleans up stale content that wasn't cleared by
    /// <see cref="ClearInputBlockForReRender"/> because the offset excludes
    /// the separator line at the top of the block:
    /// - Growing case: clears the old separator line at block_top
    /// - Shrinking case: also clears excess lines below the new block
    /// </summary>
    public void HandleBlockHeightChange(int oldBlockOffset, int newBlockOffset)
    {
        int delta = newBlockOffset - oldBlockOffset;
        if (delta == 0)
            return;

        // Cursor is at the last panel line after the full render.
        // visual = offset + 2, cursor at visual - 1 = offset + 1
        // So block_top = cursorTop - (offset + 1) = cursorTop - newBlockOffset - 1
        int cursorBottom = Console.CursorTop;
        int blockTop = cursorBottom - newBlockOffset - 1;

        // Handle shrinking first so ClearLinesBelow uses the correct cursor
        if (delta < 0)
            ClearLinesBelow(-delta);

        // The old separator line at block_top was not cleared by
        // ClearInputBlockForReRender (starts from block_top+1).
        if (blockTop >= 0)
        {
            Console.SetCursorPosition(0, blockTop);
            ClearLine();
        }

        // Restore cursor to block bottom
        Console.CursorTop = Math.Max(0, Math.Min(cursorBottom, Console.BufferHeight - 1));
    }

    /// <summary>Clears <paramref name="count"/> lines below the new block that were
    /// part of the old block but not re-filled (block shrank). These are uncleared
    /// gaps between the new block bottom and the old clear area end.</summary>
    private void ClearLinesBelow(int count)
    {
        if (count <= 0)
            return;

        // Save cursor position first — SetCursorPosition + ClearLine moves the cursor
        int savedTop = Console.CursorTop;
        int bufferHeight = Console.BufferHeight;

        for (int i = 1; i <= count; i++)
        {
            int top = savedTop + i;
            if (top >= bufferHeight)
                break;
            Console.SetCursorPosition(0, top);
            ClearLine();
        }

        // Restore cursor to saved position (block bottom)
        Console.CursorTop = Math.Max(0, Math.Min(savedTop, bufferHeight - 1));
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
