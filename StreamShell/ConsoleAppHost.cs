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
    private readonly ITerminal _terminal;
    private readonly ConcurrentQueue<string> _messages = new();
    private readonly CommandManager _commandManager = new();
    private readonly IInputHandler _inputHandler;
    private readonly IRenderer _renderer;
    private IBottomPanel _defaultPanel;
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
    public event Action<UserInputSubmittedEventArgs>? UserInputSubmitted;

    /// <summary>Creates a host wired to the real console renderer and input handler.
    /// Default bottom panel is EmptyBottomPanel; CommandPalette activates on "/".</summary>
    public ConsoleAppHost()
    {
        _terminal = new SystemTerminal();
        _renderer = new ConsoleRenderer(Settings);
        _inputHandler = new UserInputHandler();
        _defaultPanel = new EmptyBottomPanel();
        _bottomPanel = _defaultPanel;
        _renderer.SetPanelLineCount(_bottomPanel.LineCount);
        BottomPanelChanged += (_, e) => _renderer.SetPanelLineCount(e.Panel.LineCount);
        ApplySettings();
        WireUpAutoComplete();

        // Start the default panel's background loop
        _ = _bottomPanel.RunAsync(_panelCts.Token);
    }

    /// <summary>Creates a host with explicit renderer and input handler (for testing).</summary>
    internal ConsoleAppHost(IRenderer renderer, IInputHandler inputHandler, ITerminal? terminal = null)
    {
        _renderer = renderer;
        _inputHandler = inputHandler;
        _terminal = terminal ?? new SystemTerminal();
        _defaultPanel = new EmptyBottomPanel();
        _bottomPanel = _defaultPanel;
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

    /// <summary>Restores the default bottom panel (EmptyBottomPanel by default).</summary>
    public void ResetBottomPanel()
    {
        SetBottomPanel(_defaultPanel);
    }

    /// <summary>Replaces the default bottom panel with a custom one. Used when no command is active.</summary>
    public void SetDefaultPanel(IBottomPanel panel)
    {
        _defaultPanel = panel;
        // If we're currently on the old default, swap to the new one
        if (_bottomPanel is not CommandPalette && _bottomPanel is not SelectionPanel)
            ResetBottomPanel();
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

    /// <summary>
    /// Swaps between the default panel and CommandPalette based on whether the
    /// current input starts with "/". Skips if already on the correct panel.
    /// Does nothing when a non-standard panel (e.g. SelectionPanel) is active.
    /// </summary>
    private void EnsureProperPanel()
    {
        string input = _inputHandler.CurrentInput;
        bool isCommand = input.Length > 0 && input[0] == '/';

        if (isCommand && _bottomPanel is CommandPalette)
            return;
        if (!isCommand && !(_bottomPanel is CommandPalette))
            return;

        if (isCommand)
            SetBottomPanel(new CommandPalette(() => _commandManager.AllCommands));
        else
            SetBottomPanel(_defaultPanel);
    }

    /// <summary>Applies the current Settings values to the renderer and input handler.</summary>
    private void ApplySettings()
    {
        _inputHandler.LargePasteThreshold = Settings.LargePasteThreshold;
        _inputHandler.LargePasteLineThreshold = Settings.LargePasteLineThreshold;
    }

    /// <summary>Queue a markup message to be displayed.</summary>
    public void AddMessage(string markup) => _messages.Enqueue(markup);

    /// <summary>Sets the top separator (between message feed and input block).</summary>
    public void SetTopSeparator(string? leftText = null, string? rightText = null,
        char repeatedCharacter = '-', string? repeatedCharMarkup = null)
    {
        _renderer.TopSeparator = new SeparatorConfig
        {
            LeftText = leftText,
            RightText = rightText,
            RepeatedChar = repeatedCharacter,
            RepeatedCharMarkup = repeatedCharMarkup
        };
    }

    /// <summary>Sets the bottom separator (between input line and hints block).</summary>
    public void SetBottomSeparator(string? leftText = null, string? rightText = null,
        char repeatedCharacter = '-', string? repeatedCharMarkup = null)
    {
        _renderer.BottomSeparator = new SeparatorConfig
        {
            LeftText = leftText,
            RightText = rightText,
            RepeatedChar = repeatedCharacter,
            RepeatedCharMarkup = repeatedCharMarkup
        };
    }

    /// <summary>Register a command that can be triggered with /command-name.</summary>
    public void AddCommand(Command command) => _commandManager.Add(command);

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
        _commandManager.Add(new Command(name, description, handler, argumentSuggestions));
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
    internal sealed record RenderSnapshot(
        string? LastInput,
        int LastCursor,
        bool LastHasSelection,
        int LastInputLineCount,
        int LastWindowWidth,
        int LastPanelLineCount
    );

    private async Task RunLoop(CancellationToken token)
    {
        var state = new RenderSnapshot(null, 0, false, 0, _terminal.WindowWidth, _bottomPanel.LineCount);

        while (!token.IsCancellationRequested)
        {
            (string? submittedInput, state) = ProcessOneTick(state);
            if (submittedInput == "__QUIT__")
                break;

            await Task.Delay(10, token);
        }
    }

    /// <summary>
    /// Holds render-time state captured from the input handler and terminal.
    /// Extracted into a struct so <see cref="ProcessOneTick"/> can pass it
    /// to rendering methods without unpacking individual fields (SRP + readability).
    /// </summary>
    private readonly record struct TickState(
        string Input,
        int Cursor,
        bool HasSelection,
        int SelectionStart,
        int SelectionLength,
        int WindowWidth,
        int Margin
    );

    /// <summary>
    /// Processes exactly one tick of the render/input loop.
    /// Returns (submittedInput, newState) — submittedInput is null if nothing was submitted,
    /// or "__QUIT__" when Ctrl+D was pressed.
    /// For testing: allows controlled single-iteration execution without the infinite loop.
    /// </summary>
    // Tracks attachment count to avoid recomputing PlaceholderStrings on every tick
    private int _lastAttachmentCount = -1;
    private List<string>? _cachedPlaceholders;

    internal (string? SubmittedInput, RenderSnapshot NewState) ProcessOneTick(RenderSnapshot state)
    {
        EnsureProperPanel();

        var tick = CaptureTickState();
        SyncPlaceholderCache();

        bool rendered = TryRender(state, tick);
        var newState = rendered
            ? new RenderSnapshot(tick.Input, tick.Cursor, tick.HasSelection,
                _renderer.GetInputLineCount(tick.Input), tick.WindowWidth, _bottomPanel.LineCount)
            : state;

        if (_inputHandler.QuitRequested)
        {
            _inputHandler.QuitRequested = false;
            return ("__QUIT__", newState);
        }

        string? submittedInput = _inputHandler.ProcessInput();
        if (submittedInput != null)
        {
            HandleSubmittedInput(submittedInput, tick.WindowWidth);
            newState = new RenderSnapshot(null, 0, false, 0, tick.WindowWidth, _bottomPanel.LineCount);
        }

        return (submittedInput, newState);
    }

    /// <summary>Captures the current input handler state and terminal dimensions into a single struct.</summary>
    private TickState CaptureTickState()
    {
        // Always sync right margins with the current terminal width — the
        // user may have resized the window since the last tick. Without this,
        // ConsoleRenderer and UserInputHandler would use stale constructor-time
        // values, causing text to wrap at the wrong column ("right margin stays
        // at the same place" after a resize).
        int windowWidth = _terminal.WindowWidth;
        _inputHandler.RightMargin = windowWidth;
        if (_renderer is ConsoleRenderer cr)
            cr.RightMargin = windowWidth;

        _inputHandler.TryGetSelection(out int selStart, out int selLength);
        return new TickState(
            _inputHandler.CurrentInput,
            _inputHandler.CursorPosition,
            _inputHandler.HasSelection,
            selStart,
            selLength,
            windowWidth,
            _inputHandler.RightMargin
        );
    }

    /// <summary>
    /// Synchronizes the placeholder cache with the current attachment list.
    /// Avoids recomputing PlaceholderStrings on every tick when attachments
    /// haven't changed.
    /// </summary>
    private void SyncPlaceholderCache()
    {
        if (_renderer is not ConsoleRenderer cr)
            return;

        int attachCount = _inputHandler.Attachments.Count;
        if (attachCount != _lastAttachmentCount)
        {
            _lastAttachmentCount = attachCount;
            _cachedPlaceholders = null;
        }

        if (_cachedPlaceholders is null)
        {
            var placeholders = new List<string>(attachCount);
            foreach (var a in _inputHandler.Attachments)
            {
                if (!string.IsNullOrEmpty(a.Placeholder))
                    placeholders.Add(a.Placeholder);
            }
            _cachedPlaceholders = placeholders;
        }

        cr.PlaceholderStrings = _cachedPlaceholders;
    }

    /// <summary>Priority render check: messages first, then input changes. Returns true when the screen was updated.</summary>
    private bool TryRender(RenderSnapshot state, TickState tick)
    {
        // Priority 1: queued messages need a full re-render
        if (RenderQueuedMessages(state, tick))
            return true;

        // Priority 2: input/cursor/resize changes need an update
        return RenderInputChanges(state, tick);
    }

    /// <summary>Renders a queued message then re-renders the input block. Returns true if a message was rendered.</summary>
    private bool RenderQueuedMessages(RenderSnapshot state, TickState tick)
    {
        if (!_messages.TryDequeue(out var message))
            return false;

        if (state.LastInput is not null)
            _renderer.ClearInputBlockForReRender(state.LastInput, tick.Input, state.LastPanelLineCount);
        else
            _renderer.ClearInputLine();

        _renderer.RenderMessage(message);
        RenderFullInputBlock(tick);

        if (state.LastInput is not null)
        {
            int oldBlockOffset = (1 + state.LastPanelLineCount) + _renderer.GetInputLineCount(state.LastInput);
            int newBlockOffset = _renderer.GetBlockOffset(tick.Input);
            _renderer.HandleBlockHeightChange(oldBlockOffset, newBlockOffset);
        }

        if (_bottomPanel.IsDirty)
            _bottomPanel.ClearDirty();

        return true;
    }

    /// <summary>Applies input/cursor/resize changes. Returns true when the screen was updated.</summary>
    private bool RenderInputChanges(RenderSnapshot state, TickState tick)
    {
        bool panelDirty = _bottomPanel.IsDirty;
        if (!StateDiffersFromRender(state, tick) && !panelDirty)
            return false;

        if (panelDirty)
            _bottomPanel.ClearDirty();

        bool terminalResized = state.LastWindowWidth != tick.WindowWidth;
        bool panelChanged = state.LastPanelLineCount != _bottomPanel.LineCount;

        // Single-line → single-line: fast overwrite when panel hasn't changed
        if (UseFastOverwrite(state, tick, terminalResized, panelChanged))
        {
            int blockOffset = _renderer.GetBlockOffset(tick.Input);
            _renderer.OverwriteInputBlock(tick.Input, GetCommandHints(tick.Input), blockOffset,
                tick.Cursor, tick.HasSelection, tick.SelectionStart, tick.SelectionLength, tick.Margin);
        }
        else
        {
            FullReRenderInputBlock(state, tick);
        }

        return true;
    }

    /// <summary>True when both old and new input fit on one line and terminal/panel haven't changed.</summary>
    private bool UseFastOverwrite(RenderSnapshot state, TickState tick, bool terminalResized, bool panelChanged)
    {
        return state.LastInput != tick.Input
            && !terminalResized
            && !panelChanged
            && state.LastInputLineCount == 1
            && _renderer.GetInputLineCount(tick.Input) == 1;
    }

    /// <summary>Full clear + re-render of the input block, handling block height changes.</summary>
    private void FullReRenderInputBlock(RenderSnapshot state, TickState tick)
    {
        if (state.LastInput is not null)
            _renderer.ClearInputBlockForReRender(state.LastInput, tick.Input, state.LastPanelLineCount);
        RenderFullInputBlock(tick);

        if (state.LastInput is not null)
        {
            int oldBlockOffset = (1 + state.LastPanelLineCount) + _renderer.GetInputLineCount(state.LastInput);
            int newBlockOffset = _renderer.GetBlockOffset(tick.Input);
            _renderer.HandleBlockHeightChange(oldBlockOffset, newBlockOffset);
        }
    }

    /// <summary>Returns true when any tracked state has changed from the last render.</summary>
    private static bool StateDiffersFromRender(RenderSnapshot state, TickState tick)
    {
        return state.LastInput != tick.Input
            || state.LastCursor != tick.Cursor
            || state.LastHasSelection != tick.HasSelection
            || state.LastWindowWidth != tick.WindowWidth;
    }

    private void HandleSubmittedInput(string submittedInput, int windowWidth)
    {
        _renderer.ClearInputBlock(submittedInput);

        bool hasAttachments = _inputHandler.Attachments.Count > 0;
        bool isCommand = !hasAttachments
            && CommandManager.TryGetCommandName(submittedInput, out string? commandName)
            && _commandManager.Contains(commandName!);

        var inputType = isCommand ? InputType.Command : InputType.PlainText;
        UserInputSubmitted?.Invoke(new UserInputSubmittedEventArgs
        {
            InputType = inputType,
            Attachments = _inputHandler.Attachments,
            RawOutput = submittedInput
        });

        if (isCommand)
            _ = ExecuteCommandAsync(submittedInput);

        _inputHandler.Reset();
    }

    private void RenderFullInputBlock(TickState tick)
    {
        _renderer.RenderInputBlock(tick.Input, GetCommandHints(tick.Input),
            tick.Cursor, tick.HasSelection, tick.SelectionStart, tick.SelectionLength, tick.Margin);
    }

    private IReadOnlyList<string> GetCommandHints(string input) => _bottomPanel.GetLines(input);

    /// <summary>
    /// Opens an interactive selection panel at the bottom of the console.
    /// The user navigates variants with arrows, selects with Enter,
    /// submits with Space, and cancels with Escape.
    /// The previous panel is restored after selection or cancellation.
    /// </summary>
    /// <param name="title">Header line, supports Spectre markup.</param>
    /// <param name="variants">Options to pick from.</param>
    /// <param name="info">
    /// When null — single-select mode: Enter on a variant selects and submits immediately.
    /// When set — multi-select mode: Enter toggles variants, Space submits.
    /// </param>
    /// <returns>Array of selected variants, or null if cancelled.</returns>
    public Task<IVariant[]?> PromptSelection(string title, IVariant[] variants, SelectionInfo? info = null)
    {
        var tcs = new TaskCompletionSource<IVariant[]?>();
        var panel = new SelectionPanel(title, variants, info,
            onSubmit: result =>
            {
                tcs.TrySetResult(result);
                ResetBottomPanel();
            },
            onCancel: () =>
            {
                tcs.TrySetResult(null);
                ResetBottomPanel();
            });
        SetBottomPanel(panel);
        return tcs.Task;
    }

    /// <summary>Signal the host to stop after the current loop iteration.</summary>
    public void Stop() => _cts.Cancel();

    private bool _disposed;

    /// <summary>Dispose the host, cancelling the run loop and restoring terminal state.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        try { _panelCts.Cancel(); } catch (ObjectDisposedException) { }
        _panelCts.Dispose();
        _cts.Dispose();

        // Restore terminal state — may fail in test/headless environments
        try
        {
            Console.CursorVisible = true;
            Console.Write("\u001b[?2004l");
        }
        catch (IOException)
        {
            // No console handle available (e.g. test runner, CI)
        }
    }

    /// <summary>Executes a command asynchronously via the CommandManager.</summary>
    private async Task ExecuteCommandAsync(string input)
    {
        string? error = await _commandManager.ExecuteAsync(input);
        if (error is not null)
            AddMessage(error);
    }
}
