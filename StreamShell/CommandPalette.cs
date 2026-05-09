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
    private int _lastSelectedIndex = 0;
    private int _lastMatchCount;

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

        string query = currentInput.Length > 1 ? currentInput[1..] : string.Empty;
        List<Command> matching = GetMatchingCommands(query);

        if (matching.Count == 0)
        {
            _lastInput = currentInput;
            _lastSelectedIndex = SelectedIndex;
            _lastLines = _cachedEmptyHints;
            return _cachedEmptyHints;
        }

        // Update match count for selection clamping
        _lastMatchCount = matching.Count;

        // Build all lines: [0] = status, [1..4] = hints
        List<string> lines = new(MaxHeight);

        // Status line at index 0 (first line)
        lines.Add("[dim]Tab: autocomplete  \u2191\u2193: selection[/]");

        int spaceIndex = query.IndexOf(' ');
        string cmdPrefix = spaceIndex > 0 ? query[..spaceIndex] : query;

        if (matching.Count == 1
            && matching[0].ArgumentSuggestions is { Length: > 0 } suggestions
            && spaceIndex >= 0)
        {
            // Argument completion mode
            string argsPart = query[(spaceIndex + 1)..];
            string fullPrefix = "/" + matching[0].Name + " ";
            var info = GetArgMatchInfo(argsPart, suggestions);

            if (info.Matches.Length > 0)
            {
                CurrentSuggestion = info.CommonNextWord is not null
                    ? fullPrefix + info.CommonNextWord + " "
                    : fullPrefix + info.Matches[0] + " ";
            }

            AddArgumentHints(lines, matching[0], argsPart, suggestions);
        }
        else
        {
            // Command hint mode
            var showMatching = matching.Take(HintCapacity).ToList();

            // Determine autocomplete suggestion from the selected command (if any)
            if (showMatching.Count > 0)
            {
                int suggestionIdx = SelectedIndex >= 0 ? SelectedIndex : 0;
                if (suggestionIdx < showMatching.Count)
                {
                    CurrentSuggestion = "/" + showMatching[suggestionIdx].Name + " ";
                }
            }

            // Build hint strings with selection highlighting
            int maxSize = showMatching.MaxBy(val => val.Name.Length)?.Name.Length ?? 12;

            for (int i = 0; i < showMatching.Count; i++)
            {
                var cmd = showMatching[i];
                if (i == SelectedIndex)
                    lines.Add($"> [white]/{cmd.Name.PadRight(maxSize)}[/] {cmd.Description}");
                else
                    lines.Add($"  [grey]/{cmd.Name.PadRight(maxSize)}[/] {cmd.Description}");
                if (lines.Count >= MaxHeight) break;
            }
        }

        // Pad to MaxHeight
        while (lines.Count < MaxHeight)
            lines.Add(string.Empty);

        _lastInput = currentInput;
        _lastSelectedIndex = SelectedIndex;
        _lastLines = lines;
        return lines;
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

    /// <summary>Returns all commands matching the given query (command prefix).
    /// Limit: HintCapacity + 1 so we can detect overflow beyond what's shown.</summary>
    private List<Command> GetMatchingCommands(string query)
    {
        var currentCommands = _commandProvider();
        int spaceIndex = query.IndexOf(' ');
        string cmdPrefix = spaceIndex > 0 ? query[..spaceIndex] : query;

        if (cmdPrefix.Length == 0)
            return currentCommands.ToList();

        int limit = HintCapacity + 1;
        List<Command> matching = new(limit);
        foreach (var cmd in currentCommands)
        {
            if (cmd.Name.StartsWith(cmdPrefix, StringComparison.OrdinalIgnoreCase))
            {
                matching.Add(cmd);
                if (matching.Count > limit) break;
            }
        }
        return matching;
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
    private static ArgMatchInfo GetArgMatchInfo(string argsPart, string[] suggestions)
    {
        var matches = suggestions
            .Where(s => s.StartsWith(argsPart, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length <= 1)
            return new ArgMatchInfo(matches, null);

        // Only relevant mid-word (not at a word boundary)
        if (argsPart.Length == 0 || argsPart.EndsWith(' '))
            return new ArgMatchInfo(matches, null);

        // Find the longest common prefix across all matching suggestions
        string commonPrefix = matches[0];
        for (int i = 1; i < matches.Length; i++)
        {
            int j = 0;
            while (j < commonPrefix.Length && j < matches[i].Length &&
                   char.ToLowerInvariant(commonPrefix[j]) == char.ToLowerInvariant(matches[i][j]))
                j++;
            commonPrefix = commonPrefix[..j];
        }

        // Trim to the first space boundary — we only care about completing one word
        int spaceIdx = commonPrefix.IndexOf(' ');
        if (spaceIdx >= 0)
            commonPrefix = commonPrefix[..spaceIdx];

        // Only report a common next word if it actually extends what was typed
        if (commonPrefix.Length > argsPart.Length)
            return new ArgMatchInfo(matches, commonPrefix);

        return new ArgMatchInfo(matches, null);
    }

    /// <summary>
    /// Populates hints with argument completions. Uses GetArgMatchInfo for unified matching.
    /// </summary>
    private static void AddArgumentHints(List<string> hints, Command command, string argsPart, string[] suggestions)
    {
        string cmdPath = "/" + command.Name + " ";
        var info = GetArgMatchInfo(argsPart, suggestions);

        if (info.Matches.Length == 0)
            return;

        // Mid-word with a common next word → show just one compressed hint
        if (info.CommonNextWord is not null)
        {
            hints.Add($"  [grey]{Markup.Escape(cmdPath + info.CommonNextWord)}[/]");
            return;
        }

        bool atWordBoundary = argsPart.Length == 0 || argsPart.EndsWith(' ');

        if (atWordBoundary)
        {
            // At word boundary → show unique next words
            string contextPrefix = argsPart.Length > 0 ? cmdPath + argsPart : cmdPath;
            var seenWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var match in info.Matches)
            {
                string remaining = argsPart.Length > 0 ? match[argsPart.Length..] : match;
                string nextWord = remaining.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                if (!string.IsNullOrEmpty(nextWord) && seenWords.Add(nextWord))
                {
                    hints.Add($"  [grey]{Markup.Escape(contextPrefix)}{Markup.Escape(nextWord)}[/]");
                    if (hints.Count >= MaxHeight) break;
                }
            }
        }
        else
        {
            // Mid-word with divergent matches → show each full path
            foreach (var match in info.Matches)
            {
                hints.Add($"  [grey]{Markup.Escape(cmdPath + match)}[/]");
                if (hints.Count >= MaxHeight) break;
            }
        }
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
