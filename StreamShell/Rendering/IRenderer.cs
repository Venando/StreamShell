namespace StreamShell;

/// <summary>Abstraction for rendering the StreamShell UI block (input, hints, messages).</summary>
public interface IRenderer
{
    /// <summary>Sets the number of lines the bottom panel renders. Used for block offset calculation.</summary>
    void SetPanelLineCount(int count);
    /// <summary>Top separator configuration (between message feed and input block).</summary>
    SeparatorConfig TopSeparator { get; set; }
    /// <summary>Bottom separator configuration (between input line and hints block).</summary>
    SeparatorConfig BottomSeparator { get; set; }
    /// <summary>Total vertical space taken by the input block (separator + input + hints).</summary>
    int GetBlockOffset(string input);

    /// <summary>Number of visual lines the input string occupies at the current margin.</summary>
    int GetInputLineCount(string input);

    /// <summary>Clear the single input line (used when no input has been rendered yet).</summary>
    void ClearInputLine();

    /// <summary>Clear the entire input block for a given input state.</summary>
    void ClearInputBlock(string? lastInput);

    /// <summary>Clear enough lines to cover both the old and new block heights.
    /// <paramref name="oldPanelLineCount"/> is the panel line count that was active
    /// when <paramref name="oldInput"/> was rendered (may differ from current).</summary>
    void ClearInputBlockForReRender(string? oldInput, string newInput, int oldPanelLineCount);

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

    /// <summary>Full-block overwrite without clearing first. Positions at old block top
    /// and renders the entire block (separator + input + hints) in place.
    /// Eliminates flicker by avoiding a separate clear-before-render step.</summary>
    void OverwriteFullBlock(
        string input,
        IReadOnlyList<string> hints,
        int oldBlockOffset,
        int cursorPosition,
        bool hasSelection,
        int selectionStart,
        int selectionLength,
        int margin);

    /// <summary>
    /// Called after the input block was re-rendered and the block shrunk.
    /// Clears excess lines below the new (smaller) block that were
    /// cleared but not re-filled by the re-render.
    /// </summary>
    /// <param name="oldBlockOffset">Block offset before the change.</param>
    /// <param name="newBlockOffset">Block offset after the change.</param>
    void HandleBlockHeightChange(int oldBlockOffset, int newBlockOffset);
}
