namespace StreamShell;

/// <summary>Configurable settings for the StreamShell host.</summary>
public class StreamShellSettings
{
    /// <summary>Maximum character count before a paste is treated as a large paste. Default: 100.</summary>
    public int LargePasteThreshold { get; set; } = 100;

    /// <summary>Maximum line count before a paste is treated as a large paste. Default: 4.</summary>
    public int LargePasteLineThreshold { get; set; } = 4;
}