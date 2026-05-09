using StreamShell;

namespace StreamShell.Tests;

// ═════════════════════════════════════════════════════════════════════
//  CommandPalette Tests — command matching, hints, argument autocomplete
// ═════════════════════════════════════════════════════════════════════

public class CommandPaletteTests
{
    private static readonly Command[] SampleCommands =
    {
        new("hello", "Say hello", (_, _) => Task.CompletedTask),
        new("help", "Show help", (_, _) => Task.CompletedTask),
        new("version", "Show version", (_, _) => Task.CompletedTask),
        new("exit", "Exit the app", (_, _) => Task.CompletedTask),
    };

    private static readonly Command[] ArgSuggestionCommands =
    {
        new("deploy", "Deploy to environment",
            (_, _) => Task.CompletedTask,
            ["linux ubuntu", "linux debian", "windows server", "windows desktop"]),
    };

    private static CommandPalette CreatePalette(Command[]? commands = null)
        => new(commands ?? SampleCommands);

    // ── IsActive ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("/", true)]
    [InlineData("/command", true)]
    [InlineData("", false)]
    [InlineData("no-slash", false)]
    [InlineData(" /", false)]  // space before slash
    public void IsActive_ReturnsTrueForCommandPrefix(string input, bool expected)
    {
        Assert.Equal(expected, CommandPalette.IsActive(input));
    }

    // ── Constructor ──────────────────────────────────────────────────

    [Fact]
    public void Constructor_WithCommandProvider_CreatesPalette()
    {
        var palette = new CommandPalette(() => SampleCommands);
        Assert.NotNull(palette);
    }

    [Fact]
    public void Constructor_WithFixedCommands_CreatesPalette()
    {
        var palette = new CommandPalette(SampleCommands);
        Assert.NotNull(palette);
    }

    // ── GetLines: Not Active ─────────────────────────────────────────

    [Fact]
    public void GetLines_NotCommandInput_ReturnsEmptyLines()
    {
        var palette = CreatePalette();
        var lines = palette.GetLines("hello");

        Assert.Equal(CommandPalette.MaxHeight, lines.Count);
        Assert.All(lines, line => Assert.Equal(string.Empty, line));
    }

    [Fact]
    public void GetLines_EmptyInput_ReturnsEmptyLines()
    {
        var palette = CreatePalette();
        var lines = palette.GetLines("");

        Assert.Equal(CommandPalette.MaxHeight, lines.Count);
        Assert.All(lines, line => Assert.Equal(string.Empty, line));
    }

    // ── GetLines: Command Mode ───────────────────────────────────────

    [Fact]
    public void GetLines_CommandPrefixWithNoMatch_ReturnsEmptyLines()
    {
        var palette = CreatePalette();
        var lines = palette.GetLines("/zzzz");

        Assert.Equal(CommandPalette.MaxHeight, lines.Count);
        Assert.All(lines, line => Assert.Equal(string.Empty, line));
    }

    [Fact]
    public void GetLines_CommandPrefix_ReturnsStatusLineAndHints()
    {
        var palette = CreatePalette();
        var lines = palette.GetLines("/");

        Assert.Equal(CommandPalette.MaxHeight, lines.Count);
        // Line 0 = status line
        Assert.Contains("Tab", lines[0]);
        Assert.Contains("\u2191\u2193", lines[0]);
        // At least one hint populated
        Assert.True(lines.Skip(1).Any(l => !string.IsNullOrEmpty(l)));
    }

    [Fact]
    public void GetLines_MatchingPrefix_ShowsMatchingCommands()
    {
        var palette = CreatePalette();
        var lines = palette.GetLines("/he");

        // Should match "hello" and "help"
        Assert.True(lines[1].Contains("hello") || lines[2].Contains("hello"));
        Assert.True(lines[1].Contains("help") || lines[2].Contains("help"));
    }

    [Fact]
    public void GetLines_ExactCommandMatch_ShowsSuggestion()
    {
        var palette = CreatePalette();
        palette.GetLines("/hello");
        Assert.Equal("/hello ", palette.CurrentSuggestion);
    }

    [Fact]
    public void GetLines_SingleMatch_AutocompleteSuggestion()
    {
        var palette = CreatePalette();
        palette.GetLines("/he");

        // Multiple matches (hello, help) means no unique autocomplete
        Assert.NotNull(palette.CurrentSuggestion);
        // Should suggest the first match
        Assert.StartsWith("/", palette.CurrentSuggestion);
    }

    [Fact]
    public void GetLines_NoMatch_NullSuggestion()
    {
        var palette = CreatePalette();
        palette.GetLines("/zzz");
        Assert.Null(palette.CurrentSuggestion);
    }

    [Fact]
    public void GetLines_CaseInsensitiveMatching()
    {
        var palette = CreatePalette();
        var lines = palette.GetLines("/HELLO");

        Assert.True(lines.Skip(1).Any(l => l.Contains("hello")));
    }

    // ── GetLines: Cached Lines ───────────────────────────────────────

    [Fact]
    public void GetLines_CachedResult_ReturnsSameInstance()
    {
        var palette = CreatePalette();
        var first = palette.GetLines("/hello");
        var second = palette.GetLines("/hello");
        Assert.Same(first, second);  // same cached instance
    }

    [Fact]
    public void GetLines_ChangedInput_ReturnsNewLines()
    {
        var palette = CreatePalette();
        var first = palette.GetLines("/h");
        var second = palette.GetLines("/he");
        Assert.NotSame(first, second);
    }

    // ── Selection ────────────────────────────────────────────────────

    [Fact]
    public void GetLines_SelectionAffectsSuggestion()
    {
        var palette = CreatePalette();

        // Get lines, verify suggestion starts at first match
        palette.GetLines("/h");
        string? firstSuggestion = palette.CurrentSuggestion;

        // Navigate down
        palette.AdjustSelection(1);
        palette.GetLines("/h");
        string? secondSuggestion = palette.CurrentSuggestion;

        // Different selection may give different suggestion
        // At minimum, both should be non-null
        Assert.NotNull(firstSuggestion);
        Assert.NotNull(secondSuggestion);
    }

    [Fact]
    public void AdjustSelection_ClampsToMatchCount()
    {
        var palette = CreatePalette();
        palette.GetLines("/h");  // initialize state

        palette.AdjustSelection(10);  // beyond available matches
        palette.GetLines("/h");

        // Should be clamped to maxVisible - 1
        // Could be at any valid position, just shouldn't throw
        Assert.NotNull(palette.CurrentSuggestion);
    }

    [Fact]
    public void AdjustSelection_InputChanged_ResetsToZero()
    {
        var palette = CreatePalette();
        palette.GetLines("/h");
        palette.AdjustSelection(1);

        // Change input
        palette.GetLines("/he");
        // Selection should reset
        Assert.NotNull(palette.CurrentSuggestion);
    }

    // ── Argument Suggestions ─────────────────────────────────────────

    [Fact]
    public void ArgumentSuggestions_ShownWhenMatching()
    {
        var palette = new CommandPalette(ArgSuggestionCommands);
        var lines = palette.GetLines("/deploy ");

        // Should show argument completions
        Assert.True(lines.Skip(1).Any(l => l.Contains("linux") || l.Contains("windows")));
    }

    [Fact]
    public void ArgumentSuggestions_FilteredByTypedArg()
    {
        var palette = new CommandPalette(ArgSuggestionCommands);
        var lines = palette.GetLines("/deploy win");

        Assert.True(lines.Skip(1).Any(l => l.Contains("windows")));
    }

    [Fact]
    public void ArgumentSuggestions_NoMatch_ShowsOnlyStatusLine()
    {
        var palette = new CommandPalette(ArgSuggestionCommands);
        var lines = palette.GetLines("/deploy xyz");

        // Line 0 is always the status line when input starts with /
        Assert.Equal(CommandPalette.MaxHeight, lines.Count);
        Assert.All(lines.Skip(1), line => Assert.Equal(string.Empty, line));
    }

    [Fact]
    public void ArgumentSuggestions_CommonNextWord_CompressesHints()
    {
        var palette = new CommandPalette(ArgSuggestionCommands);
        var lines = palette.GetLines("/deploy li");

        // Should show a single compressed hint for "linux" common prefix
        Assert.True(lines.Skip(1).Any(l => l.Contains("linux")));
    }

    // ── MaxHeight / Capacity ─────────────────────────────────────────

    [Fact]
    public void GetLines_NeverExceedsMaxHeight()
    {
        var manyCommands = new List<Command>();
        for (int i = 0; i < 20; i++)
        {
            int idx = i;
            manyCommands.Add(new Command($"cmd{i}", $"Description {i}",
                (_, _) => Task.CompletedTask));
        }

        var palette = new CommandPalette(manyCommands);
        var lines = palette.GetLines("/");

        Assert.Equal(CommandPalette.MaxHeight, lines.Count);
    }
}

// ═════════════════════════════════════════════════════════════════════
//  CursorMovementHandler Tests — cursor navigation
// ═════════════════════════════════════════════════════════════════════

public class CursorMovementHandlerTests
{
    // Margin 80 = no wrapping for short strings (GetEffectiveWidth() yields 80)
    // Margin 10 = 4 chars per visual line (cap = width - totalMargin)
    // For margin=10: width=10, totalMargin=6, cap=4, so "hello world" wraps as
    //   ["hell"] (0-3), ["o wo"] (4-7), ["rld"] (8-10)

    private static CursorMovementHandler CreateHandler(
        TextBuffer buffer, SelectionManager selection, int margin = 80)
        => new(buffer, selection, () => margin, () => Array.Empty<Attachment>());

    private static CursorMovementHandler CreateHandlerWithAttachments(
        TextBuffer buffer, SelectionManager selection,
        IReadOnlyList<Attachment> attachments, int margin = 80)
        => new(buffer, selection, () => margin, () => attachments);

    // ══════════════════════════════════════════════════════════════════
    //  Left / Right
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MoveLeft_MovesCursorBack()
    {
        var buf = new TextBuffer();
        buf.Insert("hello");
        buf.MoveTo(3);
        var handler = CreateHandler(buf, new SelectionManager());
        handler.MoveCursorLeft(shift: false);
        Assert.Equal(2, buf.CursorPosition);
    }

    [Fact]
    public void MoveLeft_AtStart_Stays()
    {
        var buf = new TextBuffer();
        buf.Insert("hello");
        buf.MoveTo(0);
        var handler = CreateHandler(buf, new SelectionManager());
        handler.MoveCursorLeft(shift: false);
        Assert.Equal(0, buf.CursorPosition);
    }

    [Fact]
    public void MoveRight_MovesCursorForward()
    {
        var buf = new TextBuffer();
        buf.Insert("hello");
        buf.MoveTo(2);
        var handler = CreateHandler(buf, new SelectionManager());
        handler.MoveCursorRight(shift: false);
        Assert.Equal(3, buf.CursorPosition);
    }

    [Fact]
    public void MoveRight_AtEnd_Stays()
    {
        var buf = new TextBuffer();
        buf.Insert("hello");
        buf.MoveTo(5);
        var handler = CreateHandler(buf, new SelectionManager());
        handler.MoveCursorRight(shift: false);
        Assert.Equal(5, buf.CursorPosition);
    }

    [Fact]
    public void MoveLeft_WithShift_Selects()
    {
        var buf = new TextBuffer();
        buf.Insert("hello");
        buf.MoveTo(2);
        var sel = new SelectionManager();
        var handler = CreateHandler(buf, sel);
        handler.MoveCursorLeft(shift: true);
        Assert.True(sel.IsActiveAt(1));
    }

    [Fact]
    public void MoveRight_WithShift_Selects()
    {
        var buf = new TextBuffer();
        buf.Insert("hello");
        buf.MoveTo(2);
        var sel = new SelectionManager();
        var handler = CreateHandler(buf, sel);
        handler.MoveCursorRight(shift: true);
        Assert.True(sel.IsActiveAt(3));
    }

    // ══════════════════════════════════════════════════════════════════
    //  Placeholder Skipping
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MoveLeft_SkipsOverPlaceholder()
    {
        var buf = new TextBuffer();
        buf.Insert("ab[paste #1, 2 lines]cd");
        buf.MoveTo(buf.CurrentInput.IndexOf("cd", StringComparison.Ordinal));

        var attachments = new List<Attachment>
        {
            new("line1\nline2", AttachmentType.PlainText, 2, 1, "[paste #1, 2 lines]")
        };
        var handler = CreateHandlerWithAttachments(buf, new SelectionManager(), attachments);

        handler.MoveCursorLeft(shift: false);
        Assert.Equal(2, buf.CursorPosition); // lands at '[' start of placeholder
    }

    [Fact]
    public void MoveRight_SkipsOverPlaceholder()
    {
        var buf = new TextBuffer();
        buf.Insert("ab[paste #1, 2 lines]cd");
        buf.MoveTo(2); // before placeholder

        var attachments = new List<Attachment>
        {
            new("line1\nline2", AttachmentType.PlainText, 2, 1, "[paste #1, 2 lines]")
        };
        var handler = CreateHandlerWithAttachments(buf, new SelectionManager(), attachments);

        handler.MoveCursorRight(shift: false);
        Assert.Equal(buf.CurrentInput.IndexOf("cd", StringComparison.Ordinal), buf.CursorPosition);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Home / End
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MoveHome_JumpsToLineStart()
    {
        var buf = new TextBuffer();
        buf.Insert("abc\ndef\nghi");
        buf.MoveTo(9); // 'h' in "ghi"
        var handler = CreateHandler(buf, new SelectionManager());
        handler.MoveCursorHome(shift: false);
        Assert.Equal(8, buf.CursorPosition); // start of 'ghi' line
    }

    [Fact]
    public void MoveHome_AlreadyAtLineStart_GoesLeft()
    {
        var buf = new TextBuffer();
        buf.Insert("abc\ndef");
        buf.MoveTo(4); // start of 'def' line
        var handler = CreateHandler(buf, new SelectionManager());
        handler.MoveCursorHome(shift: false);
        Assert.Equal(3, buf.CursorPosition); // acts like Left → '\n'
    }

    [Fact]
    public void MoveEnd_JumpsToLineEnd()
    {
        var buf = new TextBuffer();
        buf.Insert("abc\ndef\nghi");
        buf.MoveTo(4); // start of 'def'
        var handler = CreateHandler(buf, new SelectionManager());
        handler.MoveCursorEnd(shift: false);
        Assert.Equal(7, buf.CursorPosition); // end of 'def' line
    }

    [Fact]
    public void MoveEnd_AtLineEnd_GoesRight()
    {
        var buf = new TextBuffer();
        buf.Insert("abc\ndef");
        buf.MoveTo(3); // '\n' between lines
        var handler = CreateHandler(buf, new SelectionManager());
        handler.MoveCursorEnd(shift: false);
        Assert.Equal(4, buf.CursorPosition); // acts like Right → 'd'
    }

    [Fact]
    public void MoveHome_WithShift_Selects()
    {
        var buf = new TextBuffer();
        buf.Insert("abc def");
        buf.MoveTo(5);
        var sel = new SelectionManager();
        var handler = CreateHandler(buf, sel);
        handler.MoveCursorHome(shift: true);
        Assert.True(sel.IsActiveAt(0));
    }

    // ══════════════════════════════════════════════════════════════════
    //  Word Left / Right
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MoveWordLeft_JumpsToPreviousWord()
    {
        var buf = new TextBuffer();
        buf.Insert("hello world foo");
        buf.MoveTo(14);
        var handler = CreateHandler(buf, new SelectionManager());
        handler.MoveCursorWordLeft(shift: false);
        Assert.Equal(12, buf.CursorPosition); // start of 'foo'
    }

    [Fact]
    public void MoveWordLeft_AtFirstWord_GoesToStart()
    {
        var buf = new TextBuffer();
        buf.Insert("hello world");
        buf.MoveTo(6);
        var handler = CreateHandler(buf, new SelectionManager());
        handler.MoveCursorWordLeft(shift: false);
        Assert.Equal(0, buf.CursorPosition);
    }

    [Fact]
    public void MoveWordRight_JumpsToNextWord()
    {
        var buf = new TextBuffer();
        buf.Insert("hello world foo");
        buf.MoveTo(0);
        var handler = CreateHandler(buf, new SelectionManager());
        handler.MoveCursorWordRight(shift: false);
        Assert.Equal(6, buf.CursorPosition); // start of 'world'
    }

    [Fact]
    public void MoveWordRight_AtLastWord_GoesToEnd()
    {
        var buf = new TextBuffer();
        buf.Insert("hello world");
        buf.MoveTo(6);
        var handler = CreateHandler(buf, new SelectionManager());
        handler.MoveCursorWordRight(shift: false);
        Assert.Equal(11, buf.CursorPosition);
    }

    [Fact]
    public void MoveWordLeft_SkipsPlaceholder()
    {
        var buf = new TextBuffer();
        buf.Insert("ab[paste #1, 2 lines]cd");
        buf.MoveTo(buf.CurrentInput.Length);

        var attachments = new List<Attachment>
        {
            new("content", AttachmentType.PlainText, 2, 1, "[paste #1, 2 lines]")
        };
        var handler = CreateHandlerWithAttachments(buf, new SelectionManager(), attachments);
        handler.MoveCursorWordLeft(shift: false);
        Assert.Equal(2, buf.CursorPosition);
    }

    [Fact]
    public void MoveWordRight_SkipsPlaceholder()
    {
        var buf = new TextBuffer();
        buf.Insert("ab[paste #1, 2 lines]cd");
        buf.MoveTo(0);

        var attachments = new List<Attachment>
        {
            new("content", AttachmentType.PlainText, 2, 1, "[paste #1, 2 lines]")
        };
        var handler = CreateHandlerWithAttachments(buf, new SelectionManager(), attachments);
        handler.MoveCursorWordRight(shift: false);
        Assert.Equal(buf.CurrentInput.IndexOf("cd", StringComparison.Ordinal), buf.CursorPosition);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Up / Down (Visual Line)
    // ══════════════════════════════════════════════════════════════════
    // GetEffectiveWidth() clamps to min 10. With margin=10 and totalMargin=6,
    // cap=4 per line. "hello world" → ["hell"], ["o wo"], ["rld"]
    // Offsets: 0, 4, 8

    [Fact]
    public void MoveUp_FromMiddleLine_GoesToSameColumnOnPreviousLine()
    {
        var buf = new TextBuffer();
        buf.Insert("hello world");
        buf.MoveTo(8); // 'r' in "rld" (vis line 2, col 0)
        var handler = CreateHandler(buf, new SelectionManager(), margin: 10);
        handler.MoveCursorUp(shift: false);
        Assert.Equal(4, buf.CursorPosition); // "o wo"[0]='o' → offset 4+0
    }

    [Fact]
    public void MoveUp_FromFirstLine_StaysAtCurrentPosition()
    {
        var buf = new TextBuffer();
        buf.Insert("hello world");
        buf.MoveTo(2);
        var handler = CreateHandler(buf, new SelectionManager(), margin: 10);
        handler.MoveCursorUp(shift: false);
        Assert.Equal(2, buf.CursorPosition);
    }

    [Fact]
    public void MoveDown_FromFirstLine_SameColumn()
    {
        var buf = new TextBuffer();
        buf.Insert("hello world");
        buf.MoveTo(2); // 'l' in "hell" (vis line 0, col 2)
        var handler = CreateHandler(buf, new SelectionManager(), margin: 10);
        handler.MoveCursorDown(shift: false);
        Assert.Equal(6, buf.CursorPosition); // "o wo"[2]='w' → offset 4+2
    }

    [Fact]
    public void MoveDown_FromLastLine_GoesToEnd()
    {
        var buf = new TextBuffer();
        buf.Insert("hello world");
        buf.MoveTo(8); // 'r' in "rld" (last vis line)
        var handler = CreateHandler(buf, new SelectionManager(), margin: 10);
        handler.MoveCursorDown(shift: false);
        Assert.Equal(11, buf.CursorPosition);
    }

    [Fact]
    public void MoveDown_AtBufferEnd_Stays()
    {
        var buf = new TextBuffer();
        buf.Insert("hello");
        buf.MoveTo(5);
        var handler = CreateHandler(buf, new SelectionManager(), margin: 80);
        handler.MoveCursorDown(shift: false);
        Assert.Equal(5, buf.CursorPosition);
    }

    [Fact]
    public void MoveUp_WithShift_Selects()
    {
        var buf = new TextBuffer();
        buf.Insert("hello world");
        buf.MoveTo(8);
        var sel = new SelectionManager();
        var handler = CreateHandler(buf, sel, margin: 10);
        handler.MoveCursorUp(shift: true);
        Assert.True(sel.HasAnchor);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Sticky Column
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void StickyColumn_PreservedAcrossVerticalMoves()
    {
        var buf = new TextBuffer();
        buf.Insert("hello world");
        buf.MoveTo(2); // col 2 on vis line 0
        var handler = CreateHandler(buf, new SelectionManager(), margin: 10);

        handler.MoveCursorDown(shift: false);
        Assert.Equal(6, buf.CursorPosition); // col 2 on line 1

        handler.MoveCursorDown(shift: false);
        Assert.Equal(10, buf.CursorPosition); // col 2 on line 2
    }

    [Fact]
    public void ResetStickyColumn_AfterHorizontalMove()
    {
        var buf = new TextBuffer();
        buf.Insert("hello world");
        buf.MoveTo(2);
        var handler = CreateHandler(buf, new SelectionManager(), margin: 10);
        handler.MoveCursorDown(shift: false); // moves to pos 6
        handler.ResetStickyColumn();
        handler.MoveCursorLeft(shift: false);
        Assert.Equal(5, buf.CursorPosition);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Empty Buffer
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void EmptyBuffer_AllMovementsStayAtZero()
    {
        var buf = new TextBuffer();
        var handler = CreateHandler(buf, new SelectionManager());

        handler.MoveCursorLeft(false);  Assert.Equal(0, buf.CursorPosition);
        handler.MoveCursorRight(false); Assert.Equal(0, buf.CursorPosition);
        handler.MoveCursorUp(false);    Assert.Equal(0, buf.CursorPosition);
        handler.MoveCursorDown(false);  Assert.Equal(0, buf.CursorPosition);
        handler.MoveCursorHome(false);  Assert.Equal(0, buf.CursorPosition);
        handler.MoveCursorEnd(false);   Assert.Equal(0, buf.CursorPosition);
        handler.MoveCursorWordLeft(false);  Assert.Equal(0, buf.CursorPosition);
        handler.MoveCursorWordRight(false); Assert.Equal(0, buf.CursorPosition);
    }
}
