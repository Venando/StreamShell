namespace StreamShell;

public enum AttachmentType
{
    PlainText
}

public record Attachment(string Content, AttachmentType Type, int LineCount);