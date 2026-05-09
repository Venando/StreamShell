namespace StreamShell;

/// <summary>
/// A bottom panel renders additional lines below the user input (hints, status, etc.).
/// Panels can optionally run a background loop via <see cref="RunAsync"/>.
/// </summary>
public interface IBottomPanel
{
    /// <summary>Number of lines this panel returns. Determines vertical space at the bottom.</summary>
    int LineCount { get; }

    /// <summary>
    /// Returns all panel lines for the current input.
    /// First line (index 0) is the Tab autocomplete suggestion (empty string = no suggestion).
    /// All lines are rendered in order by the renderer.
    /// Must return exactly <see cref="LineCount"/> strings.
    /// </summary>
    IReadOnlyList<string> GetLines(string currentInput);

    /// <summary>
    /// Runs the panel's own background loop.
    /// Called by the host when the panel is set active.
    /// The host cancels the token when the panel is swapped out or the host stops.
    /// Default implementation returns immediately (no background work).
    /// </summary>
    Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
