namespace StreamShell;

/// <summary>
/// A bottom panel renders additional lines below the user input (hints, status, etc.).
/// Panels can optionally run a background loop via <see cref="RunAsync"/>,
/// intercept keyboard keys via <see cref="TryHandleKey"/>,
/// and signal internal state changes via <see cref="IsDirty"/>.
/// </summary>
public interface IBottomPanel : IDisposable
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
    /// When true, the host should re-render even if no input/cursor/resize state
    /// has changed. The panel sets this when its internal visual state changes
    /// (e.g. hint selection). The host clears it after consuming.
    /// </summary>
    bool IsDirty => false;

    /// <summary>Clears the dirty flag after the host has rendered the panel.</summary>
    void ClearDirty() { }

    /// <summary>
    /// Called by the host for each console key before normal processing.
    /// Return true to consume the key and skip further handling.
    /// </summary>
    bool TryHandleKey(ConsoleKeyInfo key) => false;

    /// <summary>
    /// Optional autocomplete suggestion for Tab completion.
    /// Populated during <see cref="GetLines"/>. Null when no suggestion is available.
    /// </summary>
    string? CurrentSuggestion => null;

    /// <summary>
    /// When true, the bottom separator is printed between the input line and the panel.
    /// When false, the separator is suppressed (e.g. for panels that need tight spacing).
    /// Default: true.
    /// </summary>
    bool ShowBottomSeparator => true;

    /// <summary>
    /// Runs the panel's own background loop.
    /// Called by the host when the panel is set active.
    /// The host cancels the token when the panel is swapped out or the host stops.
    /// Default implementation returns immediately (no background work).
    /// </summary>
    Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
