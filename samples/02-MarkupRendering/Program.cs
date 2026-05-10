using Spectre.Console;
using StreamShell;

// ── 02-MarkupRendering ─────────────────────────────────────────────
// Demonstrates message queue with different colors, styles, and
// background event injection.

using var host = new ConsoleAppHost();

host.AddCommand(new Command("rainbow", "Show rainbow colors", async (_, _) =>
{
    string[] colors = ["red", "yellow", "green", "cyan", "blue", "magenta"];
    foreach (string c in colors)
    {
        host.AddMessage($"[{c}]■ This is [{c} bold]{c}[/] text![/]");
        await Task.Delay(300);
    }
}));

host.AddCommand(new Command("table", "Render a styled table", (_, _) =>
{
    host.AddMessage("[bold underline]User Directory[/]");
    host.AddMessage("[grey]──────────────────────────────[/]");
    host.AddMessage("[cyan]alice[/]  [green]Online[/]   [dim]2h ago[/]");
    host.AddMessage("[cyan]bob[/]    [yellow]Away[/]    [dim]15m ago[/]");
    host.AddMessage("[cyan]carol[/]  [green]Online[/]   [dim]just now[/]");
    host.AddMessage("[grey]──────────────────────────────[/]");
    host.AddMessage("[dim]3 users displayed[/]");
    return Task.CompletedTask;
}));

host.AddCommand(new Command("panic", "Show a multi-line error", (_, _) =>
{
    host.AddMessage("[bold][red]FATAL:[/] System.OverflowException[/]");
    host.AddMessage("[red]  └─ at Calculator.Divide()[/]");
    host.AddMessage("[red]  └─ at Program.Main()[/]");
    host.AddMessage("[yellow]  ⚠ Suggested fix: check divisor[/]");
    return Task.CompletedTask;
}));

// Inject background events
_ = Task.Run(async () =>
{
    var rng = new Random();
    string[] levels = ["[dim]info[/]", "[green]ok[/]", "[yellow]warn[/]"];
    int i = 0;
    while (true)
    {
        await Task.Delay(3000 + rng.Next(2000));
        string lvl = levels[rng.Next(levels.Length)];
        host.AddMessage($"{lvl} background event #{++i}");
    }
});

host.AddMessage("[bold]Markup Rendering Demo[/]");
host.AddMessage("[yellow]Commands: [cyan]/rainbow[/] [cyan]/table[/] [cyan]/panic[/][/]");
host.AddMessage("[dim]Background events arrive every 3-5s[/]");

await host.Run();
