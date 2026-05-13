namespace StreamShell;

/// <summary>
/// Configuration for multi-select mode in <see cref="SelectionPanel"/>.
/// When null, the panel operates in single-select mode (Enter selects and submits immediately).
/// </summary>
public class SelectionInfo
{
    /// <summary>Label for the submit button rendered as the last navigable item.</summary>
    public string SubmitTitle { get; set; } = "Done";

    /// <summary>Minimum number of items that must be selected before submit is allowed.</summary>
    public int Min { get; set; }

    /// <summary>Maximum number of items that can be selected. 0 = unlimited.</summary>
    public int Max { get; set; }

    /// <summary>When true, the user cannot cancel the prompt with Escape.</summary>
    public bool PreventCancel { get; set; }

    /// <summary>
    /// Number of visible rows for the selection panel (including controls and title lines).
    /// Default: 8. When set to less than 2 (meaning "not specified"), the panel will
    /// auto-resolve to the lesser of the item count or (console height - 5).
    /// If the item count exceeds the effective row count, the panel becomes scrollable.
    /// </summary>
    public int Rows { get; set; } = 8;

    /// <summary>
    /// Returns the effective row count, resolving the default/auto behavior.
    /// When <see cref="Rows"/> is less than 2, returns the lesser of
    /// <paramref name="itemCount"/> and <paramref name="consoleHeight"/> - 5
    /// (with a floor of 3 to always show controls + title + at least one item).
    /// </summary>
    internal int GetEffectiveRows(int itemCount, int consoleHeight)
    {
        if (Rows >= 2)
            return Rows;

        int auto = Math.Min(itemCount, consoleHeight - 5);
        return Math.Max(3, auto); // minimum 3: controls + title + at least 1 item
    }
}
