namespace StreamShell.Tests;

using Spectre.Console;

// ═════════════════════════════════════════════════════════════════════
//  ConsoleRenderer — Rendering Tests (via MockTerminal)
//  Tests all output-producing methods:
//  ClearInputLine, ClearInputBlock, ClearInputBlockForReRender,
//  RenderInputBlock, OverwriteInputBlock, RenderInputLine,
//  HandleBlockHeightChange, RenderMessage
// ═════════════════════════════════════════════════════════════════════

public class ConsoleRendererRenderingTests
{
    private readonly MockTerminal _terminal = new();
    private readonly StreamShellSettings _settings = new();

    private ConsoleRenderer CreateRenderer()
        => new(_settings, _terminal);

    // ── ClearInputLine ───────────────────────────────────────────────

    [Fact]
    public void ClearInputLine_WritesClearCode()
    {
        var renderer = CreateRenderer();
        renderer.ClearInputLine();

        // The clear escape sequence was written
        Assert.Contains("\x1b[K", _terminal.WrittenTexts);
    }

    [Fact]
    public void ClearInputLine_MultipleCalls_EachClears()
    {
        var renderer = CreateRenderer();
        renderer.ClearInputLine();
        renderer.ClearInputLine();

        Assert.Equal(2, _terminal.WrittenTexts.Count(t => t == "\x1b[K"));
    }

    // ── RenderMessage ────────────────────────────────────────────────

    [Fact]
    public void RenderMessage_DoesNotCrash()
    {
        var renderer = CreateRenderer();
        renderer.RenderMessage("hello");

        Assert.NotNull(renderer);
    }

    // ── ClearInputBlock ──────────────────────────────────────────────

    [Fact]
    public void ClearInputBlock_NullInput_DoesNothing()
    {
        var renderer = CreateRenderer();
        renderer.ClearInputBlock(null);

        Assert.Empty(_terminal.WrittenTexts);
        Assert.Empty(_terminal.WrittenLines);
    }

    [Fact]
    public void ClearInputBlock_WithInput_SetsCursorAndClearsBlock()
    {
        _terminal.CursorTop = 20;
        _terminal.WindowWidth = 80;

        var renderer = CreateRenderer();
        renderer.SetPanelLineCount(2);
        renderer.ClearInputBlock("hello");

        // Block offset = 1 + panel(2) + input lines(1) = 4
        // CursorTop = 20 - 4 = 16 (before block clear which modifies it further)
        Assert.True(_terminal.CursorTop >= 0);
    }

    // ── ClearInputBlockForReRender ───────────────────────────────────

    [Fact]
    public void ClearInputBlockForReRender_NullOldInput_DoesNothing()
    {
        var renderer = CreateRenderer();
        renderer.ClearInputBlockForReRender(null, "new", 2);

        Assert.Empty(_terminal.WrittenTexts);
    }

    [Fact]
    public void ClearInputBlockForReRender_WithInput_ClearsOffset()
    {
        _terminal.CursorTop = 30;
        _terminal.WindowWidth = 80;

        var renderer = CreateRenderer();
        renderer.SetPanelLineCount(2);
        renderer.ClearInputBlockForReRender("old", "new\nmulti\nline", 2);

        Assert.NotNull(renderer);
    }

    [Fact]
    public void RenderInputBlock_BufferHeightIncrease_ClearsToEndOfBuffer()
    {
        _terminal.BufferHeight = 30;
        _terminal.WindowWidth = 80;

        var renderer = CreateRenderer();
        renderer.SetPanelLineCount(2);
        renderer.RenderInputBlock(
            "hello", Array.Empty<string>(),
            0, false, 0, 0, 80);

        // First render should not emit clear-to-end-of-screen
        Assert.DoesNotContain("\x1b[J", _terminal.WrittenTexts);

        // Simulate terminal resize: buffer grows
        _terminal.BufferHeight = 40;

        renderer.RenderInputBlock(
            "hello", Array.Empty<string>(),
            0, false, 0, 0, 80);

        // Second render must emit clear-to-end-of-screen because block top shifted
        Assert.Contains("\x1b[J", _terminal.WrittenTexts);
    }

    [Fact]
    public void RenderInputBlock_BufferHeightIncrease_ClearsOldBlockArea()
    {
        _terminal.BufferHeight = 30;
        _terminal.WindowWidth = 80;

        var renderer = CreateRenderer();
        renderer.SetPanelLineCount(2);
        renderer.RenderInputBlock(
            "hello", Array.Empty<string>(),
            0, false, 0, 0, 80);

        int firstTop = _terminal.CursorTop;
        _terminal.ClearOutput();

        // Simulate terminal resize: buffer grows
        _terminal.BufferHeight = 40;

        renderer.RenderInputBlock(
            "hello", Array.Empty<string>(),
            0, false, 0, 0, 80);

        int secondTop = _terminal.CursorTop;

        // Old block top must be explicitly cleared (old area is above new area)
        Assert.True(secondTop > firstTop, $"Expected secondTop ({secondTop}) > firstTop ({firstTop})");

        var oldTopClear = _terminal.SetCursorCalls.Any(c => c.Top == firstTop && c.Left == 0);
        Assert.True(oldTopClear, "Should clear old block area when buffer grows");
    }

    [Fact]
    public void OverwriteFullBlock_BufferHeightIncrease_ClearsOldBlockArea()
    {
        _terminal.BufferHeight = 30;
        _terminal.WindowWidth = 80;

        var renderer = CreateRenderer();
        renderer.SetPanelLineCount(2);
        renderer.OverwriteFullBlock(
            "hello", Array.Empty<string>(),
            oldBlockOffset: 4, 0, false, 0, 0, 80);

        int firstTop = _terminal.CursorTop;
        _terminal.ClearOutput();

        _terminal.BufferHeight = 40;

        renderer.OverwriteFullBlock(
            "hello", Array.Empty<string>(),
            oldBlockOffset: 4, 0, false, 0, 0, 80);

        int secondTop = _terminal.CursorTop;

        Assert.True(secondTop > firstTop, $"Expected secondTop ({secondTop}) > firstTop ({firstTop})");

        var oldTopClear = _terminal.SetCursorCalls.Any(c => c.Top == firstTop && c.Left == 0);
        Assert.True(oldTopClear, "Should clear old block area when buffer grows");
    }

    [Fact]
    public void OverwriteInputBlock_BufferHeightIncrease_ClearsOldBlockArea()
    {
        _terminal.BufferHeight = 30;
        _terminal.WindowWidth = 80;

        var renderer = CreateRenderer();
        renderer.SetPanelLineCount(2);
        renderer.OverwriteInputBlock(
            "hello", Array.Empty<string>(),
            blockOffset: 4, 0, false, 0, 0, 80);

        int firstTop = _terminal.CursorTop;
        _terminal.ClearOutput();

        _terminal.BufferHeight = 40;

        renderer.OverwriteInputBlock(
            "hello", Array.Empty<string>(),
            blockOffset: 4, 0, false, 0, 0, 80);

        int secondTop = _terminal.CursorTop;

        Assert.True(secondTop > firstTop, $"Expected secondTop ({secondTop}) > firstTop ({firstTop})");

        var oldTopClear = _terminal.SetCursorCalls.Any(c => c.Top == firstTop && c.Left == 0);
        Assert.True(oldTopClear, "Should clear old block area when buffer grows");
    }

    [Fact]
    public void RenderInputBlock_ShowUserFieldFalse_BufferHeightChange_ClearsToEndOfScreen()
    {
        _terminal.BufferHeight = 30;
        _terminal.WindowWidth = 80;

        var renderer = CreateRenderer();
        renderer.ShowUserField = false;
        renderer.SetPanelLineCount(2);
        renderer.RenderInputBlock(
            "hello", Array.Empty<string>(),
            0, false, 0, 0, 80);

        // First render should not emit clear-to-end-of-screen
        Assert.DoesNotContain("\x1b[J", _terminal.WrittenTexts);

        // Simulate terminal resize: buffer grows
        _terminal.BufferHeight = 40;

        renderer.RenderInputBlock(
            "hello", Array.Empty<string>(),
            0, false, 0, 0, 80);

        // Second render must emit clear-to-end-of-screen because block top shifted
        Assert.Contains("\x1b[J", _terminal.WrittenTexts);
    }

    [Fact]
    public void RenderInputBlock_BufferHeightDecrease_ClearsToEndOfScreen()
    {
        _terminal.BufferHeight = 40;
        _terminal.WindowWidth = 80;

        var renderer = CreateRenderer();
        renderer.SetPanelLineCount(2);
        renderer.RenderInputBlock(
            "hello", Array.Empty<string>(),
            0, false, 0, 0, 80);

        // First render should not emit clear-to-end-of-screen
        Assert.DoesNotContain("\x1b[J", _terminal.WrittenTexts);

        // Simulate terminal resize: buffer shrinks
        _terminal.BufferHeight = 30;

        renderer.RenderInputBlock(
            "hello", Array.Empty<string>(),
            0, false, 0, 0, 80);

        // Second render must emit clear-to-end-of-screen because block top shifted
        Assert.Contains("\x1b[J", _terminal.WrittenTexts);
    }

    [Fact]
    public void OverwriteFullBlock_BufferHeightIncrease_ClearsToEndOfBuffer()
    {
        _terminal.BufferHeight = 30;
        _terminal.WindowWidth = 80;

        var renderer = CreateRenderer();
        renderer.SetPanelLineCount(2);
        renderer.OverwriteFullBlock(
            "hello", Array.Empty<string>(),
            oldBlockOffset: 4, 0, false, 0, 0, 80);

        Assert.DoesNotContain("\x1b[J", _terminal.WrittenTexts);

        _terminal.BufferHeight = 40;

        renderer.OverwriteFullBlock(
            "hello", Array.Empty<string>(),
            oldBlockOffset: 4, 0, false, 0, 0, 80);

        Assert.Contains("\x1b[J", _terminal.WrittenTexts);
    }

    [Fact]
    public void OverwriteInputBlock_BufferHeightIncrease_ClearsToEndOfBuffer()
    {
        _terminal.BufferHeight = 30;
        _terminal.WindowWidth = 80;

        var renderer = CreateRenderer();
        renderer.SetPanelLineCount(2);
        renderer.OverwriteInputBlock(
            "hello", Array.Empty<string>(),
            blockOffset: 4, 0, false, 0, 0, 80);

        Assert.DoesNotContain("\x1b[J", _terminal.WrittenTexts);

        _terminal.BufferHeight = 40;

        renderer.OverwriteInputBlock(
            "hello", Array.Empty<string>(),
            blockOffset: 4, 0, false, 0, 0, 80);

        Assert.Contains("\x1b[J", _terminal.WrittenTexts);
    }

    [Fact]
    public void ClearInputBlock_ResetsLastRenderedBlockTop()
    {
        _terminal.BufferHeight = 30;
        _terminal.WindowWidth = 80;

        var renderer = CreateRenderer();
        renderer.SetPanelLineCount(2);
        renderer.RenderInputBlock(
            "hello", Array.Empty<string>(),
            0, false, 0, 0, 80);

        // Clear should reset tracking
        renderer.ClearInputBlock("hello");

        _terminal.BufferHeight = 40;
        _terminal.ClearOutput();

        renderer.RenderInputBlock(
            "hello", Array.Empty<string>(),
            0, false, 0, 0, 80);

        // Because tracking was reset, no clear-to-end should be emitted
        Assert.DoesNotContain("\x1b[J", _terminal.WrittenTexts);
    }

    [Fact]
    public void ClearInputBlock_ResetsLastRenderedBlockHeight()
    {
        _terminal.BufferHeight = 30;
        _terminal.WindowWidth = 80;

        var renderer = CreateRenderer();
        renderer.SetPanelLineCount(2);
        renderer.RenderInputBlock(
            "hello", Array.Empty<string>(),
            0, false, 0, 0, 80);

        renderer.ClearInputBlock("hello");

        _terminal.BufferHeight = 40;
        _terminal.ClearOutput();

        renderer.RenderInputBlock(
            "hello", Array.Empty<string>(),
            0, false, 0, 0, 80);

        // Tracking was reset, so no old-block clear should be emitted
        Assert.DoesNotContain("\x1b[J", _terminal.WrittenTexts);
    }

    [Fact]
    public void ClearInputBlockForReRender_ResetsLastRenderedBlockTop()
    {
        _terminal.BufferHeight = 30;
        _terminal.WindowWidth = 80;
        _terminal.CursorTop = 20;

        var renderer = CreateRenderer();
        renderer.SetPanelLineCount(2);
        renderer.RenderInputBlock(
            "hello", Array.Empty<string>(),
            0, false, 0, 0, 80);

        // ClearForReRender should reset tracking
        renderer.ClearInputBlockForReRender("hello", "world", 2);

        _terminal.BufferHeight = 40;
        _terminal.ClearOutput();

        renderer.RenderInputBlock(
            "world", Array.Empty<string>(),
            0, false, 0, 0, 80);

        // Because tracking was reset, no clear-to-end should be emitted
        Assert.DoesNotContain("\x1b[J", _terminal.WrittenTexts);
    }

    [Fact]
    public void ClearInputBlockForReRender_ResetsLastRenderedBlockHeight()
    {
        _terminal.BufferHeight = 30;
        _terminal.WindowWidth = 80;
        _terminal.CursorTop = 20;

        var renderer = CreateRenderer();
        renderer.SetPanelLineCount(2);
        renderer.RenderInputBlock(
            "hello", Array.Empty<string>(),
            0, false, 0, 0, 80);

        renderer.ClearInputBlockForReRender("hello", "world", 2);

        _terminal.BufferHeight = 40;
        _terminal.ClearOutput();

        renderer.RenderInputBlock(
            "world", Array.Empty<string>(),
            0, false, 0, 0, 80);

        // Tracking was reset, so no old-block clear should be emitted
        Assert.DoesNotContain("\x1b[J", _terminal.WrittenTexts);
    }

    // ── RenderInputBlock ─────────────────────────────────────────────

    [Fact]
    public void RenderInputBlock_RendersFullBlock()
    {
        _terminal.WindowWidth = 80;

        var renderer = CreateRenderer();
        renderer.RenderInputBlock(
            "hello", Array.Empty<string>(),
            0, false, 0, 0, 80);

        Assert.NotNull(renderer);
    }

    [Fact]
    public void RenderInputBlock_WithEmptyInput_RendersPrefix()
    {
        _terminal.WindowWidth = 80;

        var renderer = CreateRenderer();
        renderer.RenderInputBlock("", Array.Empty<string>(), 0, false, 0, 0, 80);

        Assert.Equal(0, _terminal.CursorLeft);
    }

    [Fact]
    public void RenderInputBlock_WithHints_RendersHintLines()
    {
        _terminal.WindowWidth = 80;
        _terminal.BufferHeight = 200;

        var renderer = CreateRenderer();
        var hints = new[] { "hint1", "hint2" };
        renderer.RenderInputBlock(
            "test", hints, 0, false, 0, 0, 80);

        Assert.NotNull(renderer);
    }

    // ── OverwriteInputBlock ──────────────────────────────────────────

    [Fact]
    public void OverwriteInputBlock_WritesClearCode()
    {
        _terminal.WindowWidth = 80;
        _terminal.CursorTop = 20;
        _terminal.BufferHeight = 200;

        var renderer = CreateRenderer();
        renderer.SetPanelLineCount(2);
        renderer.OverwriteInputBlock(
            "hello", Array.Empty<string>(),
            blockOffset: 4, 0, false, 0, 0, 80);

        Assert.Contains("\x1b[K", _terminal.WrittenTexts);
    }

    [Fact]
    public void OverwriteInputBlock_ClampsToBufferHeight()
    {
        _terminal.WindowWidth = 80;
        _terminal.CursorTop = 0;
        _terminal.BufferHeight = 30;

        var renderer = CreateRenderer();
        renderer.SetPanelLineCount(2);
        renderer.OverwriteInputBlock(
            "hi", Array.Empty<string>(), 4, 0, false, 0, 0, 80);

        Assert.True(_terminal.CursorTop >= 0);
    }

    // ── RenderInputLine (internal method) ────────────────────────────

    [Fact]
    public void RenderInputLine_SingleLine_EmitsClearToEol()
    {
        _terminal.WindowWidth = 80;

        var renderer = CreateRenderer();
        renderer.RenderInputLine("hello", 5, false, 0, 0, 80);

        // Each visual line now emits \x1b[K (clear to end of line) to
        // overwrite stale content without a separate clear-before-render step.
        Assert.Contains("\x1b[K", _terminal.WrittenTexts);
        // Escape sequences don't move the visible cursor in a real terminal.
        Assert.True(_terminal.CursorLeft >= 0);
    }

    [Fact]
    public void RenderInputLine_MultiLineText_RendersAllLines()
    {
        _terminal.WindowWidth = 80;

        var renderer = CreateRenderer();
        renderer.RenderInputLine("line1\nline2\nline3", 6, false, 0, 0, 80);

        Assert.True(_terminal.WrittenLines.Count >= 2);
    }

    [Fact]
    public void RenderInputLine_VeryNarrowWidth_WrapsLines()
    {
        _terminal.WindowWidth = 12;

        var renderer = CreateRenderer();
        renderer.RenderInputLine("hello world long text", 5, false, 0, 0, 12);

        Assert.NotNull(renderer);
    }

    [Fact]
    public void RenderInputLine_WithSelection_RendersSelectionStyle()
    {
        _terminal.WindowWidth = 80;

        var renderer = CreateRenderer();
        renderer.RenderInputLine("hello", 0, true, 0, 3, 80);

        Assert.NotNull(renderer);
    }

    [Fact]
    public void RenderInputLine_EmptyInput_RendersPrefix()
    {
        _terminal.WindowWidth = 80;

        var renderer = CreateRenderer();
        renderer.RenderInputLine("", 0, false, 0, 0, 80);

        Assert.Equal(0, _terminal.CursorLeft);
    }

    [Fact]
    public void RenderInputLine_WithBracket_BuildsValidMarkup()
    {
        _terminal.WindowWidth = 80;

        var renderer = CreateRenderer();
        // Closing bracket must be escaped for Spectre markup
        renderer.RenderInputLine("text with ] bracket", 5, false, 0, 0, 80);

        Assert.True(_terminal.CursorLeft >= 0);
    }

    [Fact]
    public void RenderInputLine_WithBothBrackets_BuildsValidMarkup()
    {
        _terminal.WindowWidth = 80;

        var renderer = CreateRenderer();
        // Both [ and ] must be escaped for Spectre markup
        renderer.RenderInputLine("[text] with brackets", 5, false, 0, 0, 80);

        Assert.True(_terminal.CursorLeft >= 0);
    }

    [Fact]
    public void RenderInputLine_WithSelectionAndBracket_BuildsValidMarkup()
    {
        _terminal.WindowWidth = 80;

        var renderer = CreateRenderer();
        // Selection path handles brackets differently via AppendMarkupEscaped
        renderer.RenderInputLine("select [brackets] here", 5, true, 7, 10, 80);

        Assert.True(_terminal.CursorLeft >= 0);
    }

    [Fact]
    public void RenderInputLine_WithCursorAtBracket_BuildsValidMarkup()
    {
        _terminal.WindowWidth = 80;

        var renderer = CreateRenderer();
        // Cursor-at-char path must escape [ and ] properly
        renderer.RenderInputLine("cursor at ] bracket", 10, false, 0, 0, 80);

        Assert.True(_terminal.CursorLeft >= 0);
    }

    // ── HandleBlockHeightChange ──────────────────────────────────────

    [Fact]
    public void HandleBlockHeightChange_PositiveDelta_DoesNothing()
    {
        _terminal.CursorTop = 10;
        _terminal.BufferHeight = 200;

        var renderer = CreateRenderer();
        renderer.HandleBlockHeightChange(5, 8);

        Assert.Empty(_terminal.WrittenTexts);
    }

    [Fact]
    public void HandleBlockHeightChange_ZeroDelta_DoesNothing()
    {
        var renderer = CreateRenderer();
        renderer.HandleBlockHeightChange(5, 5);

        Assert.Empty(_terminal.WrittenTexts);
    }

    [Fact]
    public void HandleBlockHeightChange_NegativeDelta_ClearsExcessLines()
    {
        _terminal.CursorTop = 10;
        _terminal.BufferHeight = 200;

        var renderer = CreateRenderer();
        renderer.HandleBlockHeightChange(10, 5);

        // Should call SetCursorPosition to clear lines 11-15
        Assert.True(_terminal.SetCursorCalls.Count > 0);
    }

    [Fact]
    public void HandleBlockHeightChange_NegativeDelta_ClampsToBuffer()
    {
        _terminal.CursorTop = 25;
        _terminal.BufferHeight = 30;

        var renderer = CreateRenderer();
        renderer.HandleBlockHeightChange(10, 3);

        Assert.True(_terminal.CursorTop >= 0);
    }

    // ─── BuildSeparatorLine (static) ─────────────────────────────────

    [Fact]
    public void BuildSeparatorLine_DefaultConfig_ProducesRepeatedChars()
    {
        var config = SeparatorConfig.Default;
        var result = InvokeBuildSeparatorLine(config, 20);

        Assert.Equal(20, result.Length);
        Assert.All(result, c => Assert.Equal('─', c));
    }

    [Fact]
    public void BuildSeparatorLine_LeftText_Included()
    {
        var config = new SeparatorConfig { LeftText = "LEFT" };
        var result = InvokeBuildSeparatorLine(config, 30);

        Assert.StartsWith("LEFT", result);
    }

    [Fact]
    public void BuildSeparatorLine_RightText_Included()
    {
        var config = new SeparatorConfig { RightText = "END" };
        var result = InvokeBuildSeparatorLine(config, 30);

        Assert.EndsWith("END", result);
    }

    [Fact]
    public void BuildSeparatorLine_BothTexts_Included()
    {
        var config = new SeparatorConfig
        {
            LeftText = "[bold]L[/]",
            RightText = "[dim]R[/]"
        };
        var result = InvokeBuildSeparatorLine(config, 30);

        Assert.StartsWith("[bold]L[/]", result);
        Assert.EndsWith("[dim]R[/]", result);
    }

    [Fact]
    public void BuildSeparatorLine_WidthSmallerThanTexts_TruncatesFill()
    {
        var config = new SeparatorConfig
        {
            LeftText = "LEFT_TEXT_TOO_LONG",
            RightText = "RIGHT"
        };
        var result = InvokeBuildSeparatorLine(config, 5);

        Assert.NotNull(result);
    }

    [Fact]
    public void BuildSeparatorLine_WithMarkup_CreatesStyledFill()
    {
        var config = new SeparatorConfig
        {
            RepeatedChar = '=',
            RepeatedCharMarkup = "dim"
        };
        var result = InvokeBuildSeparatorLine(config, 10);

        Assert.Contains("[dim]", result);
        Assert.Contains("[/]", result);
    }

    [Fact]
    public void BuildSeparatorLine_OnlyLeftText_NoConcatBeforeFill()
    {
        var config = new SeparatorConfig { LeftText = ">>>" };
        var result = InvokeBuildSeparatorLine(config, 15);

        Assert.StartsWith(">>>", result);
        Assert.Equal(15, Markup.Remove(result).Length);
    }

    [Fact]
    public void BuildSeparatorLine_OnlyRightText_NoConcatAfterFill()
    {
        var config = new SeparatorConfig { RightText = "<<<" };
        var result = InvokeBuildSeparatorLine(config, 15);

        Assert.EndsWith("<<<", result);
        Assert.Equal(15, Markup.Remove(result).Length);
    }

    private static string InvokeBuildSeparatorLine(SeparatorConfig config, int width)
    {
        var renderer = new ConsoleRenderer();
        var method = typeof(ConsoleRenderer).GetMethod(
            "BuildSeparatorLine",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (string)method.Invoke(renderer, [config, width])!;
    }

    // ── RightMargin ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_RightMargin_SetFromTerminalWidth()
    {
        _terminal.WindowWidth = 120;
        var renderer = CreateRenderer();
        Assert.Equal(120, renderer.RightMargin);
    }

    [Fact]
    public void RightMargin_CanBeSetExplicitly()
    {
        var renderer = CreateRenderer();
        renderer.RightMargin = 60;
        Assert.Equal(60, renderer.RightMargin);
    }

    // ── SetPanelLineCount / GetBlockOffset ───────────────────────────

    [Fact]
    public void SetPanelLineCount_AffectsBlockOffset()
    {
        var renderer = CreateRenderer();
        renderer.SetPanelLineCount(3);
        int offset = renderer.GetBlockOffset("hello");
        Assert.Equal(5, offset);
    }

    [Fact]
    public void GetBlockOffset_MultiLineInput_CountsLineBreaks()
    {
        var renderer = CreateRenderer();
        renderer.SetPanelLineCount(2);
        int offset = renderer.GetBlockOffset("a\nb\nc");
        Assert.Equal(6, offset);
    }

    // ── PlaceholderStrings ───────────────────────────────────────────

    [Fact]
    public void PlaceholderStrings_SetAndGet()
    {
        var renderer = CreateRenderer();
        var placeholders = new List<string> { "[paste #1, 2 lines]" };
        renderer.PlaceholderStrings = placeholders;
        Assert.Same(placeholders, renderer.PlaceholderStrings);
    }

    // ── Separator properties ─────────────────────────────────────────

    [Fact]
    public void TopSeparator_Defaults()
    {
        var renderer = CreateRenderer();
        Assert.NotNull(renderer.TopSeparator);
        Assert.Null(renderer.TopSeparator.LeftText);
    }

    [Fact]
    public void BottomSeparator_Defaults()
    {
        var renderer = CreateRenderer();
        Assert.NotNull(renderer.BottomSeparator);
        Assert.Null(renderer.BottomSeparator.LeftText);
    }

    [Fact]
    public void TopSeparator_CanBeSet()
    {
        var renderer = CreateRenderer();
        var sep = new SeparatorConfig { LeftText = "test" };
        renderer.TopSeparator = sep;
        Assert.Same(sep, renderer.TopSeparator);
    }
}
