using Spectre.Console;

namespace StreamShell;

internal class CommandPalette : IBottomPanel
{
    /// <summary>Default maximum height for the command palette. Configurable via <see cref="StreamShellSettings.CommandPaletteHeight"/>.</summary>
    public static int DefaultMaxHeight { get; set; } = 8;

    /// <summary>Total visible lines in the hints block (status + hints).</summary>
    public int MaxHeight { get; init; } = DefaultMaxHeight;

    /// <summary>Index of the status line (always populated when hints are visible).</summary>
    public const int StatusLineIndex = 0;

    /// <summary>Index of the first actual hint line.</summary>
    public const int HintsStartIndex = 1;

    /// <summary>Maximum number of actual hint entries.</summary>
    public int HintCapacity => MaxHeight - HintsStartIndex;

    int IBottomPanel.LineCount => MaxHeight;

    /// <summary>Backing field for interface IsDirty. Volatile for cross-thread visibility.</summary>
    private volatile bool _isDirty;
    bool IBottomPanel.IsDirty => _isDirty;
    void IBottomPanel.ClearDirty() => _isDirty = false;

    /// <summary>
    /// The current autocomplete suggestion (text that Tab would fill).
    /// Null or empty when no suggestion is available.
    /// Populated during <see cref="GetLines"/>.
    /// </summary>
    internal string? CurrentSuggestion { get; private set; }
    string? IBottomPanel.CurrentSuggestion => CurrentSuggestion;

    /// <summary>
    /// Intercepts Up/Down arrows to navigate hint selection.
    /// </summary>
    bool IBottomPanel.TryHandleKey(ConsoleKeyInfo key)
    {
        if (!IsActive(_lastInput ?? string.Empty))
            return false;

        if (key.Key == ConsoleKey.UpArrow)
        {
            AdjustSelection(-1);
            return true;
        }

        if (key.Key == ConsoleKey.DownArrow)
        {
            AdjustSelection(1);
            return true;
        }

        return false;
    }

    /// <summary>Shared scroll navigator tracking selection and viewport position.</summary>
    private readonly ScrollNavigator _nav = new();

    /// <summary>Index of the currently selected hint within the visible window (0 = first hint).</summary>
    internal int SelectedIndex => _nav.SelectedIndex;

    /// <summary>Scroll offset into the full match list. 0 means showing the first page.</summary>
    internal int ScrollOffset => _nav.ScrollOffset;

    /// <summary>True when there are hints to navigate.</summary>
    internal bool CanNavigate => _lastLines?.Count > 0;

    /// <summary>
    /// Adjusts the selection by <paramref name="delta"/> and clamps
    /// to the available hint range. Call when the user presses Up/Down.
    /// Marks the panel as dirty so the host forces a re-render.
    /// Supports scrolling through all matches (not just the visible window).
    /// Delegates scroll math to <see cref="ScrollNavigator"/>.
    /// </summary>
    internal void AdjustSelection(int delta)
    {
        if (_lastMatchCount <= 0)
        {
            ResetSelection();
            return;
        }

        // Sync navigator state before adjustment
        _nav.TotalItems = _lastMatchCount;
        _nav.VisibleCapacity = HintCapacity;

        if (_nav.AdjustSelection(delta))
            _isDirty = true;
    }

    public static bool IsActive(string currentInput) => currentInput.StartsWith('/');

    private readonly string[] _emptyHints;
    private readonly IReadOnlyList<string> _cachedEmptyHints;

    private string[] BuildEmptyHints()
    {
        var arr = new string[MaxHeight];
        for (int i = 0; i < MaxHeight; i++) arr[i] = string.Empty;
        return arr;
    }

    private readonly Func<IEnumerable<Command>> _commandProvider;
    private string? _lastInput;
    private IReadOnlyList<string>? _lastLines;
    private int _lastSelectedIndex;
    private int _lastScrollOffset;
    private int _lastMatchCount;

    // Reusable lists for building content on every GetLines call.
    // Cleared and repopulated on each cache miss to avoid per-call allocation.
    private readonly List<string> _linesBuffer;
    private readonly List<Command> _matchingBuffer;

    // Reusable StringBuilder for building hint lines without per-line
    // PadRight or string interpolation allocation.
    private readonly System.Text.StringBuilder _sb = new(capacity: 128);

    /// <summary>Creates a palette that reads from a live command provider.</summary>
    public CommandPalette(Func<IEnumerable<Command>> commandProvider, StreamShellSettings? settings = null)
    {
        _commandProvider = commandProvider;
        if (settings != null)
        {
            MaxHeight = settings.CommandPaletteHeight;
            _paletteStyle = settings.Palette ?? new PaletteStyle();
        }
        _emptyHints = BuildEmptyHints();
        _cachedEmptyHints = _emptyHints;
        _linesBuffer = new(MaxHeight);
        _matchingBuffer = new(HintCapacity + 1);
    }

    /// <summary>Creates a palette with a fixed set of commands (for testing).</summary>
    public CommandPalette(IEnumerable<Command> commands, StreamShellSettings? settings = null)
    {
        var arr = commands.ToArray();
        _commandProvider = () => arr;
        if (settings != null)
        {
            MaxHeight = settings.CommandPaletteHeight;
            _paletteStyle = settings.Palette ?? new PaletteStyle();
        }
        _emptyHints = BuildEmptyHints();
        _cachedEmptyHints = _emptyHints;
        _linesBuffer = new(MaxHeight);
        _matchingBuffer = new(HintCapacity + 1);
    }

    private readonly PaletteStyle _paletteStyle = new();

    /// <summary>
    /// Returns all panel lines. Line 0 is the status/instruction line.
    /// Lines 1..4 contain command hints. The Tab autocomplete suggestion
    /// is available via <see cref="CurrentSuggestion"/>.
    /// Uses pre-allocated reusable buffers (<see cref="_linesBuffer"/>,
    /// <see cref="_matchingBuffer"/>) to avoid per-call list allocation.
    /// </summary>
    public IReadOnlyList<string> GetLines(string currentInput)
    {
        bool inputChanged = currentInput != _lastInput;
        bool selectionChanged = _lastSelectedIndex != SelectedIndex;
        bool scrollChanged = _lastScrollOffset != ScrollOffset;

        // Cache hit: nothing changed
        if (!inputChanged && !selectionChanged && !scrollChanged && _lastLines != null)
            return _lastLines;

        CurrentSuggestion = null;

        // Input changed — reset selection
        if (inputChanged)
            ResetSelection();

        if (!IsActive(currentInput))
        {
            _lastInput = currentInput;
            _lastSelectedIndex = SelectedIndex;
            _lastLines = _cachedEmptyHints;
            return _cachedEmptyHints;
        }

        // Use spans for query slicing to avoid substring allocations
        ReadOnlySpan<char> query = currentInput.Length > 1
            ? currentInput.AsSpan(1)
            : ReadOnlySpan<char>.Empty;

        GetMatchingCommands(query, _matchingBuffer);

        if (_matchingBuffer.Count == 0)
        {
            _lastInput = currentInput;
            _lastSelectedIndex = SelectedIndex;
            _lastLines = _cachedEmptyHints;
            return _cachedEmptyHints;
        }

        // Update match count for selection clamping
        _lastMatchCount = _matchingBuffer.Count;

        // Reuse the lines buffer: clear and repopulate
        _linesBuffer.Clear();

        // Status line at index 0 (first line) — shows input schema + scroll position
        int startIdx = ScrollOffset + 1;
        int endIdx = Math.Min(ScrollOffset + HintCapacity, _matchingBuffer.Count);
        _linesBuffer.Add($"  [dim gray]\u2191\u2193: scroll, tab: autocomplete, {startIdx}-{endIdx}/{_matchingBuffer.Count}[/]");

        int spaceIndex = query.IndexOf(' ');

        if (_matchingBuffer.Count == 1
            && _matchingBuffer[0].ArgumentSuggestions is { Length: > 0 } suggestions
            && spaceIndex >= 0)
        {
            // Argument completion mode
            ReadOnlySpan<char> argsPart = query[(spaceIndex + 1)..];
            string fullPrefix = "/" + _matchingBuffer[0].Name + " ";
            CollectArgumentHints(_linesBuffer, _matchingBuffer[0], fullPrefix, argsPart, suggestions);
        }
        else
        {
            // Command hint mode — show slice based on scroll offset
            int totalMatches = _matchingBuffer.Count;
            int effectiveOffset = Math.Min(ScrollOffset, Math.Max(0, totalMatches - HintCapacity));
            int showCount = Math.Min(HintCapacity, totalMatches - effectiveOffset);

            // Determine autocomplete suggestion from the selected command (if any)
            if (showCount > 0)
            {
                int suggestionIdx = SelectedIndex >= 0 ? SelectedIndex : 0;
                int actualIdx = effectiveOffset + suggestionIdx;
                if (actualIdx < totalMatches)
                {
                    // Strip markup from suggestion for clean command path
                    CurrentSuggestion = "/" + StripMarkup(_matchingBuffer[actualIdx].Name) + " ";
                }
            }

            // Build hint strings with selection highlighting
            int maxSize = MaxNameVisualLength(effectiveOffset, showCount);
            int descriptionWidth = Console.BufferWidth - maxSize - 1;
            for (int i = 0; i < showCount; i++)
            {
                var cmd = _matchingBuffer[effectiveOffset + i];
                bool isSelected = i == SelectedIndex;
                AppendHintLine(isSelected, cmd.Name, cmd.Description,
                    maxSize, isSelected ? descriptionWidth : 0);
                _linesBuffer.Add(_sb.ToString());
                if (_linesBuffer.Count >= MaxHeight) break;
            }
        }

        // Pad to MaxHeight
        while (_linesBuffer.Count < MaxHeight)
            _linesBuffer.Add(string.Empty);

        _lastInput = currentInput;
        _lastSelectedIndex = SelectedIndex;
        _lastScrollOffset = ScrollOffset;
        _lastLines = _linesBuffer;
        return _linesBuffer;
    }

    /// <summary>
    /// Runs the panel's background loop. Monitors input changes and clamps
    /// selection when the matching hint count shrinks below the current index.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // Clamp selection when matching count shrinks below current index
            if (_lastMatchCount > 0)
            {
                _nav.TotalItems = _lastMatchCount;
                _nav.VisibleCapacity = HintCapacity;
                _nav.ClampToBounds();
            }

            try { await Task.Delay(100, cancellationToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Populates <paramref name="result"/> with commands matching the given query.
    /// Limit: HintCapacity + 1 so we can detect overflow beyond what's shown.
    /// Uses the caller-provided list to avoid per-call allocation.</summary>
    private void GetMatchingCommands(ReadOnlySpan<char> query, List<Command> result)
    {
        result.Clear();

        var currentCommands = _commandProvider();
        int spaceIndex = query.IndexOf(' ');
        bool isSpacePresent = spaceIndex > 0;
        ReadOnlySpan<char> cmdPrefix = isSpacePresent ? query[..spaceIndex] : query;

        if (cmdPrefix.Length == 0)
        {
            foreach (var cmd in currentCommands)
                result.Add(cmd);
            return;
        }

        foreach (var cmd in currentCommands)
        {
            string nameForMatch = StripMarkup(cmd.Name);
            ReadOnlySpan<char> nameSpan = nameForMatch.AsSpan();
            if (isSpacePresent)
            {
                if (nameSpan.Equals(cmdPrefix, StringComparison.OrdinalIgnoreCase))
                    result.Add(cmd);
            }
            else
            {
                if (nameSpan.StartsWith(cmdPrefix, StringComparison.OrdinalIgnoreCase))
                    result.Add(cmd);
            }
        }
    }

    /// <summary>Result of matching argument suggestions against typed args.</summary>
    private sealed record ArgMatchInfo(string[] Matches);

    /// <summary>
    /// Matches argument suggestions against the typed args part.
    /// Shared by both hint display and Tab completion for a single source of truth.
    /// </summary>
    private static ArgMatchInfo GetArgMatchInfo(ReadOnlySpan<char> argsPart, string[] suggestions)
    {
        // Count matches first so we can allocate the exact array size
        // (avoids the List<T> + ToArray() double allocation).
        int matchCount = 0;
        foreach (var s in suggestions)
        {
            if (s.AsSpan().StartsWith(argsPart, StringComparison.OrdinalIgnoreCase))
                matchCount++;
        }

        var matches = new string[matchCount];
        int idx = 0;
        foreach (var s in suggestions)
        {
            if (s.AsSpan().StartsWith(argsPart, StringComparison.OrdinalIgnoreCase))
                matches[idx++] = s;
        }

        return new ArgMatchInfo(matches);
    }

    /// <summary>
    /// Collects argument hint entries with selection highlighting.
    /// Sets <see cref="_lastMatchCount"/> and <see cref="CurrentSuggestion"/>.
    /// </summary>
    private void CollectArgumentHints(List<string> lines, Command command,
        string fullPrefix, ReadOnlySpan<char> argsPart, string[] suggestions)
    {
        var info = GetArgMatchInfo(argsPart, suggestions);

        if (info.Matches.Length == 0)
        {
            _lastMatchCount = 0;
            return;
        }

        bool atWordBoundary = argsPart.Length == 0 || argsPart.EndsWith(' ');

        // Collect unique display entries
        var entries = new List<string>();
        string cmdPath = fullPrefix;

        if (atWordBoundary)
        {
            // At word boundary → show unique next words
            // Build each entry using the reusable StringBuilder to avoid
            // intermediate ToString() on argsPart and ExtractFirstWord.
            var seenWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var match in info.Matches)
            {
                ReadOnlySpan<char> remaining = argsPart.Length > 0
                    ? match.AsSpan(argsPart.Length)
                    : match.AsSpan();
                // Extract first word as span; allocate only when adding to HashSet
                var firstWord = ExtractFirstWordSpan(remaining);
                if (firstWord.IsEmpty)
                    continue;

                // Build full entry: cmdPath + argsPart + firstWord using
                // the reusable StringBuilder — zero intermediate allocations.
                _sb.Clear();
                _sb.Append(cmdPath);
                _sb.Append(argsPart);
                _sb.Append(firstWord);
                string entry = _sb.ToString();

                if (seenWords.Add(entry))
                {
                    entries.Add(entry);
                    if (entries.Count >= HintCapacity) break;
                }
            }
        }
        else
        {
            // Mid-word with divergent matches → show each full path
            foreach (var match in info.Matches)
            {
                entries.Add(cmdPath + match);
                if (entries.Count >= HintCapacity) break;
            }
        }

        _lastMatchCount = entries.Count;

        if (entries.Count == 0)
            return;

        // Autocomplete suggestion from selected entry
        int suggestionIdx = SelectedIndex >= 0 ? SelectedIndex : 0;
        if (suggestionIdx < entries.Count)
        {
            CurrentSuggestion = StripMarkup(entries[suggestionIdx]) + " ";
        }

        // Build hint strings with selection highlighting
        for (int i = 0; i < entries.Count; i++)
        {
            bool isSelected = i == SelectedIndex;
            string content = entries[i].StartsWith('/') ? entries[i][1..] : entries[i];
            AppendHintLine(isSelected, content, includeSlash: true);
            lines.Add(_sb.ToString());
            if (lines.Count >= MaxHeight) break;
        }
    }

    /// <summary>
    /// Computes the maximum visual name length across the visible command slice.
    /// Capped at 40 to leave room for descriptions.
    /// </summary>
    private int MaxNameVisualLength(int offset, int count)
    {
        int max = 0;
        for (int i = 0; i < count; i++)
        {
            int len = GetVisualLength(_matchingBuffer[offset + i].Name);
            if (len > max) max = len;
        }
        const int cap = 40;
        return max > cap ? cap : max;
    }

    /// <summary>
    /// Appends a single hint line to <see cref="_sb"/> using palette styling.
    /// Shared by command hints and argument hints for consistent selection highlighting.
    /// </summary>
    /// <param name="isSelected">True for the currently selected hint.</param>
    /// <param name="content">The name/path portion after the slash.</param>
    /// <param name="description">Optional description text (null for argument hints).</param>
    /// <param name="namePadTo">Padding target for the name visual length.</param>
    /// <param name="descriptionPadTo">Padding target for the description visual length (selected only).</param>
    /// <param name="includeSlash">When true, prepends a '/' before content. Default: true.</param>
    private void AppendHintLine(bool isSelected, string content, string? description = null,
        int namePadTo = 0, int descriptionPadTo = 0, bool includeSlash = true)
    {
        var p = _paletteStyle;
        _sb.Clear();

        if (isSelected)
        {
            _sb.Append("[on ");
            _sb.Append(p.SelectedBackground);
            _sb.Append("][");
            _sb.Append(p.SelectedCursorColor);
            _sb.Append(']');
            _sb.Append(p.SelectedCursorSymbol);
            _sb.Append("[/][");
            _sb.Append(p.SelectedNameColor);
            _sb.Append(']');
            if (includeSlash) _sb.Append('/');
            _sb.Append(content);
            PadTo(_sb, GetVisualLength(content), namePadTo);
            _sb.Append("[/]");

            if (description != null)
            {
                _sb.Append(" [");
                _sb.Append(p.SelectedDescriptionColor);
                _sb.Append(']');
                _sb.Append(description);
                _sb.Append("[/]");
                PadTo(_sb, GetVisualLength(description), descriptionPadTo);
            }
            _sb.Append("[/]"); // closes [on ...]
        }
        else
        {
            _sb.Append(p.NormalIndent);
            _sb.Append('[');
            _sb.Append(p.NormalNameColor);
            _sb.Append(']');
            if (includeSlash) _sb.Append('/');
            _sb.Append(content);
            PadTo(_sb, GetVisualLength(content), namePadTo);
            _sb.Append("[/]");

            if (description != null)
            {
                _sb.Append(' ');
                _sb.Append(description);
            }
        }
    }

    /// <summary>
    /// Extracts the first whitespace-delimited word as a span, without allocation.
    /// Returns an empty span if no word is found.
    /// </summary>
    private static ReadOnlySpan<char> ExtractFirstWordSpan(ReadOnlySpan<char> span)
    {
        if (span.Length == 0)
            return ReadOnlySpan<char>.Empty;

        int start = 0;
        while (start < span.Length && char.IsWhiteSpace(span[start]))
            start++;

        if (start >= span.Length)
            return ReadOnlySpan<char>.Empty;

        int end = start;
        while (end < span.Length && !char.IsWhiteSpace(span[end]))
            end++;

        return span[start..end];
    }

    /// <summary>
    /// Appends spaces to <paramref name="sb"/> to pad <paramref name="currentLength"/>
    /// to <paramref name="targetLength"/>. Avoids string allocation from PadRight().
    /// </summary>
    private static void PadTo(System.Text.StringBuilder sb, int currentLength, int targetLength)
    {
        int pad = targetLength - currentLength;
        if (pad > 0)
            sb.Append(' ', pad);
    }

    /// <summary>Strips Spectre.Console markup tags ([...]) from text for matching purposes.</summary>
    private static string StripMarkup(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty) return string.Empty;

        Span<char> result = stackalloc char[text.Length];
        int pos = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '[')
            {
                int close = i + 1;
                while (close < text.Length && text[close] != ']') close++;

                if (close < text.Length)
                {
                    if (close + 1 < text.Length && text[i + 1] == '[')
                    {
                        result[pos++] = '[';
                        i = close + 1;
                        continue;
                    }
                    i = close;
                    continue;
                }
            }
            result[pos++] = text[i];
        }

        return result[..pos].ToString();
    }

    private static int GetVisualLength(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int len = 0;
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '[')
            {
                int close = text.IndexOf(']', i + 1);
                if (close > i) { i = close + 1; continue; }
            }
            len++;
            i++;
        }
        return len;
    }

    private void ResetSelection()
    {
        if (_nav.SelectedIndex != 0 || _nav.ScrollOffset != 0)
        {
            _nav.Reset();
            _isDirty = true;
        }
    }

    /// <summary>Disposes the panel. Clears buffers to release references.</summary>
    public void Dispose()
    {
        _linesBuffer.Clear();
        _matchingBuffer.Clear();
        _sb.Clear();
    }
}
