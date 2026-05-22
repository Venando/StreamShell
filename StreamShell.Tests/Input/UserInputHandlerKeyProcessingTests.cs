namespace StreamShell.Tests;

// ═════════════════════════════════════════════════════════════════════
//  UserInputHandler — Key Processing Tests (via MockTerminal)
//  Tests the full key dispatch pipeline: ProcessInput reads keys
//  from the mock terminal and dispatches to all handlers.
// ═════════════════════════════════════════════════════════════════════

public class UserInputHandlerKeyProcessingTests
{
    private readonly MockTerminal _terminal = new();

    private UserInputHandler CreateHandler() => new(_terminal);

    // ══════════════════════════════════════════════════════════════════
    //  Constructor
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_CreatesHandler()
    {
        var handler = CreateHandler();
        Assert.NotNull(handler);
        Assert.Equal("", handler.CurrentInput);
        Assert.Equal(0, handler.CursorPosition);
        Assert.False(handler.QuitRequested);
    }

    [Fact]
    public void Constructor_RightMargin_FromTerminal()
    {
        _terminal.WindowWidth = 120;
        var handler = CreateHandler();
        Assert.Equal(120, handler.RightMargin);
    }

    [Fact]
    public void Constructor_FallbackMargin_WhenWidthIsZero()
    {
        _terminal.WindowWidth = 0;
        var handler = CreateHandler();
        Assert.Equal(80, handler.RightMargin);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Typing Characters
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessInput_TypingCharacters_BuildsText()
    {
        _terminal.EnqueueChar('h');
        _terminal.EnqueueChar('e');
        _terminal.EnqueueChar('l');
        _terminal.EnqueueChar('l');
        _terminal.EnqueueChar('o');

        var handler = CreateHandler();
        var result = handler.ProcessInput();

        Assert.Equal("hello", handler.CurrentInput);
        Assert.Equal(5, handler.CursorPosition);
        Assert.Null(result); // no submit yet
    }

    [Fact]
    public void ProcessInput_SingleCharacter_InsertsAtCursor()
    {
        _terminal.EnqueueChar('a');
        var handler = CreateHandler();
        handler.ProcessInput();

        Assert.Equal("a", handler.CurrentInput);
        Assert.Equal(1, handler.CursorPosition);
    }

    [Fact]
    public void ProcessInput_EmptyBuffer_NoKeys_ReturnsNull()
    {
        var handler = CreateHandler();
        var result = handler.ProcessInput();

        Assert.Null(result);
        Assert.Equal("", handler.CurrentInput);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Enter key — Submit
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessInput_Enter_SubmitsInput()
    {
        var handler = CreateHandler();
        // Set up text via API, then press Enter
        handler.SetInputFieldContent("hi");

        _terminal.EnqueueEnter();
        var result = handler.ProcessInput();

        Assert.Equal("hi", result);
        Assert.Equal("", handler.CurrentInput); // reset after submit
        Assert.Equal(0, handler.CursorPosition);
    }

    [Fact]
    public void ProcessInput_Enter_WithEmptyBuffer_SubmitsEmptyString()
    {
        _terminal.EnqueueEnter();

        var handler = CreateHandler();
        var result = handler.ProcessInput();

        Assert.Equal("", result);
        Assert.Equal("", handler.CurrentInput); // reset after submit
        Assert.Equal(0, handler.CursorPosition);
    }

    [Fact]
    public void ProcessInput_Enter_OnlySubmitsOnce()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("first");

        _terminal.EnqueueEnter();
        var first = handler.ProcessInput();
        Assert.Equal("first", first);
        Assert.Equal("", handler.CurrentInput); // reset

        handler.SetInputFieldContent("second");
        _terminal.EnqueueEnter();
        var second = handler.ProcessInput();
        Assert.Equal("second", second);
    }

    [Fact]
    public void ProcessInput_TypingAndSubmit_AcrossMultipleCalls()
    {
        var handler = CreateHandler();

        // Type text in one call
        _terminal.EnqueueChar('h');
        _terminal.EnqueueChar('e');
        _terminal.EnqueueChar('l');
        _terminal.EnqueueChar('p');
        handler.ProcessInput();
        Assert.Equal("help", handler.CurrentInput);

        // Submit in another call
        _terminal.EnqueueEnter();
        var result = handler.ProcessInput();

        Assert.Equal("help", result);
        Assert.Equal("", handler.CurrentInput);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Enter key with modifiers — insert newline
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessInput_ShiftEnter_InsertsNewline()
    {
        _terminal.EnqueueChar('a');
        _terminal.EnqueueShiftEnter();

        var handler = CreateHandler();
        handler.ProcessInput();

        Assert.Equal("a\n", handler.CurrentInput);
    }

    [Fact]
    public void ProcessInput_CtrlEnter_InsertsNewline()
    {
        _terminal.EnqueueChar('x');
        _terminal.EnqueueCtrl(ConsoleKey.Enter);

        var handler = CreateHandler();
        handler.ProcessInput();

        Assert.Equal("x\n", handler.CurrentInput);
    }

    [Fact]
    public void ProcessInput_AltEnter_InsertsNewline()
    {
        _terminal.EnqueueChar('1');
        _terminal.EnqueueRaw(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, true));

        var handler = CreateHandler();
        handler.ProcessInput();

        Assert.Equal("1\n", handler.CurrentInput);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Ctrl+D — Quit
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessInput_CtrlD_SetsQuitRequested()
    {
        _terminal.EnqueueCtrl(ConsoleKey.D);

        var handler = CreateHandler();
        handler.ProcessInput();

        Assert.True(handler.QuitRequested);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Ctrl+A — Select All
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessInput_CtrlA_SelectsAll()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("abc");

        _terminal.EnqueueCtrl(ConsoleKey.A);
        handler.ProcessInput();

        Assert.True(handler.HasSelection);
        Assert.True(handler.TryGetSelection(out int start, out int length));
        Assert.Equal(0, start);
        Assert.Equal(3, length);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Ctrl+C — Copy (no-op in mock, should not crash)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessInput_CtrlC_DoesNotCrash()
    {
        _terminal.EnqueueCtrl(ConsoleKey.C);

        var handler = CreateHandler();
        handler.ProcessInput();

        // Copy succeeds silently, buffer unchanged
        Assert.Equal("", handler.CurrentInput);
    }

    [Fact]
    public void ProcessInput_CtrlC_DoesNotInterrupt()
    {
        _terminal.EnqueueCtrl(ConsoleKey.C);

        var handler = CreateHandler();
        handler.ProcessInput();

        Assert.False(handler.QuitRequested);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Ctrl+X — Cut
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessInput_CtrlX_CutsWithSelection()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("ab");

        // Select all then cut
        _terminal.EnqueueCtrl(ConsoleKey.A);
        _terminal.EnqueueCtrl(ConsoleKey.X);
        handler.ProcessInput();

        Assert.Equal("", handler.CurrentInput);
        Assert.Equal(0, handler.CursorPosition);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Ctrl+Z — Undo
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessInput_CtrlZ_UndoesLastChange()
    {
        // Set text, then type one char
        var handler = CreateHandler();
        handler.SetInputFieldContent("ab");

        _terminal.EnqueueChar('c');
        handler.ProcessInput();
        Assert.Equal("abc", handler.CurrentInput);

        // Undo
        _terminal.EnqueueCtrl(ConsoleKey.Z);
        handler.ProcessInput();

        Assert.Equal("ab", handler.CurrentInput);
        Assert.Equal(2, handler.CursorPosition);
    }

    [Fact]
    public void ProcessInput_CtrlZ_WithEmptyHistory_DoesNothing()
    {
        _terminal.EnqueueCtrl(ConsoleKey.Z);

        var handler = CreateHandler();
        handler.ProcessInput();

        Assert.Equal("", handler.CurrentInput);
    }

    [Fact]
    public void ProcessInput_Undo_MultipleTimes()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("a");

        // Type 'b' then 'c' in separate ProcessInput calls
        _terminal.EnqueueChar('b');
        handler.ProcessInput();
        Assert.Equal("ab", handler.CurrentInput);

        _terminal.EnqueueChar('c');
        handler.ProcessInput();
        Assert.Equal("abc", handler.CurrentInput);

        // First undo: removes last char
        _terminal.EnqueueCtrl(ConsoleKey.Z);
        handler.ProcessInput();
        Assert.Equal("ab", handler.CurrentInput);

        // Keep undoing until we get back to "a"
        // (may need multiple undoes due to snapshot batching)
        while (handler.CurrentInput != "a")
        {
            _terminal.EnqueueCtrl(ConsoleKey.Z);
            handler.ProcessInput();
        }
        Assert.Equal("a", handler.CurrentInput);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Navigation Keys
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessInput_LeftArrow_MovesCursorLeft()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("abc");

        _terminal.EnqueueRaw(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false));
        handler.ProcessInput();

        Assert.Equal(2, handler.CursorPosition);
    }

    [Fact]
    public void ProcessInput_RightArrow_MovesCursorRight()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("ab");

        // Move to start
        _terminal.EnqueueRaw(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
        handler.ProcessInput();
        Assert.Equal(0, handler.CursorPosition);

        // Move right
        _terminal.EnqueueRaw(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));
        handler.ProcessInput();

        Assert.Equal(1, handler.CursorPosition);
    }

    [Fact]
    public void ProcessInput_Home_MovesToStart()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("xyz");

        _terminal.EnqueueRaw(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
        handler.ProcessInput();

        Assert.Equal(0, handler.CursorPosition);
    }

    [Fact]
    public void ProcessInput_End_MovesToEnd()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("xy");

        // Move to start, then end
        _terminal.EnqueueRaw(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
        handler.ProcessInput();

        _terminal.EnqueueRaw(new ConsoleKeyInfo('\0', ConsoleKey.End, false, false, false));
        handler.ProcessInput();

        Assert.Equal(2, handler.CursorPosition);
    }

    [Fact]
    public void ProcessInput_LeftArrowAtStart_StaysAtZero()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("a");

        _terminal.EnqueueRaw(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false));
        handler.ProcessInput();
        Assert.Equal(0, handler.CursorPosition);

        _terminal.EnqueueRaw(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false));
        handler.ProcessInput();
        Assert.Equal(0, handler.CursorPosition);
    }

    [Fact]
    public void ProcessInput_RightArrowAtEnd_StaysAtEnd()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("x");
        Assert.Equal(1, handler.CursorPosition);

        _terminal.EnqueueRaw(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));
        handler.ProcessInput();
        Assert.Equal(1, handler.CursorPosition);
    }

    [Fact]
    public void ProcessInput_UpArrow_WithMultiLine_MovesUp()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("a\nb");
        // Cursor at 3 (past 'b')

        _terminal.EnqueueRaw(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        handler.ProcessInput();

        // Moves to end of first line (position 1, just after 'a')
        Assert.Equal(1, handler.CursorPosition);
    }

    [Fact]
    public void ProcessInput_DownArrow_WithMultiLine_MovesDown()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("a\nbc");
        // Cursor at 4 (past 'c')

        // Move up, then down should return to original position (sticky column)
        _terminal.EnqueueRaw(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        _terminal.EnqueueRaw(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        handler.ProcessInput();

        // Returns to original position due to sticky column
        Assert.Equal(4, handler.CursorPosition);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Ctrl+Arrow — VS Code-style word navigation
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessInput_CtrlRightArrow_JumpsToEndOfWord()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("hello");

        _terminal.EnqueueRaw(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
        handler.ProcessInput();
        Assert.Equal(0, handler.CursorPosition);

        _terminal.EnqueueCtrlArrow(ConsoleKey.RightArrow);
        handler.ProcessInput();

        // Stops at end of word
        Assert.Equal(5, handler.CursorPosition);
    }

    [Fact]
    public void ProcessInput_CtrlRightArrow_JumpsWordRight_VsCodeStyle()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("a b");
        Assert.Equal(3, handler.CursorPosition);

        // Move to start first
        _terminal.EnqueueRaw(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
        handler.ProcessInput();
        Assert.Equal(0, handler.CursorPosition);

        // First jump: end of 'a'
        _terminal.EnqueueCtrlArrow(ConsoleKey.RightArrow);
        handler.ProcessInput();
        Assert.Equal(1, handler.CursorPosition);

        // Second jump: skip ' b' (space + word) to end of 'b'
        _terminal.EnqueueCtrlArrow(ConsoleKey.RightArrow);
        handler.ProcessInput();
        Assert.Equal(3, handler.CursorPosition);
    }

    [Fact]
    public void ProcessInput_CtrlRightArrow_VsCodeStyle_DotSeparated()
    {
        // VS Code: <start>selected<here>.hint<here>
        // Package (before fix): <start>selected<here>.<here>hint<here>
        var handler = CreateHandler();
        handler.SetInputFieldContent("selected.hint");

        _terminal.EnqueueRaw(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
        handler.ProcessInput();
        Assert.Equal(0, handler.CursorPosition);

        // First jump: end of 'selected'
        _terminal.EnqueueCtrlArrow(ConsoleKey.RightArrow);
        handler.ProcessInput();
        Assert.Equal(8, handler.CursorPosition); // selected|

        // Second jump: separator '.' + 'hint' as one group
        _terminal.EnqueueCtrlArrow(ConsoleKey.RightArrow);
        handler.ProcessInput();
        Assert.Equal(13, handler.CursorPosition); // .hint|
    }

    [Fact]
    public void ProcessInput_CtrlRightArrow_VsCodeStyle_MixedTokens()
    {
        // Reproduces the user's exact example:
        // ValueKind = Array : "[{"role":"
        var handler = CreateHandler();
        handler.SetInputFieldContent("ValueKind = Array : \"[{\"role\":\"");

        _terminal.EnqueueRaw(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
        handler.ProcessInput();
        Assert.Equal(0, handler.CursorPosition);

        // VS Code stops: ValueKind| =| Array| :| "[{"|role|":"| (end)
        int[] expectedStops = [9, 11, 17, 19, 24, 28, 31];
        foreach (int expected in expectedStops)
        {
            _terminal.EnqueueCtrlArrow(ConsoleKey.RightArrow);
            handler.ProcessInput();
            Assert.Equal(expected, handler.CursorPosition);
        }
    }

    [Fact]
    public void ProcessInput_CtrlLeftArrow_VsCodeStyle_MixedTokens()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("ValueKind = Array : \"[{\"role\":\"");
        // Cursor at end (31)
        Assert.Equal(31, handler.CursorPosition);

        // VS Code backward: jumps to start of each previous word,
        // skipping all intervening non-word chars (whitespace + separators)
        int[] expectedStops = [24, 12, 0];
        foreach (int expected in expectedStops)
        {
            _terminal.EnqueueCtrlArrow(ConsoleKey.LeftArrow);
            handler.ProcessInput();
            Assert.Equal(expected, handler.CursorPosition);
        }
    }

    [Fact]
    public void ProcessInput_CtrlLeftArrow_JumpsWordLeft()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("hello");
        // Cursor at end
        Assert.Equal(5, handler.CursorPosition);

        _terminal.EnqueueCtrlArrow(ConsoleKey.LeftArrow);
        handler.ProcessInput();

        // Stops at start of word
        Assert.Equal(0, handler.CursorPosition);
    }

    [Fact]
    public void ProcessInput_CtrlRightArrow_SeparatorsOnly()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("::==>>");

        _terminal.EnqueueRaw(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
        handler.ProcessInput();
        Assert.Equal(0, handler.CursorPosition);

        // All separators are one group? No, :: and == are separate if whitespace between...
        // Actually without whitespace, all contiguous separators are one group
        _terminal.EnqueueCtrlArrow(ConsoleKey.RightArrow);
        handler.ProcessInput();
        Assert.Equal(6, handler.CursorPosition);
    }

    [Fact]
    public void ProcessInput_CtrlRightArrow_SeparatorsWithWhitespace()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent(":: == >>");

        _terminal.EnqueueRaw(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
        handler.ProcessInput();
        Assert.Equal(0, handler.CursorPosition);

        int[] expectedStops = [2, 5, 8];
        foreach (int expected in expectedStops)
        {
            _terminal.EnqueueCtrlArrow(ConsoleKey.RightArrow);
            handler.ProcessInput();
            Assert.Equal(expected, handler.CursorPosition);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Shift+Arrow — Selection
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessInput_ShiftRight_SelectsText()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("hi");

        // Move to start
        _terminal.EnqueueRaw(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
        handler.ProcessInput();

        // Shift+Right
        _terminal.EnqueueShiftArrow(ConsoleKey.RightArrow);
        handler.ProcessInput();

        Assert.Equal(1, handler.CursorPosition);
        Assert.True(handler.HasSelection);
        Assert.True(handler.TryGetSelection(out int start, out int length));
        Assert.Equal(0, start);
        Assert.Equal(1, length);
    }

    [Fact]
    public void ProcessInput_ShiftLeft_SelectsTextLeft()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("ab");

        // Shift+Left from end
        _terminal.EnqueueShiftArrow(ConsoleKey.LeftArrow);
        handler.ProcessInput();

        Assert.True(handler.HasSelection);
        Assert.True(handler.TryGetSelection(out int start, out int length));
        Assert.Equal(1, start);
        Assert.Equal(1, length);
    }

    [Fact]
    public void ProcessInput_ShiftHome_SelectsToStart()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("xyz");

        _terminal.EnqueueShiftArrow(ConsoleKey.Home);
        handler.ProcessInput();

        Assert.Equal(0, handler.CursorPosition);
        Assert.True(handler.HasSelection);
    }

    [Fact]
    public void ProcessInput_ShiftEnd_SelectsToEnd()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("ab");

        // Move to start, then Shift+End
        _terminal.EnqueueRaw(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
        handler.ProcessInput();

        _terminal.EnqueueShiftArrow(ConsoleKey.End);
        handler.ProcessInput();

        Assert.Equal(2, handler.CursorPosition);
        Assert.True(handler.HasSelection);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Backspace
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessInput_Backspace_RemovesCharBeforeCursor()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("abc");

        _terminal.EnqueueRaw(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));
        handler.ProcessInput();

        Assert.Equal("ab", handler.CurrentInput);
        Assert.Equal(2, handler.CursorPosition);
    }

    [Fact]
    public void ProcessInput_Backspace_AtStart_DoesNothing()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("a");

        _terminal.EnqueueRaw(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
        handler.ProcessInput();
        Assert.Equal(0, handler.CursorPosition);

        _terminal.EnqueueRaw(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));
        handler.ProcessInput();

        Assert.Equal("a", handler.CurrentInput);
        Assert.Equal(0, handler.CursorPosition);
    }

    [Fact]
    public void ProcessInput_Backspace_WithSelection_RemovesSelection()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("abc");

        // Select last two chars via Shift+Left twice
        _terminal.EnqueueShiftArrow(ConsoleKey.LeftArrow);
        _terminal.EnqueueShiftArrow(ConsoleKey.LeftArrow);
        handler.ProcessInput();

        _terminal.EnqueueRaw(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));
        handler.ProcessInput();

        Assert.Equal("a", handler.CurrentInput);
        Assert.Equal(1, handler.CursorPosition);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Delete
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessInput_Delete_RemovesCharAtCursor()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("abc");

        // Move cursor to position 1 ('b'), then Delete removes 'b'
        _terminal.EnqueueRaw(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false));
        _terminal.EnqueueRaw(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false));
        handler.ProcessInput();
        Assert.Equal(1, handler.CursorPosition);

        _terminal.EnqueueRaw(new ConsoleKeyInfo('\x7f', ConsoleKey.Delete, false, false, false));
        handler.ProcessInput();

        Assert.Equal("ac", handler.CurrentInput);
    }

    [Fact]
    public void ProcessInput_Delete_AtEnd_DoesNothing()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("z");

        _terminal.EnqueueRaw(new ConsoleKeyInfo('\x7f', ConsoleKey.Delete, false, false, false));
        handler.ProcessInput();

        Assert.Equal("z", handler.CurrentInput);
    }

    [Fact]
    public void ProcessInput_Delete_WithSelection_RemovesSelection()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("xyz");

        // Select all, then Delete
        _terminal.EnqueueCtrl(ConsoleKey.A);
        _terminal.EnqueueRaw(new ConsoleKeyInfo('\x7f', ConsoleKey.Delete, false, false, false));
        handler.ProcessInput();

        Assert.Equal("", handler.CurrentInput);
        Assert.Equal(0, handler.CursorPosition);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Escape — Reset
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessInput_Escape_ResetsBuffer()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("part");

        _terminal.EnqueueEscape();
        handler.ProcessInput();

        Assert.Equal("", handler.CurrentInput);
        Assert.Equal(0, handler.CursorPosition);
    }

    [Fact]
    public void ProcessInput_Escape_OnEmptyBuffer_DoesNotThrow()
    {
        _terminal.EnqueueEscape();

        var handler = CreateHandler();
        handler.ProcessInput();

        Assert.Equal("", handler.CurrentInput);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Tab — AutoComplete (with provider)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessInput_Tab_WithProvider_CompletesText()
    {
        var handler = CreateHandler();
        handler.AutoCompleteProvider = input =>
            input == "h" ? "hello" : null;
        handler.SetInputFieldContent("h");

        _terminal.EnqueueTab();
        handler.ProcessInput();

        Assert.Equal("hello", handler.CurrentInput);
    }

    [Fact]
    public void ProcessInput_Tab_WithNoProvider_DoesNothing()
    {
        var handler = CreateHandler();
        handler.AutoCompleteProvider = null;
        handler.SetInputFieldContent("h");

        _terminal.EnqueueTab();
        handler.ProcessInput();

        Assert.Equal("h", handler.CurrentInput);
    }

    [Fact]
    public void ProcessInput_Tab_WithProviderThatReturnsNull_DoesNothing()
    {
        var handler = CreateHandler();
        handler.AutoCompleteProvider = _ => null;
        handler.SetInputFieldContent("z");

        _terminal.EnqueueTab();
        handler.ProcessInput();

        Assert.Equal("z", handler.CurrentInput);
    }

    // ══════════════════════════════════════════════════════════════════
    //  KeyInterceptor
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessInput_KeyInterceptor_ConsumesKey_WhenReturnsTrue()
    {
        bool intercepted = false;
        var handler = CreateHandler();
        handler.KeyInterceptor = key =>
        {
            if (key.Key == ConsoleKey.Enter)
            {
                intercepted = true;
                return true;
            }
            return false;
        };

        handler.SetInputFieldContent("ab");
        _terminal.EnqueueEnter();
        handler.ProcessInput();

        Assert.True(intercepted, "Interceptor should have been called");
        Assert.Equal("ab", handler.CurrentInput); // Enter was intercepted, not submitted
    }

    [Fact]
    public void ProcessInput_KeyInterceptor_DoesNotConsume_WhenReturnsFalse()
    {
        var handler = CreateHandler();
        handler.KeyInterceptor = _ => false;
        handler.SetInputFieldContent("h");

        _terminal.EnqueueEnter();
        var result = handler.ProcessInput();

        Assert.Equal("h", result);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Ctrl+V — Paste (doesn't crash)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessInput_CtrlV_Paste_DoesNotThrow()
    {
        var handler = CreateHandler();
        handler.SetInputFieldContent("x");

        _terminal.EnqueueCtrl(ConsoleKey.V);
        handler.ProcessInput();

        Assert.Equal("x", handler.CurrentInput);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Mixed key sequences
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessInput_TypeAndEscape_NoSubmit()
    {
        _terminal.EnqueueChar('t');
        _terminal.EnqueueChar('e');
        _terminal.EnqueueChar('m');
        _terminal.EnqueueChar('p');
        _terminal.EnqueueEscape();

        var handler = CreateHandler();
        var result = handler.ProcessInput();

        Assert.Null(result); // not submitted
        Assert.Equal("", handler.CurrentInput); // reset by escape
    }

    [Fact]
    public void ProcessInput_TypeCharacters_ThenSubmit()
    {
        var handler = CreateHandler();

        _terminal.EnqueueChar('h');
        _terminal.EnqueueChar('e');
        _terminal.EnqueueChar('l');
        _terminal.EnqueueChar('l');
        _terminal.EnqueueChar('o');
        handler.ProcessInput();

        Assert.Equal("hello", handler.CurrentInput);

        // Now submit
        _terminal.EnqueueEnter();
        var result = handler.ProcessInput();
        Assert.Equal("hello", result);
    }

    // ══════════════════════════════════════════════════════════════════
    //  RightMargin changes
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void RightMargin_CanBeSet()
    {
        var handler = CreateHandler();
        handler.RightMargin = 100;
        Assert.Equal(100, handler.RightMargin);
    }

    [Fact]
    public void RightMargin_DefaultFromTerminalWidth()
    {
        _terminal.WindowWidth = 50;
        var handler = CreateHandler();
        Assert.Equal(50, handler.RightMargin);
    }
}
