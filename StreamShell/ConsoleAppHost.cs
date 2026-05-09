using System.Collections.Concurrent;
using System.Text;

namespace StreamShell;

/// <summary>Event args for custom bottom panel changes.</summary>
public class BottomPanelChangedEventArgs : EventArgs
{
    public IBottomPanel Panel { get; }
    public BottomPanelChangedEventArgs(IBottomPanel panel) => Panel = panel;
}

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
    private IBottomPanel _bottomPanel;
    private CancellationTokenSource _panelCts = new();
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Raised when the bottom panel is swapped. Lets the renderer update its line count.</summary>
    public event EventHandler<BottomPanelChangedEventArgs>? BottomPanelChanged;

    /// <summary>Current settings that control paste thresholds and other behavior.</summary>
    public StreamShellSettings Settings { get; } = new();

    /// <summary>Exposes the input handler for save/load/reset operations.</summary>
    public IInputHandler InputHandler => _inputHandler;

    /// <summary>
    /// Raised when the user submits input (Enter without modifiers).
    /// Provides the raw text, whether it is a command or plain text,
    /// and any attachments (large pastes).
    /// </summary>
    public event Action<string, InputType, IReadOnlyList<Attachment>>? UserInputSubmitted;

    /// <summary>Creates a host wired to the real console renderer and input handler.
    /// Default bottom panel is CommandPalette.</summary>
    public ConsoleAppHost()
    {
        _renderer = new ConsoleRenderer(Settings);
        _inputHandler = new UserInputHandler();
        _bottomPanel = new CommandPalette(() => _commands.Values);
        _renderer.SetPanelLineCount(_bottomPanel.LineCount);
        BottomPanelChanged += (_, e) => _renderer.SetPanelLineCount(e.Panel.LineCount);
        ApplySettings();
        WireUpAutoComplete();

        // Start the default panel's background loop
        _ = _bottomPanel.RunAsync(_panelCts.Token);
    }

    /// <summary>Creates a host with explicit renderer and input handler (for testing).</summary>
    internal ConsoleAppHost(IRenderer renderer, IInputHandler inputHandler)
    {
        _renderer = renderer;
        _inputHandler = inputHandler;
        _bottomPanel = new CommandPalette(() => _commands.Values);
        _renderer.SetPanelLineCount(_bottomPanel.LineCount);
        BottomPanelChanged += (_, e) => _renderer.SetPanelLineCount(e.Panel.LineCount);
        ApplySettings();
        WireUpAutoComplete();

        // Start the default panel's background loop
        _ = _bottomPanel.RunAsync(_panelCts.Token);
    }

    /// <summary>
    /// Swaps the bottom panel. Cancels the previous panel's background task (if any) and
    /// starts the new one. Raises BottomPanelChanged so the renderer adjusts.
    /// </summary>
    public void SetBottomPanel(IBottomPanel panel)
    {
        // Cancel previous panel's background task
        _panelCts.Cancel();
        _panelCts.Dispose();
        _panelCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

        _bottomPanel = panel;
        WireUpAutoComplete();
        BottomPanelChanged?.Invoke(this, new BottomPanelChangedEventArgs(panel));

        // Start new panel's background loop (fire-and-forget; the linked token
        // ensures it is cancelled when swapped or when the host stops).
        _ = panel.RunAsync(_panelCts.Token);
    }

    /// <summary>Restores the default CommandPalette bottom panel.</summary>
    public void ResetBottomPanel()
    {
        SetBottomPanel(new CommandPalette(() => _commands.Values));
    }

    private void WireUpAutoComplete()
    {
        if (_inputHandler is UserInputHandler uih)
        {
            uih.AutoCompleteProvider = input =>
            {
                _bottomPanel.GetLines(input);
                return _bottomPanel.CurrentSuggestion;
            };

            // Let the active panel intercept keys (e.g. Up/Down for hint selection)
            uih.KeyInterceptor = key => _bottomPanel.TryHandleKey(key);
        }
    }

    /// <summary>Applies the current Settings values to the renderer and input handler.</summary>
    private void ApplySettings()
    {
        _inputHandler.LargePasteThreshold = Settings.LargePasteThreshold;
        _inputHandler.LargePasteLineThreshold = Settings.LargePasteLineThreshold;
    }

    /// <summary>Queue a markup message to be displayed.</summary>
    public void AddMessage(string markup) => _messages.Enqueue(markup);

    /// <summary>Register a command that can be triggered with /command-name.</summary>
    public void AddCommand(Command command) => _commands[command.Name] = command;

    /// <summary>
    /// Register a command with argument suggestions for autocomplete.
    /// After typing the command name, the hint palette shows argument completions
    /// from <paramref name="argumentSuggestions"/> and Tab fills the top suggestion.
    /// Each entry is a full multi-word argument string (e.g. "linux ubuntu").
    /// </summary>
    public void AddCommand(string name, string description,
        Func<string[], Dictionary<string, string>, Task> handler,
        string[]? argumentSuggestions)
    {
        _commands[name] = new Command(name, description, handler, argumentSuggestions);
    }

    /// <summary>Run the main input/render loop until cancelled or Ctrl+D is pressed.</summary>
    public async Task Run(CancellationToken cancellationToken = default)
    {
        Console.OutputEncoding = Encoding.UTF8; // Enable Unicode symbols (arrows, etc.)
        Console.Write("\u001b[?2004h");         // Enable bracketed paste mode
        Console.TreatControlCAsInput = true;    // Ctrl+C is used for Copy
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

            if (TryRender(state, input, cursor, hasSelection, selStart, selLength, margin, windowWidth))
            {
                state = new RenderSnapshot(input, cursor, hasSelection,
                    _renderer.GetInputLineCount(input), windowWidth);
            }

            if (_inputHandler.QuitRequested)
            {
                _inputHandler.QuitRequested = false;
                break;
            }

            string? submittedInput = _inputHandler.ProcessInput();
            if (submittedInput != null)
            {
                HandleSubmittedInput(submittedInput, windowWidth);
                state = new RenderSnapshot(null, 0, false, 0, windowWidth);
            }

            await Task.Delay(10, token);
        }
    }

    /// <summary>Priority render check: messages first, then input changes. Returns true when the screen was updated.</summary>
    private bool TryRender(
        RenderSnapshot state,
        string input, int cursor, bool hasSelection, int selStart, int selLength,
        int margin, int windowWidth)
    {
        // Priority 1: queued messages need a full re-render
        if (RenderQueuedMessages(state, input, cursor, hasSelection, selStart, selLength, margin))
            return true;

        // Priority 2: input/cursor/resize changes need an update
        return RenderInputChanges(state, input, cursor, hasSelection, selStart, selLength,
            margin, windowWidth);
    }

    /// <summary>Renders a queued message then re-renders the input block. Returns true if a message was rendered.</summary>
    private bool RenderQueuedMessages(
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
        RenderFullInputBlock(input, cursor, hasSelection, selStart, selLength, margin);

        // Consume panel dirty — GetLines was already called via RenderFullInputBlock
        if (_bottomPanel.IsDirty)
            _bottomPanel.ClearDirty();

        return true;
    }

    /// <summary>Applies input/cursor/resize changes to the display. Returns true when the screen was updated.</summary>
    private bool RenderInputChanges(
        RenderSnapshot state,
        string input, int cursor, bool hasSelection, int selStart, int selLength,
        int margin, int windowWidth)
    {
        bool panelDirty = _bottomPanel.IsDirty;
        if (!StateDiffersFromRender(state, input, cursor, hasSelection, windowWidth) && !panelDirty)
            return false;

        // Consume the dirty flag before rendering (GetLines will be called below)
        if (panelDirty)
            _bottomPanel.ClearDirty();

        // Single-line → single-line: use faster overwrite (not on resize)
        bool terminalResized = state.LastWindowWidth != windowWidth;
        if (state.LastInput != input && !terminalResized
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
            RenderFullInputBlock(input, cursor, hasSelection, selStart, selLength, margin);
        }

        return true;
    }

    /// <summary>Returns true when any tracked state has changed from the last render.</summary>
    private static bool StateDiffersFromRender(
        RenderSnapshot state,
        string input, int cursor, bool hasSelection, int windowWidth)
    {
        return state.LastInput != input
            || state.LastCursor != cursor
            || state.LastHasSelection != hasSelection
            || state.LastWindowWidth != windowWidth;
    }

    private void HandleSubmittedInput(string submittedInput, int windowWidth)
    {
        _renderer.ClearInputBlock(submittedInput);

        bool hasAttachments = _inputHandler.Attachments.Count > 0;
        bool isCommand = !hasAttachments && TryGetCommandName(submittedInput, out string? commandName)
            && _commands.ContainsKey(commandName!);

        var inputType = isCommand ? InputType.Command : InputType.PlainText;
        UserInputSubmitted?.Invoke(submittedInput, inputType, _inputHandler.Attachments);

        if (isCommand)
            ExecuteCommand(submittedInput);

        _inputHandler.Reset();
    }

    private void RenderFullInputBlock(
        string input, int cursor, bool hasSelection,
        int selStart, int selLength, int margin)
    {
        _renderer.RenderInputBlock(input, GetCommandHints(input), cursor,
            hasSelection, selStart, selLength, margin);
    }

    private IReadOnlyList<string> GetCommandHints(string input) => _bottomPanel.GetLines(input);

    /// <summary>Signal the host to stop after the current loop iteration.</summary>
    public void Stop() => _cts.Cancel();

    /// <summary>Dispose the host, cancelling the run loop and restoring terminal state.</summary>
    public void Dispose()
    {
        _cts.Cancel();
        _panelCts.Cancel();
        _panelCts.Dispose();
        _cts.Dispose();
        Console.CursorVisible = true;
        Console.Write("\u001b[?2004l");
    }

    private void ExecuteCommand(string input)
    {
        if (!TryGetCommandName(input, out string? commandName, out string? argsString))
            return;

        if (!_commands.TryGetValue(commandName!, out var command))
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

    /// <summary>Extracts the command name and argument string from a /command input.
    /// Returns false if the input doesn't look like a valid command reference.</summary>
    private static bool TryGetCommandName(string input, out string? name, out string args)
    {
        name = null;
        args = string.Empty;

        if (input.Length <= 1 || input[0] != '/')
            return false;

        string query = input[1..];
        var parts = CommandParser.Split(query);
        if (parts.Count == 0)
            return false;

        name = parts[0];
        args = query.Length > name.Length ? query[(name.Length + 1)..] : string.Empty;
        return true;
    }

    /// <summary>Convenience overload when only the command name is needed.</summary>
    private static bool TryGetCommandName(string input, out string? name)
        => TryGetCommandName(input, out name, out _);
}
