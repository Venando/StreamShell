namespace StreamShell;

public class Command
{
    public string Name { get; }
    public string Description { get; }
    public Func<string[], Dictionary<string, string>, Task> Handler { get; }

    public Command(string name, string description, Func<string[], Dictionary<string, string>, Task> handler)
    {
        Name = name;
        Description = description;
        Handler = handler;
    }
}
