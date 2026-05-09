namespace StreamShell;

/// <summary>Type of an attachment attached to a submitted input.</summary>
public enum AttachmentType
{
    /// <summary>Plain text content (e.g. a large paste).</summary>
    PlainText
}

/// <summary>An attachment (e.g. a large paste) bundled with submitted input.</summary>
/// <param name="Content">The full content of the attachment.</param>
/// <param name="Type">The type of attachment.</param>
/// <param name="LineCount">Number of lines in the content.</param>
/// <param name="Counter">Incrementing counter reset on each submit.</param>
/// <param name="Placeholder">Pre-computed placeholder string for this attachment.</param>
public record Attachment(string Content, AttachmentType Type, int LineCount, int Counter = 0, string Placeholder = "");