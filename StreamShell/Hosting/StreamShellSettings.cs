using Spectre.Console;

namespace StreamShell;

/// <summary>Configurable settings for the StreamShell host.</summary>
public class StreamShellSettings
{
    /// <summary>Raised when any setting property changes. Subscribers can re-apply settings.</summary>
    public event Action? SettingsChanged;

    /// <summary>Maximum character count before a paste is treated as a large paste. Default: 300.</summary>
    public int LargePasteThreshold { get; set; } = 300;

    /// <summary>Maximum line count before a paste is treated as a large paste. Default: 4.</summary>
    public int LargePasteLineThreshold { get; set; } = 4;

    private string _cursorMarkup = "bold black on cyan";
    private string _selectionMarkup = "bold cyan on Grey27";
    private string _commandSlashMarkup = "Red1";
    private string _inputPrefix = "[bold SkyBlue1]> [/]";
    private string _continuationPrefix = "  ";
    private int _prefixMargin = -1; // -1 = dirty, needs recompute

    /// <summary>
    /// Spectre.Console markup style string for the cursor highlight.
    /// Applied to the character under the cursor (or a placeholder space past the end).
    /// Default: "bold black on cyan".
    /// </summary>
    public string CursorMarkup
    {
        get => _cursorMarkup;
        set => _cursorMarkup = value ?? "bold black on cyan";
    }

    /// <summary>
    /// Spectre.Console markup style string for selected text.
    /// Applied to the range of characters selected with Shift+arrow.
    /// Default: "bold cyan on Grey27".
    /// </summary>
    public string SelectionMarkup
    {
        get => _selectionMarkup;
        set => _selectionMarkup = value ?? "bold cyan on Grey27";
    }

    /// <summary>
    /// Spectre.Console markup style string for the command slash character (/)
    /// displayed as the first character of the input field.
    /// Default: "Red1".
    /// </summary>
    public string CommandSlashMarkup
    {
        get => _commandSlashMarkup;
        set { _commandSlashMarkup = value ?? "Red1"; InvalidatePrefixMargin(); NotifyChanged(); }
    }

    /// <summary>
    /// Spectre.Console markup for the first-line input field prefix.
    /// Default: "[bold SkyBlue1]> [/]"
    /// </summary>
    public string InputPrefix
    {
        get => _inputPrefix;
        set { _inputPrefix = value ?? "[bold SkyBlue1]> [/]"; InvalidatePrefixMargin(); NotifyChanged(); }
    }

    /// <summary>
    /// Plain text prefix for continuation (wrapped) input lines.
    /// Default: "  " (two spaces)
    /// </summary>
    public string ContinuationPrefix
    {
        get => _continuationPrefix;
        set { _continuationPrefix = value ?? "  "; InvalidatePrefixMargin(); NotifyChanged(); }
    }

    /// <summary>
    /// Right-edge buffer in characters, reserved between the wrapped text and
    /// the console right edge. Used by WrapSegment to calculate wrapping caps.
    /// Default: 4.
    /// </summary>
    public int WrappingRightMargin { get; set; } = 4;

    /// <summary>
    /// Visual width of the widest input prefix (first-line or continuation),
    /// after stripping Spectre markup. Cached and recomputed only when one of
    /// the prefix properties changes.
    /// Used by WrapSegment to calculate the available text width.
    /// </summary>
    public int PrefixMargin
    {
        get
        {
            if (_prefixMargin < 0)
                _prefixMargin = Math.Max(
                    Markup.Remove(_inputPrefix).Length,
                    Markup.Remove(_continuationPrefix).Length);
            return _prefixMargin;
        }
    }

    private void InvalidatePrefixMargin() => _prefixMargin = -1;

    /// <summary>Force recomputation of the prefix margin on the next read.</summary>
    internal void RecomputePrefixMargin() => _prefixMargin = -1;

    private void NotifyChanged() => SettingsChanged?.Invoke();
}