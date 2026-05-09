namespace StreamShell;

/// <summary>
/// Configuration for the separator line rendered between the message feed
/// and the input block. Left/right text support Spectre markup.
/// </summary>
public record SeparatorConfig
{
    /// <summary>Text rendered at the left side of the separator (supports markup).</summary>
    public string? LeftText { get; init; }

    /// <summary>Text rendered at the right side of the separator (supports markup).</summary>
    public string? RightText { get; init; }

    /// <summary>Character used to fill the gap between left and right text.</summary>
    public char RepeatedChar { get; init; } = '─';

    /// <summary>
    /// Optional Spectre markup style applied to the repeated fill section.
    /// Example: "dim" wraps the fill in [dim]...[/dim].
    /// When null, the fill is rendered as plain characters.
    /// </summary>
    public string? RepeatedCharMarkup { get; init; }

    /// <summary>Default separator: full line of '─' (no text).</summary>
    public static readonly SeparatorConfig Default = new();
}
