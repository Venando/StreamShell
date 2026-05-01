namespace StreamShell;

public class StreamShellSettings
{
    public int LargePasteThreshold { get; set; } = 100;
    public int LargePasteLineThreshold { get; internal set; } = 4;
}