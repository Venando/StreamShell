using Spectre.Console;

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
    /// Right-edge buffer in characters, reserved between the wrapped text and
    /// the console right edge. Used by WrapSegment to calculate wrapping caps.
    /// Default: 4.
    /// </summary>
    public int WrappingRightMargin { get; set; } = 4;

    /// <summary>
    /// Visual width of the widest input prefix (first-line or continuation),
    /// after stripping Spectre markup. Derived automatically from
    /// <see cref="InputPrefix"/> and <see cref="ContinuationPrefix"/>.
    /// Used by WrapSegment to calculate the available text width.
    /// </summary>
    public int PrefixMargin => Math.Max(
        Markup.Remove(InputPrefix).Length,
        Markup.Remove(ContinuationPrefix).Length);

}