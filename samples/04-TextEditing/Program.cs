using Spectre.Console;
using StreamShell;

// ── 04-TextEditing ─────────────────────────────────────────────────
// Demonstrates cursor movement, selection, clipboard, large paste
// detection, and save/load of input field states.

using var host = new ConsoleAppHost();

// Lower threshold so large pastes trigger in the demo
host.Settings.LargePasteThreshold = 100;

// ── Commands that show editing features ──

host.AddCommand(new Command("help", "Show editing controls", (_, _) =>
{
    host.AddMessage("[bold underline]Editing Controls[/]");
    host.AddMessage("  [cyan]\u2190 \u2192[/]             — Move cursor");
    host.AddMessage("  [cyan]Shift+\u2190 \u2192[/]        — Select text");
    host.AddMessage("  [cyan]Ctrl+C[/]            — Copy selected");
    host.AddMessage("  [cyan]Ctrl+V[/]            — Paste");
    host.AddMessage("  [cyan]Ctrl+X[/]            — Cut selected");
    host.AddMessage("  [cyan]Ctrl+Z[/]            — Undo");
    host.AddMessage("  [cyan]Ctrl+A[/]            — Select all");
    host.AddMessage("  [cyan]Home / End[/]        — Line start/end");
    host.AddMessage("");
    host.AddMessage("[yellow]Try pasting >100 chars to see attachment detection[/]");
    return Task.CompletedTask;
}));

host.AddMessage("[bold]Text Editing Demo[/]");
host.AddMessage("[yellow]Type text, use arrows, ctrl, home/end, Shift+arrows to select[/]");
host.AddMessage("[yellow]Paste a large block (>100 chars) to trigger attachment[/]");
host.AddMessage("[yellow]Type /help for full controls[/]");

await host.Run();
