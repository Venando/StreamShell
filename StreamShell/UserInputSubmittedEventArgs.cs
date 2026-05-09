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
}
