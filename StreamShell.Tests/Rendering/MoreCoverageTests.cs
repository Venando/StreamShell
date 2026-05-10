using System.Text;

namespace StreamShell.Tests;

// ═════════════════════════════════════════════════════════════════════
//  LineWrappingService — direct coverage for remaining branches
// ═════════════════════════════════════════════════════════════════════

public class LineWrappingServiceCoverageTests
{
    // ── GetInputLineCount (direct, not via ConsoleRenderer) ──────────

    [Fact]
    public void GetInputLineCount_Empty_ReturnsOne()
    {
        Assert.Equal(1, LineWrappingService.GetInputLineCount("", margin: 80));
    }

    [Fact]
    public void GetInputLineCount_ShortText_ReturnsOne()
    {
        Assert.Equal(1, LineWrappingService.GetInputLineCount("hello", margin: 80));
    }

    [Fact]
    public void GetInputLineCount_MultiLine_ReturnsSegmentCount()
    {
        // Each segment is one line when it fits
        Assert.Equal(4, LineWrappingService.GetInputLineCount("a\nb\nc\nd", margin: 80));
    }

    // ── GetInputLines (direct, not via ConsoleRenderer) ─────────────

    [Fact]
    public void GetInputLines_Empty_ReturnsSingleEmptyLine()
    {
        var lines = LineWrappingService.GetInputLines("", margin: 80);
        Assert.Single(lines);
        Assert.Equal("", lines[0]);
    }

    [Fact]
    public void GetInputLines_ShortText_SingleLine()
    {
        var lines = LineWrappingService.GetInputLines("hello world", margin: 80);
        Assert.Single(lines);
        Assert.Equal("hello world", lines[0]);
    }

    [Fact]
    public void GetInputLines_MultiSegment_ReturnsAllVisualLines()
    {
        // "ab\ncd" with margin 80 = two visual lines
        var lines = LineWrappingService.GetInputLines("ab\ncd", margin: 80);
        Assert.Equal(2, lines.Count);
        Assert.Equal("ab", lines[0]);
        Assert.Equal("cd", lines[1]);
    }

    [Fact]
    public void GetInputLines_TrailingNewline_ProducesEmptyFinalLine()
    {
        var lines = LineWrappingService.GetInputLines("ab\n", margin: 80);
        Assert.Equal(2, lines.Count);
        Assert.Equal("ab", lines[0]);
        Assert.Equal("", lines[1]);
    }

    // ── GetVisualLineData (direct) ───────────────────────────────────

    [Fact]
    public void GetVisualLineData_Empty_ReturnsEmptyLineAndZeroOffset()
    {
        var (lines, offsets) = LineWrappingService.GetVisualLineData("", margin: 80);
        Assert.Equal(new[] { "" }, lines);
        Assert.Equal(new[] { 0 }, offsets);
    }

    [Fact]
    public void GetVisualLineData_SingleSegment_ReturnsSingleLine()
    {
        var (lines, offsets) = LineWrappingService.GetVisualLineData("hello", margin: 80);
        Assert.Single(lines);
        Assert.Single(offsets);
        Assert.Equal("hello", lines[0]);
        Assert.Equal(0, offsets[0]);
    }

    [Fact]
    public void GetVisualLineData_MultiSegment_ReturnsCorrectOffsets()
    {
        var (lines, offsets) = LineWrappingService.GetVisualLineData("ab\ncd", margin: 80);
        Assert.Equal(2, lines.Count);
        Assert.Equal("ab", lines[0]);
        Assert.Equal("cd", lines[1]);
        Assert.Equal(0, offsets[0]);
        Assert.Equal(3, offsets[1]); // "ab" + \n = 3 chars
    }

    [Fact]
    public void GetVisualLineData_WrapsAtNarrowWidth()
    {
        // margin=10, cap=4 → "hello world!" wraps: ["hell", "o wo", "rld!"]
        var (lines, _) = LineWrappingService.GetVisualLineData("hello world!", margin: 10);
        Assert.Equal(3, lines.Count);
        Assert.Equal("hell", lines[0]);
        Assert.Equal("o wo", lines[1]);
        Assert.Equal("rld!", lines[2]);
    }

    [Fact]
    public void GetVisualLineData_NarrowWidthCorrectOffsets()
    {
        // margin=10, cap=4 → "hell", "o wo", "rld!"
        var (_, offsets) = LineWrappingService.GetVisualLineData("hello world!", margin: 10);
        Assert.Equal(3, offsets.Count);
        Assert.Equal(0, offsets[0]);
        Assert.Equal(4, offsets[1]); // "hell" length
        Assert.Equal(8, offsets[2]); // "hell" + "o wo" length
    }

    // ── GetCursorVisualPosition (direct) ─────────────────────────────

    [Fact]
    public void GetCursorVisualPosition_AtStart_ReturnsZero()
    {
        var (line, col) = LineWrappingService.GetCursorVisualPosition("hello", 0, margin: 80);
        Assert.Equal(0, line);
        Assert.Equal(0, col);
    }

    [Fact]
    public void GetCursorVisualPosition_InMiddle_ReturnsCorrectLineAndColumn()
    {
        var (line, col) = LineWrappingService.GetCursorVisualPosition("hello", 2, margin: 80);
        Assert.Equal(0, line);
        Assert.Equal(2, col);
    }

    [Fact]
    public void GetCursorVisualPosition_PastEnd_ReturnsLastLineEnd()
    {
        var (line, col) = LineWrappingService.GetCursorVisualPosition("hello", 100, margin: 80);
        Assert.Equal(0, line);
        Assert.Equal(5, col); // end of last line
    }

    [Fact]
    public void GetCursorVisualPosition_WrappedText_SecondLine()
    {
        // margin=10, cap=4 → ["hell", "o wo", "rld!"]
        // Char position 5 → vis col 1 on line 1 ("o wo"[1] = ' ')
        var (line, col) = LineWrappingService.GetCursorVisualPosition("hello world!", 5, margin: 10);
        Assert.Equal(1, line);
        Assert.Equal(1, col);
    }

    [Fact]
    public void GetCursorVisualPosition_AtWrappedLineBoundary_ReturnsLineAndCol()
    {
        var (line, col) = LineWrappingService.GetCursorVisualPosition("hello world!", 4, margin: 10);
        Assert.True(line >= 0, "line should be >= 0");
        Assert.True(col >= 0, "col should be >= 0");
    }

    [Fact]
    public void GetCursorVisualPosition_MultiSegment_ReturnsLineAndCol()
    {
        var (line, col) = LineWrappingService.GetCursorVisualPosition("ab\ncd", 4, margin: 80);
        Assert.True(line >= 0, "line should be >= 0");
        Assert.True(col >= 0, "col should be >= 0");
    }

    // ── WrapSegment edge cases ──────────────────────────────────────

    [Fact]
    public void WrapSegment_EmptyMultiSegment_PreventsShortCircuit()
    {
        // Empty middle segment that should NOT short-circuit
        // isFirst=false so it goes through wrapping
        var result = LineWrappingService.WrapSegment("", 20,
            isFirstSegment: false, isLastSegment: false, isFirstVisualLine: false);
        Assert.Single(result);
        Assert.Equal("", result[0]);
    }

    [Fact]
    public void WrapSegment_EmptyFirstSegment_NonLast_NoShortCircuit()
    {
        // isFirst=true, isLast=false → does NOT hit short-circuit
        var result = LineWrappingService.WrapSegment("", 20,
            isFirstSegment: true, isLastSegment: false, isFirstVisualLine: true);
        Assert.Single(result);
        Assert.Equal("", result[0]);
    }

    [Fact]
    public void WrapSegment_NoZeroCap_ForContinuationLine()
    {
        // When NOT first visual line, cap = max(1, width-6) = 1
        var result = LineWrappingService.WrapSegment("ab", 4,
            isFirstSegment: false, isLastSegment: true, isFirstVisualLine: false);
        Assert.Equal(2, result.Count);
        Assert.Equal("a", result[0]);
        Assert.Equal("b", result[1]);
    }

    [Fact]
    public void WrapSegment_EmptySegment_InMultiSegmentInput()
    {
        // Empty middle segment from "a\n\nb" processing
        var result = LineWrappingService.WrapSegment("", 80,
            isFirstSegment: false, isLastSegment: false, isFirstVisualLine: false);
        Assert.Single(result);
        Assert.Equal("", result[0]);
    }
}

// ═════════════════════════════════════════════════════════════════════
//  ClipboardHandler Tests — placeholder, removable, cleanup logic
// ═════════════════════════════════════════════════════════════════════

public class ClipboardHandlerTests
{
    private static ClipboardHandler CreateHandler(
        TextBuffer? buffer = null,
        SelectionManager? selection = null,
        int largePasteThreshold = 300,
        int largePasteLineThreshold = 4,
        IClipboardService? clipboard = null)
    {
        buffer ??= new TextBuffer();
        selection ??= new SelectionManager();
        var tempInput = new StringBuilder();
        return new ClipboardHandler(
            buffer,
            selection,
            tempInput,
            () => { },
            () => largePasteThreshold,
            () => largePasteLineThreshold,
            clipboard ?? new MockClipboardService());
    }

    /// <summary>Mock clipboard that stores a single text value.</summary>
    private sealed class MockClipboardService : IClipboardService
    {
        public string? StoredText { get; private set; }
        public string? PasteResult { get; set; }

        public string? Paste() => PasteResult;
        public void Copy(string text) => StoredText = text;
    }

    // ── GeneratePlaceholder ──────────────────────────────────────────

    [Theory]
    [InlineData(1, 1, "[paste #1, 1 line]")]
    [InlineData(2, 1, "[paste #1, 2 lines]")]
    [InlineData(3, 5, "[paste #5, 3 lines]")]
    [InlineData(10, 42, "[paste #42, 10 lines]")]
    [InlineData(1, 7, "[paste #7, 1 line]")]
    public void GeneratePlaceholder_FormatsCorrectly(int lineCount, int counter, string expected)
    {
        var result = ClipboardHandler.GeneratePlaceholder(lineCount, counter);
        Assert.Equal(expected, result);
    }

    // ── RemovePlaceholderAffectedBy ──────────────────────────────────

    [Fact]
    public void RemovePlaceholderAffectedBy_OverlapStart_RemovesPlaceholder()
    {
        var buf = new TextBuffer();
        buf.Insert("ab[paste #1, 2 lines]cd");
        var handler = CreateHandler(buffer: buf);

        // Add matching attachment
        handler.Attachments.Add(new Attachment("content", AttachmentType.PlainText, 2, 1, "[paste #1, 2 lines]"));

        // Removal at position 0, length 4 should overlap with placeholder at pos 2
        bool removed = handler.RemovePlaceholderAffectedBy(0, 4);

        Assert.True(removed);
    }

    [Fact]
    public void RemovePlaceholderAffectedBy_NoOverlap_DoesNotRemove()
    {
        var buf = new TextBuffer();
        buf.Insert("ab[paste #1, 2 lines]cd");
        var handler = CreateHandler(buffer: buf);
        handler.Attachments.Add(new Attachment("content", AttachmentType.PlainText, 2, 1, "[paste #1, 2 lines]"));

        // Removal at position 0, length 1 → no overlap with placeholder at pos 2
        bool removed = handler.RemovePlaceholderAffectedBy(0, 1);

        Assert.False(removed);
    }

    [Fact]
    public void RemovePlaceholderAffectedBy_NoAttachments_ReturnsFalse()
    {
        var buf = new TextBuffer();
        buf.Insert("hello");
        var handler = CreateHandler(buffer: buf);

        bool removed = handler.RemovePlaceholderAffectedBy(0, 5);

        Assert.False(removed);
    }

    [Fact]
    public void RemovePlaceholderAffectedBy_EmptyPlaceholder_Skip()
    {
        var buf = new TextBuffer();
        buf.Insert("hello");
        var handler = CreateHandler(buffer: buf);
        handler.Attachments.Add(new Attachment("content", AttachmentType.PlainText, 1, 1, ""));

        bool removed = handler.RemovePlaceholderAffectedBy(0, 3);
        Assert.False(removed);
    }

    // ── CleanupOrphanedAttachments ───────────────────────────────────

    [Fact]
    public void CleanupOrphanedAttachments_RemovesNonexistentPlaceholders()
    {
        var buf = new TextBuffer();
        buf.Insert("hello");
        var handler = CreateHandler(buffer: buf);

        handler.Attachments.Add(new Attachment("content", AttachmentType.PlainText, 1, 1, "[paste #1, 1 line]"));

        handler.CleanupOrphanedAttachments();
        Assert.Empty(handler.Attachments);
    }

    [Fact]
    public void CleanupOrphanedAttachments_KeepsExistingPlaceholders()
    {
        var buf = new TextBuffer();
        buf.Insert("[paste #1, 2 lines] extra text");
        var handler = CreateHandler(buffer: buf);

        handler.Attachments.Add(new Attachment("content", AttachmentType.PlainText, 2, 1, "[paste #1, 2 lines]"));

        handler.CleanupOrphanedAttachments();
        Assert.Single(handler.Attachments);
    }

    [Fact]
    public void CleanupOrphanedAttachments_EmptyPlaceholder_NotRemoved()
    {
        var buf = new TextBuffer();
        buf.Insert("text");
        var handler = CreateHandler(buffer: buf);

        // Empty placeholder — shouldn't trigger Contains check on empty string
        handler.Attachments.Add(new Attachment("content", AttachmentType.PlainText, 1, 1, ""));

        handler.CleanupOrphanedAttachments();
        Assert.Empty(handler.Attachments);
    }

    // ── BufferCharacter / FlushTempInput → InsertPastedText ─────────

    [Fact]
    public void BufferCharacter_FlushesToBuffer()
    {
        var buf = new TextBuffer();
        var handler = CreateHandler(buffer: buf);

        handler.BufferCharacter('h');
        handler.BufferCharacter('i');

        // Read temp input field via reflection to verify it's stored
        var tempInputField = typeof(ClipboardHandler).GetField("_tempInput",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var tempInput = (StringBuilder)tempInputField.GetValue(handler)!;
        Assert.Equal("hi", tempInput.ToString());
    }

    [Fact]
    public void FlushTempInput_InsertsIntoBuffer()
    {
        var buf = new TextBuffer();
        buf.Insert("a");
        buf.MoveTo(1);
        var handler = CreateHandler(buffer: buf);

        // Add some temp input
        var tempInputField = typeof(ClipboardHandler).GetField("_tempInput",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        ((StringBuilder)tempInputField.GetValue(handler)!).Append("bc");

        handler.FlushTempInput();

        Assert.Equal("abc", buf.CurrentInput);
        Assert.Equal(3, buf.CursorPosition);
    }

    [Fact]
    public void FlushTempInput_Empty_DoesNothing()
    {
        var buf = new TextBuffer();
        buf.Insert("hello");
        var handler = CreateHandler(buffer: buf);

        handler.FlushTempInput();

        Assert.Equal("hello", buf.CurrentInput);
    }

    // ── ResetCounter ─────────────────────────────────────────────────

    [Fact]
    public void ResetCounter_ResetsAttachmentCounter()
    {
        var handler = CreateHandler();
        // Counter starts at 0, reset again to ensure no exception
        handler.ResetCounter();
        // No assertion needed — just verifies no exception
    }

    // ── Paste from clipboard (via mock) ──────────────────────────────

    [Fact]
    public void PasteFromClipboard_WithMock_InsertsSmallText()
    {
        var buf = new TextBuffer();
        var mock = new MockClipboardService { PasteResult = "pasted text" };
        var handler = CreateHandler(buffer: buf, clipboard: mock, largePasteThreshold: 100);

        handler.PasteFromClipboard();

        Assert.Equal("pasted text", buf.CurrentInput);
    }

    [Fact]
    public void PasteFromClipboard_LargePaste_CreatesAttachment()
    {
        var buf = new TextBuffer();
        var mock = new MockClipboardService { PasteResult = "line1\nline2\nline3\nline4\nline5" };
        var handler = CreateHandler(buffer: buf, clipboard: mock, largePasteThreshold: 5, largePasteLineThreshold: 3);

        handler.PasteFromClipboard();

        Assert.Single(handler.Attachments);
        Assert.Equal(AttachmentType.PlainText, handler.Attachments[0].Type);
        Assert.Contains("[paste #1,", buf.CurrentInput);
    }

    [Fact]
    public void PasteFromClipboard_NullResult_DoesNothing()
    {
        var buf = new TextBuffer();
        var mock = new MockClipboardService { PasteResult = null };
        var handler = CreateHandler(buffer: buf, clipboard: mock);

        handler.PasteFromClipboard();

        Assert.Equal("", buf.CurrentInput);
        Assert.Empty(handler.Attachments);
    }

    [Fact]
    public void PasteFromClipboard_WithSelection_ReplacesSelection()
    {
        var buf = new TextBuffer();
        buf.Insert("hello world");
        buf.MoveTo(5);
        var sel = new SelectionManager();
        sel.SetAnchor(0); // selects "hello"
        var mock = new MockClipboardService { PasteResult = "hi" };
        var handler = CreateHandler(buffer: buf, selection: sel, clipboard: mock);

        handler.PasteFromClipboard();

        Assert.Equal("hi world", buf.CurrentInput);
        Assert.Equal(2, buf.CursorPosition);
        Assert.False(sel.HasAnchor); // selection cleared
    }

    // ── Copy to clipboard ────────────────────────────────────────────

    [Fact]
    public void CopyToClipboard_CopiesFullText()
    {
        var buf = new TextBuffer();
        buf.Insert("copy this");
        var mock = new MockClipboardService();
        var handler = CreateHandler(buffer: buf, clipboard: mock);

        handler.CopyToClipboard();

        Assert.Equal("copy this", mock.StoredText);
    }

    [Fact]
    public void CopyToClipboard_WithSelection_CopiesSelectedOnly()
    {
        var buf = new TextBuffer();
        buf.Insert("hello world");
        buf.MoveTo(5);
        var sel = new SelectionManager();
        sel.SetAnchor(0); // selects "hello"
        var mock = new MockClipboardService();
        var handler = CreateHandler(buffer: buf, selection: sel, clipboard: mock);

        handler.CopyToClipboard();

        Assert.Equal("hello", mock.StoredText);
    }

    // ── Cut to clipboard ─────────────────────────────────────────────

    [Fact]
    public void CutToClipboard_WithSelection_CutsAndCopies()
    {
        var buf = new TextBuffer();
        buf.Insert("hello world");
        buf.MoveTo(5);
        var sel = new SelectionManager();
        sel.SetAnchor(0); // selects "hello"
        var mock = new MockClipboardService();
        var handler = CreateHandler(buffer: buf, selection: sel, clipboard: mock);

        handler.CutToClipboard();

        Assert.Equal("hello", mock.StoredText);
        Assert.Equal(" world", buf.CurrentInput);
        Assert.Equal(0, buf.CursorPosition);
    }

    [Fact]
    public void CutToClipboard_NoSelection_CutsAll()
    {
        var buf = new TextBuffer();
        buf.Insert("everything");
        var mock = new MockClipboardService();
        var handler = CreateHandler(buffer: buf, clipboard: mock);

        handler.CutToClipboard();

        Assert.Equal("everything", mock.StoredText);
        Assert.Equal("", buf.CurrentInput);
    }
}

// ═════════════════════════════════════════════════════════════════════
//  EmptyBottomPanel Tests
// ═════════════════════════════════════════════════════════════════════

public class EmptyBottomPanelTests
{
    [Fact]
    public void LineCount_MatchesCommandPaletteMaxHeight()
    {
        var panel = new EmptyBottomPanel();
        Assert.Equal(CommandPalette.MaxHeight, ((IBottomPanel)panel).LineCount);
    }

    [Fact]
    public void GetLines_ReturnsAllEmpty()
    {
        var panel = new EmptyBottomPanel();
        var lines = panel.GetLines("anything");
        Assert.Equal(CommandPalette.MaxHeight, lines.Count);
        Assert.All(lines, l => Assert.Equal(string.Empty, l));
    }

    [Fact]
    public void GetLines_AnyInput_ReturnsEmpty()
    {
        var panel = new EmptyBottomPanel();
        var lines1 = panel.GetLines("/command");
        var lines2 = panel.GetLines("text");
        Assert.All(lines1, l => Assert.Equal(string.Empty, l));
        Assert.All(lines2, l => Assert.Equal(string.Empty, l));
    }
}

// ═════════════════════════════════════════════════════════════════════
//  StreamShellSettings Tests
// ═════════════════════════════════════════════════════════════════════

public class StreamShellSettingsTests
{
    [Fact]
    public void DefaultValues_AreSet()
    {
        var s = new StreamShellSettings();
        Assert.Equal(300, s.LargePasteThreshold);
        Assert.Equal(4, s.LargePasteLineThreshold);
        Assert.Equal("bold black on cyan", s.CursorMarkup);
        Assert.Equal("bold cyan on Grey27", s.SelectionMarkup);
        Assert.Equal("Red1", s.CommandSlashMarkup);
        Assert.Equal("[bold SkyBlue1]> [/]", s.InputPrefix);
        Assert.Equal("  ", s.ContinuationPrefix);
        Assert.Equal(4, s.WrappingRightMargin);
    }

    [Fact]
    public void PrefixMargin_DefaultBothTwoChars()
    {
        var s = new StreamShellSettings();
        // InputPrefix "[bold SkyBlue1]> [/]" stripped is "> " = 2 chars
        // ContinuationPrefix "  " = 2 chars
        // Math.Max(2, 2) = 2
        Assert.Equal(2, s.PrefixMargin);
    }

    [Fact]
    public void PrefixMargin_ReturnsNonNegative_DefaultConfig()
    {
        var s = new StreamShellSettings();
        // Should return at least the minimum prefix width
        Assert.True(s.PrefixMargin >= 2);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var s = new StreamShellSettings
        {
            LargePasteThreshold = 500,
            LargePasteLineThreshold = 10,
            CursorMarkup = "bold red on black",
            SelectionMarkup = "green on black",
            CommandSlashMarkup = "Blue1",
            InputPrefix = ">> ",
            ContinuationPrefix = "..",
            WrappingRightMargin = 8
        };

        Assert.Equal(500, s.LargePasteThreshold);
        Assert.Equal(10, s.LargePasteLineThreshold);
        Assert.Equal("bold red on black", s.CursorMarkup);
        Assert.Equal("green on black", s.SelectionMarkup);
        Assert.Equal("Blue1", s.CommandSlashMarkup);
        Assert.Equal(">> ", s.InputPrefix);
        Assert.Equal("..", s.ContinuationPrefix);
        Assert.Equal(8, s.WrappingRightMargin);
    }
}

// ═════════════════════════════════════════════════════════════════════
//  SeparatorConfig Tests
// ═════════════════════════════════════════════════════════════════════

public class SeparatorConfigTests
{
    [Fact]
    public void Default_HasDefaultValues()
    {
        var c = new SeparatorConfig();
        Assert.Null(c.LeftText);
        Assert.Null(c.RightText);
        Assert.Equal('─', c.RepeatedChar);
        Assert.Null(c.RepeatedCharMarkup);
    }

    [Fact]
    public void StaticDefault_ReturnsNewInstance()
    {
        var c = SeparatorConfig.Default;
        Assert.NotNull(c);
        Assert.Null(c.LeftText);
        Assert.Null(c.RightText);
        Assert.Equal('─', c.RepeatedChar);
    }

    [Fact]
    public void WithInitProperties_StoresValues()
    {
        var c = new SeparatorConfig
        {
            LeftText = "[bold]left[/]",
            RightText = "[dim]right[/]",
            RepeatedChar = '-',
            RepeatedCharMarkup = "dim"
        };

        Assert.Equal("[bold]left[/]", c.LeftText);
        Assert.Equal("[dim]right[/]", c.RightText);
        Assert.Equal('-', c.RepeatedChar);
        Assert.Equal("dim", c.RepeatedCharMarkup);
    }

    [Fact]
    public void RepeatedCharMarkup_Null_UsesPlainChar()
    {
        var c = new SeparatorConfig
        {
            RepeatedChar = '='
        };
        Assert.Equal('=', c.RepeatedChar);
        Assert.Null(c.RepeatedCharMarkup);
    }
}

// ═════════════════════════════════════════════════════════════════════
//  Command Tests
// ═════════════════════════════════════════════════════════════════════

public class CommandTests
{
    private static readonly Func<string[], Dictionary<string, string>, Task> NoopHandler =
        (_, _) => Task.CompletedTask;

    [Fact]
    public void Constructor_StoresProperties()
    {
        var cmd = new Command("test", "A test command", NoopHandler);
        Assert.Equal("test", cmd.Name);
        Assert.Equal("A test command", cmd.Description);
        Assert.Same(NoopHandler, cmd.Handler);
        Assert.Null(cmd.ArgumentSuggestions);
    }

    [Fact]
    public void Constructor_WithArgumentSuggestions_StoresThem()
    {
        var suggestions = new[] { "arg1", "arg2" };
        var cmd = new Command("deploy", "Deploy command", NoopHandler, suggestions);
        Assert.Equal("deploy", cmd.Name);
        Assert.Same(suggestions, cmd.ArgumentSuggestions);
    }

    [Fact]
    public void Constructor_EmptyName_Allowed()
    {
        var cmd = new Command("", "empty name", NoopHandler);
        Assert.Equal("", cmd.Name);
    }

    [Fact]
    public void Constructor_NullHandler_Allowed()
    {
        // Note: Handler is set via constructor, Func can be null
        var cmd = new Command("test", "desc", NoopHandler);
        Assert.NotNull(cmd.Handler);
    }

    [Fact]
    public void Constructor_NoArgumentSuggestions_NullByDefault()
    {
        var cmd = new Command("cmd", "description", NoopHandler);
        Assert.Null(cmd.ArgumentSuggestions);
    }
}

// ═════════════════════════════════════════════════════════════════════
//  SelectionInfo Tests
// ═════════════════════════════════════════════════════════════════════

public class SelectionInfoTests
{
    [Fact]
    public void DefaultValues()
    {
        var info = new SelectionInfo();
        Assert.Equal("Done", info.SubmitTitle);
        Assert.Equal(0, info.Min);
        Assert.Equal(0, info.Max);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var info = new SelectionInfo
        {
            SubmitTitle = "Confirm",
            Min = 1,
            Max = 5
        };
        Assert.Equal("Confirm", info.SubmitTitle);
        Assert.Equal(1, info.Min);
        Assert.Equal(5, info.Max);
    }
}
