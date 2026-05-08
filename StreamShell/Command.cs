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

    /// <summary>Creates a new command definition.</summary>
    public Command(string name, string description, Func<string[], Dictionary<string, string>, Task> handler)
    {
        Name = name;
        Description = description;
        Handler = handler;
    }
}
