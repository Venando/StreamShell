using StreamShell;

// ── 01-HelloStreamShell ────────────────────────────────────────────
// Hero demo: full app lifecycle, basic commands, typing text and
// submitting, plus an auto-exit to keep the GIF loop short.

using var host = new ConsoleAppHost();

host.AddCommand(new Command("hello", "Say hello", (_, _) =>
{
    host.AddMessage("[green]Hello, world![/]");
    return Task.CompletedTask;
}));

host.AddCommand(new Command("count", "Count to 5", async (_, _) =>
{
    for (int i = 1; i <= 5; i++)
    {
        host.AddMessage($"[cyan]Count: {i}[/]");
        await Task.Delay(200);
    }
}));

host.AddCommand(new Command("exit", "Exit the app", (_, _) =>
{
    host.AddMessage("[yellow]Goodbye![/]");
    host.Stop();
    return Task.CompletedTask;
}));

host.UserInputSubmitted += args =>
{
    if (args.InputType == InputType.PlainText)
        host.AddMessage($"[grey]You typed:[/] [italic]{args.RawOutput}[/]");
};

host.AddMessage("[bold]Welcome to StreamShell![/]");
host.AddMessage("[dim]Try: [green]/hello[/]  [cyan]/count[/]  [yellow]/exit[/][/]");

await host.Run();
