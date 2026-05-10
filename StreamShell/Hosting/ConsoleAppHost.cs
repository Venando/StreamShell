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
public partial class ConsoleAppHost : IDisposable
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
        _defaultPanel = new EmptyBottomPanel(Settings.CommandPaletteHeight);
        _bottomPanel = _defaultPanel;
        _renderer.SetPanelLineCount(_bottomPanel.LineCount);
        if (_renderer is ConsoleRenderer cr)
            cr.ShowBottomSeparator = _bottomPanel.ShowBottomSeparator;
        BottomPanelChanged += (_, e) =>
        {
            _renderer.SetPanelLineCount(e.Panel.LineCount);
            if (_renderer is ConsoleRenderer cr2)
                cr2.ShowBottomSeparator = e.Panel.ShowBottomSeparator;
        };
        Settings.SettingsChanged += OnSettingsChanged;
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
        _defaultPanel = new EmptyBottomPanel(Settings.CommandPaletteHeight);
        _bottomPanel = _defaultPanel;
        _renderer.SetPanelLineCount(_bottomPanel.LineCount);
        if (_renderer is ConsoleRenderer cr)
            cr.ShowBottomSeparator = _bottomPanel.ShowBottomSeparator;
        BottomPanelChanged += (_, e) =>
        {
            _renderer.SetPanelLineCount(e.Panel.LineCount);
            if (_renderer is ConsoleRenderer cr2)
                cr2.ShowBottomSeparator = e.Panel.ShowBottomSeparator;
        };
        Settings.SettingsChanged += OnSettingsChanged;
        ApplySettings();
        WireUpAutoComplete();

        // Start the default panel's background loop
        _ = _bottomPanel.RunAsync(_panelCts.Token);
    }

    /// <summary>Applies the current Settings values to the renderer and input handler.</summary>
    private void ApplySettings()
    {
        _inputHandler.LargePasteThreshold = Settings.LargePasteThreshold;
        _inputHandler.LargePasteLineThreshold = Settings.LargePasteLineThreshold;
    }

    /// <summary>Re-applies settings when they change at runtime.</summary>
    private void OnSettingsChanged()
    {
        ApplySettings();
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

        // Dispose active panel
        _bottomPanel?.Dispose();
        _defaultPanel?.Dispose();

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

}
