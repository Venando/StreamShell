using Spectre.Console;
using StreamShell;

// ── 03-CommandAutocomplete ─────────────────────────────────────────
// Demonstrates tab completion, argument suggestions, command palette.

using var host = new ConsoleAppHost();

// Command with argument autocomplete suggestions
host.AddCommand("deploy", "Deploy to an environment",
    (args, named) =>
    {
        string env = args.Length > 0 ? args[0] : "(none)";
        string region = named.TryGetValue("region", out var r) ? r : "auto";
        host.AddMessage($"[green]Deploying to [bold]{Markup.Escape(env)}[/] ({Markup.Escape(region)})[/]");
        return Task.CompletedTask;
    },
    ["staging", "production", "development", "canary"]);

// Command with multi-word argument completions
host.AddCommand("ssh", "SSH to a server",
    (args, named) =>
    {
        string target = string.Join(" ", args);
        host.AddMessage($"[cyan]Connecting to [bold]{Markup.Escape(target)}[/]...[/]");
        return Task.CompletedTask;
    },
    [
        "web-01",
        "web-02",
        "db-primary",
        "db-replica",
        "cache-01",
        "monitoring"
    ]);

// Regular command (no arguments)
host.AddCommand(new Command("status", "Show system status", (_, _) =>
{
    host.AddMessage("[green]All systems operational[/]");
    host.AddMessage("[dim]  CPU: 23%  RAM: 1.2/8 GB  Disk: 45%[/]");
    return Task.CompletedTask;
}));

host.AddCommand(new Command("help", "List available commands", (_, _) =>
{
    host.AddMessage("[bold underline]Available Commands[/]");
    host.AddMessage("  [cyan]/deploy[/]  [grey]<env> --region <r>[/]  — Deploy to environment");
    host.AddMessage("  [cyan]/ssh[/]     [grey]<target>[/]          — SSH to a server");
    host.AddMessage("  [cyan]/status[/]                           — System status");
    host.AddMessage("  [cyan]/help[/]                             — This help");
    return Task.CompletedTask;
}));

host.AddCommand(new Command("exit", "Exit the app", (_, _) =>
{
    host.Stop();
    return Task.CompletedTask;
}));

host.AddMessage("[bold]Command Autocomplete Demo[/]");
host.AddMessage("[yellow]Type /deploy and Tab to see environment suggestions[/]");
host.AddMessage("[yellow]Type /ssh and Tab to see server names[/]");
host.AddMessage("[yellow]Use [bold]--key value[/] for named arguments[/]");

await host.Run();
