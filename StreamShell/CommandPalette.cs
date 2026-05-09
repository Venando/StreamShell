using Spectre.Console;

namespace StreamShell;

internal class CommandPalette
{
    public const int MaxHeight = 5;
    public const int StatusLineIndex = 0;
    public const int HintsStartIndex = 1;

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
    private List<string>? _hintsCache;          // Raw hints without selection markers
    private string[]? _currentSuggestions;      // Suggestion strings for each hint position
    private int _selectedIndex = -1;
    private int _selectionVersion;

    /// <summary>Version counter bumped on selection changes. Used by render diff.</summary>
    public int SelectionVersion => _selectionVersion;

    /// <summary>Moves selection up (true) or down (false). Returns true if changed.</summary>
    public bool MoveSelection(bool up)
    {
        if (_hintsCache == null || _currentSuggestions == null || _currentSuggestions.Length == 0)
            return false;

        int maxIndex = Math.Min(_currentSuggestions.Length, MaxHeight - HintsStartIndex) - 1;
        if (maxIndex < 0) return false;

        int newIndex;
        if (up)
            newIndex = _selectedIndex <= 0 ? -1 : _selectedIndex - 1;
        else
            newIndex = _selectedIndex >= maxIndex ? -1 : _selectedIndex + 1;

        if (newIndex == _selectedIndex) return false;
        _selectedIndex = newIndex;
        _selectionVersion++;
        return true;
    }

    /// <summary>Applies " >" selection marker to the selected hint line.</summary>
    private IReadOnlyList<string> ApplySelection(List<string> raw)
    {
        var result = new List<string>(raw.Count);
        for (int i = 0; i < raw.Count; i++)
        {
            if (i == StatusLineIndex)
            {
                result.Add(raw[i]);
            }
            else if (i - HintsStartIndex == _selectedIndex
                     && !string.IsNullOrEmpty(raw[i])
                     && raw[i].Length >= 2
                     && raw[i][..2] == "  ")
            {
                result.Add(" >" + raw[i][2..]);
            }
            else
            {
                result.Add(raw[i]);
            }
        }
        return result;
    }

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

    public IReadOnlyList<string> GetHints(string currentInput)
    {
        if (_lastInput == currentInput && _hintsCache != null)
            return ApplySelection(_hintsCache);

        // Reset selection on any input change
        _selectedIndex = -1;
        _selectionVersion++;

        if (!IsActive(currentInput))
        {
            _lastInput = currentInput;
            _hintsCache = null;
            _currentSuggestions = null;
            return _cachedEmptyHints;
        }

        string query = currentInput.Length > 1 ? currentInput[1..] : string.Empty;
        List<Command> matching = GetMatchingCommands(query);

        if (matching.Count == 0)
        {
            _lastInput = currentInput;
            _hintsCache = null;
            _currentSuggestions = null;
            return _cachedEmptyHints;
        }

        // Build raw hints — index 0 = status line, 1..4 = actual hints
        List<string> raw = new(MaxHeight);
        raw.Add("[dim]Tab: autocomplete  \u2191\u2193: select[/]");

        int spaceIndex = query.IndexOf(' ');
        int hintSlots = MaxHeight - HintsStartIndex; // 4

        if (matching.Count == 1
            && matching[0].ArgumentSuggestions is { Length: > 0 } suggestions
            && spaceIndex >= 0)
        {
            // Exactly one command with argument suggestions → show argument completions
            string argsPart = query[(spaceIndex + 1)..];
            var info = GetArgMatchInfo(argsPart, suggestions);

            // Build suggestions for selection
            if (info.Matches.Length > 0)
            {
                string fullPrefix = "/" + matching[0].Name + " ";
                _currentSuggestions = info.Matches
                    .Take(hintSlots)
                    .Select(m => fullPrefix + m + " ")
                    .ToArray();
            }
            else
            {
                _currentSuggestions = null;
            }

            AddArgumentHints(raw, matching[0], argsPart, suggestions);
        }
        else
        {
            // Show command hints
            var showHints = matching.Take(hintSlots).ToList();
            _currentSuggestions = showHints
                .Select(cmd => "/" + cmd.Name + " ")
                .ToArray();

            int maxSize = showHints.MaxBy(val => val.Name.Length)?.Name.Length ?? 12;
            foreach (var cmd in showHints)
            {
                raw.Add($"  [grey]/{cmd.Name.PadRight(maxSize)}[/] {cmd.Description}");
            }
        }

        // Pad to MaxHeight
        while (raw.Count < MaxHeight)
            raw.Add(string.Empty);

        _lastInput = currentInput;
        _hintsCache = raw;
        return ApplySelection(raw);
    }

    /// <summary>
    /// Returns the best autocomplete suggestion for the given input, or null if no
    /// completion is possible. Used by the Tab key autocomplete in UserInputHandler.
    /// If a hint is selected via arrow keys, completes to that specific hint.
    /// </summary>
    public string? GetTopSuggestion(string currentInput)
    {
        if (!IsActive(currentInput))
            return null;

        // If a hint is selected, complete to the selected suggestion
        if (_selectedIndex >= 0 && _currentSuggestions != null
            && _selectedIndex < _currentSuggestions.Length)
        {
            return _currentSuggestions[_selectedIndex];
        }

        string query = currentInput.Length > 1 ? currentInput[1..] : string.Empty;
        List<Command> matching = GetMatchingCommands(query);

        if (matching.Count == 0)
            return null;

        int spaceIndex = query.IndexOf(' ');
        string cmdPrefix = spaceIndex > 0 ? query[..spaceIndex] : query;

        if (matching.Count > 1)
        {
            return "/" + matching[0].Name + " ";
        }

        // Exactly one command matches
        var command = matching[0];

        // Complete the command name if not fully typed or exact match without space
        bool nameExact = string.Equals(command.Name, cmdPrefix, StringComparison.OrdinalIgnoreCase);
        bool namePartial = !nameExact
            && command.Name.Length > cmdPrefix.Length
            && command.Name.StartsWith(cmdPrefix, StringComparison.OrdinalIgnoreCase);

        if (nameExact && spaceIndex < 0)
        {
            return "/" + command.Name + " ";
        }

        if (namePartial)
        {
            return "/" + command.Name + " ";
        }

        // Command name is fully typed with space. Check for argument suggestions.
        if (command.ArgumentSuggestions is { Length: > 0 } && spaceIndex >= 0)
        {
            string argsPart = query[(spaceIndex + 1)..];
            string fullPrefix = "/" + command.Name + " ";

            var info = GetArgMatchInfo(argsPart, command.ArgumentSuggestions);
            if (info.Matches.Length == 0)
                return null;

            // Mid-word with common next word → complete to the common prefix
            if (info.CommonNextWord is not null)
                return fullPrefix + info.CommonNextWord + " ";

            // Complete to the first matching suggestion
            return fullPrefix + info.Matches[0] + " ";
        }

        return null;
    }

    /// <summary>Returns all commands matching the given query (command prefix).
    /// Limit: MaxHeight entries (hint slots, not counting status line).</summary>
    private List<Command> GetMatchingCommands(string query)
    {
        var currentCommands = _commandProvider();
        int spaceIndex = query.IndexOf(' ');
        string cmdPrefix = spaceIndex > 0 ? query[..spaceIndex] : query;

        if (cmdPrefix.Length == 0)
            return currentCommands.ToList();

        // Match up to hintSlotCount + 1 so we can detect overflow
        int limit = MaxHeight - HintsStartIndex + 1;
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
}
