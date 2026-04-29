namespace StreamShell;

internal class UserInputHandler
{
    public string CurrentInput { get; private set; } = string.Empty;

    public string? ProcessInput()
    {
        string? submitted = null;

        while (Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                {
                    CurrentInput += '\n';
                    continue;
                }

                if (Console.KeyAvailable)
                {
                    CurrentInput += '\n';
                    continue;
                }

                submitted = CurrentInput;
                CurrentInput = string.Empty;
                break;
            }

            if (key.Key == ConsoleKey.V && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (ClipboardHelper.GetText() is { } text)
                {
                    CurrentInput += text.ReplaceLineEndings("\n");
                }
                continue;
            }

            if (key.Key == ConsoleKey.Escape)
            {
                CurrentInput = string.Empty;
            }
            else if (key.Key == ConsoleKey.Backspace && CurrentInput.Length > 0)
            {
                CurrentInput = CurrentInput[..^1];
            }
            else if (!char.IsControl(key.KeyChar))
            {
                CurrentInput += key.KeyChar;
            }
        }

        return submitted;
    }
}
