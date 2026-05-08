namespace StreamShell;

/// <summary>Abstraction for rendering the StreamShell UI block (input, hints, messages).</summary>
public interface IRenderer
{
    /// <summary>Total vertical space taken by the input block (separator + input + hints).</summary>
    int GetBlockOffset(string input);

    /// <summary>Number of visual lines the input string occupies at the current margin.</summary>
    int GetInputLineCount(string input);

    /// <summary>Clear the single input line (used when no input has been rendered yet).</summary>
    void ClearInputLine();

    /// <summary>Clear the entire input block for a given input state.</summary>
    void ClearInputBlock(string? lastInput);

    /// <summary>Clear enough lines to cover both the old and new block heights.</summary>
    void ClearInputBlockForReRender(string? oldInput, string newInput);

    /// <summary>Render a markup message line.</summary>
    void RenderMessage(string markup);

    /// <summary>Render the full input block (separator + input lines + blank + hints).</summary>
    void RenderInputBlock(
        string input,
        IReadOnlyList<string> hints,
        int cursorPosition,
        bool hasSelection,
        int selectionStart,
        int selectionLength,
        int margin);

    /// <summary>Overwrite-only render for single-line input changes (no full clear).</summary>
    void OverwriteInputBlock(
        string input,
        IReadOnlyList<string> hints,
        int blockOffset,
        int cursorPosition,
        bool hasSelection,
        int selectionStart,
        int selectionLength,
        int margin);
}
