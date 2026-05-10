namespace StreamShell;

/// <summary>
/// Bottom panel that renders nothing — all lines are empty.
/// Used as the default panel when no command input is detected.
/// LineCount matches <see cref="CommandPalette.MaxHeight"/> to keep the
/// same vertical space allocation.
/// </summary>
public class EmptyBottomPanel : IBottomPanel
{
    private readonly int _lineCount;
    int IBottomPanel.LineCount => _lineCount;

    private readonly IReadOnlyList<string> _emptyLines;

    public EmptyBottomPanel(int? lineCount = null)
    {
        _lineCount = lineCount ?? CommandPalette.DefaultMaxHeight;
        var arr = new string[_lineCount];
        for (int i = 0; i < arr.Length; i++)
            arr[i] = string.Empty;
        _emptyLines = arr;
    }

    public IReadOnlyList<string> GetLines(string currentInput) => _emptyLines;

    public void Dispose() { }
}
