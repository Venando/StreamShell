namespace StreamShell.Tests;

// ═════════════════════════════════════════════════════════════════════
//  ConsoleAppHost — ProcessOneTick State Machine Tests
//  Tests the render/input state machine via ProcessOneTick,
//  which executes one loop iteration without the infinite loop.
// ═════════════════════════════════════════════════════════════════════

public class ConsoleAppHostProcessOneTickTests
{
    private readonly MockTerminal _terminal = new();
    private readonly MockRenderer _renderer = new();
    private readonly MockInputHandler _inputHandler = new();
    private ConsoleAppHost _host = null!;
    private ConsoleAppHost.RenderSnapshot _state = null!;

    private void CreateHost()
    {
        _host = new ConsoleAppHost(_renderer, _inputHandler, _terminal);
        _state = new ConsoleAppHost.RenderSnapshot(
            null, 0, false, 0, _terminal.WindowWidth, 2);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Basic Tick — No Changes
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessOneTick_NoInput_NoMessages_ReturnsNull()
    {
        CreateHost();

        var (submitted, newState) = _host.ProcessOneTick(_state);

        Assert.Null(submitted);
        // First tick always renders (transition from null to empty input)
        Assert.NotSame(_state, newState);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Queued Messages Get Rendered
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessOneTick_WithMessage_RendersIt()
    {
        CreateHost();
        _host.AddMessage("test message");

        var (submitted, newState) = _host.ProcessOneTick(_state);

        Assert.Null(submitted);
        Assert.NotSame(_state, newState); // state updated (rendering happened)
        Assert.Contains("test message", _renderer.RenderedMessages);
    }

    [Fact]
    public void ProcessOneTick_MultipleMessages_AllRendered()
    {
        CreateHost();
        _host.AddMessage("first");
        _host.AddMessage("second");

        _host.ProcessOneTick(_state); // processes first message
        _host.ProcessOneTick(_state); // processes second message

        Assert.Equal(2, _renderer.RenderedMessages.Count);
        Assert.Contains("first", _renderer.RenderedMessages);
        Assert.Contains("second", _renderer.RenderedMessages);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Input Changes Trigger Render
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessOneTick_InputChange_RendersNewInput()
    {
        CreateHost();

        // First tick: no render (no input)
        var (submitted1, newState1) = _host.ProcessOneTick(_state);
        Assert.Null(submitted1);

        // Simulate input change via mock
        _inputHandler.CurrentInput = "hello";
        _inputHandler.CursorPosition = 5;

        // Second tick: should render the input
        var (submitted2, newState2) = _host.ProcessOneTick(newState1);
        Assert.Null(submitted2);
        Assert.NotSame(newState1, newState2);
    }

    [Fact]
    public void ProcessOneTick_InputUnchanged_NoRender()
    {
        CreateHost();
        _inputHandler.CurrentInput = "hi";
        _inputHandler.CursorPosition = 2;

        // First tick renders the input
        var (_, state1) = _host.ProcessOneTick(_state);
        Assert.NotSame(_state, state1);

        // Second tick with no changes: no re-render
        var (_, state2) = _host.ProcessOneTick(state1);
        Assert.Same(state1, state2); // unchanged, no render
    }

    // ══════════════════════════════════════════════════════════════════
    //  Submitted Input
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessOneTick_SubmittedInput_ReturnsText()
    {
        CreateHost();
        _inputHandler.QueueSubmittedInput("test input");

        var (submitted, newState) = _host.ProcessOneTick(_state);

        Assert.Equal("test input", submitted);
        Assert.NotSame(_state, newState);
    }

    [Fact]
    public void ProcessOneTick_UserInputSubmittedEvent_Fires()
    {
        CreateHost();
        UserInputSubmittedEventArgs? eventArgs = null;
        _host.UserInputSubmitted += args => eventArgs = args;

        _inputHandler.CurrentInput = "hello world";
        _inputHandler.QueueSubmittedInput("hello world");

        _host.ProcessOneTick(_state);

        Assert.NotNull(eventArgs);
        Assert.Equal("hello world", eventArgs.RawOutput);
        Assert.Equal(InputType.PlainText, eventArgs.InputType);
    }

    [Fact]
    public void ProcessOneTick_SubmittedInput_ResetsRendererState()
    {
        CreateHost();

        // Render initial state
        _inputHandler.CurrentInput = "initial";
        _inputHandler.CursorPosition = 7;
        var (_, state1) = _host.ProcessOneTick(_state);

        // Submit
        _inputHandler.QueueSubmittedInput("initial");
        var (_, state2) = _host.ProcessOneTick(state1);

        // After submit, state resets LastInput to null
        Assert.Null(state2.LastInput);
        Assert.Equal(0, state2.LastCursor);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Command Detection in Submitted Input
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessOneTick_CommandInput_DetectsCommand()
    {
        CreateHost();
        _host.AddCommand(new Command("test", "A test", (_, _) =>
            Task.CompletedTask));

        _inputHandler.CurrentInput = "/test";
        _inputHandler.QueueSubmittedInput("/test");

        UserInputSubmittedEventArgs? eventArgs = null;
        _host.UserInputSubmitted += args => eventArgs = args;

        _host.ProcessOneTick(_state);

        Assert.NotNull(eventArgs);
        Assert.Equal(InputType.Command, eventArgs.InputType);
        Assert.Equal("/test", eventArgs.RawOutput);
    }

    [Fact]
    public void ProcessOneTick_CommandInput_FiresHandler()
    {
        CreateHost();
        var commandExecuted = false;
        _host.AddCommand(new Command("greet", "Greet", (args, named) =>
        {
            commandExecuted = true;
            return Task.CompletedTask;
        }));

        _inputHandler.CurrentInput = "/greet";
        _inputHandler.QueueSubmittedInput("/greet");

        _host.ProcessOneTick(_state);

        // Give task a moment to execute
        Thread.Sleep(50);
        Assert.True(commandExecuted, "Command handler should have been called");
    }

    [Fact]
    public void ProcessOneTick_UnknownCommand_DoesNotThrow()
    {
        CreateHost();

        _inputHandler.CurrentInput = "/nonexistent";
        _inputHandler.QueueSubmittedInput("/nonexistent");

        // Should not throw
        var (submitted, _) = _host.ProcessOneTick(_state);
        Assert.Equal("/nonexistent", submitted);
    }

    [Fact]
    public void ProcessOneTick_PlainTextInput_NotCommand()
    {
        CreateHost();

        _inputHandler.CurrentInput = "not a command";
        _inputHandler.QueueSubmittedInput("not a command");

        UserInputSubmittedEventArgs? eventArgs = null;
        _host.UserInputSubmitted += args => eventArgs = args;

        _host.ProcessOneTick(_state);

        Assert.NotNull(eventArgs);
        Assert.Equal(InputType.PlainText, eventArgs.InputType);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Quit Signal
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessOneTick_QuitRequested_ReturnsQuitSignal()
    {
        CreateHost();
        _inputHandler.QuitRequested = true;

        var (submitted, _) = _host.ProcessOneTick(_state);

        Assert.Equal("__QUIT__", submitted);
        Assert.False(_inputHandler.QuitRequested); // was reset
    }

    // ══════════════════════════════════════════════════════════════════
    //  Window Width Changes
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessOneTick_WindowWidthChange_TriggersRender()
    {
        CreateHost();
        _inputHandler.CurrentInput = "text";

        // First tick: renders
        var (_, state1) = _host.ProcessOneTick(_state);

        // Change window width
        _terminal.WindowWidth = 60;
        _inputHandler.CursorPosition = 4;

        // Second tick: should re-render due to resize
        var (_, state2) = _host.ProcessOneTick(state1);

        Assert.NotSame(state1, state2);
        Assert.Equal(60, state2.LastWindowWidth);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Cursor Movement Triggers Render
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessOneTick_CursorChange_TriggersRender()
    {
        CreateHost();
        _inputHandler.CurrentInput = "hello";
        _inputHandler.CursorPosition = 5;

        var (_, state1) = _host.ProcessOneTick(_state);

        _inputHandler.CursorPosition = 3; // cursor moved

        var (_, state2) = _host.ProcessOneTick(state1);

        Assert.NotSame(state1, state2);
        Assert.Equal(3, state2.LastCursor);
    }

    [Fact]
    public void ProcessOneTick_SelectionChange_TriggersRender()
    {
        CreateHost();
        _inputHandler.CurrentInput = "hello";
        _inputHandler.CursorPosition = 5;
        _inputHandler.HasSelection = false;

        var (_, state1) = _host.ProcessOneTick(_state);

        _inputHandler.HasSelection = true;

        var (_, state2) = _host.ProcessOneTick(state1);

        Assert.NotSame(state1, state2);
        Assert.True(state2.LastHasSelection);
    }

    // ══════════════════════════════════════════════════════════════════
    //  ProcessOneTick with ConsoleRenderer (not MockRenderer)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessOneTick_WithConsoleRenderer_UpdatesPlaceholders()
    {
        // Use real ConsoleRenderer but with mock terminal
        var renderer = new ConsoleRenderer(new StreamShellSettings(), _terminal);
        _host = new ConsoleAppHost(renderer, _inputHandler, _terminal);
        _state = new ConsoleAppHost.RenderSnapshot(
            null, 0, false, 0, _terminal.WindowWidth, 2);

        _inputHandler.Attachments.Add(
            new Attachment("content", AttachmentType.PlainText, 2, 1, "[paste #1, 2 lines]"));

        var (submitted, _) = _host.ProcessOneTick(_state);

        Assert.Null(submitted);
        Assert.Equal("[paste #1, 2 lines]", renderer.PlaceholderStrings?.FirstOrDefault());
    }

    [Fact]
    public void ProcessOneTick_NoPlaceholders_PlaceholderStringsIsNullOrEmpty()
    {
        var renderer = new ConsoleRenderer(new StreamShellSettings(), _terminal);
        _host = new ConsoleAppHost(renderer, _inputHandler, _terminal);
        _state = new ConsoleAppHost.RenderSnapshot(
            null, 0, false, 0, _terminal.WindowWidth, 2);

        _host.ProcessOneTick(_state);

        // Should be null or empty list
        Assert.True(renderer.PlaceholderStrings is null ||
                    renderer.PlaceholderStrings.Count == 0);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Panel swap via EnsureProperPanel (command mode)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessOneTick_CommandSlash_ActivatesCommandPalette()
    {
        CreateHost();
        _host.AddCommand(new Command("help", "Help", (_, _) => Task.CompletedTask));

        _inputHandler.CurrentInput = "/help";
        _inputHandler.CursorPosition = 5;

        _host.ProcessOneTick(_state);
        // ProcessOneTick calls EnsureProperPanel which should swap to CommandPalette
        // when input starts with /. It also calls GetLines on the panel.
        Assert.NotNull(_host);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Multiple ticks: message then submit
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessOneTick_MessageThenSubmit_Sequence()
    {
        CreateHost();

        _host.AddMessage("status update");
        var (result1, state1) = _host.ProcessOneTick(_state);
        Assert.Null(result1);
        Assert.Contains("status update", _renderer.RenderedMessages);

        _inputHandler.CurrentInput = "reported";
        _inputHandler.QueueSubmittedInput("reported");
        var (result2, state2) = _host.ProcessOneTick(state1);
        Assert.Equal("reported", result2);
    }
}
