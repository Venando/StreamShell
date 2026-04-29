namespace StreamShell;

using Spectre.Console;

internal class ConsoleRenderer
{
    public int GetBlockOffset(string input) => 7 + GetInputLineCount(input);

    public int GetInputLineCount(string input) => GetInputLines(input).Count;

    public void RenderMessage(string markup) => AnsiConsole.MarkupLine(markup);

    public void RenderSubmittedInput(string input) => AnsiConsole.MarkupLine($"[green]USER:[/] {input}");

    public void RenderSubmittedCommand(string input) => AnsiConsole.MarkupLine($"[blue]CMD:[/] {input}");

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

    public void RenderInputBlock(string input, List<string> hints)
    {
        RenderSeparatorLine();
        RenderInputLine(input);
        Console.WriteLine();

        if (HasHints(hints))
            RenderSeparatorLine();
        else
            Console.WriteLine();

        for (int i = 0; i < CommandPalette.MaxHeight; i++)
        {
            Console.CursorLeft = 0;
            ClearLine();
            if (!string.IsNullOrEmpty(hints[i]))
            {
                AnsiConsole.Markup(hints[i]);
            }
            if (i < CommandPalette.MaxHeight - 1)
                Console.WriteLine();
        }
    }

    public void OverwriteInputBlock(string input, List<string> hints, int blockOffset)
    {
        Console.CursorTop -= blockOffset - 1;

        RenderInputLine(input);
        Console.Write("\x1b[K");
        Console.WriteLine();

        if (HasHints(hints))
            RenderSeparatorLine();
        else
            Console.WriteLine();

        for (int i = 0; i < CommandPalette.MaxHeight; i++)
        {
            Console.CursorLeft = 0;
            ClearLine();
            if (!string.IsNullOrEmpty(hints[i]))
            {
                AnsiConsole.Markup(hints[i]);
            }
            if (i < CommandPalette.MaxHeight - 1)
                Console.WriteLine();
        }
    }

    private static bool HasHints(List<string> hints) => hints.Any(h => !string.IsNullOrEmpty(h));

    private static void RenderInputLine(string input)
    {
        var lines = GetInputLines(input);

        for (int i = 0; i < lines.Count; i++)
        {
            Console.CursorLeft = 0;
            if (lines.Count == 1)
            {
                AnsiConsole.Markup($"[blue]> [/] {lines[i]}[white]|[/]");
            }
            else if (i == 0)
            {
                AnsiConsole.Markup($"[blue]> [/] {lines[i]}");
            }
            else if (i == lines.Count - 1)
            {
                AnsiConsole.Markup($"{lines[i]}[white]|[/]");
            }
            else
            {
                Console.Write(lines[i]);
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
