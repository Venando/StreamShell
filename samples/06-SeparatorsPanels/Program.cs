using Spectre.Console;
using StreamShell;

// ── 06-SeparatorsPanels ────────────────────────────────────────────
// Demonstrates SetTopSeparator, SetBottomSeparator, custom IBottomPanel.

using var host = new ConsoleAppHost();

// ── Commands ──

host.AddCommand(new Command("top-sep", Markup.Escape("Set top separator. Usage: /top-sep [left] [right] [char]"),
    (args, _) =>
    {
        string? left = args.Length > 0 ? args[0] : null;
        string? right = args.Length > 1 ? args[1] : null;
        char fill = args.Length > 2 && args[2].Length > 0 ? args[2][0] : '-';
        host.SetTopSeparator(left, right, fill);
        host.AddMessage($"[green]Top separator updated[/]");
        return Task.CompletedTask;
    }));

host.AddCommand(new Command("bot-sep", Markup.Escape("Set bottom separator. Usage: /bot-sep [left] [right] [char]"),
    (args, _) =>
    {
        string? left = args.Length > 0 ? args[0] : null;
        string? right = args.Length > 1 ? args[1] : null;
        char fill = args.Length > 2 && args[2].Length > 0 ? args[2][0] : '-';
        host.SetBottomSeparator(left, right, fill);
        host.AddMessage($"[green]Bottom separator updated[/]");
        return Task.CompletedTask;
    }));

host.AddCommand(new Command("counter", "Switch to character counter panel", (_, _) =>
{
    host.SetBottomPanel(new CharacterCounterPanel());
    host.AddMessage("[cyan]Panel: Character Counter[/]");
    return Task.CompletedTask;
}));

host.AddCommand(new Command("clock", "Switch to clock panel", (_, _) =>
{
    host.SetBottomPanel(new ClockPanel());
    host.AddMessage("[cyan]Panel: Clock[/]");
    return Task.CompletedTask;
}));

host.AddCommand(new Command("default", "Restore default panel", (_, _) =>
{
    host.ResetBottomPanel();
    host.AddMessage("[cyan]Panel: Default (empty)[/]");
    return Task.CompletedTask;
}));

host.AddCommand(new Command("color-sep", "Demo colored separators", (_, _) =>
{
    host.SetTopSeparator("Messages", "StreamShell", '-');
    host.SetBottomSeparator("Input", null, '\u2500');
    host.AddMessage("[green]Colored separators applied[/]");
    return Task.CompletedTask;
}));

host.AddCommand(new Command("help", "Show available commands", (_, _) =>
{
    host.AddMessage("[bold underline]Separator & Panel Commands[/]");
    host.AddMessage("  [cyan]/top-sep [left] [right] [char][/]  — Set top separator");
    host.AddMessage("  [cyan]/bot-sep [left] [right] [char][/]  — Set bottom separator");
    host.AddMessage("  [cyan]/color-sep[/]                    — Demo colored separators");
    host.AddMessage("  [cyan]/counter[/]                      — Character counter panel");
    host.AddMessage("  [cyan]/clock[/]                        — Live clock panel");
    host.AddMessage("  [cyan]/default[/]                      — Restore default panel");
    return Task.CompletedTask;
}));

host.AddCommand(new Command("exit", "Exit", (_, _) => { host.Stop(); return Task.CompletedTask; }));

// Apply initial separators for the GIF
host.SetTopSeparator("[bold]StreamShell[/]", "demo", '\u2501');
host.SetBottomSeparator("input", null, '\u2500');

host.AddMessage("[bold]Separators & Panels Demo[/]");
host.AddMessage("[yellow]Try [cyan]/counter[/] to see the character counter panel[/]");
host.AddMessage("[yellow]Try [cyan]/clock[/] to see the live clock panel[/]");
host.AddMessage("[yellow]Type to see the counter update in real-time[/]");

await host.Run();

// ── Custom bottom panels ──
class CharacterCounterPanel : IBottomPanel
{
    public int LineCount => 2;
    private string[] _lines = new string[2];
    private string? _lastInput;

    public IReadOnlyList<string> GetLines(string currentInput)
    {
        if (currentInput == _lastInput) return _lines;
        _lastInput = currentInput;

        _lines[0] = "";
        _lines[1] = string.IsNullOrEmpty(currentInput)
            ? "[dim]Type something to count...[/]"
            : $"[grey]Input length: [green]{currentInput.Length}[/] chars | words: [green]{currentInput.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length}[/][/]";
        return _lines;
    }

    public void Dispose() { }
}

class ClockPanel : IBottomPanel
{
    public int LineCount => 2;
    private string[] _lines = new string[2];
    private bool _isDirty;

    public bool IsDirty => _isDirty;
    public void ClearDirty() => _isDirty = false;

    public IReadOnlyList<string> GetLines(string currentInput)
    {
        _lines[0] = "";
        _lines[1] = $"[dim]\u23f0 [green]{DateTime.Now:HH:mm:ss}[/] UTC+{TimeZoneInfo.Local.BaseUtcOffset.Hours}[/]";
        return _lines;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(1000, cancellationToken);
            _isDirty = true;
        }
    }

    public void Dispose() { }
}
