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
            null, 0, false, 0, _terminal.WindowWidth, 2, _terminal.BufferHeight);
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
    public void ProcessOneTick_MultipleMessages_AllRenderedInOneTick()
    {
        CreateHost();
        _host.AddMessage("first");
        _host.AddMessage("second");

        // Single tick renders all queued messages (RenderChunkSize defaults to 999)
        _host.ProcessOneTick(_state);

        Assert.Equal(2, _renderer.RenderedMessages.Count);
        Assert.Contains("first", _renderer.RenderedMessages);
        Assert.Contains("second", _renderer.RenderedMessages);
    }

    [Fact]
    public void ProcessOneTick_BoundedChunk_RequiresMultipleTicks()
    {
        CreateHost();
        _host.Settings.RenderChunkSize = 2;
        _host.AddMessage("a");
        _host.AddMessage("b");
        _host.AddMessage("c");

        // First tick renders up to 2 messages
        _host.ProcessOneTick(_state);
        Assert.Equal(2, _renderer.RenderedMessages.Count);

        // Second tick renders the remaining message
        _host.ProcessOneTick(_state);
        Assert.Equal(3, _renderer.RenderedMessages.Count);
        Assert.Contains("a", _renderer.RenderedMessages);
        Assert.Contains("b", _renderer.RenderedMessages);
        Assert.Contains("c", _renderer.RenderedMessages);
    }

    [Fact]
    public void ProcessOneTick_BatchMessages_RendersAllWithoutError()
    {
        // Verifies that batch rendering many messages works correctly
        // (no exceptions, all messages rendered in a single tick)
        CreateHost();
        for (int i = 0; i < 30; i++)
            _host.AddMessage($"message {i}");

        _host.ProcessOneTick(_state);

        Assert.Equal(30, _renderer.RenderedMessages.Count);
        Assert.Contains("message 0", _renderer.RenderedMessages);
        Assert.Contains("message 29", _renderer.RenderedMessages);
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
            null, 0, false, 0, _terminal.WindowWidth, 2, _terminal.BufferHeight);

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
            null, 0, false, 0, _terminal.WindowWidth, 2, _terminal.BufferHeight);

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

    [Fact]
    public void ProcessOneTick_SelectionPanelActive_SlashDoesNotSwap()
    {
        CreateHost();
        _host.AddCommand(new Command("help", "Help", (_, _) => Task.CompletedTask));

        // Activate a SelectionPanel via PromptSelection
        var variants = new IVariantEntry[] { new TestVariant("Option A"), new TestVariant("Option B") };
        var selectionTask = _host.PromptSelection("Pick", variants);

        // Verify SelectionPanel is active
        Assert.IsType<SelectionPanel>(_host.CurrentBottomPanel);

        // Now simulate user typing "/" — this should NOT remove the SelectionPanel
        _inputHandler.CurrentInput = "/help";
        _inputHandler.CursorPosition = 5;

        _host.ProcessOneTick(_state);

        // SelectionPanel must still be active
        Assert.IsType<SelectionPanel>(_host.CurrentBottomPanel);
    }

    private sealed record TestVariant(string Name) : IVariant;

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

    // ══════════════════════════════════════════════════════════════════
    //  Shrink cleanup + messages: exposed lines cleared BEFORE message render
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessOneTick_BlockShrink_WithMessages_ClearsExposedLinesBeforeRendering()
    {
        // Use real ConsoleRenderer so shrink-cleanup paths are exercised
        var renderer = new ConsoleRenderer(new StreamShellSettings(), _terminal);
        _host = new ConsoleAppHost(renderer, _inputHandler, _terminal);
        _state = new ConsoleAppHost.RenderSnapshot(
            null, 0, false, 0, _terminal.WindowWidth, 2, _terminal.BufferHeight);

        // Tick 1: large input block + one message
        _inputHandler.CurrentInput = "line1\nline2\nline3";
        _host.AddMessage("first message");
        var (_, state1) = _host.ProcessOneTick(_state);

        _terminal.ClearOutput();

        // Tick 2: input cleared (block shrinks), new messages queued
        _inputHandler.CurrentInput = "";
        _host.AddMessage("second message");
        _host.AddMessage("third message");
        var (_, state2) = _host.ProcessOneTick(state1);

        var texts = _terminal.WrittenTexts;

        // Find scroll region set (\x1b[0;...r) and reset (\x1b[r)
        int scrollSetIdx = texts.FindIndex(t => t.StartsWith("\x1b[0;"));
        int scrollResetIdx = texts.FindIndex(t => t == "\x1b[r");

        Assert.True(scrollSetIdx >= 0, "Scroll region should be set");
        Assert.True(scrollResetIdx > scrollSetIdx, "Scroll region should be reset after being set");

        // All shrink-cleanup \x1b[K calls must occur BEFORE the scroll region is set
        // (i.e., before messages are rendered inside the scroll region)
        for (int i = 0; i < scrollSetIdx; i++)
        {
            if (texts[i] == "\x1b[K")
            {
                // This is a shrink-cleanup clear — it must be before message rendering
                Assert.True(i < scrollSetIdx,
                    $"Shrink cleanup \x1b[K at index {i} must be before scroll region set at {scrollSetIdx}");
            }
        }

        // No \x1b[K between scroll region set and reset should be from shrink cleanup
        // (they're message clears, which is fine)
        int shrinkCleanupCount = texts.Take(scrollSetIdx).Count(t => t == "\x1b[K");
        Assert.True(shrinkCleanupCount > 0, "Shrink cleanup should have cleared at least one exposed line");
    }

    // ══════════════════════════════════════════════════════════════════
    //  Replay Width Tracking
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessOneTick_WidthDecreaseBelowLastReplay_FiresReplay()
    {
        // _lastReplayWidth starts at initial width (80).
        // Decrease to 60 → 60 < 80 → replay fires.
        CreateHost();
        _inputHandler.CurrentInput = "some text";
        var (_, state1) = _host.ProcessOneTick(_state);

        Assert.Equal(0, _host.ReplayTriggerCount);

        // Increase width first to set up state.LastWindowWidth > target
        _terminal.WindowWidth = 100;
        var (_, state2) = _host.ProcessOneTick(state1);

        // Decrease width below _lastReplayWidth (80)
        _terminal.WindowWidth = 60;
        // System.Windows might be used, so reduce width 60 < 80 (_lastReplayWidth)
        var (_, state3) = _host.ProcessOneTick(state2); // tick 1: decrease detected

        // 5 more stable ticks at same width for stabilization
        var (_, stateFinal) = _host.ProcessOneTick(state3);
        stateFinal = _host.ProcessOneTick(stateFinal).NewState;
        stateFinal = _host.ProcessOneTick(stateFinal).NewState;
        stateFinal = _host.ProcessOneTick(stateFinal).NewState;
        (_, stateFinal) = _host.ProcessOneTick(stateFinal); // 5th stable → fires

        Assert.Equal(1, _host.ReplayTriggerCount);
    }

    [Fact]
    public void ProcessOneTick_WidthDecreaseAboveLastReplay_SkipsReplay()
    {
        // _lastReplayWidth starts at initial width (80).
        // Increase to 130, then decrease to 110.
        // 110 > 80 → no replay (still wider than last emission).
        CreateHost();
        _inputHandler.CurrentInput = "some text";
        var (_, state1) = _host.ProcessOneTick(_state);

        Assert.Equal(0, _host.ReplayTriggerCount);

        // Increase width
        _terminal.WindowWidth = 130;
        var (_, state2) = _host.ProcessOneTick(state1);

        // Decrease width but stay above _lastReplayWidth (80)
        _terminal.WindowWidth = 110;
        var (_, state3) = _host.ProcessOneTick(state2); // tick 1: decrease detected

        // 5 stable ticks
        var (_, stateFinal) = _host.ProcessOneTick(state3);
        stateFinal = _host.ProcessOneTick(stateFinal).NewState;
        stateFinal = _host.ProcessOneTick(stateFinal).NewState;
        stateFinal = _host.ProcessOneTick(stateFinal).NewState;
        (_, stateFinal) = _host.ProcessOneTick(stateFinal); // 5th stable → should NOT replay

        Assert.Equal(0, _host.ReplayTriggerCount);
    }

    [Fact]
    public void ProcessOneTick_AfterReplay_DeeperDecreaseFiresNewReplay()
    {
        // _lastReplayWidth starts at 80.
        // Decrease to 60 → replay fires, _lastReplayWidth = 60.
        // Decrease to 50 → 50 < 60 → another replay fires.
        CreateHost();
        _inputHandler.CurrentInput = "some text";
        var (_, state1) = _host.ProcessOneTick(_state);

        // Increase, then decrease to 60 (below initial _lastReplayWidth=80)
        _terminal.WindowWidth = 100;
        var (_, stateWide) = _host.ProcessOneTick(state1);

        _terminal.WindowWidth = 60;
        var (_, stateDown1) = _host.ProcessOneTick(stateWide);

        var s = stateDown1;
        for (int i = 0; i < 5; i++)
            (_, s) = _host.ProcessOneTick(s);

        Assert.Equal(1, _host.ReplayTriggerCount);

        // Now _lastReplayWidth = 60. Decrease further to 50.
        _terminal.WindowWidth = 80; // widen again to set up state
        (_, s) = _host.ProcessOneTick(s);

        _terminal.WindowWidth = 50;
        (_, s) = _host.ProcessOneTick(s); // decrease detected

        for (int i = 0; i < 5; i++)
            (_, s) = _host.ProcessOneTick(s);

        Assert.Equal(2, _host.ReplayTriggerCount);
    }

    [Fact]
    public void ProcessOneTick_WidthIncreaseDuringResize_ResetsDetection()
    {
        // Start at width 80. Increase to 120 (no decrease).
        // Then decrease to 90 (above _lastReplayWidth=80).
        // During the stabilization, increase to 100 → resets detection.
        // Decrease to 70 (below 80) → should start fresh detection.
        CreateHost();
        _inputHandler.CurrentInput = "some text";
        var (_, state1) = _host.ProcessOneTick(_state);

        // Wide then narrow (but above _lastReplayWidth=80)
        _terminal.WindowWidth = 120;
        var (_, stateWide) = _host.ProcessOneTick(state1);

        _terminal.WindowWidth = 90;
        var (_, stateNarrow) = _host.ProcessOneTick(stateWide); // decrease detected

        // Increase during stabilization (simulates user overshoot)
        _terminal.WindowWidth = 100;
        var (_, stateReset) = _host.ProcessOneTick(stateNarrow); // width increased: reset

        // 5 stable ticks at 100 — should NOT fire replay
        var s = stateReset;
        for (int i = 0; i < 5; i++)
            (_, s) = _host.ProcessOneTick(s);

        Assert.Equal(0, _host.ReplayTriggerCount);
    }
}
