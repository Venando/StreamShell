using System.Collections.Concurrent;

namespace StreamShell;
public enum InputType
{
    PlainText,
    Command
}

// TODO: Fix issues with hints able to take only 1 line
// TODO: Add ability to stream message
// TODO: Add advanced user space text rendering
public class ConsoleAppHost : IDisposable
{
    private readonly ConcurrentQueue<string> _messages = new();
    private readonly List<Command> _commands = new();
    private readonly UserInputHandler _inputHandler = new();
    private readonly ConsoleRenderer _renderer = new();
    private readonly CommandPalette _commandPalette;
    private readonly CancellationTokenSource _cts = new();

    public StreamShellSettings Settings { get; } = new();

    public event Action<string, InputType, IReadOnlyList<Attachment>>? UserInputSubmitted;

    public ConsoleAppHost()
    {
        _commandPalette = new CommandPalette(_commands);
        _inputHandler.LargePasteThreshold = Settings.LargePasteThreshold;
        _inputHandler.LargePasteLineThreshold = Settings.LargePasteLineThreshold;
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
        // Enable bracketed paste mode
        Console.Write("\u001b[?2004h");
        // Treat Ctrl+C as ordinary input so we can use it for Copy
        Console.TreatControlCAsInput = true;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

        Console.CursorVisible = false;

        string? lastRenderedInput = null;
        int previousInputLineCount = 0;
        int lastCursorPosition = 0;
        bool lastHasSelection = false;

        while (!linkedCts.Token.IsCancellationRequested)
        {
            // ── Snapshot current state ───────────────────────────────
            IReadOnlyList<string> hints = _commandPalette.GetHints(_inputHandler.CurrentInput);
            int blockOffset = _renderer.GetBlockOffset(_inputHandler.CurrentInput);
            int currentInputLineCount = _renderer.GetInputLineCount(_inputHandler.CurrentInput);
            int currentCursor = _inputHandler.CursorPosition;
            bool currentHasSelection = _inputHandler.HasSelection;
            _inputHandler.TryGetSelection(out int currentSelStart, out int currentSelLength);
            int margin = _inputHandler.RightMargin;

            bool inputChanged = lastRenderedInput != _inputHandler.CurrentInput;
            bool cursorChanged = lastCursorPosition != currentCursor ||
                                  lastHasSelection != currentHasSelection;

            // ── Always process pending messages first ────────────────
            bool rendered = false;
            if (_messages.TryDequeue(out var message))
            {
                if (lastRenderedInput is not null)
                    _renderer.ClearInputBlockForReRender(lastRenderedInput, _inputHandler.CurrentInput);
                else
                    _renderer.ClearInputLine();

                RenderMessage(message);
                RenderFullInputBlock(hints, currentCursor, currentHasSelection,
                    currentSelStart, currentSelLength, margin);
                lastRenderedInput = _inputHandler.CurrentInput;
                previousInputLineCount = currentInputLineCount;
                rendered = true;
            }
            else if (inputChanged || cursorChanged)
            {
                // Input text or cursor/selection changed — update display
                if (inputChanged && previousInputLineCount == 1 && currentInputLineCount == 1)
                {
                    // Single-line → single-line: optimized overwrite
                    ConsoleRenderer.OverwriteInputBlock(
                        _inputHandler.CurrentInput, hints, blockOffset,
                        currentCursor, currentHasSelection,
                        currentSelStart, currentSelLength, margin);
                }
                else
                {
                    // Multi-line or structural change: full re-render
                    // Use ClearInputBlockForReRender when line count may have grown
                    // (e.g. Shift+Enter adds a trailing empty line), so stale
                    // characters below the old block are erased too.
                    if (lastRenderedInput is not null)
                        _renderer.ClearInputBlockForReRender(lastRenderedInput, _inputHandler.CurrentInput);
                    RenderFullInputBlock(hints, currentCursor, currentHasSelection,
                        currentSelStart, currentSelLength, margin);
                }
                lastRenderedInput = _inputHandler.CurrentInput;
                previousInputLineCount = currentInputLineCount;
                rendered = true;
            }

            if (rendered)
            {
                lastCursorPosition = currentCursor;
                lastHasSelection = currentHasSelection;
            }

            if (_inputHandler.QuitRequested)
            {
                _inputHandler.QuitRequested = false;
                break;
            }

            if (_inputHandler.ProcessInput() is { } submittedInput)
            {
                _renderer.ClearInputBlock(lastRenderedInput);

                bool isCommand = IsValidCommand(submittedInput);
                var inputType = isCommand ? InputType.Command : InputType.PlainText;
                List<Attachment> attachments = _inputHandler.Attachments;

                UserInputSubmitted?.Invoke(submittedInput, inputType, attachments);

                if (isCommand)
                {
                    ExecuteCommand(submittedInput);
                }

                _inputHandler.Reset();
                lastRenderedInput = null;
                previousInputLineCount = 0;
                lastCursorPosition = 0;
                lastHasSelection = false;
            }

            await Task.Delay(10, linkedCts.Token);
        }
    }

    private void RenderFullInputBlock(
        IReadOnlyList<string> hints,
        int cursor, bool hasSelection, int selStart, int selLength,
        int margin)
    {
        ConsoleRenderer.RenderInputBlock(
            _inputHandler.CurrentInput, hints,
            cursor, hasSelection, selStart, selLength, margin);
    }

    private static void RenderMessage(string markup)
    {
        ConsoleRenderer.RenderMessage(markup);
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

    private bool IsValidCommand(string input)
    {
        if (_inputHandler.Attachments.Count > 0)
            return false;

        if (!input.StartsWith('/'))
            return false;

        string query = input.Length > 1 ? input[1..] : string.Empty;
        var parts = CommandParser.Split(query);
        if (parts.Count == 0)
            return false;

        string commandName = parts[0];
        return _commands.Any(c => c.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase));
    }
}
