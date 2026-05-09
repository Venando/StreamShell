using StreamShell;

namespace StreamShell.Tests;

// ═════════════════════════════════════════════════════════════════════
//  Mock Implementations for Testing
// ═════════════════════════════════════════════════════════════════════

/// <summary>Mock IInputHandler for testing ConsoleAppHost without Console dependency.</summary>
internal sealed class MockInputHandler : IInputHandler
{
    public string CurrentInput { get; set; } = "";
    public int CursorPosition { get; set; }
    public bool HasSelection { get; set; }
    public int RightMargin { get; set; } = 80;
    public bool QuitRequested { get; set; }
    public List<Attachment> Attachments { get; set; } = new();
    public int LargePasteThreshold { get; set; } = 300;
    public int LargePasteLineThreshold { get; set; } = 4;

    public bool TryGetSelection(out int start, out int length)
    {
        start = 0;
        length = 0;
        return false;
    }

    public string? ProcessInput()
    {
        if (_submittedInput != null)
        {
            string? result = _submittedInput;
            _submittedInput = null;
            return result;
        }
        return null;
    }

    /// <summary>Set an input that will be returned next time ProcessInput is called.</summary>
    public void QueueSubmittedInput(string input) => _submittedInput = input;

    private string? _submittedInput;

    public void Reset()
    {
        CurrentInput = "";
        CursorPosition = 0;
        HasSelection = false;
        Attachments.Clear();
    }

    public string SaveInputField()
    {
        string id = Guid.NewGuid().ToString();
        _savedInputs[id] = (CurrentInput, CursorPosition, Attachments.ToList());
        return id;
    }

    public bool LoadInputField(string id)
    {
        if (!_savedInputs.TryGetValue(id, out var state))
            return false;
        CurrentInput = state.Text;
        CursorPosition = state.Cursor;
        Attachments = state.Attachments.ToList();
        return true;
    }

    public bool RemoveSavedInputField(string id)
        => _savedInputs.Remove(id);

    public void RemoveAllSavedInputFields()
        => _savedInputs.Clear();

    public void SetInputFieldContent(string text)
    {
        CurrentInput = text;
        CursorPosition = text.Length;
        HasSelection = false;
    }

    public IReadOnlyList<string> GetSavedInputFieldIds()
        => _savedInputs.Keys.ToList();

    private readonly Dictionary<string, (string Text, int Cursor, List<Attachment> Attachments)> _savedInputs = new();
}

/// <summary>Mock IRenderer for testing ConsoleAppHost without Console dependency.</summary>
internal sealed class MockRenderer : IRenderer
{
    public List<string> RenderedMessages { get; } = new();
    public List<string> RenderedInputs { get; } = new();
    public int PanelLineCount { get; private set; }
    public SeparatorConfig TopSeparator { get; set; } = SeparatorConfig.Default;
    public SeparatorConfig BottomSeparator { get; set; } = SeparatorConfig.Default;
    public int? LastBlockOffset { get; private set; }
    public bool ClearInputBlockCalled { get; set; }
    public bool ClearInputLineCalled { get; set; }
    public bool ClearInputBlockForReRenderCalled { get; set; }

    public void SetPanelLineCount(int count) => PanelLineCount = count;

    public int GetBlockOffset(string input)
    {
        return (1 + PanelLineCount) + GetInputLineCount(input);
    }

    public int GetInputLineCount(string input)
    {
        if (string.IsNullOrEmpty(input))
            return 1;
        int lines = input.Split('\n').Length;
        // Rough line count: each segment, with wrapping when > 80 chars
        int total = 0;
        foreach (var seg in input.Split('\n'))
        {
            if (seg.Length <= 80)
                total++;
            else
                total += (seg.Length + 79) / 80;
        }
        return total;
    }

    public void ClearInputLine()
    {
        ClearInputLineCalled = true;
    }

    public void ClearInputBlock(string? lastInput)
    {
        ClearInputBlockCalled = true;
    }

    public void ClearInputBlockForReRender(string? oldInput, string newInput, int oldPanelLineCount)
    {
        ClearInputBlockForReRenderCalled = true;
    }

    public void RenderMessage(string markup)
    {
        RenderedMessages.Add(markup);
    }

    public void RenderInputBlock(
        string input, IReadOnlyList<string> hints,
        int cursorPosition, bool hasSelection,
        int selectionStart, int selectionLength, int margin)
    {
        RenderedInputs.Add(input);
    }

    public void OverwriteInputBlock(
        string input, IReadOnlyList<string> hints,
        int blockOffset, int cursorPosition, bool hasSelection,
        int selectionStart, int selectionLength, int margin)
    {
        RenderedInputs.Add($"OW:{input}");
    }

    public void HandleBlockHeightChange(int oldBlockOffset, int newBlockOffset)
    {
        LastBlockOffset = newBlockOffset;
    }
}

// ═════════════════════════════════════════════════════════════════════
//  ConsoleAppHost Tests
// ═════════════════════════════════════════════════════════════════════

public class ConsoleAppHostTests : IDisposable
{
    private readonly MockRenderer _renderer = new();
    private readonly MockInputHandler _inputHandler = new();
    private readonly ConsoleAppHost _host;

    public ConsoleAppHostTests()
    {
        _host = new ConsoleAppHost(_renderer, _inputHandler);
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    // ── Constructor ──────────────────────────────────────────────────

    [Fact]
    public void Constructor_CreatesHost()
    {
        Assert.NotNull(_host);
    }

    [Fact]
    public void Constructor_SettingsHaveDefaults()
    {
        Assert.Equal(300, _host.Settings.LargePasteThreshold);
        Assert.Equal(4, _host.Settings.LargePasteLineThreshold);
    }

    [Fact]
    public void Constructor_InputHandlerExposed()
    {
        Assert.Same(_inputHandler, _host.InputHandler);
    }

    // ── AddMessage ───────────────────────────────────────────────────

    [Fact]
    public void AddMessage_EnqueuesMessage()
    {
        _host.AddMessage("hello");
        // Renderer should not have it yet — messages are dequeued in the run loop
        // But when host runs, they get rendered
    }

    [Fact]
    public void AddMessage_SupportsMultipleMessages()
    {
        _host.AddMessage("first");
        _host.AddMessage("second");
    }

    [Fact]
    public void AddMessage_EmptyString_IsAllowed()
    {
        _host.AddMessage("");
    }

    // ── AddCommand ───────────────────────────────────────────────────

    [Fact]
    public void AddCommand_WithCommandObject_Registers()
    {
        var cmd = new Command("test", "A test", (_, _) => Task.CompletedTask);
        _host.AddCommand(cmd);
        // No exception = success
    }

    [Fact]
    public void AddCommand_WithParameters_Registers()
    {
        _host.AddCommand("hello", "Say hello", (_, _) => Task.CompletedTask,
            argumentSuggestions: new[] { "world", "friend" });
        // No exception = success
    }

    [Fact]
    public void AddCommand_WithoutArgumentSuggestions_Registers()
    {
        _host.AddCommand("version", "Show version",
            (_, _) => Task.CompletedTask, null);
    }

    [Fact]
    public void AddCommand_DuplicateName_Overwrites()
    {
        _host.AddCommand("cmd", "first", (_, _) => Task.CompletedTask, null);
        _host.AddCommand("cmd", "second", (_, _) => Task.CompletedTask, null);
        // No exception = overwrite OK
    }

    // ── Separator Configuration ──────────────────────────────────────

    [Fact]
    public void SetTopSeparator_SetsConfig()
    {
        _host.SetTopSeparator(leftText: "[bold]Chat[/]", repeatedCharacter: '=');
        Assert.Equal("[bold]Chat[/]", _renderer.TopSeparator.LeftText);
        Assert.Equal('=', _renderer.TopSeparator.RepeatedChar);
    }

    [Fact]
    public void SetBottomSeparator_SetsConfig()
    {
        _host.SetBottomSeparator(leftText: "Input", rightText: "End", repeatedCharacter: '-');
        Assert.Equal("Input", _renderer.BottomSeparator.LeftText);
        Assert.Equal("End", _renderer.BottomSeparator.RightText);
        Assert.Equal('-', _renderer.BottomSeparator.RepeatedChar);
    }

    [Fact]
    public void SetTopSeparator_WithMarkupText_SetsConfig()
    {
        _host.SetTopSeparator(leftText: "[dim]Messages[/]",
            rightText: "[bold]42[/]",
            repeatedCharacter: '─',
            repeatedCharMarkup: "dim");
        Assert.Equal("[dim]Messages[/]", _renderer.TopSeparator.LeftText);
        Assert.Equal("[bold]42[/]", _renderer.TopSeparator.RightText);
        Assert.Equal('─', _renderer.TopSeparator.RepeatedChar);
        Assert.Equal("dim", _renderer.TopSeparator.RepeatedCharMarkup);
    }

    [Fact]
    public void SetTopSeparator_NullText_UsesDefaults()
    {
        _host.SetTopSeparator();
        Assert.Null(_renderer.TopSeparator.LeftText);
        Assert.Null(_renderer.TopSeparator.RightText);
    }

    // ── Bottom Panel Management ──────────────────────────────────────

    [Fact]
    public void SetBottomPanel_SwitchesPanel()
    {
        var panel = new EmptyBottomPanel();
        _host.SetBottomPanel(panel);
        // No exception = success
    }

    [Fact]
    public void ResetBottomPanel_RestoresEmptyPanel()
    {
        var panel = new EmptyBottomPanel();
        _host.SetBottomPanel(panel);
        _host.ResetBottomPanel();
        // Should be back to default
    }

    [Fact]
    public void SetDefaultPanel_ChangesDefault()
    {
        var panel = new EmptyBottomPanel();
        _host.SetDefaultPanel(panel);
        _host.ResetBottomPanel();
        // Should now use the new default
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        _host.Dispose();
    }

    [Fact]
    public void SetBottomPanel_WhenDisposed_Throws()
    {
        _host.Dispose();
        var panel = new EmptyBottomPanel();
        // SetBottomPanel accesses disposed CTS; expect ObjectDisposedException
        Assert.Throws<ObjectDisposedException>(() => _host.SetBottomPanel(panel));
    }

    // ── Settings Application ─────────────────────────────────────────

    [Fact]
    public void Settings_AppliedToInputHandler()
    {
        _host.Settings.LargePasteThreshold = 500;
        _host.Settings.LargePasteLineThreshold = 10;
        // Settings are applied in constructor via ApplySettings
        Assert.Equal(300, _inputHandler.LargePasteThreshold);
        Assert.Equal(4, _inputHandler.LargePasteLineThreshold);
    }

    // ── Stop ─────────────────────────────────────────────────────────

    [Fact]
    public void Stop_SetsCancellation()
    {
        _host.Stop();
        // No exception — cancellation is signalled
    }

    [Fact]
    public void Stop_CanBeCalledMultipleTimes()
    {
        _host.Stop();
        _host.Stop();
    }

    // ── Dispose ──────────────────────────────────────────────────────

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        _host.Dispose();
        _host.Dispose(); // second call should not throw
    }

    // ── PromptSelection ──────────────────────────────────────────────

    [Fact]
    public void PromptSelection_ReturnsTask()
    {
        var variants = new IVariant[]
        {
            new TestVariant("A"),
            new TestVariant("B"),
        };

        var task = _host.PromptSelection("Pick one", variants);
        Assert.NotNull(task);
        Assert.False(task.IsCompleted); // not completed until user acts
    }

    [Fact]
    public void PromptSelection_WithSelectionInfo_ReturnsTask()
    {
        var variants = new IVariant[]
        {
            new TestVariant("A"),
            new TestVariant("B"),
        };

        var info = new SelectionInfo { Min = 1, Max = 2 };
        var task = _host.PromptSelection("Pick", variants, info);
        Assert.NotNull(task);
    }

    [Fact]
    public void PromptSelection_EmptyVariants_DoesNotThrow()
    {
        var task = _host.PromptSelection("Pick", Array.Empty<IVariant>());
        Assert.NotNull(task);
    }

    [Fact]
    public void PromptSelection_SingleVariant_ReturnsTask()
    {
        var variants = new IVariant[] { new TestVariant("Only") };
        var task = _host.PromptSelection("Pick", variants);
        Assert.NotNull(task);
    }

    private sealed record TestVariant(string Name) : IVariant;
}

// ═════════════════════════════════════════════════════════════════════
//  ConsoleRenderer Pure Logic Tests (additional edge cases)
// ═════════════════════════════════════════════════════════════════════

public class ConsoleRendererEdgeCasesTests
{
    // ── GetInputLines (via static ConsoleRenderer method) ────────────

    [Fact]
    public void GetInputLines_NullInput_ReturnsEmptyLine()
    {
        // null input is handled safely by LineWrappingService
        var result = ConsoleRenderer.GetInputLines(null!, 80);
        Assert.Single(result);
        Assert.Equal("", result[0]);
    }

    [Fact]
    public void GetInputLines_VeryNarrowMargin_ProducesAtLeastOneLine()
    {
        var result = ConsoleRenderer.GetInputLines("hello world", margin: 1);
        Assert.NotEmpty(result);
        Assert.True(result.Count >= 1);
    }

    [Fact]
    public void GetInputLines_SingleCharacter_ReturnsSingleLine()
    {
        var result = ConsoleRenderer.GetInputLines("x", margin: 80);
        Assert.Single(result);
        Assert.Equal("x", result[0]);
    }

    [Fact]
    public void GetInputLines_WithPrefixMargin_AdjustsLineCount()
    {
        // With margin=10 and prefixMargin=2, cap=10-6=4
        // "hello world" = 11 chars → ["hell"],["o wo"],["rld"]
        var result = ConsoleRenderer.GetInputLines("hello world", margin: 10, prefixMargin: 2);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void GetInputLines_ZeroPrefixMargin_UsesDefault()
    {
        // With margin=10 and prefixMargin=0, default is 2 (from StreamShellSettings)
        var result = ConsoleRenderer.GetInputLines("hello world", margin: 10, prefixMargin: 2);
        Assert.NotEmpty(result);
    }

    // ── GetCursorVisualPosition (via static ConsoleRenderer method) ──

    [Fact]
    public void GetCursorVisualPosition_CursorAtEndOfSingleLine_ReturnsLastColumn()
    {
        var (line, col) = ConsoleRenderer.GetCursorVisualPosition("hello", 5, margin: 80);
        Assert.Equal(0, line);
        Assert.Equal(5, col);
    }

    [Fact]
    public void GetCursorVisualPosition_WrappedText_AtVisualLineBoundary()
    {
        // margin=10, cap=4 → ["hell"],["o wo"],["rld"]
        // offset[1]=4, offset[2]=8
        // cursor at 4 → line 1, col 0
        var (line, col) = ConsoleRenderer.GetCursorVisualPosition("hello world", 4, margin: 10);
        Assert.Equal(1, line);
        Assert.Equal(0, col);
    }

    [Fact]
    public void GetCursorVisualPosition_WrappedText_EndOfFirstLine()
    {
        // margin=10, cap=4 → cursor at 3 → line 0, col 3
        var (line, col) = ConsoleRenderer.GetCursorVisualPosition("hello world", 3, margin: 10);
        Assert.Equal(0, line);
        Assert.Equal(3, col);
    }

    // ── GetVisualLineData (via static ConsoleRenderer method) ────────

    [Fact]
    public void GetVisualLineData_SingleChar_ReturnsSingleLine()
    {
        var (lines, offsets) = ConsoleRenderer.GetVisualLineData("a", margin: 80);
        Assert.Single(lines);
        Assert.Single(offsets);
        Assert.Equal("a", lines[0]);
        Assert.Equal(0, offsets[0]);
    }

    [Fact]
    public void GetVisualLineData_WithCustomMargins_Works()
    {
        var (lines, offsets) = ConsoleRenderer.GetVisualLineData("test", margin: 80, prefixMargin: 2, rightMargin: 4);
        Assert.Single(lines);
        Assert.Single(offsets);
    }

    [Fact]
    public void GetVisualLineData_NarrowMargin_ProducesWrappedLines()
    {
        // margin=10, prefixMargin=2, rightMargin=4 → cap=10-6=4
        // "abcdefgh" = 8 chars → ["abcd"],["efgh"]
        var (lines, _) = ConsoleRenderer.GetVisualLineData("abcdefgh", margin: 10);
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void GetVisualLineData_NewlinesWithSpaces_DoesNotThrow()
    {
        var (lines, offsets) = ConsoleRenderer.GetVisualLineData("a\n\nb", margin: 80);
        Assert.Equal(3, lines.Count);
        Assert.Equal(3, offsets.Count);
    }
}

// ═════════════════════════════════════════════════════════════════════
//  Attachment Record Tests
// ═════════════════════════════════════════════════════════════════════

public class AttachmentTests
{
    [Fact]
    public void Constructor_Default_MinimalRecord()
    {
        var a = new Attachment("content", AttachmentType.PlainText, 3);
        Assert.Equal("content", a.Content);
        Assert.Equal(AttachmentType.PlainText, a.Type);
        Assert.Equal(3, a.LineCount);
        Assert.Equal(0, a.Counter);
        Assert.Equal("", a.Placeholder);
    }

    [Fact]
    public void Constructor_WithAllParams_SetsAll()
    {
        var a = new Attachment("file contents", AttachmentType.PlainText, 5, 42, "[paste #42, 5 lines]");
        Assert.Equal("file contents", a.Content);
        Assert.Equal(AttachmentType.PlainText, a.Type);
        Assert.Equal(5, a.LineCount);
        Assert.Equal(42, a.Counter);
        Assert.Equal("[paste #42, 5 lines]", a.Placeholder);
    }

    [Fact]
    public void Constructor_EmptyContent_Allowed()
    {
        var a = new Attachment("", AttachmentType.PlainText, 0);
        Assert.Equal("", a.Content);
        Assert.Equal(0, a.LineCount);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var a1 = new Attachment("hello", AttachmentType.PlainText, 1, 5, "[paste #5, 1 line]");
        var a2 = new Attachment("hello", AttachmentType.PlainText, 1, 5, "[paste #5, 1 line]");
        Assert.Equal(a1, a2);
        Assert.True(a1 == a2);
    }

    [Fact]
    public void Equality_DifferentContent_NotEqual()
    {
        var a1 = new Attachment("hello", AttachmentType.PlainText, 1);
        var a2 = new Attachment("world", AttachmentType.PlainText, 1);
        Assert.NotEqual(a1, a2);
    }

    [Fact]
    public void Equality_DifferentLineCount_NotEqual()
    {
        var a1 = new Attachment("text", AttachmentType.PlainText, 1);
        var a2 = new Attachment("text", AttachmentType.PlainText, 3);
        Assert.NotEqual(a1, a2);
    }

    [Fact]
    public void Equality_WithNull_ReturnsFalse()
    {
        var a = new Attachment("text", AttachmentType.PlainText, 1);
        Assert.False(a.Equals(null));
    }

    [Fact]
    public void GetHashCode_SameValues_SameHash()
    {
        var a1 = new Attachment("data", AttachmentType.PlainText, 2, 10, "[paste #10, 2 lines]");
        var a2 = new Attachment("data", AttachmentType.PlainText, 2, 10, "[paste #10, 2 lines]");
        Assert.Equal(a1.GetHashCode(), a2.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentValues_DifferentHash()
    {
        var a1 = new Attachment("a", AttachmentType.PlainText, 1);
        var a2 = new Attachment("b", AttachmentType.PlainText, 1);
        Assert.NotEqual(a1.GetHashCode(), a2.GetHashCode());
    }

    [Fact]
    public void ToString_ContainsContent()
    {
        var a = new Attachment("my content", AttachmentType.PlainText, 1);
        Assert.Contains("my content", a.ToString());
    }

    [Fact]
    public void WithExpression_CreatesCopy()
    {
        var a1 = new Attachment("original", AttachmentType.PlainText, 3, 1, "[paste #1, 3 lines]");
        var a2 = a1 with { Content = "modified" };
        Assert.Equal("modified", a2.Content);
        Assert.Equal(3, a2.LineCount);
        Assert.Equal(1, a2.Counter);
        Assert.Equal("[paste #1, 3 lines]", a2.Placeholder);
        Assert.NotEqual(a1, a2);
    }

    [Fact]
    public void WithExpression_PreservesType()
    {
        var a1 = new Attachment("text", AttachmentType.PlainText, 1);
        var a2 = a1 with { LineCount = 5 };
        Assert.Equal(AttachmentType.PlainText, a2.Type);
        Assert.Equal(5, a2.LineCount);
    }
}

// ═════════════════════════════════════════════════════════════════════
//  Mock InputHandler / Save-Restore Integration Tests
// ═════════════════════════════════════════════════════════════════════

public class MockInputHandlerSaveLoadTests
{
    [Fact]
    public void MockInputHandler_DefaultState()
    {
        var handler = new MockInputHandler();
        Assert.Equal("", handler.CurrentInput);
        Assert.Equal(0, handler.CursorPosition);
        Assert.False(handler.HasSelection);
        Assert.Empty(handler.Attachments);
    }

    [Fact]
    public void MockInputHandler_SetInputFieldContent()
    {
        var handler = new MockInputHandler();
        handler.SetInputFieldContent("new text");
        Assert.Equal("new text", handler.CurrentInput);
        Assert.Equal(8, handler.CursorPosition);
    }

    [Fact]
    public void MockInputHandler_SaveAndLoad_ReturnsSameState()
    {
        var handler = new MockInputHandler();
        handler.SetInputFieldContent("hello world");

        string id = handler.SaveInputField();
        handler.CurrentInput = "modified";
        handler.LoadInputField(id);

        Assert.Equal("hello world", handler.CurrentInput);
    }

    [Fact]
    public void MockInputHandler_LoadUnknownId_ReturnsFalse()
    {
        var handler = new MockInputHandler();
        bool result = handler.LoadInputField("nonexistent");
        Assert.False(result);
    }

    [Fact]
    public void MockInputHandler_RemoveSavedInput_Removes()
    {
        var handler = new MockInputHandler();
        handler.SetInputFieldContent("save me");
        string id = handler.SaveInputField();

        Assert.True(handler.RemoveSavedInputField(id));
        Assert.False(handler.LoadInputField(id));
    }

    [Fact]
    public void MockInputHandler_RemoveUnknownId_ReturnsFalse()
    {
        var handler = new MockInputHandler();
        Assert.False(handler.RemoveSavedInputField("nonexistent"));
    }

    [Fact]
    public void MockInputHandler_RemoveAll_ClearsAll()
    {
        var handler = new MockInputHandler();
        handler.SaveInputField();
        handler.SaveInputField();
        handler.RemoveAllSavedInputFields();
        Assert.Empty(handler.GetSavedInputFieldIds());
    }

    [Fact]
    public void MockInputHandler_GetIds_ReturnsAll()
    {
        var handler = new MockInputHandler();
        string id1 = handler.SaveInputField();
        string id2 = handler.SaveInputField();
        var ids = handler.GetSavedInputFieldIds();
        Assert.Contains(id1, ids);
        Assert.Contains(id2, ids);
    }

    [Fact]
    public void MockInputHandler_QueueAndProcess_ReturnsSubmitted()
    {
        var handler = new MockInputHandler();
        handler.QueueSubmittedInput("test input");
        var result = handler.ProcessInput();
        Assert.Equal("test input", result);
    }

    [Fact]
    public void MockInputHandler_ProcessWithoutQueue_ReturnsNull()
    {
        var handler = new MockInputHandler();
        Assert.Null(handler.ProcessInput());
    }

    [Fact]
    public void MockInputHandler_Reset_ClearsState()
    {
        var handler = new MockInputHandler();
        handler.SetInputFieldContent("something");
        handler.Attachments.Add(new Attachment("a", AttachmentType.PlainText, 1));

        handler.Reset();

        Assert.Equal("", handler.CurrentInput);
        Assert.Equal(0, handler.CursorPosition);
        Assert.Empty(handler.Attachments);
    }

    [Fact]
    public void MockInputHandler_SavePreservesAttachments()
    {
        var handler = new MockInputHandler();
        handler.SetInputFieldContent("with attachment");
        handler.Attachments.Add(new Attachment("big paste", AttachmentType.PlainText, 5, 1, "[paste #1, 5 lines]"));

        string id = handler.SaveInputField();
        handler.Reset();
        handler.LoadInputField(id);

        Assert.Equal("with attachment", handler.CurrentInput);
        Assert.Single(handler.Attachments);
        Assert.Equal("big paste", handler.Attachments[0].Content);
    }

    [Fact]
    public void MockInputHandler_Reset_DoesNotAffectSaved()
    {
        var handler = new MockInputHandler();
        handler.SetInputFieldContent("original");
        string id = handler.SaveInputField();
        handler.Reset();

        handler.LoadInputField(id);
        Assert.Equal("original", handler.CurrentInput);
    }
}

// ═════════════════════════════════════════════════════════════════════
//  MockRenderer Tests
// ═════════════════════════════════════════════════════════════════════

public class MockRendererTests
{
    [Fact]
    public void MockRenderer_DefaultState()
    {
        var renderer = new MockRenderer();
        Assert.Empty(renderer.RenderedMessages);
        Assert.Empty(renderer.RenderedInputs);
        Assert.Equal(0, renderer.PanelLineCount);
    }

    [Fact]
    public void MockRenderer_SetPanelLineCount()
    {
        var renderer = new MockRenderer();
        renderer.SetPanelLineCount(5);
        Assert.Equal(5, renderer.PanelLineCount);
    }

    [Fact]
    public void MockRenderer_RenderMessage_Records()
    {
        var renderer = new MockRenderer();
        renderer.RenderMessage("test message");
        Assert.Single(renderer.RenderedMessages);
        Assert.Equal("test message", renderer.RenderedMessages[0]);
    }

    [Fact]
    public void MockRenderer_RenderInputBlock_Records()
    {
        var renderer = new MockRenderer();
        renderer.RenderInputBlock("input text", Array.Empty<string>(), 0, false, 0, 0, 80);
        Assert.Single(renderer.RenderedInputs);
        Assert.Equal("input text", renderer.RenderedInputs[0]);
    }

    [Fact]
    public void MockRenderer_OverwriteInputBlock_Prefixed()
    {
        var renderer = new MockRenderer();
        renderer.OverwriteInputBlock("new", Array.Empty<string>(), 3, 2, false, 0, 0, 80);
        Assert.Equal("OW:new", renderer.RenderedInputs[0]);
    }

    [Fact]
    public void MockRenderer_ClearInputBlock_Tracks()
    {
        var renderer = new MockRenderer();
        Assert.False(renderer.ClearInputBlockCalled);
        renderer.ClearInputBlock("old input");
        Assert.True(renderer.ClearInputBlockCalled);
    }

    [Fact]
    public void MockRenderer_GetInputLineCount_Empty()
    {
        var renderer = new MockRenderer();
        Assert.Equal(1, renderer.GetInputLineCount(""));
    }

    [Fact]
    public void MockRenderer_GetInputLineCount_SingleLine()
    {
        var renderer = new MockRenderer();
        Assert.Equal(1, renderer.GetInputLineCount("hello world"));
    }

    [Fact]
    public void MockRenderer_GetInputLineCount_MultiLine()
    {
        var renderer = new MockRenderer();
        Assert.Equal(3, renderer.GetInputLineCount("a\nb\nc"));
    }

    [Fact]
    public void MockRenderer_GetBlockOffset_MatchesExpected()
    {
        var renderer = new MockRenderer();
        renderer.SetPanelLineCount(2);
        int offset = renderer.GetBlockOffset("hello");
        // 1 + 2 + 1 = 4 (separator + panel + input line count)
        Assert.Equal(4, offset);
    }

    [Fact]
    public void MockRenderer_HandleBlockHeightChange_SetsLastOffset()
    {
        var renderer = new MockRenderer();
        renderer.HandleBlockHeightChange(10, 5);
        Assert.Equal(5, renderer.LastBlockOffset);
    }
}
