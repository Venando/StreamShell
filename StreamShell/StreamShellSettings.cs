namespace StreamShell;

/// <summary>Configurable settings for the StreamShell host.</summary>
public class StreamShellSettings
{
    /// <summary>Maximum character count before a paste is treated as a large paste. Default: 300.</summary>
    public int LargePasteThreshold { get; set; } = 300;

    /// <summary>Maximum line count before a paste is treated as a large paste. Default: 4.</summary>
    public int LargePasteLineThreshold { get; set; } = 4;

    /// <summary>
    /// Spectre.Console markup style string for the cursor highlight.
    /// Applied to the character under the cursor (or a placeholder space past the end).
    /// Default: "black on gray".
    /// </summary>
    public string CursorMarkup { get; set; } = "black on gray";

    /// <summary>
    /// Spectre.Console markup style string for selected text.
    /// Applied to the range of characters selected with Shift+arrow.
    /// Default: "white on gray".
    /// </summary>
    public string SelectionMarkup { get; set; } = "white on gray";

    /// <summary>
    /// Right-edge margin for text wrapping in the input field.
    /// When set to -1, uses Console.WindowWidth at construction time.
    /// Must be at least 10 to ensure visible characters.
    /// </summary>
    public int RightEdgeMargin { get; set; } = -1;

    /// <summary>
    /// Spectre.Console markup for the first-line input field prefix.
    /// Default: "[blue]> [/]"
    /// </summary>
    public string InputPrefix { get; set; } = "[blue]> [/]";

    /// <summary>
    /// Plain text prefix for continuation (wrapped) input lines.
    /// Default: "  " (two spaces)
    /// </summary>
    public string ContinuationPrefix { get; set; } = "  ";

    /// <summary>
    /// Visual width of the first-line input prefix (after Spectre processes markup).
    /// Used by WrapSegment to calculate the available text width.
    /// Default: 2 (for the default "[blue]> [/]" prefix, which renders as "> ").
    /// </summary>
    public int PrefixMargin { get; set; } = 2;

    /// <summary>
    /// Right-edge buffer in characters, reserved between the wrapped text and
    /// the console right edge. Used by WrapSegment to calculate wrapping caps.
    /// Default: 4.
    /// </summary>
    public int WrappingRightMargin { get; set; } = 4;

    /// <summary>Resolves the effective right edge margin, substituting Console.WindowWidth for -1.</summary>
    public int GetEffectiveRightMargin()
        => RightEdgeMargin > 0 ? RightEdgeMargin : Console.WindowWidth;
}