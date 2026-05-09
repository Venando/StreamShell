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
    public bool Contains(string commandName) => _commands.ContainsKey(commandName);

    /// <summary>Register a command.</summary>
    public void Add(Command command) => _commands[command.Name] = command;

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

        string query = input[1..];
        var parts = CommandParser.Split(query);
        if (parts.Count == 0)
            return false;

        name = parts[0];
        args = query.Length > name.Length ? query[(name.Length + 1)..] : string.Empty;
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

        if (!_commands.TryGetValue(commandName!, out var command))
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
}
