namespace StreamShell;

internal class CommandPalette(List<Command> commands)
{
    public const int MaxHeight = 6;

    public static bool IsActive(string currentInput) => currentInput.StartsWith('/');

    private static readonly string[] _emptyHints = new string[MaxHeight];
    private static readonly IReadOnlyList<string> _cachedEmptyHints = _emptyHints;

    private string? _lastInput;
    private IReadOnlyList<string>? _lastHints;

    public IReadOnlyList<string> GetHints(string currentInput)
    {
        if (_lastInput == currentInput)
            return _lastHints!;

        if (IsActive(currentInput))
        {
            string query = currentInput.Length > 1 ? currentInput[1..] : string.Empty;
            int spaceIndex = query.IndexOf(' ');
            string prefix = spaceIndex > 0 ? query[..spaceIndex] : query;

            List<string> hints = new(MaxHeight);
            foreach (var cmd in commands)
            {
                if (cmd.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    hints.Add($"  [grey]{cmd.Name,-10}[/] {cmd.Description}");
                }
            }

            int emptyHintsToAdd = MaxHeight - hints.Count;
            
            if (emptyHintsToAdd > 0)
                hints.AddRange(_emptyHints.AsSpan(0, emptyHintsToAdd));

            _lastInput = currentInput;
            _lastHints = hints;
            return hints;
        }

        _lastInput = currentInput;
        _lastHints = _cachedEmptyHints;
        return _cachedEmptyHints;
    }
}
