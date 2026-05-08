namespace StreamShell;

/// <summary>Represents a registered command that can be triggered with /name.</summary>
public class Command
{
    /// <summary>The command name (without leading slash).</summary>
    public string Name { get; }

    /// <summary>Human-readable description shown in the hint palette.</summary>
    public string Description { get; }

    /// <summary>Async handler invoked when the command is executed.</summary>
    public Func<string[], Dictionary<string, string>, Task> Handler { get; }

    /// <summary>
    /// Optional suggestions for autocomplete after the command name is typed.
    /// Each entry is a full multi-word argument string (e.g. "linux ubuntu").
    /// When provided, the hint palette shows argument completions once the
    /// command is uniquely identified, and Tab fills the top suggestion.
    /// </summary>
    public string[]? ArgumentSuggestions { get; }

    /// <summary>Creates a new command definition.</summary>
    public Command(string name, string description, Func<string[], Dictionary<string, string>, Task> handler)
    {
        Name = name;
        Description = description;
        Handler = handler;
    }

    /// <summary>Creates a new command definition with argument autocomplete suggestions.</summary>
    public Command(string name, string description, Func<string[], Dictionary<string, string>, Task> handler, string[]? argumentSuggestions)
        : this(name, description, handler)
    {
        ArgumentSuggestions = argumentSuggestions;
    }
}
