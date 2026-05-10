using Spectre.Console;
using StreamShell;

// ── 04-TextEditing ─────────────────────────────────────────────────
// Demonstrates cursor movement, selection, clipboard, large paste
// detection, and save/load of input field states.

using var host = new ConsoleAppHost();

// Lower threshold so large pastes trigger in the demo
host.Settings.LargePasteThreshold = 100;

// ── Commands that show editing features ──

host.AddCommand(new Command("save", "Save current input as a named state. Usage: /save <name>",
    (args, named) =>
    {
        string name = args.Length > 0 ? args[0] : "default";

        // Feed content into input field, save it, then clear
        host.InputHandler.SetInputFieldContent(
            string.Join(" ", args.Skip(1)));
        string id = host.InputHandler.SaveInputField();
        host.InputHandler.Reset();
        host.AddMessage($"[green]Saved as [bold]#{Markup.Escape(name ?? id)}[/][/]");
        return Task.CompletedTask;
    }));

host.AddCommand(new Command("load", "Load a saved input field. Usage: /load <id>",
    (args, _) =>
    {
        string id = args.Length > 0 ? args[0] : "default";
        if (host.InputHandler.LoadInputField(id))
            host.AddMessage($"[green]Loaded #[/][cyan]{Markup.Escape(id)}[/]");
        else
            host.AddMessage($"[red]No saved state #[bold]{Markup.Escape(id)}[/][/]");
        return Task.CompletedTask;
    }));

host.AddCommand(new Command("clip", "Demo clipboard by setting input to sample text",
    (_, _) =>
    {
        host.InputHandler.SetInputFieldContent("This text was set programmatically — try Ctrl+C to copy, then clear and Ctrl+V to paste.");
        host.AddMessage("[green]Input field populated. Try copy/paste![/]");
        return Task.CompletedTask;
    }));

host.AddCommand(new Command("undo-demo", "Demonstrate undo by setting multi-word text",
    (_, _) =>
    {
        host.InputHandler.SetInputFieldContent("Type something, then press Ctrl+Z to undo");
        host.AddMessage("[yellow]Now press Ctrl+Z to undo characters[/]");
        return Task.CompletedTask;
    }));

host.AddCommand(new Command("help", "Show editing controls", (_, _) =>
{
    host.AddMessage("[bold underline]Editing Controls[/]");
    host.AddMessage("  [cyan]\u2190 \u2192[/]             — Move cursor");
    host.AddMessage("  [cyan]Shift+\u2190 \u2192[/]        — Select text");
    host.AddMessage("  [cyan]Ctrl+C[/]            — Copy selected");
    host.AddMessage("  [cyan]Ctrl+V[/]            — Paste");
    host.AddMessage("  [cyan]Ctrl+X[/]            — Cut selected");
    host.AddMessage("  [cyan]Ctrl+Z[/]            — Undo");
    host.AddMessage("  [cyan]Home / End[/]        — Line start/end");
    host.AddMessage("  [cyan]Ctrl+L / Ctrl+R[/]   — Load / Rename saved");
    host.AddMessage("");
    host.AddMessage("[yellow]Try pasting >100 chars to see attachment detection[/]");
    return Task.CompletedTask;
}));

host.UserInputSubmitted += args =>
{
    foreach (var att in args.Attachments)
        host.AddMessage($"[yellow]Attachment detected: {att.Type}, {att.Content.Length} chars, {att.LineCount} lines[/]");
};

host.AddMessage("[bold]Text Editing Demo[/]");
host.AddMessage("[yellow]Type text, use arrows, Shift+arrows to select[/]");
host.AddMessage("[yellow]Paste a large block (>100 chars) to trigger attachment[/]");
host.AddMessage("[yellow]Type /help for full controls[/]");

await host.Run();
