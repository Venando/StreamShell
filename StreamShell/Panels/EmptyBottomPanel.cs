namespace StreamShell;

/// <summary>
/// Bottom panel that renders nothing — all lines are empty.
/// Used as the default panel when no command input is detected.
/// LineCount matches <see cref="CommandPalette.MaxHeight"/> to keep the
/// same vertical space allocation.
/// </summary>
public class EmptyBottomPanel : IBottomPanel
{
    int IBottomPanel.LineCount => CommandPalette.MaxHeight;

    private static readonly IReadOnlyList<string> _emptyLines = BuildEmptyLines();

    private static IReadOnlyList<string> BuildEmptyLines()
    {
        var arr = new string[CommandPalette.MaxHeight];
        for (int i = 0; i < arr.Length; i++)
            arr[i] = string.Empty;
        return arr;
    }

    public IReadOnlyList<string> GetLines(string currentInput) => _emptyLines;

    /// <summary>Disposes the panel. No-op for EmptyBottomPanel.</summary>
    public void Dispose() { }
}
