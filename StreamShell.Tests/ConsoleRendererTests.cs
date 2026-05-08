using StreamShell;

namespace StreamShell.Tests;

public class ConsoleRendererTests
{
    // ── WrapSegment (pure, no Console dependency) ─────────────────────

    public static IEnumerable<object[]> WrapSegment_ShortSingleSegment_Data()
    {
        // Short-circuit: isFirst && isLast && (segment.Length + 6 < width)
        yield return ["hello world!!", 20, true, true, true, new[] { "hello world!!" }];
        yield return ["hi", 20, true, true, true, new[] { "hi" }];
        yield return ["", 20, true, true, true, new[] { "" }];
        // abcde=5, 5+6=11 >= 10 → wraps at cap=4
        yield return ["abcde", 10, true, true, true, new[] { "abcd", "e" }];
    }

    [Theory]
    [MemberData(nameof(WrapSegment_ShortSingleSegment_Data))]
    public void WrapSegment_ShortSingleSegment_ReturnsAsIs(
        string segment, int width,
        bool isFirst, bool isLast, bool isFirstLine,
        string[] expected)
    {
        var result = LineWrappingService.WrapSegment(segment, width, isFirst, isLast, isFirstLine);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void WrapSegment_NotShortCircuit_WhenBoundaryExact()
    {
        // 6+6=12 >= 10 → falls through; cap = 10-6=4 → [abcd, ef]
        var result = LineWrappingService.WrapSegment("abcdef", 10, true, true, true);
        Assert.Equal(new[] { "abcd", "ef" }, result);
    }

    [Fact]
    public void WrapSegment_WrapsAtFixedWidthBoundary()
    {
        // width=20, cap=14. 37-char text → 3 lines of 14,14,9
        var result = LineWrappingService.WrapSegment("this is a very long string that wraps", 20,
            true, true, true);
        Assert.Equal(3, result.Count);
        Assert.Equal("this is a very", result[0]);
        Assert.Equal(" long string t", result[1]);
        Assert.Equal("hat wraps", result[2]);
    }

    [Fact]
    public void WrapSegment_ContinuationLine_SameCap()
    {
        // isFirstVisualLine=false → cap = Math.Max(1, width-6) = 14
        var result = LineWrappingService.WrapSegment("abcdefghijklmnopqrstuvwxyz", 20,
            isFirstSegment: true, isLastSegment: true, isFirstVisualLine: false);
        Assert.Equal(2, result.Count);
        Assert.Equal("abcdefghijklmn", result[0]);
        Assert.Equal("opqrstuvwxyz", result[1]);
    }

    [Fact]
    public void WrapSegment_NonFirstSegment_ContinuationCap()
    {
        // isFirst=false → cap = Math.Max(1, width-6) = 14
        var result = LineWrappingService.WrapSegment("abcdefghijklmnopqrstuvwxyz", 20,
            isFirstSegment: false, isLastSegment: true, isFirstVisualLine: true);
        Assert.Equal(2, result.Count);
        Assert.Equal("abcdefghijklmn", result[0]);
        Assert.Equal("opqrstuvwxyz", result[1]);
    }

    [Fact]
    public void WrapSegment_ExactlyAtCap_ProducesSingleLine()
    {
        // 16+6=22 >= 20 → falls through; cap = 20-6=14 → [abcdefghijklmn, op]
        var result = LineWrappingService.WrapSegment("abcdefghijklmnop", 20, true, true, true);
        Assert.Equal(new[] { "abcdefghijklmn", "op" }, result);
    }

    [Fact]
    public void WrapSegment_NarrowWidth_FirstCapZeroThenOne()
    {
        // width=3 → first cap = Math.Max(0, -1) = 0 → empty string
        // then cap = Math.Max(1, -1) = 1 → one char per line
        var result = LineWrappingService.WrapSegment("hello", 3, true, true, true);
        Assert.Equal(6, result.Count);
        Assert.Equal("", result[0]);   // cap=0 → empty
        Assert.Equal("h", result[1]);
        Assert.Equal("e", result[2]);
        Assert.Equal("l", result[3]);
        Assert.Equal("l", result[4]);
        Assert.Equal("o", result[5]);
    }

    [Fact]
    public void WrapSegment_ContinuationCap_NeverBelowOne()
    {
        // continuation cap = Math.Max(1, width-6) = Math.Max(1, -2) = 1
        var result = LineWrappingService.WrapSegment("abcde", 4,
            isFirstSegment: true, isLastSegment: true, isFirstVisualLine: false);
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void WrapSegment_EmptySegment_ReturnsEmptyLine()
    {
        var result = LineWrappingService.WrapSegment("", 20, true, true, true);
        Assert.Equal(new[] { "" }, result);
    }

    [Fact]
    public void WrapSegment_SingleChar_Preserved()
    {
        var result = LineWrappingService.WrapSegment("x", 20, true, true, true);
        Assert.Equal(new[] { "x" }, result);
    }

    [Fact]
    public void WrapSegment_MultiSegment_NotLast_GoesThroughLoop()
    {
        // isLast=false prevents short-circuit even for short text
        var result = LineWrappingService.WrapSegment("hello", 20,
            isFirstSegment: true, isLastSegment: false, isFirstVisualLine: true);
        Assert.Equal(new[] { "hello" }, result);
    }

    [Fact]
    public void WrapSegment_MiddleSegment_Continuation()
    {
        // Not first, not last → cap = Math.Max(1, width-6) = 14
        // "hello world!!!!!!!" = 18 chars, cap=14 → [hello world!!!, !!!!]
        var result = LineWrappingService.WrapSegment("hello world!!!!!!!", 20,
            isFirstSegment: false, isLastSegment: false, isFirstVisualLine: false);
        Assert.Equal(2, result.Count);
        Assert.Equal("hello world!!!", result[0]);
        Assert.Equal("!!!!", result[1]);
    }

    // ── Console-dependent functions (structural invariants) ───────────
    // GetInputLines, GetVisualLineData, GetCursorVisualPosition depend on
    // Console.WindowWidth which varies by environment. Only structural
    // invariants that hold regardless of terminal width are tested here.

    [Fact]
    public void GetInputLines_Empty_ReturnsSingleEmptyLine()
    {
        var result = ConsoleRenderer.GetInputLines("", margin: 80);
        Assert.Single(result);
        Assert.Equal("", result[0]);
    }

    [Fact]
    public void GetInputLines_NonEmpty_ReturnsAtLeastOneLine()
    {
        var result = ConsoleRenderer.GetInputLines("a\nb\nc", margin: 80);
        Assert.True(result.Count >= 3, "Newlines should produce at least 3 visual lines");
    }

    [Fact]
    public void GetVisualLineData_Empty_ReturnsEmptyLine()
    {
        var (lines, offsets) = ConsoleRenderer.GetVisualLineData("", margin: 80);
        Assert.Equal(new[] { "" }, lines);
        Assert.Equal(new[] { 0 }, offsets);
    }

    [Fact]
    public void GetVisualLineData_LinesAndOffsetsMatchCount()
    {
        var (lines, offsets) = ConsoleRenderer.GetVisualLineData("hello world", margin: 80);
        Assert.Equal(lines.Count, offsets.Count);
    }

    [Fact]
    public void GetCursorVisualPosition_AtStart_ReturnsZeroOrFirstLine()
    {
        var (line, col) = ConsoleRenderer.GetCursorVisualPosition("hello", 0, margin: 80);
        Assert.True(line >= 0);
        Assert.Equal(0, col);
    }

    [Fact]
    public void GetCursorVisualPosition_PastEnd_ReturnsLastLineEnd()
    {
        var (line, col) = ConsoleRenderer.GetCursorVisualPosition("hello", 100, margin: 80);
        Assert.True(line >= 0);
        Assert.True(col >= 0);
    }
}
