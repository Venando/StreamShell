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
public record Attachment(string Content, AttachmentType Type, int LineCount);