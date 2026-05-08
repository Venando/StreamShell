using System.Collections.Concurrent;

namespace StreamShell;

/// <summary>Distinguishes user-submitted input as plain text or a command.</summary>
public enum InputType
{
    /// <summary>Ordinary text input.</summary>
    PlainText,
    /// <summary>Input that matched a registered command (starts with /).</summary>
    Command
}

/// <summary>
/// Console-based app host built on Spectre.Console.
/// Manages message display, command processing, and interactive input
/// with cursor navigation, selection, clipboard, and undo support.
/// </summary>
public class ConsoleAppHost : IDisposable
{
    private readonly ConcurrentQueue<string> _messages = new();
    private readonly ConcurrentDictionary<string, Command> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly IInputHandler _inputHandler;
    private readonly IRenderer _renderer;
    private readonly CommandPalette _commandPalette;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Current settings that control paste thresholds and other behavior.</summary>
    public StreamShellSettings Settings { get; } = new();

    /// <summary>
    /// Raised when the user submits input (Enter without modifiers).
    /// Provides the raw text, whether it is a command or plain text,
    /// and any attachments (large pastes).
    /// </summary>
    public event Action<string, InputType, IReadOnlyList<Attachment>>? UserInputSubmitted;

    /// <summary>Creates a host wired to the real console renderer and input handler.</summary>
    public ConsoleAppHost()
        : this(new ConsoleRenderer(), new UserInputHandler())
    {
    }

    /// <summary>Creates a host with explicit renderer and input handler (for testing).</summary>
    internal ConsoleAppHost(IRenderer renderer, IInputHandler inputHandler)
    {
        _renderer = renderer;
        _inputHandler = inputHandler;
        _commandPalette = new CommandPalette(_commands.Values);
        _inputHandler.LargePasteThreshold = Settings.LargePasteThreshold;
        _inputHandler.LargePasteLineThreshold = Settings.LargePasteLineThreshold;
    }

    /// <summary>Queue a markup message to be displayed.</summary>
    public void AddMessage(string markup)
    {
        _messages.Enqueue(markup);
    }

    /// <summary>Register a command that can be triggered with /command-name.</summary>
    /// <summary>Register a command that can be triggered with /command-name.</summary>
    public void AddCommand(Command command)
    {
        _commands[command.Name] = command;
    }

    /// <summary>Run the main input/render loop until cancelled or Ctrl+D is pressed.</summary>
    public async Task Run(CancellationToken cancellationToken = default)
    {
        // Enable bracketed paste mode
        Console.Write("\u001b[?2004h");
        // Treat Ctrl+C as ordinary input so we can use it for Copy
        Console.TreatControlCAsInput = true;

        Console.CursorVisible = false;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

        try
        {
            await RunLoop(linkedCts.Token);
        }
        finally
        {
            Console.CursorVisible = true;
            Console.Write("\u001b[?2004l");
            Console.TreatControlCAsInput = false;
        }
    }

    private async Task RunLoop(CancellationToken token)
    {
        string? lastRenderedInput = null;
        int previousInputLineCount = 0;
        int lastCursorPosition = 0;
        bool lastHasSelection = false;
        int lastWindowWidth = Console.WindowWidth;

        while (!token.IsCancellationRequested)
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
            bool terminalResized = lastWindowWidth != Console.WindowWidth;
            lastWindowWidth = Console.WindowWidth;

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
            else if (inputChanged || cursorChanged || terminalResized)
            {
                // Input text or cursor/selection changed — update display
                if (inputChanged && !terminalResized && previousInputLineCount == 1 && currentInputLineCount == 1)
                {
                    // Single-line → single-line: optimized overwrite (not on resize)
                    _renderer.OverwriteInputBlock(
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

            await Task.Delay(10, token);
        }
    }

    private void RenderFullInputBlock(
        IReadOnlyList<string> hints,
        int cursor, bool hasSelection, int selStart, int selLength,
        int margin)
    {
        _renderer.RenderInputBlock(
            _inputHandler.CurrentInput, hints,
            cursor, hasSelection, selStart, selLength, margin);
    }

    private void RenderMessage(string markup)
    {
        _renderer.RenderMessage(markup);
    }

    /// <summary>Signal the host to stop after the current loop iteration.</summary>
    public void Stop() => _cts.Cancel();

    /// <summary>Dispose the host, cancelling the run loop and restoring terminal state.</summary>
    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        Console.CursorVisible = true;
        Console.Write("\u001b[?2004l");
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

        if (!_commands.TryGetValue(commandName, out var command))
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
        return _commands.ContainsKey(commandName);
    }
}
