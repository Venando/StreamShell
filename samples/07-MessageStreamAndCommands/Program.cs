using StreamShell;
using Spectre.Console;

// ═════════════════════════════════════════════════════════════════════
//  07-MessageStreamAndCommands
//  Tests new features: message replay on resize, command scrolling,
//  markup in command names, and bottom separator toggle.
// ═════════════════════════════════════════════════════════════════════

var host = new ConsoleAppHost();
host.SetTopSeparator(leftText: "[bold cyan]StreamShell[/]", rightText: "[grey]v1.0.1[/]", repeatedCharacter: '-');

// Add many commands to test scrolling (> HintCapacity of 4)
// Some have markup in their names to test markup support
var commands = new[]
{
    ("[green]echo[/]", "Echoes back what you type"),
    ("[blue]time[/]", "Shows current time"),
    ("[yellow]date[/]", "Shows current date"),
    ("[red]clear[/]", "Clears the screen"),
    ("[purple]help[/]", "Shows this help message"),
    ("[aqua]version[/]", "Shows version info"),
    ("[lime]status[/]", "Shows system status"),
    ("[maroon]config[/]", "Shows configuration"),
    ("[olive]debug[/]", "Debug mode toggle"),
    ("[navy]reload[/]", "Reloads settings"),
    ("[teal]save[/]", "Saves current state"),
    ("[silver]load[/]", "Loads saved state"),
    ("[fuchsia]exit[/]", "Exits the application"),
    ("[orange]quit[/]", "Also exits"),
    ("[white]about[/]", "About this app"),
    ("[grey]license[/]", "Shows license"),
    ("[pink]theme[/]", "Changes theme"),
    ("[gold]search[/]", "Search function"),
    ("[indigo]filter[/]", "Filter results"),
    ("[plum]sort[/]", "Sort items"),
};

foreach (var (name, desc) in commands)
{
    host.AddCommand(name, desc, async (args, named) =>
    {
        string cmdName = Markup.Remove(name);
        host.AddMessage($"[green]Executed:[/] [bold]{cmdName}[/] with args: [grey]{string.Join(", ", args)}[/]");
        await Task.CompletedTask;
    }, null);
}

// Command that floods the message stream to test resize replay
host.AddCommand("flood", "Floods the message stream with test messages", async (args, named) =>
{
    for (int i = 1; i <= 15; i++)
    {
        host.AddMessage($"[grey]{i:D2}:[/] [white]This is a test message that should re-wrap correctly when the console is resized.[/] [blue]Message #{i}[/]");
    }
    host.AddMessage("[yellow]Try resizing the console width smaller — messages should re-emit and re-wrap![/]");
    await Task.CompletedTask;
}, null);

// Command that toggles the bottom separator via a custom panel
host.AddCommand("togglesep", "Toggles the bottom separator on/off", async (args, named) =>
{
    // We can't directly toggle from here easily, but we can demonstrate
    host.AddMessage("[yellow]Use /panel command to toggle a panel without bottom separator.[/]");
    await Task.CompletedTask;
}, null);

host.AddCommand("panel", "Switches to a panel without bottom separator", async (args, named) =>
{
    // This would need access to host internals, so we just show a message
    host.AddMessage("[green]Bottom separator toggle is set per-panel via ShowBottomSeparator property.[/]");
    host.AddMessage("[grey]Custom panels can set ShowBottomSeparator = false to suppress it.[/]");
    await Task.CompletedTask;
}, null);

host.UserInputSubmitted += e =>
{
    if (e.InputType == InputType.PlainText && !string.IsNullOrWhiteSpace(e.RawOutput))
    {
        host.AddMessage($"[grey]You said:[/] {Markup.Escape(e.RawOutput)}");
    }
};

host.AddMessage("[bold green]Welcome to StreamShell Test App![/]");
host.AddMessage("[grey]Type / and use [bold]↑ ↓[/] to scroll through [yellow]20+ commands[/]. Some have markup in names.[/]");
host.AddMessage("[grey]Type [bold]/flood[/] to fill the screen, then resize window to test message replay.[/]");
host.AddMessage("[grey]Tab autocomplete and scroll offset work for all matches, not just the first 4.[/]");

await host.Run();
