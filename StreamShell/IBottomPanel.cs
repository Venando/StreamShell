namespace StreamShell;

/// <summary>
/// Result from a single <see cref="IBottomPanel.GetResult"/> call.
/// Contains both the panel lines to render and the Tab autocomplete suggestion,
/// ensuring they are always in sync.
/// </summary>
public record PanelResult(
    IReadOnlyList<string> Hints,
    string? TopSuggestion
);

/// <summary>
/// A bottom panel renders additional lines below the user input (hints, status, etc.).
/// Swappable — different implementations can show different content at the bottom.
/// </summary>
public interface IBottomPanel
{
    /// <summary>Number of lines this panel renders. Determines vertical space at the bottom.</summary>
    int LineCount { get; }

    /// <summary>
    /// Returns both the panel lines and the Tab completion for the current input.
    /// Computed in one call so display and autocomplete stay in sync.
    /// Hints must be exactly <see cref="LineCount"/> in length.
    /// </summary>
    PanelResult GetResult(string currentInput);
}
