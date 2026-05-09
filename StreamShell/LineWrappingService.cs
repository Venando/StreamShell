namespace StreamShell;

/// <summary>
/// Computes visual line geometry for text displayed in the input block.
/// Separate from ConsoleRenderer (which owns rendering) so that input
/// handling (UserInputHandler) can query line positions without depending
/// on the renderer.
/// </summary>
public static class LineWrappingService
{
    /// <summary>
    /// Wraps a single segment (no newline characters) into visual lines
    /// at the given terminal <paramref name="width"/>.
    /// Accepts a <see cref="ReadOnlySpan{T}"/> to avoid intermediate string
    /// allocations when called from <see cref="GetVisualLineData"/>.
    /// </summary>
    public static List<string> WrapSegment(
        ReadOnlySpan<char> segment,
        int width,
        bool isFirstSegment,
        bool isLastSegment,
        bool isFirstVisualLine,
        int prefixMargin = 2,
        int rightMargin = 4)
    {
        int totalMargin = prefixMargin + rightMargin;

        // Estimate capacity: at most ceil(segment.Length / (width - totalMargin)) + 1
        int cap = Math.Max(1, Math.Min(segment.Length, width - totalMargin));
        int estimatedLines = segment.Length == 0 ? 1 : (segment.Length + cap - 1) / cap;
        var lines = new List<string>(estimatedLines);

        // Short single segment that fits on one line
        if (isFirstSegment && isLastSegment && segment.Length + totalMargin < width)
        {
            lines.Add(segment.ToString());
            return lines;
        }

        int remaining = segment.Length;
        int pos = 0;

        while (remaining > 0)
        {
            // First visual line has "> " prefix (2 chars), continuation gets "  " (2 chars)
            // Both use cap = width - 4 for consistent right margin
            int takeCap = Math.Max(isFirstSegment && isFirstVisualLine && lines.Count == 0 ? 0 : 1, width - totalMargin);
            int take = Math.Min(remaining, takeCap);
            lines.Add(segment.Slice(pos, take).ToString());
            pos += take;
            remaining -= take;
        }

        // Empty segment produces an empty visual line so the cursor
        // after a newline has somewhere to render (e.g. Shift+Enter).
        if (segment.Length == 0)
            lines.Add(string.Empty);

        return lines;
    }

    /// <summary>
    /// Counts visual lines the input occupies without allocating any strings or lists.
    /// Called on every render tick — must be allocation-free.
    /// </summary>
    public static int GetInputLineCount(string input, int margin,
        int prefixMargin = 2, int rightMargin = 4)
    {
        if (string.IsNullOrEmpty(input))
            return 1;

        int width = Math.Max(1, margin);
        int totalMargin = prefixMargin + rightMargin;

        ReadOnlySpan<char> span = input.AsSpan();
        int count = 0;
        bool anyLinesProduced = false;
        int segStart = 0;
        int segIdx = 0;

        while (segStart <= span.Length)
        {
            int nlPos = segStart < span.Length ? span[segStart..].IndexOf('\n') : -1;
            int segEnd = nlPos >= 0 ? segStart + nlPos : span.Length;
            ReadOnlySpan<char> segment = span[segStart..segEnd];
            bool isFirstSegment = segIdx == 0;

            // Count wrapped lines for this segment (mirrors WrapSegment logic)
            if (isFirstSegment && segEnd == span.Length && segment.Length + totalMargin < width)
            {
                count++;
            }
            else
            {
                int remaining = segment.Length;
                int lineIdx = 0;
                while (remaining > 0)
                {
                    int cap = Math.Max(isFirstSegment && !anyLinesProduced && lineIdx == 0 ? 0 : 1,
                        width - totalMargin);
                    int take = Math.Min(remaining, cap);
                    remaining -= take;
                    count++;
                    lineIdx++;
                }
                if (segment.Length == 0)
                    count++;
            }

            if (count > 0)
                anyLinesProduced = true;

            segStart = segEnd + 1;
            segIdx++;

            if (segEnd >= span.Length)
                break;
        }

        return count > 0 ? count : 1;
    }

    /// <summary>Returns the wrapped visual lines for an input string at the given margin.</summary>
    public static List<string> GetInputLines(string input, int margin,
        int prefixMargin = 2, int rightMargin = 4)
    {
        var (lines, _) = GetVisualLineData(input, margin, prefixMargin, rightMargin);
        return lines;
    }

    /// <summary>
    /// Gets both visual line text and the character offset of each line
    /// in the raw input. Offsets account for newline characters between segments.
    /// Needed for up/down cursor navigation.
    /// </summary>
    public static (List<string> lines, List<int> offsets) GetVisualLineData(string input, int margin,
        int prefixMargin = 2, int rightMargin = 4)
    {
        if (input is null)
        {
            var emptyLines = new List<string> { "" };
            var emptyOffsets = new List<int> { 0 };
            return (emptyLines, emptyOffsets);
        }

        int width = Math.Max(1, margin);
        // Estimate: roughly input.Length / (width - margin) newlines + some for hard breaks
        int estimatedLines = Math.Max(1, input.Length / Math.Max(1, width - prefixMargin - rightMargin - 4) + 4);
        var lines = new List<string>(estimatedLines);
        var offsets = new List<int>(estimatedLines);

        if (string.IsNullOrEmpty(input))
        {
            lines.Add("");
            offsets.Add(0);
            return (lines, offsets);
        }

        // Manual iteration over newline-delimited segments using ReadOnlySpan<char>
        // to avoid the string[] and per-segment string allocations from Split('\n').
        ReadOnlySpan<char> span = input.AsSpan();
        bool anyLinesProduced = false;
        int charOffset = 0;
        int segStart = 0;
        int segIdx = 0;

        while (segStart <= span.Length)
        {
            // Find the next newline (or end of span)
            int nlPos = segStart < span.Length ? span[segStart..].IndexOf('\n') : -1;
            int segEnd = nlPos >= 0 ? segStart + nlPos : span.Length;
            ReadOnlySpan<char> segment = span[segStart..segEnd];
            bool isFirstSegment = segIdx == 0;
            bool isLastSegment = segEnd == span.Length;

            var wrapped = WrapSegment(segment, width, isFirstSegment, isLastSegment, !anyLinesProduced,
                prefixMargin, rightMargin);

            for (int lineIdx = 0; lineIdx < wrapped.Count; lineIdx++)
            {
                offsets.Add(charOffset);
                lines.Add(wrapped[lineIdx]);
                charOffset += wrapped[lineIdx].Length;
            }

            if (wrapped.Count > 0)
                anyLinesProduced = true;

            charOffset++; // Account for \n between segments
            segStart = segEnd + 1;
            segIdx++;

            if (segEnd >= span.Length)
                break;
        }

        if (lines.Count == 0)
        {
            lines.Add("");
            offsets.Add(0);
        }

        return (lines, offsets);
    }

    /// <summary>
    /// Converts a character position in the raw input string to
    /// (visual line index, visual column) at the given <paramref name="margin"/>.
    /// </summary>
    public static (int line, int column) GetCursorVisualPosition(
        string input, int cursorPosition, int margin,
        int prefixMargin = 2, int rightMargin = 4)
    {
        var visualLines = GetInputLines(input, margin, prefixMargin, rightMargin);
        int accumulated = 0;

        for (int i = 0; i < visualLines.Count; i++)
        {
            int lineLen = visualLines[i].Length;
            if (accumulated + lineLen > cursorPosition)
                return (i, cursorPosition - accumulated);
            accumulated += lineLen;
        }

        if (visualLines.Count == 0)
            return (0, 0);

        return (visualLines.Count - 1, visualLines[^1].Length);
    }
}
