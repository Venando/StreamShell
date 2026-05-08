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
    public void AddMessage(string markup) => _messages.Enqueue(markup);

    /// <summary>Register a command that can be triggered with /command-name.</summary>
    public void AddCommand(Command command) => _commands[command.Name] = command;

    /// <summary>Run the main input/render loop until cancelled or Ctrl+D is pressed.</summary>
    public async Task Run(CancellationToken cancellationToken = default)
    {
        Console.Write("\u001b[?2004h");      // Enable bracketed paste mode
        Console.TreatControlCAsInput = true; // Ctrl+C is used for Copy
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

    // ── Tracks render state between loop iterations ───────────────────
    private sealed record RenderSnapshot(
        string? LastInput,
        int LastCursor,
        bool LastHasSelection,
        int LastInputLineCount,
        int LastWindowWidth
    );

    private async Task RunLoop(CancellationToken token)
    {
        var state = new RenderSnapshot(null, 0, false, 0, Console.WindowWidth);

        while (!token.IsCancellationRequested)
        {
            string input = _inputHandler.CurrentInput;
            int cursor = _inputHandler.CursorPosition;
            bool hasSelection = _inputHandler.HasSelection;
            _inputHandler.TryGetSelection(out int selStart, out int selLength);
            int windowWidth = Console.WindowWidth;
            int margin = _inputHandler.RightMargin;

            if (TryDequeueAndRender(state, input, cursor, hasSelection, selStart, selLength, margin)
                || TryUpdateRender(state, input, cursor, hasSelection, selStart, selLength, margin,
                    windowWidth))
            {
                state = new RenderSnapshot(input, cursor, hasSelection,
                    _renderer.GetInputLineCount(input), windowWidth);
            }

            if (_inputHandler.QuitRequested)
            {
                _inputHandler.QuitRequested = false;
                break;
            }

            if (_inputHandler.ProcessInput() is { } submittedInput)
            {
                _renderer.ClearInputBlock(state.LastInput);

                bool isCommand = IsValidCommand(submittedInput);
                var inputType = isCommand ? InputType.Command : InputType.PlainText;

                UserInputSubmitted?.Invoke(submittedInput, inputType, _inputHandler.Attachments);

                if (isCommand)
                    ExecuteCommand(submittedInput);

                _inputHandler.Reset();
                state = new RenderSnapshot(null, 0, false, 0, windowWidth);
            }

            await Task.Delay(10, token);
        }
    }

    /// <summary>Renders queued messages and re-renders the input block. Returns true if anything was rendered.</summary>
    private bool TryDequeueAndRender(
        RenderSnapshot state,
        string input, int cursor, bool hasSelection, int selStart, int selLength,
        int margin)
    {
        if (!_messages.TryDequeue(out var message))
            return false;

        if (state.LastInput is not null)
            _renderer.ClearInputBlockForReRender(state.LastInput, input);
        else
            _renderer.ClearInputLine();

        _renderer.RenderMessage(message);
        RenderFullBlock(input, cursor, hasSelection, selStart, selLength, margin);
        return true;
    }

    /// <summary>Renders input/cursor changes or handles terminal resize. Returns true if anything changed.</summary>
    private bool TryUpdateRender(
        RenderSnapshot state,
        string input, int cursor, bool hasSelection, int selStart, int selLength,
        int margin, int windowWidth)
    {
        bool terminalResized = state.LastWindowWidth != windowWidth;
        bool inputChanged = state.LastInput != input;
        bool cursorChanged = state.LastCursor != cursor || state.LastHasSelection != hasSelection;

        if (!inputChanged && !cursorChanged && !terminalResized)
            return false;

        // Single-line → single-line: use faster overwrite (not on resize)
        if (inputChanged && !terminalResized
            && state.LastInputLineCount == 1
            && _renderer.GetInputLineCount(input) == 1)
        {
            int blockOffset = _renderer.GetBlockOffset(input);
            _renderer.OverwriteInputBlock(
                input, GetCommandHints(input), blockOffset,
                cursor, hasSelection, selStart, selLength, margin);
        }
        else
        {
            if (state.LastInput is not null)
                _renderer.ClearInputBlockForReRender(state.LastInput, input);
            RenderFullBlock(input, cursor, hasSelection, selStart, selLength, margin);
        }

        return true;
    }

    private void RenderFullBlock(
        string input, int cursor, bool hasSelection,
        int selStart, int selLength, int margin)
    {
        _renderer.RenderInputBlock(input, GetCommandHints(input), cursor,
            hasSelection, selStart, selLength, margin);
    }

    private IReadOnlyList<string> GetCommandHints(string input) => _commandPalette.GetHints(input);

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
