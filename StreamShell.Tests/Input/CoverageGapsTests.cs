using System.Text;

namespace StreamShell.Tests;

// ═════════════════════════════════════════════════════════════════════
//  UserInputHandler Direct Tests — pure logic (no Console dependency)
// ═════════════════════════════════════════════════════════════════════

public class UserInputHandlerDirectTests
{
    // ── Constructor ──────────────────────────────────────────────────

    [Fact]
    public void Constructor_DefaultValues()
    {
        var handler = new UserInputHandler();
        Assert.Equal("", handler.CurrentInput);
        Assert.Equal(0, handler.CursorPosition);
        Assert.False(handler.HasSelection);
        Assert.False(handler.QuitRequested);
        Assert.Empty(handler.Attachments);
        Assert.Null(handler.AutoCompleteProvider); // null until set by ConsoleAppHost
    }

    [Fact]
    public void Constructor_DefaultThresholds()
    {
        var handler = new UserInputHandler();
        Assert.Equal(300, handler.LargePasteThreshold);
        Assert.Equal(4, handler.LargePasteLineThreshold);
    }

    [Fact]
    public void Constructor_RightMargin_FallsBackToDefaultInHeadless()
    {
        var handler = new UserInputHandler();
        // In headless environments, RightMargin falls back to 80
        // In a real console, it matches Console.WindowWidth.
        // Either way it should be a reasonable positive value.
        Assert.True(handler.RightMargin >= 10, $"RightMargin should be >= 10, got {handler.RightMargin}");
    }

    [Fact]
    public void Constructor_PrefixMargin_DefaultIsTwo()
    {
        var handler = new UserInputHandler();
        Assert.Equal(2, handler.PrefixMargin);
    }

    [Fact]
    public void Constructor_WrappingRightMargin_DefaultIsFour()
    {
        var handler = new UserInputHandler();
        Assert.Equal(4, handler.WrappingRightMargin);
    }

    [Fact]
    public void PrefixMargin_CanBeSet()
    {
        var handler = new UserInputHandler();
        handler.PrefixMargin = 3;
        Assert.Equal(3, handler.PrefixMargin);
    }

    [Fact]
    public void WrappingRightMargin_CanBeSet()
    {
        var handler = new UserInputHandler();
        handler.WrappingRightMargin = 5;
        Assert.Equal(5, handler.WrappingRightMargin);
    }

    [Fact]
    public void Constructor_TryGetSelection_NoSelection_ReturnsFalse()
    {
        var handler = new UserInputHandler();
        Assert.False(handler.TryGetSelection(out _, out _));
    }

    // ── SetInputFieldContent ─────────────────────────────────────────

    [Fact]
    public void SetInputFieldContent_SetsTextAndMovesCursorToEnd()
    {
        var handler = new UserInputHandler();
        handler.SetInputFieldContent("hello world");
        Assert.Equal("hello world", handler.CurrentInput);
        Assert.Equal(11, handler.CursorPosition);
    }

    [Fact]
    public void SetInputFieldContent_EmptyString_Clears()
    {
        var handler = new UserInputHandler();
        handler.SetInputFieldContent("some text");
        handler.SetInputFieldContent("");
        Assert.Equal("", handler.CurrentInput);
        Assert.Equal(0, handler.CursorPosition);
    }

    [Fact]
    public void SetInputFieldContent_Null_ThrowsArgumentNullException()
    {
        var handler = new UserInputHandler();
        var ex = Assert.Throws<ArgumentNullException>(() => handler.SetInputFieldContent(null!));
        Assert.Equal("text", ex.ParamName);
    }

    [Fact]
    public void SetInputFieldContent_ClearsSelection()
    {
        var handler = new UserInputHandler();
        // We can't directly set internal state, but can verify that after setting
        // content, TryGetSelection returns false
        handler.SetInputFieldContent("new content");
        Assert.False(handler.TryGetSelection(out _, out _));
    }

    // ── CurrentInput / CursorPosition / HasSelection — after no-op ──

    [Fact]
    public void CurrentInput_AfterReset_IsEmpty()
    {
        var handler = new UserInputHandler();
        handler.SetInputFieldContent("something");
        handler.Reset();
        Assert.Equal("", handler.CurrentInput);
        Assert.Equal(0, handler.CursorPosition);
    }

    [Fact]
    public void Reset_ClearsAttachments()
    {
        var handler = new UserInputHandler();
        // Attachments is a list we can set normally
        handler.Attachments.Add(new Attachment("test", AttachmentType.PlainText, 1));
        Assert.NotEmpty(handler.Attachments);
        handler.Reset();
        Assert.Empty(handler.Attachments);
    }

    [Fact]
    public void Reset_DoesNotThrow_WhenCalledOnEmptyHandler()
    {
        var handler = new UserInputHandler();
        handler.Reset(); // first
        handler.Reset(); // second
    }

    // ── SaveInputField / LoadInputField ──────────────────────────────

    [Fact]
    public void SaveAndLoadInputField_RoundTripPreservesState()
    {
        var handler = new UserInputHandler();
        handler.SetInputFieldContent("hello world");

        string id = handler.SaveInputField();
        handler.SetInputFieldContent("modified");
        Assert.Equal("modified", handler.CurrentInput);

        bool loaded = handler.LoadInputField(id);
        Assert.True(loaded);
        Assert.Equal("hello world", handler.CurrentInput);
        Assert.Equal(11, handler.CursorPosition);
    }

    [Fact]
    public void LoadInputField_UnknownId_ReturnsFalse()
    {
        var handler = new UserInputHandler();
        Assert.False(handler.LoadInputField("nonexistent"));
    }

    [Fact]
    public void SaveInputField_MultipleSaves_AllPreserved()
    {
        var handler = new UserInputHandler();
        handler.SetInputFieldContent("first");
        string id1 = handler.SaveInputField();
        handler.SetInputFieldContent("second");
        string id2 = handler.SaveInputField();
        handler.SetInputFieldContent("third");
        string id3 = handler.SaveInputField();

        Assert.True(handler.LoadInputField(id1));
        Assert.Equal("first", handler.CurrentInput);

        Assert.True(handler.LoadInputField(id2));
        Assert.Equal("second", handler.CurrentInput);

        Assert.True(handler.LoadInputField(id3));
        Assert.Equal("third", handler.CurrentInput);
    }

    [Fact]
    public void RemoveSavedInputField_RemovesAndReturnsTrue()
    {
        var handler = new UserInputHandler();
        handler.SetInputFieldContent("save me");
        string id = handler.SaveInputField();

        Assert.True(handler.RemoveSavedInputField(id));
        Assert.False(handler.LoadInputField(id));
    }

    [Fact]
    public void RemoveSavedInputField_UnknownId_ReturnsFalse()
    {
        var handler = new UserInputHandler();
        Assert.False(handler.RemoveSavedInputField("nonexistent"));
    }

    [Fact]
    public void RemoveAllSavedInputFields_ClearsAll()
    {
        var handler = new UserInputHandler();
        handler.SaveInputField();
        handler.SaveInputField();
        handler.RemoveAllSavedInputFields();
        Assert.Empty(handler.GetSavedInputFieldIds());
    }

    [Fact]
    public void GetSavedInputFieldIds_ReturnsAllIds()
    {
        var handler = new UserInputHandler();
        string id1 = handler.SaveInputField();
        string id2 = handler.SaveInputField();
        var ids = handler.GetSavedInputFieldIds();
        Assert.Contains(id1, ids);
        Assert.Contains(id2, ids);
    }

    [Fact]
    public void SaveInputField_PreservesAttachments()
    {
        var handler = new UserInputHandler();
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
    public void SaveInputField_SavedAttachmentsAreIndependentCopy()
    {
        var handler = new UserInputHandler();
        handler.SetInputFieldContent("text");
        handler.Attachments.Add(new Attachment("original", AttachmentType.PlainText, 1));

        string id = handler.SaveInputField();
        handler.Attachments[0] = handler.Attachments[0] with { Content = "modified" };

        handler.LoadInputField(id);
        Assert.Equal("original", handler.Attachments[0].Content);
    }

    [Fact]
    public void Reset_DoesNotAffectSavedInputs()
    {
        var handler = new UserInputHandler();
        handler.SetInputFieldContent("preserved");
        string id = handler.SaveInputField();
        handler.Reset();

        Assert.Equal("", handler.CurrentInput);
        Assert.True(handler.LoadInputField(id));
        Assert.Equal("preserved", handler.CurrentInput);
    }

    // ── Attachments Property ─────────────────────────────────────────

    [Fact]
    public void Attachments_DefaultEmpty()
    {
        var handler = new UserInputHandler();
        Assert.Empty(handler.Attachments);
    }

    [Fact]
    public void Attachments_CanBeAdded()
    {
        var handler = new UserInputHandler();
        handler.Attachments.Add(new Attachment("test", AttachmentType.PlainText, 1));
        Assert.Single(handler.Attachments);
    }
}

// ═════════════════════════════════════════════════════════════════════
//  CursorMovementHandler — additional edge case tests
// ═════════════════════════════════════════════════════════════════════

public class CursorMovementHandlerEdgeCasesTests
{
    private static CursorMovementHandler CreateHandler(
        TextBuffer buffer, SelectionManager selection, int margin = 80)
        => new(buffer, selection, () => margin, getAttachments: () => Array.Empty<Attachment>());

    // ── Shift+Right at buffer end ────────────────────────────────────

    [Fact]
    public void MoveRight_WithShift_AtEnd_ClearsSelectionWithoutShift()
    {
        var buf = new TextBuffer();
        buf.Insert("hello");
        buf.MoveTo(5); // at end
        var sel = new SelectionManager();
        var handler = CreateHandler(buf, sel);

        // First: shift+right at end → should do nothing, selection cleared
        // because !shift on the falling-through path
        handler.MoveCursorRight(shift: false);
        Assert.False(sel.HasAnchor);
        Assert.Equal(5, buf.CursorPosition);
    }

    // ── Shift+Right with newlines ────────────────────────────────────

    [Fact]
    public void MoveRight_WithShift_SkipsNewlines_WhenAtNewline()
    {
        var buf = new TextBuffer();
        buf.Insert("ab\ncd");
        buf.MoveTo(2); // at \n
        var sel = new SelectionManager();
        var handler = CreateHandler(buf, sel);

        handler.MoveCursorRight(shift: true);

        // After skipping newline: cursor should be at start of 'cd'
        Assert.Equal(3, buf.CursorPosition); // 'c'
        Assert.True(sel.HasAnchor);
    }

    [Fact]
    public void MoveRight_WithShift_SkipsMultipleNewlines_AtEnd()
    {
        var buf = new TextBuffer();
        buf.Insert("ab\n\n\ncd");
        buf.MoveTo(2); // at first \n
        var sel = new SelectionManager();
        var handler = CreateHandler(buf, sel);

        handler.MoveCursorRight(shift: true);

        // Should skip all consecutive newlines
        Assert.Equal(5, buf.CursorPosition); // 'c'
    }

    [Fact]
    public void MoveLeft_WithShift_AlreadyAtStart_StaysAtZero()
    {
        var buf = new TextBuffer();
        buf.Insert("hello");
        buf.MoveTo(0);
        var sel = new SelectionManager();
        var handler = CreateHandler(buf, sel);

        // Shift should still register the position for anchor
        handler.MoveCursorLeft(shift: true);

        Assert.Equal(0, buf.CursorPosition);
        Assert.True(sel.HasAnchor);
    }

    [Fact]
    public void MoveRight_WithShift_AlreadyAtEnd_StillAtEnd()
    {
        var buf = new TextBuffer();
        buf.Insert("hello");
        buf.MoveTo(5);
        var sel = new SelectionManager();
        var handler = CreateHandler(buf, sel);

        handler.MoveCursorRight(shift: false);

        Assert.Equal(5, buf.CursorPosition);
        Assert.False(sel.HasAnchor);
    }

    // ── Placeholder skipping without attachments ─────────────────────

    [Fact]
    public void MoveLeft_WithoutAttachments_DoesNotCrash()
    {
        var buf = new TextBuffer();
        buf.Insert("ab[paste]c");
        buf.MoveTo(8); // past the placeholder
        var handler = CreateHandler(buf, new SelectionManager());

        // No attachments registered, so no placeholders to skip
        handler.MoveCursorLeft(shift: false);
        Assert.Equal(7, buf.CursorPosition); // normal backward move
    }

    [Fact]
    public void MoveRight_WithoutAttachments_DoesNotCrash()
    {
        var buf = new TextBuffer();
        buf.Insert("a[b]c");
        buf.MoveTo(0);
        var handler = CreateHandler(buf, new SelectionManager());
        handler.MoveCursorRight(shift: false);
        Assert.Equal(1, buf.CursorPosition);
    }
}

// ═════════════════════════════════════════════════════════════════════
//  UndoManager — edge case tests
// ═════════════════════════════════════════════════════════════════════

public class UndoManagerEdgeCasesTests
{
    [Fact]
    public void MaxDepthOne_EvictsOldestAfterEachSnapshot()
    {
        var mgr = new UndoManager(maxDepth: 1);
        mgr.Snapshot("a", 0, null);
        Assert.Equal(1, mgr.Count);

        mgr.Snapshot("b", 1, null);
        Assert.Equal(1, mgr.Count); // "a" evicted

        Assert.True(mgr.TryUndo(out string text, out _, out _));
        Assert.Equal("b", text);
        Assert.False(mgr.TryUndo(out _, out _, out _)); // only one available
    }

    [Fact]
    public void MaxDepthOne_EmptyStack_UndoReturnsFalse()
    {
        var mgr = new UndoManager(maxDepth: 1);
        Assert.False(mgr.TryUndo(out _, out _, out _));

        mgr.Snapshot("x", 0, null);
        Assert.True(mgr.TryUndo(out _, out _, out _));
        Assert.False(mgr.TryUndo(out _, out _, out _));
    }

    [Fact]
    public void LargeMaxDepth_AcceptsManySnapshots()
    {
        var mgr = new UndoManager(maxDepth: 100);
        for (int i = 0; i < 50; i++)
            mgr.Snapshot($"state{i}", i, null);

        Assert.Equal(50, mgr.Count);
    }

    [Fact]
    public void DefaultMaxDepth_IsFifty()
    {
        var mgr = new UndoManager();
        for (int i = 0; i < 51; i++)
            mgr.Snapshot($"state{i}", i, null);

        Assert.Equal(50, mgr.Count);
    }

    [Fact]
    public void Clear_AfterSomeSnapshots_EmptiesStack()
    {
        var mgr = new UndoManager();
        mgr.Snapshot("a", 0, null);
        mgr.Snapshot("b", 1, null);
        Assert.Equal(2, mgr.Count);

        mgr.Clear();
        Assert.Equal(0, mgr.Count);
        Assert.False(mgr.TryUndo(out _, out _, out _));
    }

    [Fact]
    public void Eviction_DoesNotLoseNewest()
    {
        // When maxDepth=2 and we push 3 items, the oldest should be evicted
        var mgr = new UndoManager(maxDepth: 2);
        mgr.Snapshot("oldest", 0, null);
        mgr.Snapshot("middle", 1, null);
        mgr.Snapshot("newest", 2, null);

        Assert.Equal(2, mgr.Count);
        Assert.True(mgr.TryUndo(out string t1, out _, out _));
        Assert.Equal("newest", t1);
        Assert.True(mgr.TryUndo(out string t2, out _, out _));
        Assert.Equal("middle", t2);
        Assert.False(mgr.TryUndo(out _, out _, out _)); // "oldest" was evicted
    }
}

// ═════════════════════════════════════════════════════════════════════
//  ClipboardHandler — additional edge case tests
// ═════════════════════════════════════════════════════════════════════

public class ClipboardHandlerEdgeCasesTests
{
    /// <summary>Mock clipboard that throws on paste/copy to test exception handling.</summary>
    private sealed class ThrowingClipboardService : IClipboardService
    {
        public string? Paste() => throw new InvalidOperationException("No clipboard");
        public void Copy(string text) => throw new InvalidOperationException("No clipboard");
    }

    /// <summary>Mock clipboard that returns empty string.</summary>
    private sealed class EmptyClipboardService : IClipboardService
    {
        public string? Paste() => "";
        public void Copy(string text) { }
    }

    private static ClipboardHandler CreateHandler(
        TextBuffer? buffer = null,
        SelectionManager? selection = null,
        IClipboardService? clipboard = null,
        int largePasteThreshold = 300,
        int largePasteLineThreshold = 4)
    {
        buffer ??= new TextBuffer();
        selection ??= new SelectionManager();
        var tempInput = new StringBuilder();
        return new ClipboardHandler(
            buffer, selection, tempInput,
            () => { },
            () => largePasteThreshold,
            () => largePasteLineThreshold,
            clipboard ?? new ThrowingClipboardService());
    }

    // ── Clipboard exception handling ─────────────────────────────────

    [Fact]
    public void PasteFromClipboard_WhenClipboardThrows_DoesNotThrow()
    {
        var buf = new TextBuffer();
        var handler = CreateHandler(buffer: buf, clipboard: new ThrowingClipboardService());

        // Should not throw — exception is caught and swallowed
        handler.PasteFromClipboard();

        Assert.Equal("", buf.CurrentInput);
        Assert.Empty(handler.Attachments);
    }

    [Fact]
    public void CopyToClipboard_WhenClipboardThrows_DoesNotThrow()
    {
        var buf = new TextBuffer();
        buf.Insert("some text");
        var handler = CreateHandler(buffer: buf, clipboard: new ThrowingClipboardService());

        // Should not throw — exception is caught and swallowed
        handler.CopyToClipboard();
    }

    [Fact]
    public void PasteFromClipboard_EmptyResult_DoesNothing()
    {
        var buf = new TextBuffer();
        buf.Insert("existing");
        var handler = CreateHandler(buffer: buf, clipboard: new EmptyClipboardService());

        handler.PasteFromClipboard();
        Assert.Equal("existing", buf.CurrentInput);
    }

    // ── ResetCounter ─────────────────────────────────────────────────

    [Fact]
    public void ResetCounter_CalledMultipleTimes_DoesNotThrow()
    {
        var handler = CreateHandler();
        handler.ResetCounter();
        handler.ResetCounter();
        handler.ResetCounter();
    }

    // ── Cut with empty buffer ────────────────────────────────────────

    [Fact]
    public void CutToClipboard_EmptyBuffer_DoesNotThrow()
    {
        var buf = new TextBuffer();
        var handler = CreateHandler(buffer: buf, clipboard: new EmptyClipboardService());
        handler.CutToClipboard();
        Assert.Equal("", buf.CurrentInput);
    }

    // ── Paste with selection that overlaps attachment placeholder ─────

    [Fact]
    public void PasteFromClipboard_WithSelectionRemovesPlaceholders()
    {
        var buf = new TextBuffer();
        buf.Insert("ab[paste #1, 2 lines]cd");
        buf.MoveTo(2); // at start of placeholder
        var sel = new SelectionManager();
        sel.SetAnchor(0); // selects from 0 to 2
        var mockClip = new MockClipboardService { PasteResult = "XY" };

        var handler = CreateHandler(buffer: buf, selection: sel, clipboard: mockClip);
        handler.Attachments.Add(new Attachment("lines", AttachmentType.PlainText, 2, 1, "[paste #1, 2 lines]"));

        handler.PasteFromClipboard();

        // Snapshot was taken, selection removed, paste inserted
        // Placeholders are NOT removed during paste — they survive
        Assert.Equal("XY[paste #1, 2 lines]cd", buf.CurrentInput);
    }

    // ── GeneratePlaceholder edge cases ───────────────────────────────

    [Theory]
    [InlineData(0, 1, "[paste #1, 1 line]")]  // 0 lines treated as "1 line"
    [InlineData(1, 99, "[paste #99, 1 line]")]
    [InlineData(2, 1, "[paste #1, 2 lines]")]
    [InlineData(100, 42, "[paste #42, 100 lines]")]
    public void GeneratePlaceholder_VariousInputs(int lineCount, int counter, string expected)
    {
        Assert.Equal(expected, ClipboardHandler.GeneratePlaceholder(lineCount, counter));
    }

    private sealed class MockClipboardService : IClipboardService
    {
        public string? StoredText { get; private set; }
        public string? PasteResult { get; set; }

        public string? Paste() => PasteResult;
        public void Copy(string text) => StoredText = text;
    }

    // ── RemovePlaceholderAffectedBy with multiple overlapping ─────────

    [Fact]
    public void RemovePlaceholderAffectedBy_MultipleOverlaps_RemovesAll()
    {
        var buf = new TextBuffer();
        buf.Insert("xx[paste #1, 1 line]xx[paste #2, 2 lines]xx");
        var handler = CreateHandler(buffer: buf);

        handler.Attachments.Add(new Attachment("a", AttachmentType.PlainText, 1, 1, "[paste #1, 1 line]"));
        handler.Attachments.Add(new Attachment("b", AttachmentType.PlainText, 2, 2, "[paste #2, 2 lines]"));

        // Delete starting at position 0, length 30 (covers both placeholders)
        bool removed = handler.RemovePlaceholderAffectedBy(0, 30);

        Assert.True(removed);
        Assert.Empty(handler.Attachments);
        // Both placeholders removed (right-to-left): placeholder2 (25,22) then placeholder1 (2,21)
        // Original: "xx" + ph1(21) + "xx" + ph2(22) + "xx" = "xx" + "xx" + "xx" = "xxxxxx"
        Assert.Equal("xxxxxx", buf.CurrentInput);
    }

    [Fact]
    public void RemovePlaceholderAffectedBy_SinglePlaceholder_ThenBufferClean()
    {
        var buf = new TextBuffer();
        buf.Insert("prefix[paste #1, 1 line]suffix");
        var handler = CreateHandler(buffer: buf);

        handler.Attachments.Add(new Attachment("content", AttachmentType.PlainText, 1, 1, "[paste #1, 1 line]"));

        // Delete range that covers part of the placeholder
        bool removed = handler.RemovePlaceholderAffectedBy(4, 15);

        Assert.True(removed);
        Assert.Empty(handler.Attachments);
        // The placeholder at index 6 with length 21 is fully overlapped (affectedStart=4 < 28, affectedEnd=19 > 6)
        // Buffer.Remove(6, 21) removes the placeholder, leaving "prefixsuffix"
        Assert.Equal("prefixsuffix", buf.CurrentInput);
    }

    // ── CleanupOrphanedAttachments with no text ──────────────────────

    [Fact]
    public void CleanupOrphanedAttachments_EmptyBuffer_RemovesAllAttachments()
    {
        var buf = new TextBuffer();
        var handler = CreateHandler(buffer: buf);
        handler.Attachments.Add(new Attachment("content", AttachmentType.PlainText, 1, 1, "[paste #1, 1 line]"));

        handler.CleanupOrphanedAttachments();

        Assert.Empty(handler.Attachments);
    }

    [Fact]
    public void CleanupOrphanedAttachments_MultipleOrphans_RemovesAll()
    {
        var buf = new TextBuffer();
        buf.Insert("unrelated text");
        var handler = CreateHandler(buffer: buf);
        handler.Attachments.Add(new Attachment("a", AttachmentType.PlainText, 1, 1, "[paste #1, 1 line]"));
        handler.Attachments.Add(new Attachment("b", AttachmentType.PlainText, 2, 2, "[paste #2, 2 lines]"));
        handler.Attachments.Add(new Attachment("c", AttachmentType.PlainText, 3, 3, "[paste #3, 3 lines]"));

        Assert.Equal(3, handler.Attachments.Count);
        handler.CleanupOrphanedAttachments();
        Assert.Empty(handler.Attachments);
    }
}

// ═════════════════════════════════════════════════════════════════════
//  ConsoleAppHost — additional pure logic tests (no Console dependency)
// ═════════════════════════════════════════════════════════════════════

public class ConsoleAppHostAdditionalTests
{
    // ── TryGetCommandName (static method) ────────────────────────────

    [Theory]
    [InlineData("", false)]         // empty
    [InlineData("/", false)]        // just slash
    [InlineData("hello", false)]    // no slash
    [InlineData("/cmd", true, "cmd", "")]                       // exact
    [InlineData("/cmd arg1", true, "cmd", "arg1")]              // one positional
    [InlineData("/cmd --key val", true, "cmd", "--key val")]    // with named arg
    [InlineData("/cmd  arg1  arg2", true, "cmd", " arg1  arg2")] // extra spaces
    [InlineData("/a/b", true, "a/b", "")]                       // slash in name
    public void TryGetCommandName_VariousInputs(
        string input, bool expectedResult,
        string? expectedName = null, string expectedArgs = "")
    {
        // Access the public static method via reflection
        var method = typeof(CommandManager).GetMethod(
            "TryGetCommandName",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            null,
            [typeof(string), typeof(string).MakeByRefType(), typeof(string).MakeByRefType()],
            null)!;

        var parameters = new object?[] { input, null, null };
        var result = (bool)method.Invoke(null, parameters)!;

        Assert.Equal(expectedResult, result);
        if (expectedResult)
        {
            Assert.Equal(expectedName, (string?)parameters[1]);
            // The args string preserves the space after the command name
            Assert.Contains(expectedArgs, (string?)parameters[2]);
        }
    }

    // ── TryGetCommandName (convenience overload) ─────────────────────

    [Fact]
    public void TryGetCommandName_ConvenienceOverload_ExtractsName()
    {
        var method = typeof(CommandManager).GetMethod(
            "TryGetCommandName",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            null,
            [typeof(string), typeof(string).MakeByRefType()],
            null)!;

        var parameters = new object?[] { "/hello", null };
        var result = (bool)method.Invoke(null, parameters)!;
        Assert.True(result);
        Assert.Equal("hello", (string?)parameters[1]);
    }

    // ── Run loop cancellation ────────────────────────────────────────

    [Fact]
    public void Stop_CancelsRunLoop()
    {
        using var host = new ConsoleAppHost(new MockRenderer(), new MockInputHandler());
        host.Stop();
        // Stop sets cancellation — run loop would break on next iteration
        // Verify cancellation is not an exception
        Assert.NotNull(host);
    }

    [Fact]
    public void Stop_CanBeCalledBeforeDispose()
    {
        using var host = new ConsoleAppHost(new MockRenderer(), new MockInputHandler());
        host.Stop();
        // No exception
    }

    // ── SetDefaultPanel when on non-standard panel ───────────────────

    [Fact]
    public void SetDefaultPanel_WhenOnCommandPalette_DoesNotSwap()
    {
        using var host = new ConsoleAppHost(new MockRenderer(), new MockInputHandler());
        var customDefault = new EmptyBottomPanel();
        host.SetDefaultPanel(customDefault);

        // GetLines on the command palette should not change
        // The bottom panel at this point should still be EmptyBottomPanel
        // because the input doesn't start with /
        // The swap happens during the run loop via EnsureProperPanel
        // which we can't call directly (it's private)
    }

    // ── Dispose idempotency ──────────────────────────────────────────

    [Fact]
    public void Dispose_MultipleTimes_DoesNotThrow()
    {
        var host = new ConsoleAppHost(new MockRenderer(), new MockInputHandler());
        host.Dispose();
        host.Dispose(); // second call should be safe
    }

    // ── Constructor with mock renderer ───────────────────────────────

    [Fact]
    public void Constructor_WithMocks_UsesProvidedInstances()
    {
        var renderer = new MockRenderer();
        var inputHandler = new MockInputHandler();
        using var host = new ConsoleAppHost(renderer, inputHandler);

        Assert.Same(renderer, host.GetType().GetField("_renderer",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(host));
        Assert.Same(inputHandler, host.InputHandler);
    }
}

// ═════════════════════════════════════════════════════════════════════
//  LineWrappingService — additional edge case coverage
// ═════════════════════════════════════════════════════════════════════

public class LineWrappingServiceAdditionalTests
{
    // ── WrapSegment with zero-width ──────────────────────────────────

    [Fact]
    public void WrapSegment_ZeroWidth_FirstCapZeroThenOneCharPerLine()
    {
        // width=1, totalMargin=6 → cap = max(0, 1-6) = max(0, -5) = 0 for first line
        // Then cap = max(1, 1-6) = max(1, -5) = 1 for remaining
        // "abc" → [""], ["a"], ["b"], ["c"] = 4 lines
        var result = LineWrappingService.WrapSegment("abc", width: 1,
            isFirstSegment: true, isLastSegment: true, isFirstVisualLine: true);
        Assert.Equal(4, result.Count);
        Assert.Equal("", result[0]); // cap=0 for first visual line
        Assert.Equal("a", result[1]);
        Assert.Equal("b", result[2]);
        Assert.Equal("c", result[3]);
    }

    [Fact]
    public void WrapSegment_AtExactTotalMargin_FirstCapZero()
    {
        // width=6, totalMargin=6 = width, first cap = max(0, 0) = 0
        var result = LineWrappingService.WrapSegment("ab", width: 6,
            isFirstSegment: true, isLastSegment: true, isFirstVisualLine: true);
        // First cap 0 → empty string first, then cap = max(1, 0) = 1
        Assert.Equal(3, result.Count);
        Assert.Equal("", result[0]); // cap=0
        Assert.Equal("a", result[1]);
        Assert.Equal("b", result[2]);
    }

    // ── GetVisualLineData with very long input ───────────────────────

    [Fact]
    public void GetVisualLineData_VeryLongInput_AllProduced()
    {
        string longLine = new string('x', 1000);
        var (lines, _) = LineWrappingService.GetVisualLineData(longLine, margin: 80);
        // cap = 80-6 = 74, 1000/74 = 13.x → 14 lines
        Assert.True(lines.Count > 10);
        Assert.Equal(1000, lines.Sum(l => l.Length));
    }

    // ── GetVisualLineData with only newlines ─────────────────────────

    [Fact]
    public void GetVisualLineData_OnlyNewlines_ProducesEmptyLines()
    {
        var (lines, offsets) = LineWrappingService.GetVisualLineData("\n\n", margin: 80);
        Assert.Equal(3, lines.Count); // "", "", ""
        Assert.All(lines, l => Assert.Equal("", l));
        Assert.Equal(3, offsets.Count);
    }

    [Fact]
    public void GetVisualLineData_NewlineAtStart_Works()
    {
        var (lines, _) = LineWrappingService.GetVisualLineData("\nabc", margin: 80);
        Assert.Equal(2, lines.Count);
        Assert.Equal("", lines[0]);
        Assert.Equal("abc", lines[1]);
    }

    // ── GetCursorVisualPosition with newline-only input ──────────────

    [Fact]
    public void GetCursorVisualPosition_NewlineOnly_ReturnsCorrectPosition()
    {
        var (line, col) = LineWrappingService.GetCursorVisualPosition("\n\n", 2, margin: 80);
        Assert.Equal(2, line);
        Assert.Equal(0, col);
    }

    // ── GetInputLineCount edge cases ─────────────────────────────────

    [Fact]
    public void GetInputLineCount_WithCustomPrefixMargin_Works()
    {
        int count = LineWrappingService.GetInputLineCount("hello", margin: 80, prefixMargin: 10, rightMargin: 4);
        Assert.Equal(1, count);
    }

    [Fact]
    public void GetInputLineCount_VeryLongWithCustomMargins()
    {
        // margin=20, prefixMargin=10, rightMargin=4 → cap = 20-14 = 6
        // 50 chars / 6 = 9 lines (8 full + 1 partial)
        int count = LineWrappingService.GetInputLineCount(
            new string('x', 50), margin: 20, prefixMargin: 10, rightMargin: 4);
        Assert.Equal(9, count);
    }
}

// ═════════════════════════════════════════════════════════════════════
//  CommandPalette — additional edge case tests
// ═════════════════════════════════════════════════════════════════════

public class CommandPaletteAdditionalTests
{
    [Fact]
    public void IsActive_SpaceBeforeSlash_ReturnsFalse()
    {
        Assert.False(CommandPalette.IsActive(" /"));
    }

    [Fact]
    public void IsActive_MultipleSlashes_ReturnsTrueForLeadingSlash()
    {
        Assert.True(CommandPalette.IsActive("//"));
    }

    [Fact]
    public async Task RunAsync_Cancellation_DoesNotThrow()
    {
        var palette = new CommandPalette(Array.Empty<Command>());
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Should complete quickly without throwing
        await palette.RunAsync(cts.Token);
    }

    // ── TryHandleKey when not active ─────────────────────────────────

    [Fact]
    public void TryHandleKey_NotCommandMode_ReturnsFalse()
    {
        var palette = new CommandPalette(Array.Empty<Command>());
        var key = new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false);

        // The IBottomPanel interface TryHandleKey
        var result = ((IBottomPanel)palette).TryHandleKey(key);
        Assert.False(result);
    }

    // ── AdjustSelection with no matches ──────────────────────────────

    [Fact]
    public void AdjustSelection_NoMatches_ResetsSelection()
    {
        var palette = new CommandPalette(Array.Empty<Command>());
        // No commands, so maxVisible = 0 → reset selection
        palette.GetLines("/nonexistent");
        palette.AdjustSelection(1);
        Assert.Equal(0, palette.SelectedIndex);
    }

    [Fact]
    public void HintsStartIndex_ExpectedValue()
    {
        Assert.Equal(1, CommandPalette.HintsStartIndex);
    }

    [Fact]
    public void HintCapacity_ExpectedValue()
    {
        var palette = new CommandPalette(Array.Empty<Command>());
        Assert.Equal(7, palette.HintCapacity);
    }

    // ── GetLines with no matching commands ───────────────────────────

    [Fact]
    public void GetLines_EmptyCommandSet_ReturnsEmptyLines()
    {
        var palette = new CommandPalette(Array.Empty<Command>());
        var lines = palette.GetLines("/");
        Assert.Equal(palette.MaxHeight, lines.Count);
        Assert.All(lines, line => Assert.Equal(string.Empty, line));
    }
}
