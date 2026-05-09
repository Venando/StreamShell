using System.Text;
using StreamShell;

namespace StreamShell.Tests;

/// <summary>
/// Tests for <see cref="MarkupBuilder.BuildLineMarkup"/>, specifically
/// covering <c>AppendMarkupEscapedWithPlaceholder</c> through the public
/// API surface (the private method is exercised via <c>AppendEscapedChunk</c>
/// when placeholders are configured and cursor/selection don't intercept).
///
/// Enters the placeholder path via:
///   BuildLineMarkup → hasSelection=false, cursor outside line → cursorCol=-1
///   → AppendCursorHighlight(rawText, -1, …) → delegates to AppendEscapedChunk
///   → AppendMarkupEscapedWithPlaceholder(text)
///
/// Since this is the private method, coverage is driven by varying:
/// - PlaceholderStrings (null, empty, set)
/// - Text content (matches, fragments, special chars, multiple placeholders)
/// - Cursor position (on char, past end → cursor-path with remaining text)
/// - Command slash prefix (isCommandSlash=true → text[1..] path)
///
/// Cursor-path tests also cover AppendMarkupEscapedWithPlaceholder via
/// AppendCursorHighlight when cursorCol ≥ 0 and there's text after the
/// highlighted character.
/// </summary>
public class MarkupBuilderPlaceholderTests
{
    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>Builds markup by calling BuildLineMarkup with cursor outside
    /// the line range (so cursor is hidden and placeholders are rendered via
    /// AppendEscapedChunk → AppendMarkupEscapedWithPlaceholder).</summary>
    private static string BuildWithPlaceholders(
        string text,
        IReadOnlyList<string>? placeholders,
        MarkupBuilder? builder = null)
    {
        var settings = new StreamShellSettings();
        var mb = builder ?? new MarkupBuilder(settings);
        mb.PlaceholderStrings = placeholders;
        return mb.BuildLineMarkup(
            input: text,
            lineOffset: 0,
            lineText: text,
            cursorPosition: -1,       // outside line → cursorCol=-1
            hasSelection: false,
            selectionStart: 0,
            selectionLength: 0);
    }

    /// <summary>Builds markup with the cursor positioned INSIDE the line
    /// text. This exercises AppendCursorHighlight with cursorCol ≥ 0,
    /// which calls AppendMarkupEscapedWithPlaceholder for the text
    /// remaining after the highlighted character.</summary>
    private static string BuildWithCursorAt(
        string text,
        int cursorOffset,
        IReadOnlyList<string>? placeholders = null,
        MarkupBuilder? builder = null)
    {
        var settings = new StreamShellSettings();
        var mb = builder ?? new MarkupBuilder(settings);
        mb.PlaceholderStrings = placeholders;
        return mb.BuildLineMarkup(
            input: text,
            lineOffset: 0,
            lineText: text,
            cursorPosition: cursorOffset,
            hasSelection: false,
            selectionStart: 0,
            selectionLength: 0);
    }

    /// <summary>Builds markup with a command-slash prefix so
    /// AppendEscapedChunk takes the isCommandSlash=true path,
    /// passing text[1..] to AppendMarkupEscapedWithPlaceholder.</summary>
    private static string BuildWithCommandSlash(
        string textAfterSlash,
        IReadOnlyList<string>? placeholders = null,
        MarkupBuilder? builder = null)
    {
        var settings = new StreamShellSettings();
        var mb = builder ?? new MarkupBuilder(settings);
        mb.PlaceholderStrings = placeholders;
        string fullText = "/" + textAfterSlash;
        return mb.BuildLineMarkup(
            input: fullText,
            lineOffset: 0,
            lineText: fullText,
            cursorPosition: -1,
            hasSelection: false,
            selectionStart: 0,
            selectionLength: 0);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Path 1: PlaceholderStrings is null
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void PlaceholderStrings_Null_OnlyEscapesMarkupChars()
    {
        // When no placeholders are set, the method falls through to
        // AppendMarkupEscaped which escapes [ and ].
        string result = BuildWithPlaceholders("hello [world]", placeholders: null);
        Assert.Equal("hello [[world]]", result);
    }

    [Fact]
    public void PlaceholderStrings_Null_PlainTextIsUnchanged()
    {
        string result = BuildWithPlaceholders("plain text here", placeholders: null);
        Assert.Equal("plain text here", result);
    }

    [Fact]
    public void PlaceholderStrings_Null_MultipleBrackets()
    {
        string result = BuildWithPlaceholders("[a] [b] [c]", placeholders: null);
        Assert.Equal("[[a]] [[b]] [[c]]", result);
    }

    [Fact]
    public void PlaceholderStrings_Null_EscapedBracketsMixed()
    {
        string result = BuildWithPlaceholders("before]after[", placeholders: null);
        Assert.Equal("before]]after[[", result);
    }

    [Fact]
    public void PlaceholderStrings_Null_EmptyString()
    {
        string result = BuildWithPlaceholders("", placeholders: null);
        Assert.Equal("", result);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Path 2: PlaceholderStrings is empty
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void PlaceholderStrings_Empty_BehavesLikeNull()
    {
        string result = BuildWithPlaceholders("test [stuff]", placeholders: Array.Empty<string>());
        Assert.Equal("test [[stuff]]", result);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Path 3: No match in text — all text escaped via AppendMarkupEscaped
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Placeholder_NoMatch_FullTextEscaped()
    {
        // Placeholder "[paste]" doesn't appear in "hello world"
        string result = BuildWithPlaceholders("hello world", ["[paste]"]);
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void Placeholder_NoMatch_BracketsStillEscaped()
    {
        string result = BuildWithPlaceholders("hello [world]", ["[missing]"]);
        Assert.Equal("hello [[world]]", result);
    }

    [Fact]
    public void Placeholder_NoMatch_MultiplePlaceholdersNoneMatch()
    {
        string result = BuildWithPlaceholders("random text",
            ["[a]", "[b]", "[c]"]);
        Assert.Equal("random text", result);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Path 4 & 5 & 6: Single placeholder match
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Placeholder_SingleMatch_InMiddle()
    {
        // Placeholder at the middle: text before → italic ph → text after
        string result = BuildWithPlaceholders(
            "before[paste #1, 1 line]after",
            ["[paste #1, 1 line]"]);
        Assert.Equal(
            "before[italic underline][[paste #1, 1 line]][/]after",
            result);
    }

    [Fact]
    public void Placeholder_SingleMatch_AtStart()
    {
        // No text before placeholder
        string result = BuildWithPlaceholders(
            "[paste]end",
            ["[paste]"]);
        Assert.Equal(
            "[italic underline][[paste]][/]end",
            result);
    }

    [Fact]
    public void Placeholder_SingleMatch_AtEnd()
    {
        // No text after placeholder
        string result = BuildWithPlaceholders(
            "start[paste]",
            ["[paste]"]);
        Assert.Equal(
            "start[italic underline][[paste]][/]",
            result);
    }

    [Fact]
    public void Placeholder_SingleMatch_WholeText()
    {
        // The entire text is the placeholder
        string result = BuildWithPlaceholders(
            "[paste]",
            ["[paste]"]);
        Assert.Equal(
            "[italic underline][[paste]][/]",
            result);
    }

    [Fact]
    public void Placeholder_SingleMatch_TextBeforeNeedsEscaping()
    {
        // Text before placeholder contains [ and ] that must be escaped
        string result = BuildWithPlaceholders(
            "a[b]c[paste]after",
            ["[paste]"]);
        Assert.Equal(
            "a[[b]]c[italic underline][[paste]][/]after",
            result);
    }

    [Fact]
    public void Placeholder_SingleMatch_TextAfterNeedsEscaping()
    {
        string result = BuildWithPlaceholders(
            "before[paste]a[d]",
            ["[paste]"]);
        Assert.Equal(
            "before[italic underline][[paste]][/]a[[d]]",
            result);
    }

    [Fact]
    public void Placeholder_SingleMatch_AllBracketsEscapedProperly()
    {
        // Both before and after have brackets; placeholder's own brackets
        // are doubled inside italic underline
        string result = BuildWithPlaceholders(
            "[x][paste #1][y]",
            ["[paste #1]"]);
        Assert.Equal(
            "[[x]][italic underline][[paste #1]][/][[y]]",
            result);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Path 7: Multiple placeholders
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Placeholder_MultipleMatches_TextBetween()
    {
        // Two placeholders with text between
        string result = BuildWithPlaceholders(
            "start[p1]middle[p2]end",
            ["[p1]", "[p2]"]);
        Assert.Equal(
            "start[italic underline][[p1]][/]middle[italic underline][[p2]][/]end",
            result);
    }

    [Fact]
    public void Placeholder_MultipleMatches_Adjacent()
    {
        // Two placeholders back-to-back with no gap
        string result = BuildWithPlaceholders(
            "[p1][p2]",
            ["[p1]", "[p2]"]);
        Assert.Equal(
            "[italic underline][[p1]][/][italic underline][[p2]][/]",
            result);
    }

    [Fact]
    public void Placeholder_MultipleMatches_ThreeInRow()
    {
        string result = BuildWithPlaceholders(
            "a[p1]b[p2]c[p3]d",
            ["[p1]", "[p2]", "[p3]"]);
        Assert.Equal(
            "a[italic underline][[p1]][/]b" +
            "[italic underline][[p2]][/]c" +
            "[italic underline][[p3]][/]d",
            result);
    }

    [Fact]
    public void Placeholder_MultipleMatches_OnePlaceholderInsideAnother()
    {
        // "[p1]" contains "[p1" — the algorithm picks the earliest match
        // and renders it. After that range, remaining text is searched again.
        string result = BuildWithPlaceholders(
            "x[p1]y",
            ["[p1]", "p1]"]);
        Assert.Equal(
            "x[italic underline][[p1]][/]y",
            result);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Path 9: Cursor-split fragment match
    //  When placeholder starts with '[' and the text has the fragment
    //  (remaining portion after '[', e.g. "paste #1, 2 lines]"),
    //  the fragment check triggers.
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Placeholder_FragmentMatch_TextStartsWithFragment()
    {
        // Placeholder is "[paste #1]". Text is "paste #1]" — missing the '['
        // Fragment check: ph[0]='[', fragment = "paste #1]", text starts with it
        string result = BuildWithPlaceholders(
            "paste #1]",
            ["[paste #1]"]);
        Assert.Equal(
            "[italic underline]paste #1]][/]",
            result);
    }

    [Fact]
    public void Placeholder_FragmentMatch_WithPrecedingText_FullMatchHandlesIt()
    {
        // Fragment matching only checks StartsWith at the current searchFrom
        // position. Here "then paste #1]" at position 0 doesn't start with
        // fragment "paste #1]", so no fragment match. The ']' gets escaped.
        string result = BuildWithPlaceholders(
            "then paste #1]",
            ["[paste #1]"]);
        // No placeholder match at all → just escape brackets
        Assert.Equal(
            "then paste #1]]",
            result);
    }

    [Fact]
    public void Placeholder_FragmentMatch_FragmentContainsBrackets()
    {
        // Text IS the fragment (placeholder missing leading '[')
        string result = BuildWithPlaceholders(
            "fragment]",
            ["[fragment]"]);
        // At position 0: remaining = "fragment]". Fragment "fragment]" starts at 0.
        // Fragment match → italic underline. ']' doubled inside: "fragment]]"
        Assert.Equal(
            "[italic underline]fragment]][/]",
            result);
    }

    [Fact]
    public void Placeholder_FragmentMatch_NoTrailingBracket()
    {
        // Placeholder "[paste]" — fragment is "paste]"
        // If text only has "paste" without "]", it won't match as fragment
        string result = BuildWithPlaceholders(
            "paste ",
            ["[paste]"]);
        // "paste" starts with "paste" but fragment "paste]" doesn't match "paste "
        // No full match either → all escaped
        Assert.Equal("paste ", result);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Path 10: Fragment wins over full match when fragment is earlier
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Placeholder_FragmentBeatsLaterFullMatch()
    {
        // Text starts with fragment "paste]" (without opening '['),
        // then has a full match "[paste]" later.
        // Fragment is at position 0, full match is later.
        string result = BuildWithPlaceholders(
            "paste] more [paste]",
            ["[paste]"]);
        // Fragment at 0: "paste]" → italic underline
        // " more " → escaped
        // "[paste]" → full match → italic underline
        Assert.Equal(
            "[italic underline]paste]][/] more " +
            "[italic underline][[paste]][/]",
            result);
    }

    [Fact]
    public void Placeholder_FullMatchBeatsNoFragmentMatch()
    {
        // Placeholder doesn't match as fragment (text doesn't start with fragment)
        // but matches as full match later
        string result = BuildWithPlaceholders(
            "text [paste] end",
            ["[paste]"]);
        // No fragment match at position 0 → full match at position 5
        Assert.Equal(
            "text [italic underline][[paste]][/] end",
            result);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Path 11: Placeholder content itself contains [ or ]
    //  These characters are doubled inside the italic underline block
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Placeholder_WithBracketInContent_DoubledInsideItalic()
    {
        // Placeholder "[te[st]" (7 chars) matches at position 1 in "x[te[st]y".
        // The rendered range is text[1..8] which includes the opening '[':
        //   placeholder content = "[te[st]"
        // Inside italic underline, '[' becomes '[[' and ']' becomes ']]':
        //   "[te[st]" → "[[te[[st]]"
        string result = BuildWithPlaceholders(
            "x[te[st]y",
            ["[te[st]"]);
        Assert.Equal(
            "x[italic underline][[te[[st]][/]y",
            result);
    }

    [Fact]
    public void Placeholder_WithBracketInContent_ClosingBracket()
    {
        // Placeholder "[te]st]" (7 chars) matches at position 1 in "x[te]st]y".
        // rendered range = text[1..8] = "[te]st]" (includes opening bracket)
        // Inside italic: '[', 't', 'e', ']'→']]', 's', 't', ']'→']]'
        //   = "[[te]]st]]"
        string result = BuildWithPlaceholders(
            "x[te]st]y",
            ["[te]st]"]);
        Assert.Equal(
            "x[italic underline][[te]]st]][/]y",
            result);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Path 12: Cursor-path — cursor inside line, text after cursor
    //  Exercises AppendMarkupEscapedWithPlaceholder for the remaining
    //  text after the highlighted cursor character.
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void CursorPath_AfterCursorText_UsesPlaceholderEscaping()
    {
        // Cursor at position 3 in "abc[paste]xyz"
        // Char at cursor = 'p', remaining = "aste]xyz"
        // Remaining contains the placeholder minus its opening '['
        string result = BuildWithCursorAt(
            "abc[paste]xyz",
            cursorOffset: 3,
            ["[paste]"]);
        // Expected: "abc" + cursor highlighted '[' + "paste]" → italic underline
        // Wait, cursor at position 3 is '[' in "[paste]"
        // localCol = 3, char = '[' → rendered with cursor style as "[["
        // localCol+1=4 → remaining = "paste]xyz"
        // remaining starts at "paste]" → fragment match for "[paste]" at position 0 relative to remaining
        // But wait: AppendMarkupEscapedWithPlaceholder is called with rawText[(localCol+1)..] = "paste]xyz"
        // searchFrom=0: remaining = "paste]xyz"
        // Full match: IndexOf("[paste]") → no match in "paste]xyz"
        // Fragment: ph="[paste]", fragment="paste]", remaining.StartsWith("paste]") → yes
        // So bestIdx=0, bestEnd=6 ("paste]" = 6 chars)
        // If bestIdx > searchFrom → no (0 > 0 is false)
        // Render "[italic underline]paste]][/]"
        // Then searchFrom=6, remaining = "xyz" → escaped
        // Full result: cursor-marked '[' = "abc[white]...[/]" + "[italic underline]paste]][/]xyz"

        Assert.Contains("[italic underline]paste]][/]", result);
        Assert.Contains("xyz", result);
    }

    [Fact]
    public void CursorPath_AfterCursorText_PlaceholderInRemaining()
    {
        // Cursor at position 0 of "ab[paste]cd"
        // char at cursor = 'a', remaining = "b[paste]cd"
        // Full match for "[paste]" at position 1 in remaining
        string result = BuildWithCursorAt(
            "ab[paste]cd",
            cursorOffset: 0,
            ["[paste]"]);
        // Expected: cursor at 'a', then remaining "b[paste]cd" → "b[italic underline][[paste]][/]cd"
        Assert.Contains("[italic underline][[paste]][/]cd", result);
    }

    [Fact]
    public void CursorPath_AfterCursorText_NoPlaceholder_PlainEscaped()
    {
        // Cursor at position 0, no placeholders set
        string result = BuildWithCursorAt(
            "hello world",
            cursorOffset: 0);
        // Just cursor highlight on 'h', then "ello world"
        // Since no placeholders, remaining text is just escaped (no brackets in "ello world")
        Assert.Contains("ello world", result);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Command-slash path: isCommandSlashLine=true
    //  AppendEscapedChunk renders '/' with command markup then passes
    //  the rest through AppendMarkupEscapedWithPlaceholder
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void CommandSlashPath_PlaceholderInRest()
    {
        // lineText starts with '/', lineOffset=0 → isCommandSlashLine=true
        // '/' gets command-markup, "rest[paste]" goes to placeholder path
        string result = BuildWithCommandSlash(
            "rest[paste]",
            ["[paste]"]);
        // Expected: "[springgreen]/[/]" + "rest[italic underline][paste][/]"
        // Wait, `_settings.CommandSlashMarkup` defaults to "springgreen" for Spectre markup
        // Let me use default setting
        Assert.Contains("[italic underline][[paste]][/]", result);
    }

    [Fact]
    public void CommandSlashPath_NoSlashInRest_PlainEscaped()
    {
        string result = BuildWithCommandSlash(
            "rest",
            ["[paste]"]);
        // '/' → command markup, "rest" → no placeholder match → plain
        Assert.Contains("/", result);
        Assert.Contains("rest", result);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Edge cases
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Placeholder_EmptyText_ReturnsEmpty()
    {
        string result = BuildWithPlaceholders("", ["[paste]"]);
        Assert.Equal("", result);
    }

    [Fact]
    public void Placeholder_TextLongerThanPlaceholder_EndingBracketEscaped()
    {
        // Text with no placeholder match, but has ']' that needs escaping.
        // Fragment matching only triggers at the current search position
        // when remaining starts with the fragment.
        string result = BuildWithPlaceholders(
            "start middle paste]",
            ["[paste]"]);
        // No match → ']' gets escaped by AppendMarkupEscaped
        Assert.Equal("start middle paste]]", result);
    }

    [Fact]
    public void Placeholder_CaseSensitiveMatching()
    {
        // Matching uses Ordinal (case-sensitive)
        string result = BuildWithPlaceholders(
            "before[Paste]after",
            ["[paste]"]);
        // "[paste]" won't match "[Paste]" due to Ordinal comparison
        Assert.Equal("before[[Paste]]after", result);
    }

    [Fact]
    public void Placeholder_MultiplePlaceholders_MixedMatchOrder()
    {
        // Two placeholders, second one appears before first in text
        string result = BuildWithPlaceholders(
            "alpha[beta][alpha]gamma",
            ["[alpha]", "[beta]"]);
        // "[beta]" at position 5, "[alpha]" at position 11
        // Both match in order: beta first, then alpha
        Assert.Equal(
            "alpha[italic underline][[beta]][/][italic underline][[alpha]][/]gamma",
            result);
    }

    [Fact]
    public void Placeholder_SamePlaceholderRepeated()
    {
        // Same placeholder appears multiple times
        string result = BuildWithPlaceholders(
            "[ph][ph]",
            ["[ph]"]);
        Assert.Equal(
            "[italic underline][[ph]][/][italic underline][[ph]][/]",
            result);
    }

    [Fact]
    public void Placeholder_OverlappingPlaceholders_EarliestWins()
    {
        // Two placeholders: "[abcd]" and "[bcde]"
        // In text "x[abcd]y", only "[abcd]" matches at position 1
        string result = BuildWithPlaceholders(
            "x[abcd]y",
            ["[abcd]", "[bcde]"]);
        Assert.Equal(
            "x[italic underline][[abcd]][/]y",
            result);
    }

    /// <summary>Verifies the full forward-search-continue loop by
    /// ensuring the search resumes past each rendered placeholder.</summary>
    [Fact]
    public void Placeholder_MultipleNonOverlapping_MidAndEnd()
    {
        string result = BuildWithPlaceholders(
            "start[p1]middle[p2]",
            ["[p1]", "[p2]"]);
        Assert.Equal(
            "start[italic underline][[p1]][/]middle[italic underline][[p2]][/]",
            result);
    }

    [Fact]
    public void Placeholder_WhitespaceTextWithPlaceholder()
    {
        string result = BuildWithPlaceholders(
            "  [paste]  ",
            ["[paste]"]);
        Assert.Equal(
            "  [italic underline][[paste]][/]  ",
            result);
    }
}
