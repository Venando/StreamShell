namespace StreamShell;

internal class CommandPalette(List<Command> commands)
{
    public const int MaxHeight = 6;

    public static bool IsActive(string currentInput) => currentInput.StartsWith('/');

    public List<string> GetHints(string currentInput)
    {
        List<string> hints;

        if (IsActive(currentInput))
        {
            string query = currentInput.Length > 1 ? currentInput[1..] : string.Empty;

            hints = commands
                .Where(c => c.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .Select(c => $"  [grey]{c.Name,-10}[/] {c.Description}")
                .ToList();
        }
        else
        {
            hints = [];
        }

        while (hints.Count < MaxHeight)
            hints.Add(string.Empty);

        return hints;
    }
}
