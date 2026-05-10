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
}
