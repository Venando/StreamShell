# StreamShell

A tiny console host built on [Spectre.Console](https://spectreconsole.net/) that renders markup messages and handles commands while you type.

## Install

```bash
dotnet add package StreamShell
```

## Usage

```csharp
using StreamShell;

var host = new ConsoleAppHost();

host.AddCommand(new Command("hello", "Say hello", (args, named) =>
{
    host.AddMessage("[green]Hello![/]");
    return Task.CompletedTask;
}));

host.UserInputSubmitted += input =>
{
    host.AddMessage($"[grey]You said:[/] {input}");
};

host.Run();
```

- Type `/hello` to run a command.
- Type anything else and press Enter to submit user input.
- Use `--key value` for named arguments.

## License

MIT
