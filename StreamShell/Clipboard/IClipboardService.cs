namespace StreamShell;

/// <summary>Abstraction for system clipboard access (copy/paste of Unicode text).</summary>
public interface IClipboardService
{
    /// <summary>Whether clipboard read/write operations are expected to work.</summary>
    bool IsAvailable { get; }

    /// <summary>Reads Unicode text from the system clipboard. Returns null when no text is available or on error.</summary>
    string? Paste();

    /// <summary>Writes Unicode text to the system clipboard. Silently handles platform errors.</summary>
    void Copy(string text);
}
