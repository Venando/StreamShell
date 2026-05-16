namespace StreamShell;

/// <summary>
/// Spectre.Console color/style configuration for command palette hint rows.
/// Separated from <see cref="StreamShellSettings"/> to avoid flat property bloat.
/// </summary>
public class PaletteStyle
{
    /// <summary>Background color for the selected hint row. Default: "white".</summary>
    public string SelectedBackground { get; set; } = "white";

    /// <summary>Text color for the selected row cursor arrow. Default: "black".</summary>
    public string SelectedCursorColor { get; set; } = "black";

    /// <summary>Cursor arrow symbol for selected row. Default: "→ ".</summary>
    public string SelectedCursorSymbol { get; set; } = "→ ";

    /// <summary>Text color for the slash and name in selected row. Default: "gray27".</summary>
    public string SelectedNameColor { get; set; } = "gray27";

    /// <summary>Text color for the description in selected row. Default: "gray15".</summary>
    public string SelectedDescriptionColor { get; set; } = "gray15";

    /// <summary>Indent before the slash in normal rows. Default: " ".</summary>
    public string NormalIndent { get; set; } = " ";

    /// <summary>Text color for slash and name in normal rows. Default: "grey".</summary>
    public string NormalNameColor { get; set; } = "grey";
}
