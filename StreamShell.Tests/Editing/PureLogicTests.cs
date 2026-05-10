using StreamShell;

namespace StreamShell.Tests;

// ═════════════════════════════════════════════════════════════════════
//  TextBuffer Tests — pure text buffer with cursor
// ═════════════════════════════════════════════════════════════════════

public class TextBufferTests
{
    [Fact]
    public void Constructor_EmptyBuffer()
    {
        var buf = new TextBuffer();
        Assert.Equal("", buf.CurrentInput);
        Assert.Equal(0, buf.CursorPosition);
        Assert.Equal(0, buf.Length);
    }

    [Fact]
    public void Insert_CharAtStart_InsertsAndAdvancesCursor()
    {
        var buf = new TextBuffer();
        buf.Insert('a');
        Assert.Equal("a", buf.CurrentInput);
        Assert.Equal(1, buf.CursorPosition);
        Assert.Equal(1, buf.Length);
    }

    [Fact]
    public void Insert_CharInMiddle_InsertsAndAdvancesCursor()
    {
        var buf = new TextBuffer();
        buf.Insert("ab");
        buf.MoveTo(1);
        buf.Insert('X');
        Assert.Equal("aXb", buf.CurrentInput);
        Assert.Equal(2, buf.CursorPosition);
    }

    [Fact]
    public void Insert_StringAtStart_InsertsAndAdvancesCursor()
    {
        var buf = new TextBuffer();
        buf.Insert("hello");
        Assert.Equal("hello", buf.CurrentInput);
        Assert.Equal(5, buf.CursorPosition);
    }

    [Fact]
    public void Insert_StringAtCursor_InsertsAndAdvancesCursor()
    {
        var buf = new TextBuffer();
        buf.Insert("ab");
        buf.MoveTo(1);
        buf.Insert("XY");
        Assert.Equal("aXYb", buf.CurrentInput);
        Assert.Equal(3, buf.CursorPosition);
    }

    [Fact]
    public void Insert_EmptyString_DoesNothing()
    {
        var buf = new TextBuffer();
        buf.Insert("hello");
        buf.Insert("");
        Assert.Equal("hello", buf.CurrentInput);
        Assert.Equal(5, buf.CursorPosition);
    }

    [Fact]
    public void Remove_FromMiddle_RemovesAndMovesCursor()
    {
        var buf = new TextBuffer();
        buf.Insert("abcdef");
        buf.MoveTo(4);
        buf.Remove(1, 3);
        Assert.Equal("aef", buf.CurrentInput);
        Assert.Equal(1, buf.CursorPosition);
    }

    [Fact]
    public void Remove_All_EmptiesBuffer()
    {
        var buf = new TextBuffer();
        buf.Insert("abc");
        buf.Remove(0, 3);
        Assert.Equal("", buf.CurrentInput);
        Assert.Equal(0, buf.CursorPosition);
    }

    [Fact]
    public void Backspace_ReducesCursor()
    {
        var buf = new TextBuffer();
        buf.Insert("abc");
        buf.MoveTo(2);
        buf.Backspace();
        Assert.Equal("ac", buf.CurrentInput);
        Assert.Equal(1, buf.CursorPosition);
    }

    [Fact]
    public void Backspace_AtStart_DoesNothing()
    {
        var buf = new TextBuffer();
        buf.Insert("abc");
        buf.MoveTo(0);
        buf.Backspace();
        Assert.Equal("abc", buf.CurrentInput);
        Assert.Equal(0, buf.CursorPosition);
    }

    [Fact]
    public void Delete_AtPosition_RemovesAndKeepsCursor()
    {
        var buf = new TextBuffer();
        buf.Insert("abcd");
        buf.MoveTo(1);
        buf.Delete();
        Assert.Equal("acd", buf.CurrentInput);
        Assert.Equal(1, buf.CursorPosition);
    }

    [Fact]
    public void Delete_AtEnd_DoesNothing()
    {
        var buf = new TextBuffer();
        buf.Insert("abc");
        buf.Delete();
        Assert.Equal("abc", buf.CurrentInput);
        Assert.Equal(3, buf.CursorPosition);
    }

    [Fact]
    public void MoveTo_ClampsToRange()
    {
        var buf = new TextBuffer();
        buf.Insert("abc");

        buf.MoveTo(-5);
        Assert.Equal(0, buf.CursorPosition);

        buf.MoveTo(10);
        Assert.Equal(3, buf.CursorPosition);

        buf.MoveTo(1);
        Assert.Equal(1, buf.CursorPosition);
    }

    [Fact]
    public void SetContent_ReplacesBufferAndCursor()
    {
        var buf = new TextBuffer();
        buf.Insert("old content");

        buf.SetContent("new content", 4);
        Assert.Equal("new content", buf.CurrentInput);
        Assert.Equal(4, buf.CursorPosition);
    }

    [Fact]
    public void SetContent_ClampsCursor()
    {
        var buf = new TextBuffer();
        buf.SetContent("hi", 100);
        Assert.Equal("hi", buf.CurrentInput);
        Assert.Equal(2, buf.CursorPosition);
    }

    [Fact]
    public void Clear_EmptiesBuffer()
    {
        var buf = new TextBuffer();
        buf.Insert("something");
        buf.Clear();
        Assert.Equal("", buf.CurrentInput);
        Assert.Equal(0, buf.CursorPosition);
        Assert.Equal(0, buf.Length);
    }

    [Fact]
    public void Indexer_ReturnsCorrectChar()
    {
        var buf = new TextBuffer();
        buf.Insert("hello");
        Assert.Equal('h', buf[0]);
        Assert.Equal('e', buf[1]);
        Assert.Equal('l', buf[2]);
        Assert.Equal('l', buf[3]);
        Assert.Equal('o', buf[4]);
    }

    [Fact]
    public void Remove_NegativeLength_Throws_DoesNotCorrupt()
    {
        // StringBuilder.Remove throws on invalid params; verify buffer survives
        var buf = new TextBuffer();
        buf.Insert("abc");
        Assert.Throws<ArgumentOutOfRangeException>(() => buf.Remove(1, -1));
        Assert.Equal("abc", buf.CurrentInput); // buffer unchanged
    }

    [Fact]
    public void Remove_PastEnd_Throws()
    {
        var buf = new TextBuffer();
        buf.Insert("abc");
        Assert.Throws<ArgumentOutOfRangeException>(() => buf.Remove(1, 10));
    }

    [Fact]
    public void Backspace_OnEmptyBuffer_DoesNothing()
    {
        var buf = new TextBuffer();
        buf.Backspace();
        Assert.Equal("", buf.CurrentInput);
        Assert.Equal(0, buf.CursorPosition);
    }

    [Fact]
    public void Delete_OnEmptyBuffer_DoesNothing()
    {
        var buf = new TextBuffer();
        buf.Delete();
        Assert.Equal("", buf.CurrentInput);
        Assert.Equal(0, buf.CursorPosition);
    }

    [Fact]
    public void SetContent_NegativeCursor_ClampsToZero()
    {
        var buf = new TextBuffer();
        buf.SetContent("test", -5);
        Assert.Equal("test", buf.CurrentInput);
        Assert.Equal(0, buf.CursorPosition);
    }

    [Fact]
    public void MoveTo_OnEmptyBuffer_StaysAtZero()
    {
        var buf = new TextBuffer();
        buf.MoveTo(5);
        Assert.Equal(0, buf.CursorPosition);
    }

    [Fact]
    public void Remove_LengthZero_DoesNothing()
    {
        var buf = new TextBuffer();
        buf.Insert("abc");
        buf.MoveTo(1);
        buf.Remove(1, 0);
        Assert.Equal("abc", buf.CurrentInput);
        Assert.Equal(1, buf.CursorPosition);
    }
}

// ═════════════════════════════════════════════════════════════════════
//  SelectionManager Tests — selection state tracking
// ═════════════════════════════════════════════════════════════════════

public class SelectionManagerTests
{
    private readonly SelectionManager _sel = new();

    [Fact]
    public void InitialState_NoAnchor_NotActive()
    {
        Assert.False(_sel.HasAnchor);
        Assert.False(_sel.IsActiveAt(0));
    }

    [Fact]
    public void IsActiveAt_AnchorEqualsCursor_ReturnsFalse()
    {
        _sel.ForMovement(shift: true, 5);
        Assert.False(_sel.IsActiveAt(5));
    }

    [Fact]
    public void IsActiveAt_CursorDifferentFromAnchor_ReturnsTrue()
    {
        _sel.ForMovement(shift: true, 5);
        Assert.True(_sel.IsActiveAt(10));
    }

    [Fact]
    public void ForMovement_WithoutShift_ClearsSelection()
    {
        _sel.SetAnchor(0);
        _sel.ForMovement(shift: false, 5);
        Assert.False(_sel.HasAnchor);
    }

    [Fact]
    public void ForMovement_WithShift_SetsAnchorOnFirstCall()
    {
        _sel.ForMovement(shift: true, 3);
        Assert.True(_sel.HasAnchor);
    }

    [Fact]
    public void ForMovement_WithShift_DoesNotResetExistingAnchor()
    {
        _sel.SetAnchor(3);
        _sel.ForMovement(shift: true, 7);
        Assert.Equal(3, _sel.GetAnchor());
    }

    [Fact]
    public void SelectionProperties_CursorAheadOfAnchor()
    {
        _sel.SetAnchor(2);
        int cursor = 7;
        Assert.Equal(2, _sel.SelectionStart(cursor));
        Assert.Equal(7, _sel.SelectionEnd(cursor));
        Assert.Equal(5, _sel.SelectionLength(cursor));
    }

    [Fact]
    public void SelectionProperties_CursorBehindAnchor()
    {
        _sel.SetAnchor(7);
        int cursor = 2;
        Assert.Equal(2, _sel.SelectionStart(cursor));
        Assert.Equal(7, _sel.SelectionEnd(cursor));
        Assert.Equal(5, _sel.SelectionLength(cursor));
    }

    [Fact]
    public void SelectionProperties_NoAnchor_ReturnsCursorAsBoth()
    {
        Assert.Equal(5, _sel.SelectionStart(5));
        Assert.Equal(5, _sel.SelectionEnd(5));
        Assert.Equal(0, _sel.SelectionLength(5));
    }

    [Fact]
    public void TryGetSelection_Active_ReturnsTrueAndValues()
    {
        _sel.SetAnchor(1);
        bool result = _sel.TryGetSelection(5, out int start, out int length);
        Assert.True(result);
        Assert.Equal(1, start);
        Assert.Equal(4, length);
    }

    [Fact]
    public void TryGetSelection_NotActive_ReturnsFalse()
    {
        bool result = _sel.TryGetSelection(5, out int start, out int length);
        Assert.False(result);
        Assert.Equal(0, start);
        Assert.Equal(0, length);
    }

    [Fact]
    public void SelectedText_ReturnsSubstring()
    {
        const string input = "hello world";
        _sel.SetAnchor(0);
        Assert.Equal("hello", _sel.SelectedText(5, input));
    }

    [Fact]
    public void SelectedText_NoSelection_ReturnsEmpty()
    {
        const string input = "hello world";
        Assert.Equal("", _sel.SelectedText(5, input));
    }

    [Fact]
    public void Clear_RemovesAnchor()
    {
        _sel.SetAnchor(3);
        _sel.Clear();
        Assert.False(_sel.HasAnchor);
    }

    [Fact]
    public void Reset_RemovesAnchor()
    {
        _sel.SetAnchor(3);
        _sel.Reset();
        Assert.False(_sel.HasAnchor);
    }

    [Fact]
    public void SetAnchor_SetsExplicitPosition()
    {
        _sel.SetAnchor(10);
        Assert.True(_sel.HasAnchor);
        Assert.Equal(10, _sel.GetAnchor());
    }
}

// ═════════════════════════════════════════════════════════════════════
//  UndoManager Tests — snapshot/undo stack
// ═════════════════════════════════════════════════════════════════════

public class UndoManagerTests
{
    [Fact]
    public void EmptyStack_TryUndoReturnsFalse()
    {
        var mgr = new UndoManager();
        Assert.False(mgr.TryUndo(out _, out _, out _));
    }

    [Fact]
    public void SnapshotAndUndo_ReturnsSavedState()
    {
        var mgr = new UndoManager();
        mgr.Snapshot("hello", 5, null);

        Assert.True(mgr.TryUndo(out string text, out int cursor, out int? sel));
        Assert.Equal("hello", text);
        Assert.Equal(5, cursor);
        Assert.Null(sel);
    }

    [Fact]
    public void SnapshotAndUndo_WithSelection()
    {
        var mgr = new UndoManager();
        mgr.Snapshot("test", 2, 0);

        Assert.True(mgr.TryUndo(out string text, out int cursor, out int? sel));
        Assert.Equal("test", text);
        Assert.Equal(2, cursor);
        Assert.Equal(0, sel);
    }

    [Fact]
    public void MultipleUndos_LIFOOrder()
    {
        var mgr = new UndoManager();
        mgr.Snapshot("first", 0, null);
        mgr.Snapshot("second", 1, null);
        mgr.Snapshot("third", 2, null);

        Assert.True(mgr.TryUndo(out string t1, out _, out _));
        Assert.Equal("third", t1);

        Assert.True(mgr.TryUndo(out string t2, out _, out _));
        Assert.Equal("second", t2);

        Assert.True(mgr.TryUndo(out string t3, out _, out _));
        Assert.Equal("first", t3);

        Assert.False(mgr.TryUndo(out _, out _, out _));
    }

    [Fact]
    public void MaxDepth_EvictsOldest()
    {
        var mgr = new UndoManager(maxDepth: 3);
        mgr.Snapshot("a", 0, null);
        mgr.Snapshot("b", 1, null);
        mgr.Snapshot("c", 2, null);
        mgr.Snapshot("d", 3, null); // "a" should be evicted

        Assert.True(mgr.TryUndo(out string t1, out _, out _));
        Assert.Equal("d", t1);

        Assert.True(mgr.TryUndo(out string t2, out _, out _));
        Assert.Equal("c", t2);

        Assert.True(mgr.TryUndo(out string t3, out _, out _));
        Assert.Equal("b", t3);

        Assert.False(mgr.TryUndo(out _, out _, out _));
    }

    [Fact]
    public void Count_TracksSnapshots()
    {
        var mgr = new UndoManager();
        Assert.Equal(0, mgr.Count);

        mgr.Snapshot("a", 0, null);
        Assert.Equal(1, mgr.Count);

        mgr.Snapshot("b", 1, null);
        Assert.Equal(2, mgr.Count);

        mgr.TryUndo(out _, out _, out _);
        Assert.Equal(1, mgr.Count);

        mgr.Clear();
        Assert.Equal(0, mgr.Count);
    }

    [Fact]
    public void Clear_EmptiesStack()
    {
        var mgr = new UndoManager();
        mgr.Snapshot("a", 0, null);
        mgr.Clear();
        Assert.False(mgr.TryUndo(out _, out _, out _));
    }

    [Fact]
    public void MaxDepthAtLeastOne()
    {
        var mgr = new UndoManager(maxDepth: 0);
        mgr.Snapshot("a", 0, null);
        Assert.Equal(1, mgr.Count);
        Assert.True(mgr.TryUndo(out _, out _, out _));
    }
}

// ═════════════════════════════════════════════════════════════════════
//  UserInputSubmittedEventArgs Tests — attachment expansion
// ═════════════════════════════════════════════════════════════════════

public class UserInputSubmittedEventArgsTests
{
    [Fact]
    public void NoAttachments_ReturnsRawOutput()
    {
        var args = new UserInputSubmittedEventArgs
        {
            InputType = InputType.PlainText,
            RawOutput = "hello world",
            Attachments = Array.Empty<Attachment>()
        };

        Assert.Equal("hello world", args.TextWithoutAttachments);
        Assert.Equal("hello world", args.TextWithAttachmentsExpanded);
    }

    [Fact]
    public void TextWithoutAttachments_StripsPlaceholders()
    {
        var args = new UserInputSubmittedEventArgs
        {
            InputType = InputType.PlainText,
            RawOutput = "Some text [paste #1, 3 lines] more text [paste #2, 1 line] end",
            Attachments = new[]
            {
                new Attachment("line1\nline2\nline3", AttachmentType.PlainText, 3, 1, "[paste #1, 3 lines]"),
                new Attachment("solo", AttachmentType.PlainText, 1, 2, "[paste #2, 1 line]")
            }
        };

        Assert.Equal("Some text  more text  end", args.TextWithoutAttachments);
    }

    [Fact]
    public void TextWithAttachmentsExpanded_ReplacesPlaceholders()
    {
        var args = new UserInputSubmittedEventArgs
        {
            InputType = InputType.PlainText,
            RawOutput = "[paste #1, 3 lines] --key value",
            Attachments = new[]
            {
                new Attachment("pasted content\nwith two lines", AttachmentType.PlainText, 2, 1, "[paste #1, 3 lines]")
            }
        };

        Assert.Equal("pasted content\nwith two lines --key value", args.TextWithAttachmentsExpanded);
    }

    [Fact]
    public void TextWithAttachmentsExpanded_MultipleAttachments()
    {
        var args = new UserInputSubmittedEventArgs
        {
            InputType = InputType.Command,
            RawOutput = "First: [paste #1, 1 line] Second: [paste #2, 2 lines]",
            Attachments = new[]
            {
                new Attachment("A", AttachmentType.PlainText, 1, 1, "[paste #1, 1 line]"),
                new Attachment("B\nC", AttachmentType.PlainText, 2, 2, "[paste #2, 2 lines]")
            }
        };

        Assert.Equal("First: A Second: B\nC", args.TextWithAttachmentsExpanded);
    }

    [Fact]
    public void EmptyPlaceholder_NotReplaced()
    {
        // Attachments can have empty placeholders when constructed manually
        var args = new UserInputSubmittedEventArgs
        {
            InputType = InputType.PlainText,
            RawOutput = "test [paste #1, 1 line]",
            Attachments = new[]
            {
                new Attachment("content", AttachmentType.PlainText, 1, 1, "")
            }
        };

        Assert.Equal("test [paste #1, 1 line]", args.TextWithoutAttachments);
        Assert.Equal("test [paste #1, 1 line]", args.TextWithAttachmentsExpanded);
    }

    [Fact]
    public void PlaceholderAppearsMultipleTimes_AllReplaced()
    {
        var args = new UserInputSubmittedEventArgs
        {
            InputType = InputType.PlainText,
            RawOutput = "[paste #1, 1 line] and again [paste #1, 1 line]",
            Attachments = new[]
            {
                new Attachment("content", AttachmentType.PlainText, 1, 1, "[paste #1, 1 line]")
            }
        };

        Assert.Equal("content and again content", args.TextWithAttachmentsExpanded);
    }

    [Fact]
    public void InputType_Command_StoredCorrectly()
    {
        var args = new UserInputSubmittedEventArgs
        {
            InputType = InputType.Command,
            RawOutput = "/hello"
        };

        Assert.Equal(InputType.Command, args.InputType);
    }

    [Fact]
    public void InputType_PlainText_StoredCorrectly()
    {
        var args = new UserInputSubmittedEventArgs
        {
            InputType = InputType.PlainText,
            RawOutput = "just text"
        };

        Assert.Equal(InputType.PlainText, args.InputType);
    }
}
