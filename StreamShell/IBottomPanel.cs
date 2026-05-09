namespace StreamShell;

/// <summary>
/// A bottom panel renders additional lines below the user input (hints, status, etc.).
/// Swappable — different implementations can show different content at the bottom.
/// </summary>
public interface IBottomPanel
{
    /// <summary>Number of lines this panel renders. Determines vertical space at the bottom.</summary>
    int LineCount { get; }

    /// <summary>
    /// Returns the panel lines to render for the current user input.
    /// Array must be exactly <see cref="LineCount"/> in length.
    /// Empty strings produce blank lines; markup strings are rendered with Spectre.Console.
    /// </summary>
    IReadOnlyList<string> GetHints(string currentInput);

    /// <summary>
    /// Returns the autocomplete suggestion to use on Tab, or null if no completion is possible.
    /// </summary>
    string? GetTopSuggestion(string currentInput);
}
