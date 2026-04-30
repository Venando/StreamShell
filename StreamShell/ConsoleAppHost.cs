namespace StreamShell;

using System.Collections.Concurrent;
using Spectre.Console;

public class ConsoleAppHost : IDisposable
{
    private readonly ConcurrentQueue<string> _messages = new();
    private readonly List<Command> _commands = new();
    private readonly UserInputHandler _inputHandler = new();
    private readonly ConsoleRenderer _renderer = new();
    private readonly CommandPalette _commandPalette;
    private readonly CancellationTokenSource _cts = new();

    public event Action<string>? UserInputSubmitted;

    public ConsoleAppHost()
    {
        _commandPalette = new CommandPalette(_commands);
    }

    public void AddMessage(string markup)
    {
        _messages.Enqueue(markup);
    }

    public void AddCommand(Command command)
    {
        _commands.Add(command);
    }

    public async Task Run(CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

        Console.CursorVisible = false;

        string? lastRenderedInput = null;
        int previousInputLineCount = 0;

        while (!linkedCts.Token.IsCancellationRequested)
        {
            var hints = _commandPalette.GetHints(_inputHandler.CurrentInput);
            int blockOffset = _renderer.GetBlockOffset(_inputHandler.CurrentInput);
            int currentInputLineCount = _renderer.GetInputLineCount(_inputHandler.CurrentInput);

            if (_messages.TryDequeue(out var message))
            {
                if (lastRenderedInput is not null)
                    _renderer.ClearInputBlock(lastRenderedInput);
                else
                    _renderer.ClearInputLine();

                _renderer.RenderMessage(message);
                _renderer.RenderInputBlock(_inputHandler.CurrentInput, hints);
                lastRenderedInput = _inputHandler.CurrentInput;
                previousInputLineCount = currentInputLineCount;
            }
            else if (lastRenderedInput != _inputHandler.CurrentInput)
            {
                if (previousInputLineCount == 1 && currentInputLineCount == 1)
                {
                    _renderer.OverwriteInputBlock(_inputHandler.CurrentInput, hints, blockOffset);
                }
                else
                {
                    if (lastRenderedInput is not null)
                        _renderer.ClearInputBlock(lastRenderedInput);
                    _renderer.RenderInputBlock(_inputHandler.CurrentInput, hints);
                }

                lastRenderedInput = _inputHandler.CurrentInput;
                previousInputLineCount = currentInputLineCount;
            }

            if (_inputHandler.ProcessInput() is { } submittedInput)
            {
                _renderer.ClearInputBlock(lastRenderedInput);

                if (CommandPalette.IsActive(submittedInput))
                {
                    _renderer.RenderSubmittedCommand(submittedInput);
                    ExecuteCommand(submittedInput);
                }
                else
                {
                    UserInputSubmitted?.Invoke(submittedInput);
                }

                lastRenderedInput = null;
                previousInputLineCount = 0;
            }

            await Task.Delay(10, linkedCts.Token);
        }
    }

    public void Stop() => _cts.Cancel();

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        Console.CursorVisible = true;
    }

    private void ExecuteCommand(string input)
    {
        string query = input.Length > 1 ? input[1..] : string.Empty;
        var parts = CommandParser.Split(query);
        if (parts.Count == 0) return;

        string commandName = parts[0];
        string argsString = query.Length > commandName.Length
            ? query[(commandName.Length + 1)..]
            : string.Empty;

        var command = _commands.FirstOrDefault(c =>
            c.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase));

        if (command is null)
        {
            AddMessage($"[red]Unknown command: /{commandName}[/]");
            return;
        }

        var (positionalArgs, namedArgs) = CommandParser.Parse(argsString);

        _ = Task.Run(async () =>
        {
            try
            {
                await command.Handler(positionalArgs, namedArgs);
            }
            catch (Exception ex)
            {
                AddMessage($"[red]Command error: {ex.Message}[/]");
            }
        });
    }
}
