using Spectre.Console;
using StreamShell;

// ── 05-SelectionPanels ─────────────────────────────────────────────
// Demonstrates PromptSelection with single and multi-select variants.

using var host = new ConsoleAppHost();

var planets = new IVariantEntry[]
{
    new Variant("[red]Mars[/]"),
    new Variant("[yellow]Venus[/]"),
    new Variant("[blue]Neptune[/]"),
    new Variant("[grey]Mercury[/]"),
};

var toppings = new IVariantEntry[]
{
    new Variant("[yellow]Cheese[/]"),
    new Variant("[red]Pepperoni[/]"),
    new Variant("[green]Mushrooms[/]"),
    new Variant("[cyan]Olives[/]"),
    new Variant("[magenta]Onions[/]"),
    new Variant("[orange1]Pineapple[/]"),
};

var difficulty = new IVariantEntry[]
{
    new Variant("[green]Easy[/]"),
    new Variant("[yellow]Medium[/]"),
    new Variant("[red]Hard[/]"),
    new Variant("[bold][red]Nightmare[/][/]"),
};

// ── Commands ──

host.AddCommand(new Command("planet", "Pick a planet (single-select)", async (_, _) =>
{
    var result = await host.PromptSelection("Choose your destination", planets);
    if (result is null)
        host.AddMessage("[yellow]Mission cancelled[/]");
    else
        host.AddMessage($"[green]Destination set: {result[0].Name}[/]");
}));

host.AddCommand(new Command("pizza", "Customize your pizza (multi-select, min 1, max 4)", async (_, _) =>
{
    var result = await host.PromptSelection("Build your pizza [dim](1-4 toppings)[/]", toppings,
        new SelectionInfo { Min = 1, Max = 4 });
    if (result is null)
    {
        host.AddMessage("[yellow]Pizza cancelled[/]");
        return;
    }
    var names = string.Join(", ", result.Select(v => v.Name));
    host.AddMessage($"[green]Pizza ordered with: [bold]{names}[/][/]");
}));

host.AddCommand(new Command("difficulty", "Pick difficulty (single-select)", async (_, _) =>
{
    var result = await host.PromptSelection("Select difficulty", difficulty);
    if (result is null)
        host.AddMessage("[yellow]Game cancelled[/]");
    else
        host.AddMessage($"[green]Difficulty set to: {result[0].Name}[/]");
}));

host.AddCommand(new Command("colors", "Pick colors (multi-select, min 2)", async (_, _) =>
{
    var colors = new IVariantEntry[]
    {
        new Variant("[red]Red[/]"),
        new Variant("[green]Green[/]"),
        new Variant("[blue]Blue[/]"),
        new Variant("[yellow]Yellow[/]"),
        new Variant("[magenta]Magenta[/]"),
    };

    var result = await host.PromptSelection("Choose at least 2 colors", colors,
        new SelectionInfo { Min = 2 });
    if (result is null)
        host.AddMessage("[yellow]No colors chosen[/]");
    else
        host.AddMessage($"[green]Colors: {string.Join(", ", result.Select(v => v.Name))}[/]");
}));

host.AddCommand(new Command("weapon", "Choose weapon (decorations demo)", async (_, _) =>
{
    var weapons = new IVariantEntry[]
    {
        new Decoration("-- Range weapons --"),
        new Variant("[yellow]Bow[/]"),
        new Variant("[yellow]Gun[/]"),
        new Decoration("-- Melee weapons --"),
        new Variant("[cyan]Sword[/]"),
        new Variant("[cyan]Axe[/]"),
    };

    var result = await host.PromptSelection("Choose your weapon", weapons);
    if (result is null)
        host.AddMessage("[yellow]No weapon chosen[/]");
    else
        host.AddMessage($"[green]Weapon: {result[0].Name}[/]");
}));

host.AddCommand(new Command("help", "Show available commands", (_, _) =>
{
    host.AddMessage("[bold underline]Selection Panel Demos[/]");
    host.AddMessage("  [cyan]/weapon[/]     — Decorations demo (categories between variants)");
    host.AddMessage("  [cyan]/planet[/]     — Single-select (Enter to choose)");
    host.AddMessage("  [cyan]/pizza[/]       — Multi-select (Enter toggle, Space submit)");
    host.AddMessage("  [cyan]/difficulty[/]  — Single-select difficulty");
    host.AddMessage("  [cyan]/colors[/]      — Multi-select with minimum constraint");
    return Task.CompletedTask;
}));

host.AddCommand(new Command("exit", "Exit", (_, _) => { host.Stop(); return Task.CompletedTask; }));

host.AddMessage("[bold]Selection Panel Demo[/]");
host.AddMessage("[yellow]Try [cyan]/weapon[/] for decorations, [cyan]/planet[/] for single-select, [cyan]/pizza[/] for multi-select[/]");
host.AddMessage("[yellow]Navigate with arrows, Enter to pick, Escape to cancel[/]");
host.AddMessage("[yellow]Multi-select: Enter toggles, Space submits[/]");

await host.Run();

// ── Variants & Decorations ──
class Variant : IVariant
{
    public string Name { get; }
    public Variant(string name) => Name = name;
}

class Decoration : IDecoration
{
    public string Name { get; }
    public Decoration(string name) => Name = name;
}
