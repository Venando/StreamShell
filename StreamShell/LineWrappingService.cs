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
    /// </summary>
    public static List<string> WrapSegment(
        string segment,
        int width,
        bool isFirstSegment,
        bool isLastSegment,
        bool isFirstVisualLine,
        int prefixMargin = 2,
        int rightMargin = 4)
    {
        var lines = new List<string>();
        int totalMargin = prefixMargin + rightMargin;

        // Short single segment that fits on one line
        if (isFirstSegment && isLastSegment && segment.Length + totalMargin < width)
        {
            lines.Add(segment);
            return lines;
        }

        int remaining = segment.Length;
        int pos = 0;

        while (remaining > 0)
        {
            // First visual line has "> " prefix (2 chars), continuation gets "  " (2 chars)
            // Both use cap = width - 4 for consistent right margin
            int cap = Math.Max(isFirstSegment && isFirstVisualLine && lines.Count == 0 ? 0 : 1, width - totalMargin);
            int take = Math.Min(remaining, cap);
            lines.Add(segment.Substring(pos, take));
            pos += take;
            remaining -= take;
        }

        // Empty segment produces an empty visual line so the cursor
        // after a newline has somewhere to render (e.g. Shift+Enter).
        if (segment.Length == 0)
            lines.Add("");

        return lines;
    }

    /// <summary>Number of visual lines the input occupies at the given margin.</summary>
    public static int GetInputLineCount(string input, int margin,
        int prefixMargin = 2, int rightMargin = 4)
    {
        return GetInputLines(input, margin, prefixMargin, rightMargin).Count;
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
        int width = Math.Max(1, margin);
        var lines = new List<string>();
        var offsets = new List<int>();

        if (string.IsNullOrEmpty(input))
        {
            lines.Add("");
            offsets.Add(0);
            return (lines, offsets);
        }

        var segments = input.Split('\n');
        bool anyLinesProduced = false;
        int charOffset = 0;

        for (int segIdx = 0; segIdx < segments.Length; segIdx++)
        {
            string segment = segments[segIdx];
            bool isFirstSegment = segIdx == 0;
            bool isLastSegment = segIdx == segments.Length - 1;

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
