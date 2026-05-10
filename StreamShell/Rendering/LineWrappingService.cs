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

        WrapSegment(segment, width, isFirstSegment, isLastSegment, isFirstVisualLine, lines, prefixMargin, rightMargin);
        return lines;
    }

    /// <summary>
    /// Wraps a single segment into visual lines, appending to a caller-provided
    /// <paramref name="lines"/> list. Avoids the per-call List&lt;string&gt; allocation
    /// on hot paths where the same list can be reused across segments.
    /// </summary>
    public static void WrapSegment(
        ReadOnlySpan<char> segment,
        int width,
        bool isFirstSegment,
        bool isLastSegment,
        bool isFirstVisualLine,
        List<string> lines,
        int prefixMargin = 2,
        int rightMargin = 4)
    {
        int totalMargin = prefixMargin + rightMargin;

        // Short single segment that fits on one line
        if (isFirstSegment && isLastSegment && segment.Length + totalMargin < width)
        {
            lines.Add(segment.ToString());
            return;
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
        int estimatedLines = EstimateVisualLineCount(input, margin, prefixMargin, rightMargin);
        var lines = new List<string>(estimatedLines);
        var offsets = new List<int>(estimatedLines);
        PopulateVisualLineData(input, margin, lines, offsets, prefixMargin, rightMargin);
        return (lines, offsets);
    }

    /// <summary>
    /// Populates pre-allocated lists with visual line data.
    /// Same logic as <see cref="GetVisualLineData"/> but reuses caller-provided lists
    /// to avoid allocation on hot paths.
    /// </summary>
    public static void PopulateVisualLineData(string input, int margin,
        List<string> lines, List<int> offsets,
        int prefixMargin = 2, int rightMargin = 4)
    {
        lines.Clear();
        offsets.Clear();

        if (input is null)
        {
            lines.Add("");
            offsets.Add(0);
            return;
        }

        int width = Math.Max(1, margin);

        if (string.IsNullOrEmpty(input))
        {
            lines.Add("");
            offsets.Add(0);
            return;
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
            int linesBefore = lines.Count;

            WrapSegment(segment, width, isFirstSegment, isLastSegment, !anyLinesProduced,
                lines, prefixMargin, rightMargin);

            for (int lineIdx = linesBefore; lineIdx < lines.Count; lineIdx++)
            {
                offsets.Add(charOffset);
                charOffset += lines[lineIdx].Length;
            }

            if (lines.Count > linesBefore)
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
    }

    /// <summary>Estimates how many visual lines an input would produce, for capacity pre-allocation.</summary>
    private static int EstimateVisualLineCount(string input, int margin,
        int prefixMargin, int rightMargin)
    {
        if (string.IsNullOrEmpty(input))
            return 1;
        int estimatedLineWidth = Math.Max(1, margin - prefixMargin - rightMargin);
        return Math.Max(1, input.Length / Math.Max(1, estimatedLineWidth - 4) + input.Length / 80 + 4);
    }

    /// <summary>
    /// Converts a character position in the raw input string to
    /// (visual line index, visual column) at the given <paramref name="margin"/>.
    /// Uses offset-only computation to avoid allocating a List&lt;string&gt; on the hot path.
    /// </summary>
    public static (int line, int column) GetCursorVisualPosition(
        string input, int cursorPosition, int margin,
        int prefixMargin = 2, int rightMargin = 4)
    {
        var offsets = GetVisualLineOffsets(input, margin, prefixMargin, rightMargin);
        int accumulated = 0;

        for (int i = 0; i < offsets.Count; i++)
        {
            int lineLen = offsets[i];
            if (accumulated + lineLen > cursorPosition)
                return (i, cursorPosition - accumulated);
            accumulated += lineLen;
        }

        if (offsets.Count == 0)
            return (0, 0);

        return (offsets.Count - 1, offsets[^1]);
    }

    /// <summary>
    /// Returns the length of each visual line (in characters) for the input at the given margin.
    /// Allocation-free: computes offsets without creating string objects.
    /// </summary>
    internal static List<int> GetVisualLineOffsets(string input, int margin,
        int prefixMargin = 2, int rightMargin = 4)
    {
        var offsets = new List<int>();
        if (string.IsNullOrEmpty(input))
        {
            offsets.Add(0);
            return offsets;
        }

        int width = Math.Max(1, margin);
        int totalMargin = prefixMargin + rightMargin;
        ReadOnlySpan<char> span = input.AsSpan();
        bool anyLinesProduced = false;
        int segStart = 0;
        int segIdx = 0;

        while (segStart <= span.Length)
        {
            int nlPos = segStart < span.Length ? span[segStart..].IndexOf('\n') : -1;
            int segEnd = nlPos >= 0 ? segStart + nlPos : span.Length;
            ReadOnlySpan<char> segment = span[segStart..segEnd];
            bool isFirstSegment = segIdx == 0;
            bool isLastSegment = segEnd == span.Length;

            // Same logic as WrapSegment but only counts lengths
            if (isFirstSegment && isLastSegment && segment.Length + totalMargin < width)
            {
                offsets.Add(segment.Length);
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
                    offsets.Add(take);
                    remaining -= take;
                    lineIdx++;
                }
                if (segment.Length == 0)
                    offsets.Add(0);
            }

            if (offsets.Count > 0)
                anyLinesProduced = true;

            segStart = segEnd + 1;
            segIdx++;

            if (segEnd >= span.Length)
                break;
        }

        if (offsets.Count == 0)
            offsets.Add(0);

        return offsets;
    }
}
