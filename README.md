# StreamShell

[![NuGet](https://img.shields.io/nuget/v/StreamShell)](https://www.nuget.org/packages/StreamShell/)
[![Downloads](https://img.shields.io/nuget/dt/StreamShell)](https://www.nuget.org/packages/StreamShell/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com)

**An interactive, markup-capable console host for .NET — type, render, and command without the plumbing.**

---

## Hero Demo

<img width="800" alt="Hero Demo" src="https://github.com/user-attachments/assets/73611c40-19c9-42f2-b8d2-cc2abce623bd" />

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

<img width="800" alt="demo-ezgif com-cut(1)" src="https://github.com/user-attachments/assets/1244e8b3-1238-4c80-9be7-930f4086ee5f" />


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

<img width="800" alt="Command autocomplete demo" src="https://github.com/user-attachments/assets/9c5c4f86-6fb4-40ec-ad66-0bafbf0de3eb" />

### ✨ Inline Text Editing

Full editing experience while typing — cursor movement, text selection with Shift+arrows, clipboard integration (Ctrl+C / Ctrl+V / Ctrl+X), undo (Ctrl+Z), and multi-line input wrapping.

Paste a large block of text and it's automatically detected and attached as a separate attachment object rather than flooding the input buffer.

<img width="800" alt="Text Editing" src="https://github.com/user-attachments/assets/cd70306d-a72e-44c5-91e6-0ea34a2a5a4d" />


### ✨ Interactive Selection Panels

Prompt users with a navigable selection panel at the bottom of the console. Arrow keys to navigate, Enter to select, Escape to cancel. Supports both single-select and multi-select modes with min/max constraints.

```csharp
var selected = await host.PromptSelection("Pick an OS", osVariants);
```

<img width="800" alt="Interactive Selection Panels" src="https://github.com/user-attachments/assets/40038e51-06ff-46b7-8dfc-96287509fe4b" />


### ✨ Customizable Separators

Add styled separators between the message feed, input block, and hint panel. Each separator supports left/right text labels and configurable fill characters.

```csharp
host.SetTopSeparator("Messages", "StreamShell", '-', "white");
host.SetBottomSeparator("Input", null, '\u2500');
```

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

<img width="1073" height="694" alt="Extensible Bottom Panels + Customizable Separators" src="https://github.com/user-attachments/assets/55425cc8-a92a-4480-8e68-4e8932cd0893" />


### ✨ Input Field Save/Load

Save and restore input field states by ID. Useful for preserving partially-typed input during operations that need to clear the buffer temporarily.

```csharp
string id = host.InputHandler.SaveInputField();     // save current text
host.InputHandler.LoadInputField(id);                // restore it later
host.InputHandler.RemoveSavedInputField(id);         // dispose
```

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
