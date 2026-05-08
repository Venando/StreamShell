namespace StreamShell;

/// <summary>Abstraction for input state management and key processing.</summary>
public interface IInputHandler
{
    /// <summary>The current input text being composed.</summary>
    string CurrentInput { get; }

    /// <summary>The cursor position within the current input.</summary>
    int CursorPosition { get; }

    /// <summary>Whether a text selection is active.</summary>
    bool HasSelection { get; }

    /// <summary>Gets the current selection range, if any.</summary>
    bool TryGetSelection(out int start, out int length);

    /// <summary>The right-margin character count for input display.</summary>
    int RightMargin { get; set; }

    /// <summary>Whether a quit (Ctrl+D) has been requested.</summary>
    bool QuitRequested { get; set; }

    /// <summary>Attachments collected during input (e.g. large pastes).</summary>
    List<Attachment> Attachments { get; }

    /// <summary>Maximum character count before a paste is treated as a large paste.</summary>
    int LargePasteThreshold { get; set; }

    /// <summary>Maximum line count before a paste is treated as a large paste.</summary>
    int LargePasteLineThreshold { get; set; }

    /// <summary>Process buffered keyboard input. Returns submitted text or null.</summary>
    string? ProcessInput();

    /// <summary>Reset input state for a new input cycle.</summary>
    void Reset();

    /// <summary>
    /// Save the current input field state (text, cursor position, and attachments)
    /// into a named slot. Returns the unique ID assigned to the saved state.
    /// </summary>
    string SaveInputField();

    /// <summary>Restore input field state from a previously saved slot. Returns false if the ID is unknown.</summary>
    bool LoadInputField(string id);

    /// <summary>Remove a single saved input field by its ID. Returns false if the ID is unknown.</summary>
    bool RemoveSavedInputField(string id);

    /// <summary>Remove all saved input field states.</summary>
    void RemoveAllSavedInputFields();
}
