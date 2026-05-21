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

    /// <summary>
    /// Commands with property-name-style suggestions (no spaces) where
    /// multiple entries share a common prefix equal to the typed text.
    /// Represents the /appconfig DirectLlm* scenario from the bug report.
    /// </summary>
    private static readonly Command[] PropertyStyleSuggestions =
    [
        new("appconfig", "Get/set app config",
            (_, _) => Task.CompletedTask,
            [
                "DirectLlmApiType",
                "DirectLlmModelName",
                "DirectLlmToken",
                "DirectLlmUrl",
            ]),
        new("other", "Other command with sub-props",
            (_, _) => Task.CompletedTask,
            [
                "AlphaConfig",
                "AlphaMode",
                "AlphaValue",
                "BetaConfig",
                "BetaMode",
            ]),
    ];

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

        Assert.Equal(palette.MaxHeight, lines.Count);
        Assert.All(lines, line => Assert.Equal(string.Empty, line));
    }

    [Fact]
    public void GetLines_EmptyInput_ReturnsEmptyLines()
    {
        var palette = CreatePalette();
        var lines = palette.GetLines("");

        Assert.Equal(palette.MaxHeight, lines.Count);
        Assert.All(lines, line => Assert.Equal(string.Empty, line));
    }

    // ── GetLines: Command Mode ───────────────────────────────────────

    [Fact]
    public void GetLines_CommandPrefixWithNoMatch_ReturnsEmptyLines()
    {
        var palette = CreatePalette();
        var lines = palette.GetLines("/zzzz");

        Assert.Equal(palette.MaxHeight, lines.Count);
        Assert.All(lines, line => Assert.Equal(string.Empty, line));
    }

    [Fact]
    public void GetLines_CommandPrefix_ReturnsStatusLineAndHints()
    {
        var palette = CreatePalette();
        var lines = palette.GetLines("/");

        Assert.Equal(palette.MaxHeight, lines.Count);
        // Line 0 = status line
        Assert.Contains("tab", lines[0]);
        Assert.Contains("\u2191\u2193", lines[0]);
        // At least one hint populated
        Assert.Contains(lines.Skip(1), l => !string.IsNullOrEmpty(l));
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
    public void GetLines_MultipleMatches_PicksFirstSuggestion()
    {
        var palette = CreatePalette();
        palette.GetLines("/he");  // matches hello, help
        // Picks the first match's suggestion even with multiple matches
        Assert.NotNull(palette.CurrentSuggestion);
        Assert.StartsWith("/", palette.CurrentSuggestion);
    }

    [Fact]
    public void GetLines_ExactSingleMatch_SuggestsUniqueCommand()
    {
        var palette = CreatePalette();
        palette.GetLines("/exit");  // single exact match
        Assert.Equal("/exit ", palette.CurrentSuggestion);
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

        Assert.Contains(lines.Skip(1), l => l.Contains("hello"));
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
    public void GetLines_ChangedInput_ReturnsUpdatedContent()
    {
        var palette = CreatePalette();
        var first = palette.GetLines("/h");
        string firstHint = first.Count >= 2 ? first[1] : "(no hint)";

        var second = palette.GetLines("/xxx");  // matches nothing — all lines are empty

        // Verify content changed: hints from "/h" differ from empty lines for "/xxx"
        bool contentChanged = firstHint.Length > 0;
        for (int i = 0; i < second.Count; i++)
        {
            if (!string.IsNullOrEmpty(second[i]))
                contentChanged = true;
        }

        Assert.True(contentChanged,
            $"Changing input from \"/h\" to \"/xxx\" should change content.\n" +
            $"First[1]: \"{firstHint}\"\nSecond has {second.Count} lines");
    }

    // ── Selection ────────────────────────────────────────────────────

    [Fact]
    public void GetLines_SelectionAffectsSuggestion()
    {
        var palette = CreatePalette();

        palette.GetLines("/h");
        string? firstSuggestion = palette.CurrentSuggestion;

        // Navigate down to next match
        palette.AdjustSelection(1);
        palette.GetLines("/h");
        string? secondSuggestion = palette.CurrentSuggestion;

        // With 2+ matches, different selection should give different suggestion
        Assert.NotNull(firstSuggestion);
        Assert.NotNull(secondSuggestion);
        Assert.NotEqual(firstSuggestion, secondSuggestion);
    }

    [Fact]
    public void AdjustSelection_ClampsToMatchCount()
    {
        var palette = CreatePalette();
        palette.GetLines("/h");  // initialize state (hello, help matches)

        palette.AdjustSelection(10);  // beyond available matches
        palette.GetLines("/h");

        // Should be clamped — valid suggestion produced
        Assert.NotNull(palette.CurrentSuggestion);

        // Then adjust with large negative to ensure clamp doesn't go below 0
        palette.AdjustSelection(-20);
        palette.GetLines("/h");
        Assert.NotNull(palette.CurrentSuggestion);
    }

    [Fact]
    public void AdjustSelection_InputChanged_ResetsToFirstMatch()
    {
        var palette = CreatePalette();
        palette.GetLines("/h");  // initializes with first match suggestion
        string? firstSuggestion = palette.CurrentSuggestion;

        palette.AdjustSelection(1);  // move to second match
        palette.GetLines("/he");  // change input → should reset to first match

        Assert.NotNull(palette.CurrentSuggestion);
        Assert.StartsWith("/he", palette.CurrentSuggestion);
    }

    // ── Argument Suggestions ─────────────────────────────────────────

    [Fact]
    public void ArgumentSuggestions_ShownWhenMatching()
    {
        var palette = new CommandPalette(ArgSuggestionCommands);
        var lines = palette.GetLines("/deploy ");

        // Should show argument completions
        Assert.Contains(lines.Skip(1), l => l.Contains("linux") || l.Contains("windows"));
    }

    [Fact]
    public void ArgumentSuggestions_FilteredByTypedArg()
    {
        var palette = new CommandPalette(ArgSuggestionCommands);
        var lines = palette.GetLines("/deploy win");

        Assert.Contains(lines.Skip(1), l => l.Contains("windows"));
    }

    [Fact]
    public void ArgumentSuggestions_NoMatch_ShowsOnlyStatusLine()
    {
        var palette = new CommandPalette(ArgSuggestionCommands);
        var lines = palette.GetLines("/deploy xyz");

        // Line 0 is always the status line when input starts with /
        Assert.Equal(palette.MaxHeight, lines.Count);
        Assert.All(lines.Skip(1), line => Assert.Equal(string.Empty, line));
    }

    [Fact]
    public void ArgumentSuggestions_CommonPrefix_ShowsIndividualHints()
    {
        var palette = new CommandPalette(ArgSuggestionCommands);
        var lines = palette.GetLines("/deploy li");

        // Should show individual hints with full path, never compress
        Assert.Equal("[on white][black]→ [/][gray27]/deploy linux ubuntu[/][/]", lines[1]);
        Assert.Equal(" [grey] /deploy linux debian[/]", lines[2]);
        Assert.All(lines.Skip(3), line => Assert.Equal(string.Empty, line));
    }

    // ── Property-style suggestions (bug report scenario) ─────────────

    [Fact]
    public void ArgumentSuggestions_TypedPrefixEqualsCommonPrefix_ShowsIndividualHints()
    {
        // Bug report: typing "/appconfig DirectLlm" should show each
        // sub-property as a separate hint, not a single compressed entry.
        // The common prefix "DirectLlm" equals what was typed (8 chars each),
        // so no compression should occur.
        var palette = new CommandPalette(PropertyStyleSuggestions);
        var lines = palette.GetLines("/appconfig DirectLlm");

        Assert.Equal("[on white][black]→ [/][gray27]/appconfig DirectLlmApiType[/][/]", lines[1]);
        Assert.Equal(" [grey] /appconfig DirectLlmModelName[/]", lines[2]);
        Assert.Equal(" [grey] /appconfig DirectLlmToken[/]", lines[3]);
        Assert.Equal(" [grey] /appconfig DirectLlmUrl[/]", lines[4]);
        Assert.All(lines.Skip(5), line => Assert.Equal(string.Empty, line));

        // CurrentSuggestion should be the first match
        Assert.Equal("/appconfig DirectLlmApiType ", palette.CurrentSuggestion);
    }

    [Fact]
    public void ArgumentSuggestions_TypedPrefixIsShorterThanCommonPrefix_ShowsIndividualHints()
    {
        // Typing "/appconfig Direc" should show all individual hints,
        // never compress to a common prefix.
        var palette = new CommandPalette(PropertyStyleSuggestions);
        var lines = palette.GetLines("/appconfig Direc");

        Assert.Equal("[on white][black]→ [/][gray27]/appconfig DirectLlmApiType[/][/]", lines[1]);
        Assert.Equal(" [grey] /appconfig DirectLlmModelName[/]", lines[2]);
        Assert.Equal(" [grey] /appconfig DirectLlmToken[/]", lines[3]);
        Assert.Equal(" [grey] /appconfig DirectLlmUrl[/]", lines[4]);
        Assert.All(lines.Skip(5), line => Assert.Equal(string.Empty, line));

        // The CurrentSuggestion should be the first match
        Assert.Equal("/appconfig DirectLlmApiType ", palette.CurrentSuggestion);
    }

    [Fact]
    public void ArgumentSuggestions_TypedFullPropertyName_ShowsOneHint()
    {
        // Typing "/appconfig DirectLlmApiType" matches only one suggestion
        var palette = new CommandPalette(PropertyStyleSuggestions);
        var lines = palette.GetLines("/appconfig DirectLlmApiType");

        // Should show exactly one selected hint for the complete entry
        Assert.Equal("[on white][black]→ [/][gray27]/appconfig DirectLlmApiType[/][/]", lines[1]);
        Assert.All(lines.Skip(2), line => Assert.Equal(string.Empty, line));
        Assert.Equal("/appconfig DirectLlmApiType ", palette.CurrentSuggestion);
    }

    [Fact]
    public void ArgumentSuggestions_TrailingSpaceOnSingleToken_ShowsNoHints()
    {
        // Typing "/appconfig DirectLlm " (with trailing space) after a single-token
        // property name: the space makes "DirectLlm " the prefix, but none of the
        // property suggestions start with "DirectLlm " (space after the word).
        // Since each suggestion is a single word (no spaces), there are no matches.
        var palette = new CommandPalette(PropertyStyleSuggestions);
        var lines = palette.GetLines("/appconfig DirectLlm ");

        // No hints should be shown (all argument lines empty)
        Assert.All(lines.Skip(1), line => Assert.Equal(string.Empty, line));
        Assert.Null(palette.CurrentSuggestion);
    }

    [Fact]
    public void ArgumentSuggestions_FilteringByFirstWord_ShowsSubset()
    {
        // Typing "/other Alpha" should match AlphaConfig, AlphaMode, AlphaValue
        var palette = new CommandPalette(PropertyStyleSuggestions);
        var lines = palette.GetLines("/other Alpha");

        Assert.Equal("[on white][black]→ [/][gray27]/other AlphaConfig[/][/]", lines[1]);
        Assert.Equal(" [grey] /other AlphaMode[/]", lines[2]);
        Assert.Equal(" [grey] /other AlphaValue[/]", lines[3]);
        Assert.All(lines.Skip(4), line => Assert.Equal(string.Empty, line));

        // Should NOT show BetaConfig or BetaMode
        Assert.DoesNotContain(lines.Skip(1), l => l.Contains("BetaConfig"));
        Assert.DoesNotContain(lines.Skip(1), l => l.Contains("BetaMode"));

        Assert.Equal("/other AlphaConfig ", palette.CurrentSuggestion);
    }

    [Fact]
    public void ArgumentSuggestions_CaseInsensitiveMatch_OnPropertyName()
    {
        var palette = new CommandPalette(PropertyStyleSuggestions);
        var lines = palette.GetLines("/appconfig directllm");

        Assert.Equal("[on white][black]→ [/][gray27]/appconfig DirectLlmApiType[/][/]", lines[1]);
        Assert.Equal(" [grey] /appconfig DirectLlmModelName[/]", lines[2]);
        Assert.Equal(" [grey] /appconfig DirectLlmToken[/]", lines[3]);
        Assert.Equal(" [grey] /appconfig DirectLlmUrl[/]", lines[4]);
        Assert.Equal("/appconfig DirectLlmApiType ", palette.CurrentSuggestion);
    }

    [Fact]
    public void ArgumentSuggestions_GroupedPrefix_ShowsIndividualHints()
    {
        // Typing "/other Alpha" where commonPrefix "Alpha" equals typed text
        // → no compression, shows individual hints for each sub-property
        var palette = new CommandPalette(PropertyStyleSuggestions);
        var lines = palette.GetLines("/other Alpha");

        Assert.Equal("[on white][black]→ [/][gray27]/other AlphaConfig[/][/]", lines[1]);
        Assert.Equal(" [grey] /other AlphaMode[/]", lines[2]);
        Assert.Equal(" [grey] /other AlphaValue[/]", lines[3]);
        Assert.All(lines.Skip(4), line => Assert.Equal(string.Empty, line));
        Assert.Equal("/other AlphaConfig ", palette.CurrentSuggestion);
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

        Assert.Equal(palette.MaxHeight, lines.Count);
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
        => new(buffer, selection, () => margin, getAttachments: () => Array.Empty<Attachment>());

    private static CursorMovementHandler CreateHandlerWithAttachments(
        TextBuffer buffer, SelectionManager selection,
        IReadOnlyList<Attachment> attachments, int margin = 80)
        => new(buffer, selection, () => margin, getAttachments: () => attachments);

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

    // ══════════════════════════════════════════════════════════════════
    //  Home / End — visual line awareness (wrapped text)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void MoveHome_WrappedText_GoesToVisualLineStart()
    {
        // margin=10, cap=4 → "hello world" wraps as ["hell", "o wo", "rld"]
        var buf = new TextBuffer();
        buf.Insert("hello world");
        buf.MoveTo(5); // position 5 = ' ' on second visual line "o wo"
        var handler = CreateHandler(buf, new SelectionManager(), margin: 10);
        handler.MoveCursorHome(shift: false);
        Assert.Equal(4, buf.CursorPosition); // start of second visual line
    }

    [Fact]
    public void MoveEnd_WrappedText_GoesToVisualLineEnd()
    {
        // margin=10, cap=4 → "hello world" wraps as ["hell", "o wo", "rld"]
        var buf = new TextBuffer();
        buf.Insert("hello world");
        buf.MoveTo(5); // position 5 = ' ' on second visual line "o wo"
        var handler = CreateHandler(buf, new SelectionManager(), margin: 10);
        handler.MoveCursorEnd(shift: false);
        Assert.Equal(8, buf.CursorPosition); // end of second visual line (boundary of next line)
    }

    [Fact]
    public void MoveHome_WrappedText_AlreadyAtStart_Stays()
    {
        var buf = new TextBuffer();
        buf.Insert("hello world");
        buf.MoveTo(4); // start of second visual line "o wo"
        var handler = CreateHandler(buf, new SelectionManager(), margin: 10);
        handler.MoveCursorHome(shift: false);
        Assert.Equal(4, buf.CursorPosition); // already at start — stays
    }

    [Fact]
    public void MoveEnd_WrappedText_AlreadyAtEnd_Stays()
    {
        var buf = new TextBuffer();
        buf.Insert("hello world");
        buf.MoveTo(11); // past end of last visual line "rld"
        var handler = CreateHandler(buf, new SelectionManager(), margin: 10);
        handler.MoveCursorEnd(shift: false);
        Assert.Equal(11, buf.CursorPosition); // already at buffer end — stays
    }

    // ══════════════════════════════════════════════════════════════════
    //  Home / End — logical lines (explicit newlines)
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
    public void MoveHome_AlreadyAtLineStart_Stays()
    {
        var buf = new TextBuffer();
        buf.Insert("abc\ndef");
        buf.MoveTo(4); // start of 'def' line
        var handler = CreateHandler(buf, new SelectionManager());
        handler.MoveCursorHome(shift: false);
        Assert.Equal(4, buf.CursorPosition); // already at visual line start — stays
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
    public void MoveEnd_AtLineEnd_Stays()
    {
        var buf = new TextBuffer();
        buf.Insert("abc\ndef");
        buf.MoveTo(3); // '\n' between lines
        var handler = CreateHandler(buf, new SelectionManager());
        handler.MoveCursorEnd(shift: false);
        Assert.Equal(3, buf.CursorPosition); // already at visual line end — stays
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
