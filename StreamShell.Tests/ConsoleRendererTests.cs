using StreamShell;

namespace StreamShell.Tests;

public class ConsoleRendererTests
{
    // ── WrapSegment (pure, no Console dependency) ─────────────────────

    public static IEnumerable<object[]> WrapSegment_ShortSingleSegment_Data()
    {
        // Short-circuit: isFirst && isLast && (segment.Length + 4 < width)
        yield return ["hello world!!", 20, true, true, true, new[] { "hello world!!" }];
        yield return ["hi", 20, true, true, true, new[] { "hi" }];
        yield return ["", 20, true, true, true, new[] { "" }];
        yield return ["abcde", 10, true, true, true, new[] { "abcde" }];
    }

    [Theory]
    [MemberData(nameof(WrapSegment_ShortSingleSegment_Data))]
    public void WrapSegment_ShortSingleSegment_ReturnsAsIs(
        string segment, int width,
        bool isFirst, bool isLast, bool isFirstLine,
        string[] expected)
    {
        var result = ConsoleRenderer.WrapSegment(segment, width, isFirst, isLast, isFirstLine);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void WrapSegment_NotShortCircuit_WhenBoundaryExact()
    {
        // segment.Length + 4 == width → NOT < → falls through to wrapping
        var result = ConsoleRenderer.WrapSegment("abcdef", 10, true, true, true);
        // cap = 10-4 = 6 → takes all 6 in first line
        Assert.Equal(new[] { "abcdef" }, result);
    }

    [Fact]
    public void WrapSegment_WrapsAtFixedWidthBoundary()
    {
        // width=20, cap=16. 37-char text → 3 lines of 16,16,5
        var result = ConsoleRenderer.WrapSegment("this is a very long string that wraps", 20,
            true, true, true);
        Assert.Equal(3, result.Count);
        Assert.Equal("this is a very l", result[0]);
        Assert.Equal("ong string that ", result[1]);
        Assert.Equal("wraps", result[2]);
    }

    [Fact]
    public void WrapSegment_ContinuationLine_SameCap()
    {
        // isFirstVisualLine=false → cap = Math.Max(1, width-4)
        var result = ConsoleRenderer.WrapSegment("abcdefghijklmnopqrstuvwxyz", 20,
            isFirstSegment: true, isLastSegment: true, isFirstVisualLine: false);
        Assert.Equal(2, result.Count);
        Assert.Equal("abcdefghijklmnop", result[0]);
        Assert.Equal("qrstuvwxyz", result[1]);
    }

    [Fact]
    public void WrapSegment_NonFirstSegment_ContinuationCap()
    {
        // isFirst=false → else branch → cap = width-4
        var result = ConsoleRenderer.WrapSegment("abcdefghijklmnopqrstuvwxyz", 20,
            isFirstSegment: false, isLastSegment: true, isFirstVisualLine: true);
        Assert.Equal(2, result.Count);
        Assert.Equal("abcdefghijklmnop", result[0]);
        Assert.Equal("qrstuvwxyz", result[1]);
    }

    [Fact]
    public void WrapSegment_ExactlyAtCap_ProducesSingleLine()
    {
        var result = ConsoleRenderer.WrapSegment("abcdefghijklmnop", 20, true, true, true);
        Assert.Equal(new[] { "abcdefghijklmnop" }, result);
    }

    [Fact]
    public void WrapSegment_NarrowWidth_FirstCapZeroThenOne()
    {
        // width=3 → first cap = Math.Max(0, -1) = 0 → empty string
        // then cap = Math.Max(1, -1) = 1 → one char per line
        var result = ConsoleRenderer.WrapSegment("hello", 3, true, true, true);
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
        // continuation cap = Math.Max(1, width-4)
        var result = ConsoleRenderer.WrapSegment("abcde", 4,
            isFirstSegment: true, isLastSegment: true, isFirstVisualLine: false);
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void WrapSegment_EmptySegment_ReturnsEmptyLine()
    {
        var result = ConsoleRenderer.WrapSegment("", 20, true, true, true);
        Assert.Equal(new[] { "" }, result);
    }

    [Fact]
    public void WrapSegment_SingleChar_Preserved()
    {
        var result = ConsoleRenderer.WrapSegment("x", 20, true, true, true);
        Assert.Equal(new[] { "x" }, result);
    }

    [Fact]
    public void WrapSegment_MultiSegment_NotLast_GoesThroughLoop()
    {
        // isLast=false prevents short-circuit even for short text
        var result = ConsoleRenderer.WrapSegment("hello", 20,
            isFirstSegment: true, isLastSegment: false, isFirstVisualLine: true);
        Assert.Equal(new[] { "hello" }, result);
    }

    [Fact]
    public void WrapSegment_MiddleSegment_Continuation()
    {
        // Not first, not last → cap = Math.Max(1, width-4)
        // "hello world!!!!!!!" = 18 chars, cap=16
        var result = ConsoleRenderer.WrapSegment("hello world!!!!!!!", 20,
            isFirstSegment: false, isLastSegment: false, isFirstVisualLine: false);
        Assert.Equal(2, result.Count);
        Assert.Equal("hello world!!!!!", result[0]);
        Assert.Equal("!!", result[1]);
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
