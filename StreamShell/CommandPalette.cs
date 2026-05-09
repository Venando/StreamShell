using Spectre.Console;

namespace StreamShell;

internal class CommandPalette
{
    public const int MaxHeight = 6;

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
    private IReadOnlyList<string>? _lastHints;

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
        if (_lastInput == currentInput && _lastHints != null)
            return _lastHints;

        if (!IsActive(currentInput))
        {
            _lastInput = currentInput;
            _lastHints = _cachedEmptyHints;
            return _cachedEmptyHints;
        }

        string query = currentInput.Length > 1 ? currentInput[1..] : string.Empty;
        List<Command> matching = GetMatchingCommands(query);

        if (matching.Count == 0)
        {
            _lastInput = currentInput;
            _lastHints = _cachedEmptyHints;
            return _cachedEmptyHints;
        }

        List<string> hints = new(MaxHeight);

        int spaceIndex = query.IndexOf(' ');

        if (matching.Count == 1
            && matching[0].ArgumentSuggestions is { Length: > 0 } suggestions
            && spaceIndex >= 0)
        {
            // Exactly one command with argument suggestions → show argument completions
            string argsPart = query[(spaceIndex + 1)..];
            AddArgumentHints(hints, matching[0], argsPart, suggestions);
        }
        else
        {
            // Show command hints (existing behavior)
            var showMatching = matching.Take(MaxHeight).ToList();
            int maxSize = showMatching.MaxBy(val => val.Name.Length)?.Name.Length ?? 12;

            foreach (var cmd in showMatching)
            {
                hints.Add($"  [grey]/{cmd.Name.PadRight(maxSize)}[/] {cmd.Description}");
                if (hints.Count >= MaxHeight) break;
            }
        }

        int emptyCount = MaxHeight - hints.Count;
        if (emptyCount > 0)
        {
            for (int i = 0; i < emptyCount; i++)
                hints.Add(string.Empty);
        }

        _lastInput = currentInput;
        _lastHints = hints;
        return hints;
    }

    /// <summary>
    /// Returns the best autocomplete suggestion for the given input, or null if no
    /// completion is possible. Used by the Tab key autocomplete in UserInputHandler.
    /// Always completes to the first alphabetically-sorted matching entry.
    /// </summary>
    public string? GetTopSuggestion(string currentInput)
    {
        if (!IsActive(currentInput))
            return null;

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
            // Name matches exactly but no space yet → complete with space
            return "/" + command.Name + " ";
        }

        if (namePartial)
        {
            // Partial match on command name → complete name + space
            return "/" + command.Name + " ";
        }

        // Command name is fully typed with space. Check for argument suggestions.
        if (command.ArgumentSuggestions is { Length: > 0 } && spaceIndex >= 0)
        {
            string argsPart = query[(spaceIndex + 1)..];
            string fullPrefix = "/" + command.Name + " ";

            // Find matching argument suggestions, preserving original order
            var matches = command.ArgumentSuggestions
                .Where(s => s.StartsWith(argsPart, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (matches.Length == 0)
                return null;

            // Complete with only the remainder of the first matching suggestion
            return fullPrefix + argsPart + matches[0][argsPart.Length..] + " ";
        }

        return null;
    }

    /// <summary>Returns all commands matching the given query (command prefix).</summary>
    private List<Command> GetMatchingCommands(string query)
    {
        var currentCommands = _commandProvider();
        int spaceIndex = query.IndexOf(' ');
        string cmdPrefix = spaceIndex > 0 ? query[..spaceIndex] : query;

        if (cmdPrefix.Length == 0)
            return currentCommands.ToList();

        List<Command> matching = new(MaxHeight);
        foreach (var cmd in currentCommands)
        {
            if (cmd.Name.StartsWith(cmdPrefix, StringComparison.OrdinalIgnoreCase))
            {
                matching.Add(cmd);
                if (matching.Count > MaxHeight) break;
            }
        }
        return matching;
    }

    /// <summary>Populates hints with unique next-argument completions.</summary>
    private static void AddArgumentHints(List<string> hints, Command command, string argsPart, string[] suggestions)
    {
        string cmdPath = "/" + command.Name + " ";

        // Find suggestions that match the typed args
        var matches = suggestions
            .Where(s => s.StartsWith(argsPart, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
            return;

        // At a word boundary (empty args or ends with space) → show unique next word
        bool atWordBoundary = argsPart.Length == 0 || argsPart.EndsWith(' ');

        if (atWordBoundary)
        {
            var seenWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var match in matches)
            {
                string remaining = argsPart.Length > 0 ? match[argsPart.Length..] : match;
                string nextWord = remaining.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                if (!string.IsNullOrEmpty(nextWord) && seenWords.Add(nextWord))
                {
                    hints.Add($"  [grey]{Markup.Escape(cmdPath)} {Markup.Escape(nextWord)}[/]");
                    if (hints.Count >= MaxHeight) break;
                }
            }
        }
        else
        {
            // Mid-word — show full matching paths
            foreach (var match in matches)
            {
                hints.Add($"  [grey]{Markup.Escape(cmdPath + match)}[/]");
                if (hints.Count >= MaxHeight) break;
            }
        }
    }
}
