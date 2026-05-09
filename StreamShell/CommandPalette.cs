using Spectre.Console;

namespace StreamShell;

internal class CommandPalette : IBottomPanel
{
    /// <summary>Total visible lines in the hints block (status + hints).</summary>
    public const int MaxHeight = 5;
    int IBottomPanel.LineCount => MaxHeight;
    /// <summary>Index of the status line (always populated when hints are visible).</summary>
    public const int StatusLineIndex = 0;
    /// <summary>Index of the first actual hint line.</summary>
    public const int HintsStartIndex = 1;
    /// <summary>Maximum number of actual hint entries.</summary>
    public const int HintCapacity = MaxHeight - HintsStartIndex; // 4

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

    /// <summary>
    /// Index of the currently selected hint (0 = first hint, -1 = none).
    /// Only valid while hints are shown (command mode with matches).
    /// </summary>
    internal int SelectedIndex { get; set; }

    /// <summary>True when there are hints to navigate.</summary>
    internal bool CanNavigate => _lastLines?.Count > 0;

    /// <summary>
    /// Adjusts the selection by <paramref name="delta"/> and clamps
    /// to the available hint range. Call when the user presses Up/Down.
    /// Marks the panel as dirty so the host forces a re-render.
    /// </summary>
    internal void AdjustSelection(int delta)
    {
        int maxVisible = Math.Min(HintCapacity, _lastMatchCount);
        if (maxVisible <= 0)
        {
            ResetSelection();
            return;
        }

        int oldIndex = SelectedIndex;

        // On first navigation from -1, start at the closest edge
        if (SelectedIndex < 0)
        {
            SelectedIndex = delta > 0 ? 0 : maxVisible - 1;
        }
        else
        {
            SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, maxVisible - 1);
        }

        if (SelectedIndex != oldIndex)
            _isDirty = true;
    }

    public static bool IsActive(string currentInput) => currentInput.StartsWith('/');

    private static readonly string[] _emptyHints = BuildEmptyHints();
    private static readonly IReadOnlyList<string> _cachedEmptyHints = _emptyHints;

    private static string[] BuildEmptyHints()
    {
        var arr = new string[MaxHeight];
        for (int i = 0; i < MaxHeight; i++) arr[i] = string.Empty;
        return arr;
    }

    private readonly Func<IEnumerable<Command>> _commandProvider;
    private string? _lastInput;
    private IReadOnlyList<string>? _lastLines;
    private int _lastSelectedIndex;
    private int _lastMatchCount;

    // Reusable lists for building content on every GetLines call.
    // Cleared and repopulated on each cache miss to avoid per-call allocation.
    private readonly List<string> _linesBuffer = new(MaxHeight);
    private readonly List<Command> _matchingBuffer = new(HintCapacity + 1);

    /// <summary>Creates a palette that reads from a live command provider.</summary>
    public CommandPalette(Func<IEnumerable<Command>> commandProvider)
    {
        _commandProvider = commandProvider;
    }

    /// <summary>Creates a palette with a fixed set of commands (for testing).</summary>
    public CommandPalette(IEnumerable<Command> commands)
    {
        var arr = commands.ToArray();
        _commandProvider = () => arr;
    }

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

        // Cache hit: nothing changed
        if (!inputChanged && !selectionChanged && _lastLines != null)
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

        // Status line at index 0 (first line)
        _linesBuffer.Add("[dim]Tab: autocomplete  \u2191\u2193: selection[/]");

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
            // Command hint mode — limit to HintCapacity manually to avoid LINQ Take().ToList()
            int showCount = Math.Min(_matchingBuffer.Count, HintCapacity);

            // Determine autocomplete suggestion from the selected command (if any)
            if (showCount > 0)
            {
                int suggestionIdx = SelectedIndex >= 0 ? SelectedIndex : 0;
                if (suggestionIdx < showCount)
                {
                    CurrentSuggestion = "/" + _matchingBuffer[suggestionIdx].Name + " ";
                }
            }

            // Build hint strings with selection highlighting
            // Find max name length manually to avoid LINQ MaxBy allocation
            int maxSize = 12;
            for (int i = 0; i < showCount; i++)
            {
                int nameLen = _matchingBuffer[i].Name.Length;
                if (nameLen > maxSize) maxSize = nameLen;
            }

            for (int i = 0; i < showCount; i++)
            {
                var cmd = _matchingBuffer[i];
                if (i == SelectedIndex)
                    _linesBuffer.Add($"> [white]/{cmd.Name.PadRight(maxSize)}[/] {cmd.Description}");
                else
                    _linesBuffer.Add($"  [grey]/{cmd.Name.PadRight(maxSize)}[/] {cmd.Description}");
                if (_linesBuffer.Count >= MaxHeight) break;
            }
        }

        // Pad to MaxHeight
        while (_linesBuffer.Count < MaxHeight)
            _linesBuffer.Add(string.Empty);

        _lastInput = currentInput;
        _lastSelectedIndex = SelectedIndex;
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
            if (SelectedIndex >= 0 && _lastMatchCount > 0)
            {
                int maxVisible = Math.Min(HintCapacity, _lastMatchCount);
                if (SelectedIndex >= maxVisible)
                    SelectedIndex = maxVisible - 1;
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
        int limit = HintCapacity + 1;

        if (cmdPrefix.Length == 0)
        {
            // No filter — take up to limit directly from the enumerable
            // Avoids the [.. list] full-copy allocation that would occur
            // when the provider returns an IList<Command>.
            foreach (var cmd in currentCommands)
            {
                result.Add(cmd);
                if (result.Count > limit) break;
            }
            return;
        }

        foreach (var cmd in currentCommands)
        {
            ReadOnlySpan<char> nameSpan = cmd.Name.AsSpan();
            if (isSpacePresent)
            {
                if (nameSpan.Equals(cmdPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(cmd);
                    if (result.Count > limit) break;
                }
            }
            else
            {
                if (nameSpan.StartsWith(cmdPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(cmd);
                    if (result.Count > limit) break;
                }
            }
        }
    }

    /// <summary>Result of matching argument suggestions against typed args.</summary>
    private sealed record ArgMatchInfo(
        string[] Matches,
        string? CommonNextWord);

    /// <summary>
    /// Matches argument suggestions against the typed args part and determines
    /// whether all matching suggestions share a common next word. Shared by both
    /// hint display and Tab completion for a single source of truth.
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

        if (matches.Length <= 1)
            return new ArgMatchInfo(matches, null);

        // Only relevant mid-word (not at a word boundary)
        if (argsPart.Length == 0 || argsPart.EndsWith(' '))
            return new ArgMatchInfo(matches, null);

        // Find the longest common prefix across all matching suggestions
        // using spans to avoid substring allocations during comparison
        ReadOnlySpan<char> commonPrefix = matches[0].AsSpan();
        for (int i = 1; i < matches.Length; i++)
        {
            int j = 0;
            ReadOnlySpan<char> mi = matches[i].AsSpan();
            while (j < commonPrefix.Length && j < mi.Length &&
                   char.ToLowerInvariant(commonPrefix[j]) == char.ToLowerInvariant(mi[j]))
                j++;
            commonPrefix = commonPrefix[..j];
        }

        // Trim to the first space boundary — we only care about completing one word
        int spaceIdx = commonPrefix.IndexOf(' ');
        if (spaceIdx >= 0)
            commonPrefix = commonPrefix[..spaceIdx];

        // Only report a common next word if it actually extends what was typed
        if (commonPrefix.Length > argsPart.Length)
            return new ArgMatchInfo(matches, commonPrefix.ToString());

        return new ArgMatchInfo(matches, null);
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

        // Mid-word with a common next word → show just one compressed entry
        if (info.CommonNextWord is not null)
        {
            _lastMatchCount = 1;
            string entry = fullPrefix + info.CommonNextWord + " ";
            CurrentSuggestion = entry;
            lines.Add($"  [grey]{Markup.Escape(entry)}[/]");
            return;
        }

        bool atWordBoundary = argsPart.Length == 0 || argsPart.EndsWith(' ');

        // Collect unique display entries
        var entries = new List<string>();
        string cmdPath = fullPrefix;

        if (atWordBoundary)
        {
            // At word boundary → show unique next words
            string contextPrefix = argsPart.Length > 0
                ? cmdPath + argsPart.ToString()
                : cmdPath;
            var seenWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var match in info.Matches)
            {
                ReadOnlySpan<char> remaining = argsPart.Length > 0
                    ? match.AsSpan(argsPart.Length)
                    : match.AsSpan();
                // Extract first word manually to avoid Split(' ') allocation
                string? nextWord = ExtractFirstWord(remaining);
                if (!string.IsNullOrEmpty(nextWord) && seenWords.Add(nextWord))
                {
                    entries.Add(contextPrefix + nextWord);
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
            CurrentSuggestion = entries[suggestionIdx] + " ";
        }

        // Build hint strings with selection highlighting
        for (int i = 0; i < entries.Count; i++)
        {
            if (i == SelectedIndex)
                lines.Add($"> [white]{Markup.Escape(entries[i])}[/]");
            else
                lines.Add($"  [grey]{Markup.Escape(entries[i])}[/]");
            if (lines.Count >= MaxHeight) break;
        }
    }

    /// <summary>Extracts the first whitespace-delimited word from a span, or null if empty.</summary>
    private static string? ExtractFirstWord(ReadOnlySpan<char> span)
    {
        if (span.Length == 0)
            return null;

        // Skip leading whitespace
        int start = 0;
        while (start < span.Length && char.IsWhiteSpace(span[start]))
            start++;

        if (start >= span.Length)
            return null;

        // Find end of word
        int end = start;
        while (end < span.Length && !char.IsWhiteSpace(span[end]))
            end++;

        return span[start..end].ToString();
    }

    private void ResetSelection()
    {
        if (SelectedIndex != 0)
        {
            SelectedIndex = 0;
            _isDirty = true;
        }
    }
}
