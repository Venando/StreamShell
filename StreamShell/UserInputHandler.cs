using System.Text;

namespace StreamShell;

// TODO: Add meta wrapper around Attachment that would hold information
// about its position in text and would be used to remove attachment
// if it's been edited.

internal class UserInputHandler
{
    private readonly StringBuilder _currentInput = new();
    private readonly StringBuilder _tempInput = new();
    public string CurrentInput => _currentInput.ToString();
    public List<Attachment> Attachments { get; private set; } = new ();
    public int LargePasteThreshold { get; internal set; } = 100;
    public int LargePasteLineThreshold { get; internal set; } = 4;

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
                    _tempInput.Append('\n');
                    continue;
                }

                if (Console.KeyAvailable)
                {
                    _tempInput.Append('\n');
                    continue;
                }

                if (_tempInput.Length == 0)
                {
                    submitted = _currentInput.ToString();
                    _currentInput.Clear();
                    break;
                }
            }

            if (key.Key == ConsoleKey.Escape)
            {
                _currentInput.Clear();
                Attachments.Clear();
                _tempInput.Clear();
            }
            else if (key.Key == ConsoleKey.Backspace && _currentInput.Length > 0)
            {
                _currentInput.Length -= 1;
            }
            else if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
            {
                _tempInput.Append(key.KeyChar);
            }
        }

        if (_tempInput.Length > 0)
        {
            string temp = _tempInput.ToString();
            AppendOrAttach(temp);
            _tempInput.Clear();
        }

        return submitted;
    }

    private void AppendOrAttach(string text)
    {
        int lineCount = text.Split('\n').Length;

        if (text.Length > LargePasteThreshold || lineCount > LargePasteLineThreshold)
        {
            string name = GenerateName(text);
            Attachments.Add(new Attachment(text, AttachmentType.PlainText, lineCount));
            _currentInput.Append($"[paste {lineCount} lines: {name}]");
        }
        else
        {
            _currentInput.Append(text);
        }
    }
    

    public void Reset()
    {
        _currentInput.Clear();
        Attachments.Clear();
    }

    private static string GenerateName(string content)
    {
        int newlineIndex = content.IndexOf('\n');
        string firstLine = newlineIndex > 0 ? content[..newlineIndex] : content;
        string trimmed = firstLine.TrimEnd();
        string result = trimmed.Length > 15 ? trimmed[..15] : trimmed;
        return result + "...";
    }
}

