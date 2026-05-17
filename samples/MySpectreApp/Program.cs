using Spectre.Console;
using StreamShell;

using var host = new ConsoleAppHost();

// Lower threshold for testing large paste detection
host.Settings.LargePasteThreshold = 200;
host.Settings.RenderChunkSize = 5;

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

// ════════════════════════════════════════════════════════════════
//  Config command with argument autocomplete
// ════════════════════════════════════════════════════════════════

host.AddCommand("config", "Shows or sets a config value",
    (args, named) =>
    {
        if (args.Length == 0)
        {
            host.AddMessage("[yellow]Usage: /config <name> [value][/]");
            return Task.CompletedTask;
        }

        string name = args[0];
        string value = args.Length > 1 ? args[1] : "(current)";
        host.AddMessage($"[green]Config [bold]{Markup.Escape(name)}[/] = [cyan]{Markup.Escape(value)}[/][/]");
        return Task.CompletedTask;
    },
    [
        "LargePasteThreshold",
        "CursorMarkup",
        "SelectionMarkup",
        "InputPrefix",
        "ContinuationPrefix",
        "WrappingRightMargin"
    ]);

// ════════════════════════════════════════════════════════════════
//  Demo command showing multi-word argument completions
// ════════════════════════════════════════════════════════════════

host.AddCommand("demo", "Demo command with argument completions",
    (args, named) =>
    {
        string joined = string.Join(" ", args);
        host.AddMessage($"[green]Demo executed with: [cyan]{Markup.Escape(joined)}[/][/]");
        return Task.CompletedTask;
    },
    [
        "pc",
        "mac",
        "linux ubuntu",
        "linux fedora",
        "linux arch",
        "windows 10",
        "windows 11"
    ]);

// ════════════════════════════════════════════════════════════════
//  Panel swap command — toggle between CommandPalette and CharacterCounterPanel
// ════════════════════════════════════════════════════════════════

var counterPanel = new CharacterCounterPanel();
bool useCounterPanel = false;

host.AddCommand(new Command("panel", "Toggle bottom panel (CommandPalette / CharacterCounter)", (_, _) =>
{
    useCounterPanel = !useCounterPanel;
    if (useCounterPanel)
    {
        host.SetBottomPanel(counterPanel);
        host.AddMessage($"[green]Panel: CharacterCounterPanel ({counterPanel.LineCount} lines)[/]");
    }
    else
    {
        host.ResetBottomPanel();
        host.AddMessage($"[green]Panel: CommandPalette restored[/]");
    }
    return Task.CompletedTask;
}));

// ════════════════════════════════════════════════════════════════
//  Separator test command
// ════════════════════════════════════════════════════════════════

host.AddCommand(new Command("top-sep", Markup.Escape("Set top separator. Usage: /top-sep [left] [right] [char] [markup]"), (args, named) =>
{
    string? left = args.Length > 0 ? args[0] : null;
    string? right = args.Length > 1 ? args[1] : null;
    char fill = args.Length > 2 && args[2].Length > 0 ? args[2][0] : '-';
    string? markup = args.Length > 3 ? args[3] : null;

    host.SetTopSeparator(left, right, fill, markup);
    host.AddMessage($"[green]Top separator updated: [dim]left=[/]{Markup.Escape(left ?? "(none)")}[dim] right=[/]{Markup.Escape(right ?? "(none)")}[dim] fill=[/]'{fill}'[dim] markup=[/]{Markup.Escape(markup ?? "(none)")}[/]");
    return Task.CompletedTask;
}));

host.AddCommand(new Command("bot-sep", Markup.Escape("Set bottom separator. Usage: /bot-sep [left] [right] [char] [markup]"), (args, named) =>
{
    string? left = args.Length > 0 ? args[0] : null;
    string? right = args.Length > 1 ? args[1] : null;
    char fill = args.Length > 2 && args[2].Length > 0 ? args[2][0] : '-';
    string? markup = args.Length > 3 ? args[3] : null;

    host.SetBottomSeparator(left, right, fill, markup);
    host.AddMessage($"[green]Bottom separator updated: [dim]left=[/]{Markup.Escape(left ?? "(none)")}[dim] right=[/]{Markup.Escape(right ?? "(none)")}[dim] fill=[/]'{fill}'[dim] markup=[/]{Markup.Escape(markup ?? "(none)")}[/]");
    return Task.CompletedTask;
}));

// ════════════════════════════════════════════════════════════════
//  Default panel test — toggle between EmptyBottomPanel and CharacterCounterPanel
// ════════════════════════════════════════════════════════════════

bool useCounterAsDefault = false;

host.AddCommand(new Command("default", Markup.Escape("Toggle default panel (EmptyBottomPanel / CharacterCounterPanel)"), (_, _) =>
{
    useCounterAsDefault = !useCounterAsDefault;
    if (useCounterAsDefault)
    {
        host.SetDefaultPanel(new CharacterCounterPanel());
        host.AddMessage("[green]Default panel: CharacterCounterPanel[/]");
    }
    else
    {
        host.SetDefaultPanel(new EmptyBottomPanel());
        host.AddMessage("[green]Default panel: EmptyBottomPanel[/]");
    }
    return Task.CompletedTask;
}));

// ════════════════════════════════════════════════════════════════
//  Selection test commands (PromptSelection)
// ════════════════════════════════════════════════════════════════

var osVariants = new IVariantEntry[]
{
    new Variant("[bold]Windows[/] 11"),
    new Variant("[bold][green]Linux[/][/] Ubuntu"),
    new Variant("macOS [blue]Ventura[/]"),
    new Variant("[grey]FreeBSD[/]"),
};

var toolVariants = new IVariantEntry[]
{
    new Variant("Sword"),
    new Variant("Shield"),
    new Variant("Bow"),
    new Variant("Axe"),
    new Variant("Staff"),
};

var colorVariants = new IVariantEntry[]
{
    new Variant("[red]Red[/]"),
    new Variant("[green]Green[/]"),
    new Variant("[blue]Blue[/]"),
    new Variant("[yellow]Yellow[/]"),
    new Variant("[magenta]Magenta[/]"),
    new Variant("[cyan]Cyan[/]"),
};

host.AddCommand(new Command("pick", "Single-select an OS via PromptSelection", async (_, _) =>
{
    var selected = await host.PromptSelection("Pick an OS", osVariants);
    if (selected is null)
        host.AddMessage("[yellow]Selection cancelled[/]");
    else
        host.AddMessage($"[green]You picked: [bold]{Markup.Escape(selected[0].Name)}[/][/]");
}));

host.AddCommand(new Command("multi", "Multi-select tools (min 1, max 3) via PromptSelection", async (_, _) =>
{
    var selected = await host.PromptSelection("Select your tools [dim](1-3)[/]", toolVariants,
        new SelectionInfo { Min = 1, Max = 3 });
    if (selected is null)
    {
        host.AddMessage("[yellow]Cancelled tool selection[/]");
        return;
    }
    var names = string.Join(", ", selected.Select(v => Markup.Escape(v.Name)));
    host.AddMessage($"[green]Equipped: [bold]{names}[/][/]");
}));

host.AddCommand(new Command("colors", "Multi-select colors (min 2) via PromptSelection", async (_, _) =>
{
    var selected = await host.PromptSelection("Choose at least 2 colors", colorVariants,
        new SelectionInfo { Min = 2 });
    if (selected is null)
    {
        host.AddMessage("[yellow]Cancelled color selection[/]");
        return;
    }
    var names = string.Join(", ", selected.Select(v => Markup.Escape(v.Name)));
    host.AddMessage($"[green]Colors chosen: [bold]{names}[/][/]");
}));

// ════════════════════════════════════════════════════════════════
//  Input Field Save / Load test commands
// ════════════════════════════════════════════════════════════════

// ── /save <content> — feed text into the input buffer, save it, return ID ──
host.AddCommand(new Command("save", "Save text as input field state. Usage: /save <content>", (args, named) =>
{
    string content = string.Join(" ", args);

    if (string.IsNullOrWhiteSpace(content))
    {
        // Save whatever is currently in the buffer (will be empty after submit)
        string id = host.InputHandler.SaveInputField();
        host.AddMessage($"[yellow]Saved empty buffer as #{id}[/]");
        return Task.CompletedTask;
    }

    host.InputHandler.SetInputFieldContent(content);
    string savedId = host.InputHandler.SaveInputField();
    host.InputHandler.Reset();
    host.AddMessage($"[green]Saved as #[bold]{savedId}[/][/] [grey]\"{Markup.Escape(TruncatePreview(content, 40))}\"[/]");
    return Task.CompletedTask;
}));

// ── /load <id> — restore a saved state back into the input field ──
host.AddCommand(new Command("load", "Restore a saved input field by ID. Usage: /load <id>", (args, named) =>
{
    if (args.Length == 0)
    {
        host.AddMessage("[red]Usage: /load <id>[/]");
        return Task.CompletedTask;
    }

    string id = args[0];
    if (host.InputHandler.LoadInputField(id))
        host.AddMessage($"[green]Loaded saved state #[bold]{id}[/] into input field. Type more or submit with Enter.[/]");
    else
        host.AddMessage($"[red]No saved state with ID '{Markup.Escape(id)}'[/]");
    return Task.CompletedTask;
}));

// ── /unsave <id> — remove a single saved state ──
host.AddCommand(new Command("unsave", "Remove a saved input field by ID. Usage: /unsave <id>", (args, named) =>
{
    if (args.Length == 0)
    {
        host.AddMessage("[red]Usage: /unsave <id>[/]");
        return Task.CompletedTask;
    }

    string id = args[0];
    if (host.InputHandler.RemoveSavedInputField(id))
        host.AddMessage($"[yellow]Removed saved state #[bold]{id}[/][/]");
    else
        host.AddMessage($"[red]No saved state with ID '{Markup.Escape(id)}'[/]");
    return Task.CompletedTask;
}));

// ── /clear-saved — remove all saved states ──
host.AddCommand(new Command("clear-saved", "Remove all saved input field states", (_, _) =>
{
    host.InputHandler.RemoveAllSavedInputFields();
    host.AddMessage("[yellow]All saved input field states cleared[/]");
    return Task.CompletedTask;
}));

// ── /saved — list all saved states with previews ──
host.AddCommand(new Command("saved", "List all saved input field states", (_, _) =>
{
    var ids = host.InputHandler.GetSavedInputFieldIds();
    if (ids.Count == 0)
    {
        host.AddMessage("[grey]No saved input field states[/]");
    }
    else
    {
        host.AddMessage($"[bold]Saved input fields ({ids.Count}):[/]");
        // Load each one snapshot-style to show a preview (destructive, so save current first)
        // Instead, we just list IDs with a hint to use /load
        foreach (var id in ids)
        {
            host.AddMessage($"  [cyan]#{id}[/] — use [grey]/load {id}[/] to restore");
        }
    }
    return Task.CompletedTask;
}));

var suggestions = new string[] {"DirectLlmApiType",
"DirectLlmModelName",   
"DirectLlmToken",
"DirectLlmUrl"};

var appConfigCommand = new Command("[yellow]appconfig[/]", "Configuring app", (_, _) => { return Task.CompletedTask; }, suggestions);

host.AddCommand(appConfigCommand);



// ════════════════════════════════════════════════════════════════

host.UserInputSubmitted += args =>
{
    host.AddMessage("[green]USER:[/] [cyan]" + args.InputType + "[/]");
    host.AddMessage("  [grey]\"" + Markup.Escape(args.TextWithAttachmentsExpanded) + "\"[/]");
    foreach (var att in args.Attachments)
    {   
        host.AddMessage("  [grey][[attachment: " + att.Type + ", " + att.LineCount + " lines, " + att.Content.Length + " chars]][/]");
    }
};

host.SetDefaultPanel(new CharacterCounterPanel());


_ = Task.Run(async () =>
{
    var random = new Random();
    int i = 0;
    while (true)
    {
        await Task.Delay(200);
        var separator = i++ % 2 == 0 ? '-' : '─';
        host.SetTopSeparator(null, null, separator);
    }
});


_ = Task.Run(async () =>
{
    var random = new Random();
    int i = 0;
    while (true)
    {

        await Task.Delay(1000);
        host.AddMessage("───▄▄▄");
        host.AddMessage("─▄▀░▄░▀▄");
        host.AddMessage("─█░█▄▀░█");
        host.AddMessage("─█░▀▄▄▀█▄█▄▀");
        host.AddMessage("▄▄█▄▄▄▄███▀");
        host.AddMessage("───────────");
    }
});

_ = Task.Run(async () =>
{
    var random = new Random();
    int i = 0;
    while (true)
    {
        await Task.Delay(2000);
        var lines = random.Next(1, 20);
        for (int j = 0; j < lines; j++)
            host.AddMessage("[grey][[" + DateTime.Now.ToString("HH:mm:ss") + "]][/] Background Event #" + (++i));
    }
});

host.AddMessage("[yellow]StreamShell demo started. Type text or commands like /context[/]");
host.AddMessage("[yellow]Large paste (>200 chars) will be attached as file[/]");
host.AddMessage("[yellow]Try [bold]/config[/] [grey]Large[/] (Tab autocomplete for config names)[/]");
host.AddMessage("[yellow]Try [bold]/demo[/] [grey]li[/] (Tab for multi-word arg completions)[/]");
host.AddMessage("[yellow]Try [bold]/pick[/] [grey](single-select PromptSelection)[/][/]");
host.AddMessage("[yellow]Try [bold]/multi[/] [grey](multi-select 1-3 tools)[/][/]");
host.AddMessage("[yellow]Try [bold]/colors[/] [grey](multi-select min 2 colors)[/][/]");
host.AddMessage("[yellow]Try [bold]/top-sep[/] [grey]<left> <right> <char> <markup> (top separator)[/][/]");
host.AddMessage("[yellow]Try [bold]/bot-sep[/] [grey]<left> <right> <char> <markup> (bottom separator)[/][/]");
host.AddMessage("[yellow]Try [bold]/default[/] [grey](toggle default bottom panel)[/][/]");
host.AddMessage("");
host.AddMessage("[yellow]--- Save/Load test ---[/]");
host.AddMessage("[yellow]/save <text>  — save text as input field state[/]");
host.AddMessage("[yellow]/load <id>    — restore state into input field[/]");
host.AddMessage("[yellow]/unsave <id>  — remove a saved state[/]");
host.AddMessage("[yellow]/clear-saved  — remove all saved states[/]");
host.AddMessage("[yellow]/saved        — list saved states[/]");

host.SetTopSeparator("[white]—[/]", "status:[green]ONLINE[/]———", '—', "white");

await host.Run();

// ── helpers ──
static string TruncatePreview(string text, int maxLen)
    => text.Length <= maxLen ? text : text[..maxLen] + "...";

// ── Variant helper ──
class Variant : IVariant
{
    public string Name { get; }
    public Variant(string name) => Name = name;
}

// ── Custom bottom panel ──
class CharacterCounterPanel : IBottomPanel
{
    public int LineCount => _lastCount;
    private readonly string[] _lines = new string[3];
    private string? _lastInput;

    private bool _isDirty = false;

    public bool IsDirty => _isDirty;

    private int _lastCount = 0;

    public CharacterCounterPanel()
    {
        _lastCount = 1;
    }

    public IReadOnlyList<string> GetLines(string currentInput)
    {
        _lastInput = currentInput;

        if (currentInput.Length == 0)
        {
            _lastCount = 1;
            return Enumerable.Repeat("", _lastCount).ToArray();
        }
        else
        {
            _lines[0] = "";  // No suggestion
            _lines[1] = "[bold]Character Counter[/]";
            _lines[2] = string.IsNullOrEmpty(currentInput)
                ? "[dim]Type something...[/]"
                : $"[grey]Input length: [green]{currentInput.Length}[/][/]";
            return _lines;
        }
    }

    public void Dispose() { }
}
