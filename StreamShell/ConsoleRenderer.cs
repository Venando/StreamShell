namespace StreamShell;

using Spectre.Console;

internal class ConsoleRenderer
{
    public int GetBlockOffset(string input) => 7 + GetInputLineCount(input);

    public int GetInputLineCount(string input) => GetInputLines(input).Count;

    public static void RenderMessage(string markup)
    {
        try
        {
            AnsiConsole.MarkupLine(markup);
        }
        catch (InvalidOperationException)
        {
            AnsiConsole.MarkupLine(Markup.Escape(markup));
        }
    }

    public void ClearInputLine()
    {
        Console.CursorLeft = 0;
        ClearLine();
    }

    public void ClearInputBlock(string? lastInput)
    {
        if (lastInput is null)
            return;

        int blockOffset = GetBlockOffset(lastInput);
        Console.CursorTop -= blockOffset;
        ClearBlock(blockOffset);
    }

    public static void RenderInputBlock(string input, IReadOnlyList<string> hints)
    {
        RenderSeparatorLine();
        RenderInputLine(input);
        Console.WriteLine();

        RenderHintsBlock(hints);
    }

    public static void OverwriteInputBlock(string input, IReadOnlyList<string> hints, int blockOffset)
    {
        Console.CursorTop -= blockOffset - 1;

        RenderInputLine(input);
        Console.Write("\x1b[K");
        Console.WriteLine();

        RenderHintsBlock(hints);
    }

    private static void RenderHintsBlock(IReadOnlyList<string> hints)
    {
        if (HasHints(hints))
            RenderSeparatorLine();
        else
            Console.WriteLine();

        int maxWidth = Console.WindowWidth - 1;
        for (int i = 0; i < CommandPalette.MaxHeight; i++)
        {
            Console.CursorLeft = 0;
            ClearLine();
            string hint = hints[i];
            if (!string.IsNullOrEmpty(hint))
            {
                string safeHint = TruncateToVisualWidth(hint, maxWidth);

                try
                {
                    AnsiConsole.Markup(safeHint);
                }
                catch (InvalidOperationException)
                {
                    AnsiConsole.Markup(Markup.Escape(safeHint));
                }
            }
            if (i < CommandPalette.MaxHeight - 1)
                Console.WriteLine();
        }
    }

    private static string TruncateToVisualWidth(string text, int maxWidth)
    {
        int visualWidth = 0;
        int i = 0;

        while (i < text.Length)
        {
            if (text[i] == '[')
            {
                int close = text.IndexOf(']', i);
                if (close > i)
                {
                    i = close + 1;
                    continue;
                }
            }

            visualWidth++;
            if (visualWidth > maxWidth)
                return text[..i];

            i++;
        }

        return text;
    }

    private static bool HasHints(IReadOnlyList<string> hints) => hints.Any(h => !string.IsNullOrEmpty(h));

    private static void RenderInputLine(string input)
    {
        var lines = GetInputLines(input);

        for (int i = 0; i < lines.Count; i++)
        {
            Console.CursorLeft = 0;
            
            var escapedLine = Markup.Escape(lines[i]);

            if (lines.Count == 1)
            {
                AnsiConsole.Markup($"[blue]> [/] {escapedLine}[white]|[/]");
            }
            else if (i == 0)
            {
                AnsiConsole.Markup($"[blue]> [/] {escapedLine}");
            }
            else if (i == lines.Count - 1)
            {
                AnsiConsole.Markup($"{escapedLine}[white]|[/]");
            }
            else
            {
                Console.Write(escapedLine);
            }

            if (i < lines.Count - 1)
                Console.WriteLine();
        }
    }

    private static List<string> GetInputLines(string input)
    {
        int width = Math.Max(1, Console.WindowWidth);
        var lines = new List<string>();

        if (string.IsNullOrEmpty(input))
            return new List<string> { "" };

        var segments = input.Split('\n');

        for (int segIndex = 0; segIndex < segments.Length; segIndex++)
        {
            string segment = segments[segIndex];
            bool isFirstSegment = segIndex == 0;
            bool isLastSegment = segIndex == segments.Length - 1;
            bool singleSegment = segments.Length == 1;

            if (singleSegment && segment.Length + 4 < width)
            {
                lines.Add(segment);
                continue;
            }

            int remaining = segment.Length;
            int pos = 0;

            while (remaining > 0)
            {
                int cap;
                if (isFirstSegment && lines.Count == 0)
                {
                    cap = Math.Max(0, width - 4);
                }
                else if (isLastSegment && remaining <= width - 2)
                {
                    cap = width - 2;
                    if (remaining == width - 1)
                        cap = width - 2;
                }
                else
                {
                    cap = width - 1;
                }

                int take = Math.Min(remaining, cap);
                lines.Add(segment.Substring(pos, take));
                pos += take;
                remaining -= take;
            }

            if (segment.Length == 0 && (!isLastSegment || segments.Length > 1))
            {
                lines.Add("");
            }
        }

        return lines;
    }

    private static void RenderSeparatorLine()
    {
        Console.WriteLine(new string('─', Console.WindowWidth - 1));
    }

    private static void ClearLine()
    {
        Console.Write("\x1b[K");
    }

    private static void ClearBlock(int linesBelowSeparator)
    {
        int startTop = Console.CursorTop;
        for (int i = 0; i <= linesBelowSeparator; i++)
        {
            Console.SetCursorPosition(0, startTop + i);
            ClearLine();
        }
        Console.SetCursorPosition(0, startTop);
    }
}
