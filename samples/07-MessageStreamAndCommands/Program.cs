using StreamShell;
using Spectre.Console;

// ═════════════════════════════════════════════════════════════════════
//  07-MessageStreamAndCommands
//  Tests new features: message replay on resize, command scrolling,
//  markup in command names, configurable palette height, and
//  bottom separator toggle via custom panels.
// ═════════════════════════════════════════════════════════════════════

var host = new ConsoleAppHost();
host.SetTopSeparator(leftText: "[bold cyan]StreamShell[/]", rightText: "[grey]v1.0.1[/]", repeatedCharacter: '-');

// Add many commands to test scrolling (> HintCapacity of 7)
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
    ("[DarkOrange]quit[/]", "Also exits"),
    ("[white]about[/]", "About this app"),
    ("[grey]license[/]", "Shows license"),
    ("[Pink1]theme[/]", "Changes theme"),
    ("[Gold1]search[/]", "Search function"),
    ("[SlateBlue1]filter[/]", "Filter results"),
    ("[Plum1]sort[/]", "Sort items"),
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

bool _defaultPanelHasSeparator = true;

// Toggle between two default panels: one with separator, one without
var panelWithSep = new InfoBottomPanel(showSeparator: true, host.Settings.CommandPaletteHeight);
var panelWithoutSep = new InfoBottomPanel(showSeparator: false, host.Settings.CommandPaletteHeight);

host.AddCommand("togglesep", "Toggles the bottom separator on/off", async (args, named) =>
{
    if (_defaultPanelHasSeparator)
    {
        host.SetDefaultPanel(panelWithoutSep);
        _defaultPanelHasSeparator = false;
        host.AddMessage("[yellow]Bottom separator OFF — panel tight to input.[/]");
    }
    else
    {
        host.SetDefaultPanel(panelWithSep);
        _defaultPanelHasSeparator = true;
        host.AddMessage("[green]Bottom separator ON — panel separated from input.[/]");
    }
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
host.AddMessage("[grey]Tab autocomplete and scroll offset work for all matches, not just the first window.[/]");
host.AddMessage("[grey]Type [bold]/togglesep[/] to switch default panel with/without bottom separator.[/]");

await host.Run();

// ═════════════════════════════════════════════════════════════════════
//  Custom bottom panel that shows a timestamped status line.
//  Demonstrates ShowBottomSeparator toggle.
// ═════════════════════════════════════════════════════════════════════
public class InfoBottomPanel : IBottomPanel
{
    private readonly bool _showSeparator;
    private readonly string[] _lines;

    public InfoBottomPanel(bool showSeparator, int height)
    {
        _showSeparator = showSeparator;
        _lines = new string[height];
        _lines[0] = $"[dim grey]Info panel — separator: {(showSeparator ? "ON" : "OFF")}[/]";
        for (int i = 1; i < height; i++)
            _lines[i] = string.Empty;
    }

    int IBottomPanel.LineCount => _lines.Length;
    bool IBottomPanel.ShowBottomSeparator => _showSeparator;

    public IReadOnlyList<string> GetLines(string currentInput) => _lines;
    public bool TryHandleKey(ConsoleKeyInfo key) => false;
    public void Dispose() { }
    public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
