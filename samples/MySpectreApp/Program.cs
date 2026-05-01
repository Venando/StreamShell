using Spectre.Console;
using StreamShell;

using var host = new ConsoleAppHost();

// Lower threshold for testing large paste detection
host.Settings.LargePasteThreshold = 200;

host.AddCommand(new Command("context", "Shows context info", (args, named) =>
{
    host.AddMessage("[green]Context: demo app running[/]");
    return Task.CompletedTask;
}));

host.AddCommand(new Command("clear", "Clears the screen", (args, named) =>
{
    Console.Clear();
    return Task.CompletedTask;
}));

host.AddCommand(new Command("exit", "Exits the application", (args, named) =>
{
    host.Stop();
    return Task.CompletedTask;
}));

host.AddCommand(new Command("longtest", "Long hind description test I'm wring right here to test how would it behave. Long hind description test I'm wring right here to test how would it behave.", (args, named) =>
{
    host.Stop();
    return Task.CompletedTask;
}));

host.AddCommand(new Command("subagents", "Lists active subagents", (args, named) =>
{
    host.AddMessage("[yellow]No active subagents[/]");
    return Task.CompletedTask;
}));

host.AddCommand(new Command("compact", "Compacts memory", (args, named) =>
{
    host.AddMessage("[green]Memory compacted[/]");
    return Task.CompletedTask;
}));

host.AddCommand(new Command("cost", "Shows cost info", (args, named) =>
{
    host.AddMessage("[blue]Cost: $0.00[/]");
    return Task.CompletedTask;
}));

host.UserInputSubmitted += (input, inputType, attachments) =>
{
    host.AddMessage("[green]USER:[/] [cyan]" + inputType + "[/]");
    host.AddMessage("  [grey]\"" + Markup.Escape(input) + "\"[/]");
    foreach (var att in attachments)
    {
        host.AddMessage("  [grey][[attachment: " + att.Type + ", " + att.LineCount + " lines, " + att.Content.Length + " chars]][/]");
    }
};

_ = Task.Run(async () =>
{
    int i = 0;
    while (true)
    {
        await Task.Delay(2500);
        host.AddMessage("[grey][[" + DateTime.Now.ToString("HH:mm:ss") + "]][/] Background Event #" + (++i));
    }
});

host.AddMessage("[yellow]StreamShell demo started. Type text or commands like /context[/]");
host.AddMessage("[yellow]Large paste (>200 chars) will be attached as file[/]");
host.AddMessage("[yellow]Large paste (>200 chars) will be attached as file[/]");

await host.Run();
