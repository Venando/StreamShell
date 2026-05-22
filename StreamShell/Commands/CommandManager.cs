using System.Collections.Concurrent;

namespace StreamShell;

/// <summary>
/// Manages command registration, lookup, parsing, and execution.
/// Extracted from <see cref="ConsoleAppHost"/> to separate command management
/// from input handling and rendering orchestration (SRP).
/// </summary>
internal class CommandManager
{
    private readonly ConcurrentDictionary<string, Command> _commands = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All registered commands.</summary>
    public IEnumerable<Command> AllCommands => _commands.Values;

    /// <summary>True when a command with the given name is registered.</summary>
    public bool Contains(string commandName) => _commands.ContainsKey(StripMarkup(commandName));

    /// <summary>Register a command.</summary>
    public void Add(Command command) => _commands[StripMarkup(command.Name)] = command;

    /// <summary>Unregisters a command.</summary>
    public void Remove(Command command) => _commands.TryRemove(StripMarkup(command.Name), out _);

    /// <summary>
    /// Extracts the command name and argument string from a /command input.
    /// Returns false if the input doesn't look like a valid command reference.
    /// </summary>
    public static bool TryGetCommandName(string input, out string? name, out string args)
    {
        name = null;
        args = string.Empty;

        if (input.Length <= 1 || input[0] != '/')
            return false;

        // Use spans to avoid substring allocations on every command lookup.
        ReadOnlySpan<char> querySpan = input.AsSpan(1);

        // Split the query into words using span-based iteration.
        // Only extract the first word (command name) and reconstruct args from the span.
        ReadOnlySpan<char> trimmed = querySpan.TrimStart();
        if (trimmed.IsEmpty)
            return false;

        int wordEnd = trimmed.IndexOf(' ');
        ReadOnlySpan<char> firstWord = wordEnd < 0 ? trimmed : trimmed[..wordEnd];

        if (firstWord.IsEmpty)
            return false;

        name = firstWord.ToString();
        args = wordEnd >= 0 ? trimmed[(wordEnd + 1)..].ToString() : string.Empty;
        return true;
    }

    /// <summary>Convenience overload when only the command name is needed.</summary>
    public static bool TryGetCommandName(string input, out string? name)
        => TryGetCommandName(input, out name, out _);

    /// <summary>
    /// Attempts to execute a command from /input. Returns the result markup
    /// (error or null) to be displayed.
    /// </summary>
    public async Task<string?> ExecuteAsync(string input)
    {
        if (!TryGetCommandName(input, out string? commandName, out string? argsString))
            return null;

        if (!_commands.TryGetValue(StripMarkup(commandName!), out var command))
            return $"[red]Unknown command: /{commandName}[/]";

        var (positionalArgs, namedArgs) = CommandParser.Parse(argsString);

        try
        {
            await command.Handler(positionalArgs, namedArgs);
            return null;
        }
        catch (Exception ex)
        {
            return $"[red]Command error: {ex.Message}[/]";
        }
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
}
