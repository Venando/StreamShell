# StreamShell

[![NuGet](https://img.shields.io/nuget/v/StreamShell)](https://www.nuget.org/packages/StreamShell/)
[![Downloads](https://img.shields.io/nuget/dt/StreamShell)](https://www.nuget.org/packages/StreamShell/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com)

**An interactive, markup-capable console host for .NET — type, render, and command without the plumbing.**

---

## Hero Demo

> [!INSERT GIF]
> **GIF insert request:** Animated demo showing the full StreamShell experience — launching the app, typing text, pressing Enter to submit, typing `/hello` to run a command with rendered output, using Tab for autocomplete, and navigating history with arrow keys. Record at 120×30 chars, ~10s, ~2MB.
>
> Suggested tool: [VHS](https://github.com/charmbracelet/vhs) or [asciinema](https://asciinema.org/) + [agg](https://github.com/asciinema/agg)

<p align="center">
  <img src="docs/assets/demo.gif" alt="StreamShell demo" width="700" />
</p>

---

## What Is This?

`StreamShell` is a .NET library you embed into your console app to get a fully interactive terminal session — structured prompt loop, Spectre.Console markup rendering, and command dispatch — without writing the plumbing yourself.

**It's for you if:**

- You're building a .NET CLI tool that needs a live, stateful REPL-style session
- You want styled terminal output (colors, tables, progress) without a full TUI framework
- You need inline text editing with cursor navigation, selection, clipboard, and undo
- You want to display interactive selection panels for picking options at runtime
- You need a prompt host, not a full-screen app

**It's NOT:**

- A full TUI framework (no panels, layouts, or mouse support — see [Terminal.Gui](https://github.com/gui-cs/Terminal.Gui) for that)
- A command-line argument parser (see [System.CommandLine](https://github.com/dotnet/command-line-api))
- A replacement for `dotnet run` or shell scripting

---

## Features

### ✨ Markup Rendering

Display colored, styled terminal output using Spectre.Console markup syntax. Queued messages are rendered in-line as they arrive, with the input block automatically shifting down.

```
host.AddMessage("[green]Success![/]");
host.AddMessage("[bold][red]Error:[/] something went wrong[/]");
```

> [!INSERT GIF]
> **GIF insert request:** Show multi-line messaging — emit several messages with different colors (green, red, yellow, bold) and watch the input line gracefully move down as messages fill the screen.

<p align="center">
  <img src="docs/assets/markup.gif" alt="Markup rendering demo" width="680" />
</p>

### ✨ Command Dispatch with Autocomplete

Register commands with `/name`. Type `/` and the command palette opens automatically with hints. Tab to autocomplete, or continue typing to filter.

```csharp
host.AddCommand(new Command("hello", "Say hello", (args, named) =>
{
    host.AddMessage("[green]Hello, world![/]");
    return Task.CompletedTask;
}));
```

Commands can also register argument-level autocomplete suggestions:

```csharp
host.AddCommand("config", "Set a config value",
    (args, named) => { /* handler */ },
    ["LargePasteThreshold", "CursorMarkup", "SelectionMarkup"]);
```

> [!INSERT GIF]
> **GIF insert request:** Type `/he`, press Tab to complete to `/hello`, press Enter. Then type `/config` and Tab through argument suggestions. Show the hint palette appearing below the input line.

<p align="center">
  <img src="docs/assets/autocomplete.gif" alt="Command autocomplete demo" width="680" />
</p>

### ✨ Inline Text Editing

Full editing experience while typing — cursor movement, text selection with Shift+arrows, clipboard integration (Ctrl+C / Ctrl+V / Ctrl+X), undo (Ctrl+Z), and multi-line input wrapping.

Paste a large block of text and it's automatically detected and attached as a separate attachment object rather than flooding the input buffer.

> [!INSERT GIF]
> **GIF insert request:** Show cursor navigation with arrow keys, text selection with Shift+arrows (highlighted in selection color), Ctrl+C to copy, then Ctrl+V to paste at a different position. Then paste a large block and show the attachment placeholder.

<p align="center">
  <img src="docs/assets/editing.gif" alt="Text editing demo" width="680" />
</p>

### ✨ Interactive Selection Panels

Prompt users with a navigable selection panel at the bottom of the console. Arrow keys to navigate, Enter to select, Escape to cancel. Supports both single-select and multi-select modes with min/max constraints.

```csharp
var selected = await host.PromptSelection("Pick an OS", osVariants);
```

> [!INSERT GIF]
> **GIF insert request:** Show `/pick` command triggering a selection panel with OS names. Navigate with Up/Down arrows, press Enter on one entry, and see the result printed as a message. Then show `/multi` with tool selection — toggle multiple items, then press Space to submit.

<p align="center">
  <img src="docs/assets/selection.gif" alt="Selection panel demo" width="680" />
</p>

### ✨ Customizable Separators

Add styled separators between the message feed, input block, and hint panel. Each separator supports left/right text labels and configurable fill characters.

```csharp
host.SetTopSeparator("Messages", "StreamShell", '-', "white");
host.SetBottomSeparator("Input", null, '\u2500');
```

> [!INSERT GIF]
> **GIF insert request:** Show separators appearing between message area and input line after calling `SetTopSeparator` and `SetBottomSeparator`. Then change separator text and characters via the `/top-sep` command.

<p align="center">
  <img src="docs/assets/separators.gif" alt="Separator demo" width="680" />
</p>

### ✨ Extensible Bottom Panels

The hint area below the input is fully customizable via the `IBottomPanel` interface. Create panels that show character counters, status info, dynamic suggestions, or any custom content.

```csharp
public class StatusPanel : IBottomPanel
{
    public int LineCount => 2;
    public IReadOnlyList<string> GetLines(string input)
    {
        return ["", $"[dim]Characters: {input.Length}[/]"];
    }
}

host.SetDefaultPanel(new StatusPanel());
```

> [!INSERT GIF]
> **GIF insert request:** Show a custom bottom panel that displays character count, updating in real-time as the user types. Then toggle back to the default panel.

<p align="center">
  <img src="docs/assets/panels.gif" alt="Custom panel demo" width="680" />
</p>

### ✨ Input Field Save/Load

Save and restore input field states by ID. Useful for preserving partially-typed input during operations that need to clear the buffer temporarily.

```csharp
string id = host.InputHandler.SaveInputField();     // save current text
host.InputHandler.LoadInputField(id);                // restore it later
host.InputHandler.RemoveSavedInputField(id);         // dispose
```

> [!INSERT GIF]
> **GIF insert request:** Show `/save some text` — then type something else, then `/load <id>` and watch the original text reappear in the input field.

<p align="center">
  <img src="docs/assets/saveload.gif" alt="Input save/load demo" width="680" />
</p>

---

## Installation

```bash
dotnet add package StreamShell
```

Or via Package Manager Console:

```powershell
Install-Package StreamShell
```

**Requires:** .NET 10.0 or later

> NuGet page: [https://www.nuget.org/packages/StreamShell/](https://www.nuget.org/packages/StreamShell/)

---

## Quick Start

```csharp
using StreamShell;

var host = new ConsoleAppHost();

host.AddCommand(new Command("hello", "Say hello", (args, named) =>
{
    host.AddMessage("[green]Hello, world![/]");
    return Task.CompletedTask;
}));

host.AddCommand(new Command("quit", "Exit the app", (_, _) =>
{
    host.Stop();
    return Task.CompletedTask;
}));

host.UserInputSubmitted += input =>
{
    host.AddMessage($"[grey]You said:[/] {input.RawOutput}");
};

host.AddMessage("[yellow]StreamShell ready. Type /hello or /quit[/]");
await host.Run();
```

That's it. Run the app, type `/hello`, press Enter — you'll see `Hello, world!` rendered in green. Type anything else to see your input echoed back.

---

## Usage

### Registering Commands

Commands are triggered by typing `/name`. Register them with either the `Command` class:

```csharp
host.AddCommand(new Command("echo", "Echo your message", (args, named) =>
{
    string message = string.Join(" ", args);
    host.AddMessage($"[cyan]{Markup.Escape(message)}[/]");
    return Task.CompletedTask;
}));
```

Or the fluent overload with argument suggestions:

```csharp
host.AddCommand("greet", "Greet someone", async (args, named) =>
{
    string name = args.Length > 0 ? args[0] : "world";
    host.AddMessage($"[green]Hello, {Markup.Escape(name)}![/]");
}, ["friend", "colleague", "boss"]);
```

### Handling Submitted Input

The `UserInputSubmitted` event fires for both commands and plain text. Check `InputType` to differentiate:

```csharp
host.UserInputSubmitted += args =>
{
    if (args.InputType == InputType.PlainText)
        host.AddMessage($"[grey]Text:[/] {Markup.Escape(args.RawOutput)}");

    foreach (var att in args.Attachments)
        host.AddMessage($"[grey][[{att.Type}: {att.Content.Length} chars]][/]");
};
```

### Selection Panels

Prompt the user to pick from a list of options. `PromptSelection` returns an array of `IVariant[]` or null if cancelled:

```csharp
var options = new IVariant[]
{
    new VariantItem("[bold]Option A[/]"),
    new VariantItem("[green]Option B[/]"),
};

var result = await host.PromptSelection("Choose one", options);
if (result is not null)
    host.AddMessage($"[green]Selected: {Markup.Escape(result[0].Name)}[/]");
```

For multi-select, pass a `SelectionInfo`:

```csharp
// Min 1, Max 3 selections
var result = await host.PromptSelection("Pick tools", toolVariants,
    new SelectionInfo { Min = 1, Max = 3 });
```

### Custom Bottom Panels

Implement `IBottomPanel` to replace the hint area below the input:

```csharp
public class StatusPanel : IBottomPanel
{
    public int LineCount => 2;
    public IReadOnlyList<string> GetLines(string currentInput)
    {
        return ["", $"[dim]Length: {currentInput.Length} chars[/]"];
    }
}

// Set as the default panel (shown when no command is active)
host.SetDefaultPanel(new StatusPanel());

// Or set as the active panel immediately
host.SetBottomPanel(new StatusPanel());

// Restore the built-in empty panel
host.ResetBottomPanel();
```

### Async Commands

Command handlers support async operations natively:

```csharp
host.AddCommand(new Command("fetch", "Fetch data", async (_, _) =>
{
    host.AddMessage("[yellow]Fetching...[/]");
    await Task.Delay(1000);
    host.AddMessage("[green]Data received![/]"));
}));
```

### Cancellation and Shutdown

- **Ctrl+C** is reserved for clipboard Copy (not cancellation)
- **Ctrl+D** exits the session gracefully
- Call `host.Stop()` from any command to terminate
- Pass a `CancellationToken` to `Run()` for external cancellation

---

## Configuration

| Option | Type | Default | Description |
|---|---|---|---|
| `LargePasteThreshold` | `int` | `300` | Max chars before input is treated as a large paste (attachment) |
| `LargePasteLineThreshold` | `int` | `4` | Max lines before input is treated as a large paste |
| `CursorMarkup` | `string` | `"bold black on cyan"` | Spectre markup for cursor highlight |
| `SelectionMarkup` | `string` | `"bold cyan on Grey27"` | Spectre markup for selected text |
| `CommandSlashMarkup` | `string` | `"Red1"` | Spectre markup for the command slash (`/`) |
| `InputPrefix` | `string` | `"[bold SkyBlue1]> [/]"` | Spectre markup for the input prompt prefix |
| `ContinuationPrefix` | `string` | `"  "` | Plain text prefix for wrapped continuation lines |
| `WrappingRightMargin` | `int` | `4` | Right-edge buffer for text wrapping |

Configure via the host's `Settings` property before running:

```csharp
var host = new ConsoleAppHost();
host.Settings.LargePasteThreshold = 500;
host.Settings.CursorMarkup = "bold white on blue";
host.Settings.InputPrefix = "[bold green]$ [/]";
```

---

## Markup Reference

StreamShell uses Spectre.Console markup for all styled output. Common tags:

| Tag | Effect | Example |
|---|---|---|
| `[bold]...[/]` | Bold text | `[bold]Important[/]` |
| `[red]...[/]` | Red foreground | `[red]Error[/]` |
| `[green]...[/]` | Green foreground | `[green]Success[/]` |
| `[cyan]...[/]` | Cyan foreground | `[cyan]Info[/]` |
| `[yellow]...[/]` | Yellow foreground | `[yellow]Warning[/]` |
| `[grey]...[/]` | Grey foreground | `[grey]debug[/]` |
| `[dim]...[/]` | Dimmed text | `[dim]optional[/]` |
| `[bg:blue]...[/]` | Blue background | `[bg:blue]Highlighted[/]` |

Markup can be nested: `[bold][red]Bold red text[/][/]`

> **Important:** Always wrap untrusted content (user input, dynamic values) with `Markup.Escape()` to prevent broken markup:
> ```csharp
> host.AddMessage($"[green]User: {Markup.Escape(userInput)}[/]");
> ```

---

## Recipes

### Build a simple REPL

```csharp
var host = new ConsoleAppHost();
host.AddCommand(new Command("eval", "Evaluate an expression", (args, _) =>
{
    string expr = string.Join(" ", args);
    host.AddMessage($"[cyan]Evaluated: {expr}[/]");
    return Task.CompletedTask;
}));
await host.Run();
```

### Add dynamic tab completion for a specific command

```csharp
var completions = new[] { "apple", "banana", "cherry" };
host.AddCommand("fruit", "Pick a fruit",
    (args, _) =>
    {
        string picked = args.Length > 0 ? args[0] : "none";
        host.AddMessage($"[green]You picked: {picked}[/]");
        return Task.CompletedTask;
    },
    completions);
```

### Log all commands to a file

```csharp
host.UserInputSubmitted += args =>
{
    if (args.InputType == InputType.Command)
        File.AppendAllText("commands.log",
            $"{DateTime.Now:O} {args.RawOutput}{Environment.NewLine}");
};
```

### Intercept and handle background events

```csharp
_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(5000);
        host.AddMessage("[grey][[heartbeat: OK]][/]");
    }
});
```

### Exit with a non-zero code on error

```csharp
host.AddCommand(new Command("fail", "Exit with error", (_, _) =>
{
    host.AddMessage("[red]Fatal error![/]");
    Environment.ExitCode = 1;
    host.Stop();
    return Task.CompletedTask;
}));
```

---

## How It Compares

| | StreamShell | Raw `Console.ReadLine()` | Spectre.Console | Terminal.Gui |
|---|---|---|---|---|
| Drop-in prompt loop | ✅ | ❌ | ❌ | ❌ |
| Markup/styling | ✅ (Spectre) | ❌ | ✅ | ✅ |
| Command routing | ✅ | ❌ | ❌ | ❌ |
| Inline text editing | ✅ | ✅ (basic) | ❌ | ✅ |
| Selection panels | ✅ | ❌ | ❌ | ✅ |
| Full TUI (panels, mouse) | ❌ | ❌ | ⚠️ partial | ✅ |
| Bundle size | Tiny | Zero | Medium | Large |
| Learning curve | Low | None | Low | High |

---



## Samples

Six focused sample projects are included, one per feature:

| Sample | Feature | Command |
|---|---|---|
| `01-HelloStreamShell` | Full lifecycle, basic commands | `dotnet run -p samples/01-HelloStreamShell` |
| `02-MarkupRendering` | Styled messages, background events | `dotnet run -p samples/02-MarkupRendering` |
| `03-CommandAutocomplete` | Tab completion, arg suggestions | `dotnet run -p samples/03-CommandAutocomplete` |
| `04-TextEditing` | Cursor, selection, clipboard, paste | `dotnet run -p samples/04-TextEditing` |
| `05-SelectionPanels` | Single & multi-select panels | `dotnet run -p samples/05-SelectionPanels` |
| `06-SeparatorsPanels` | Separators, custom bottom panels | `dotnet run -p samples/06-SeparatorsPanels` |

The original comprehensive demo is also available:

```bash
cd samples/MySpectreApp
dotnet run
```

---

## Contributing

### Running Locally

```bash
git clone https://github.com/Venando/StreamShell
cd StreamShell
dotnet build
dotnet test
```

### Running the Sample App

```bash
cd samples/MySpectreApp
dotnet run
```

PRs welcome. Please open an issue first for large changes.

---

## License

MIT — see [LICENSE](LICENSE) for details.
