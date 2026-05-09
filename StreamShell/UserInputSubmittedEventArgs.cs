namespace StreamShell;

/// <summary>Event args for <see cref="ConsoleAppHost.UserInputSubmitted"/>.</summary>
public class UserInputSubmittedEventArgs
{
    /// <summary>Whether the input was a command or plain text.</summary>
    public InputType InputType { get; init; }

    /// <summary>Attachments collected during input (e.g. large pastes).</summary>
    public IReadOnlyList<Attachment> Attachments { get; init; } = Array.Empty<Attachment>();

    /// <summary>The raw text the user submitted.</summary>
    public string RawOutput { get; init; } = string.Empty;

    /// <summary>RawOutput with all attachment placeholders stripped out.</summary>
    public string TextWithoutAttachments =>
        Attachments.Count == 0 ? RawOutput : ReplacePlaceholders(RawOutput, replaceWith: "");

    /// <summary>RawOutput with all attachment placeholders replaced by their actual content.</summary>
    public string TextWithAttachmentsExpanded =>
        Attachments.Count == 0 ? RawOutput : ReplacePlaceholders(RawOutput, replaceWithContent: true);

    /// <summary>Replaces each attachment's placeholder in text with the given string or content.</summary>
    private string ReplacePlaceholders(string text, string? replaceWith = null, bool replaceWithContent = false)
    {
        foreach (var attachment in Attachments)
        {
            if (string.IsNullOrEmpty(attachment.Placeholder)) continue;
            var replacement = replaceWithContent ? attachment.Content : (replaceWith ?? "");
            text = text.Replace(attachment.Placeholder, replacement);
        }

        return text;
    }
}
