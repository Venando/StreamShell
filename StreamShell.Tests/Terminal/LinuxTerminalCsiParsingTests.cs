namespace StreamShell.Tests;

// ═════════════════════════════════════════════════════════════════════
//  LinuxTerminal — CSI Sequence Parser Tests
//  Verifies that the internal VT/xterm escape sequence parser correctly
//  maps CSI sequences to ConsoleKeyInfo with proper modifiers.
// ═════════════════════════════════════════════════════════════════════

public class LinuxTerminalCsiParsingTests
{
    // ══════════════════════════════════════════════════════════════════
    //  Standard arrow keys (no modifiers)
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("[A", ConsoleKey.UpArrow)]
    [InlineData("[B", ConsoleKey.DownArrow)]
    [InlineData("[C", ConsoleKey.RightArrow)]
    [InlineData("[D", ConsoleKey.LeftArrow)]
    public void ParseCsiSequence_StandardArrows_NoModifiers(string seq, ConsoleKey expectedKey)
    {
        var result = ParseCsi(seq);

        Assert.NotNull(result);
        Assert.Equal(expectedKey, result.Value.Key);
        AssertModifiers(result.Value, shift: false, alt: false, ctrl: false);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Shift + arrows
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("[1;2A", ConsoleKey.UpArrow)]
    [InlineData("[1;2B", ConsoleKey.DownArrow)]
    [InlineData("[1;2C", ConsoleKey.RightArrow)]
    [InlineData("[1;2D", ConsoleKey.LeftArrow)]
    public void ParseCsiSequence_ShiftArrows_HasShiftModifier(string seq, ConsoleKey expectedKey)
    {
        var result = ParseCsi(seq);

        Assert.NotNull(result);
        Assert.Equal(expectedKey, result.Value.Key);
        AssertModifiers(result.Value, shift: true, alt: false, ctrl: false);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Ctrl + arrows
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("[1;5A", ConsoleKey.UpArrow)]
    [InlineData("[1;5B", ConsoleKey.DownArrow)]
    [InlineData("[1;5C", ConsoleKey.RightArrow)]
    [InlineData("[1;5D", ConsoleKey.LeftArrow)]
    public void ParseCsiSequence_CtrlArrows_HasCtrlModifier(string seq, ConsoleKey expectedKey)
    {
        var result = ParseCsi(seq);

        Assert.NotNull(result);
        Assert.Equal(expectedKey, result.Value.Key);
        AssertModifiers(result.Value, shift: false, alt: false, ctrl: true);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Ctrl+Shift + arrows
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("[1;6A", ConsoleKey.UpArrow)]
    [InlineData("[1;6B", ConsoleKey.DownArrow)]
    [InlineData("[1;6C", ConsoleKey.RightArrow)]
    [InlineData("[1;6D", ConsoleKey.LeftArrow)]
    public void ParseCsiSequence_CtrlShiftArrows_HasCtrlShiftModifiers(string seq, ConsoleKey expectedKey)
    {
        var result = ParseCsi(seq);

        Assert.NotNull(result);
        Assert.Equal(expectedKey, result.Value.Key);
        AssertModifiers(result.Value, shift: true, alt: false, ctrl: true);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Alt + arrows
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("[1;3A", ConsoleKey.UpArrow)]
    [InlineData("[1;3B", ConsoleKey.DownArrow)]
    public void ParseCsiSequence_AltArrows_HasAltModifier(string seq, ConsoleKey expectedKey)
    {
        var result = ParseCsi(seq);

        Assert.NotNull(result);
        Assert.Equal(expectedKey, result.Value.Key);
        AssertModifiers(result.Value, shift: false, alt: true, ctrl: false);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Home / End  (no modifiers)
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("[H", ConsoleKey.Home)]
    [InlineData("[F", ConsoleKey.End)]
    [InlineData("OH", ConsoleKey.Home)]    // application-mode
    [InlineData("OF", ConsoleKey.End)]     // application-mode
    public void ParseCsiSequence_HomeEnd_NoModifiers(string seq, ConsoleKey expectedKey)
    {
        var result = ParseCsi(seq);

        Assert.NotNull(result);
        Assert.Equal(expectedKey, result.Value.Key);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Shift + Home / End
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("[1;2H", ConsoleKey.Home)]
    [InlineData("[1;2F", ConsoleKey.End)]
    public void ParseCsiSequence_ShiftHomeEnd_HasShiftModifier(string seq, ConsoleKey expectedKey)
    {
        var result = ParseCsi(seq);

        Assert.NotNull(result);
        Assert.Equal(expectedKey, result.Value.Key);
        AssertModifiers(result.Value, shift: true, alt: false, ctrl: false);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Ctrl + Home / End
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("[1;5H", ConsoleKey.Home)]
    [InlineData("[1;5F", ConsoleKey.End)]
    public void ParseCsiSequence_CtrlHomeEnd_HasCtrlModifier(string seq, ConsoleKey expectedKey)
    {
        var result = ParseCsi(seq);

        Assert.NotNull(result);
        Assert.Equal(expectedKey, result.Value.Key);
        AssertModifiers(result.Value, shift: false, alt: false, ctrl: true);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Shift+Tab  (CSI Z)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void ParseCsiSequence_ShiftTab_ReturnsTab()
    {
        var result = ParseCsi("[Z");

        Assert.NotNull(result);
        Assert.Equal(ConsoleKey.Tab, result.Value.Key);
        // Note: Shift modifier is not set here — standard CSI Z is just
        // alternate Tab. The key is ConsoleKey.Tab.
    }

    // ══════════════════════════════════════════════════════════════════
    //  CSI ~ sequences (Home/End/Insert/Delete/PgUp/PgDn variants)
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("[1~", ConsoleKey.Home)]
    [InlineData("[7~", ConsoleKey.Home)]
    [InlineData("[4~", ConsoleKey.End)]
    [InlineData("[8~", ConsoleKey.End)]
    [InlineData("[2~", ConsoleKey.Insert)]
    [InlineData("[3~", ConsoleKey.Delete)]
    [InlineData("[5~", ConsoleKey.PageUp)]
    [InlineData("[6~", ConsoleKey.PageDown)]
    public void ParseCsiSequence_TildeVariants_CorrectKeys(string seq, ConsoleKey expectedKey)
    {
        var result = ParseCsi(seq);

        Assert.NotNull(result);
        Assert.Equal(expectedKey, result.Value.Key);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Application-mode F-keys (SS3 O)
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("OP", ConsoleKey.F1)]
    [InlineData("OQ", ConsoleKey.F2)]
    [InlineData("OR", ConsoleKey.F3)]
    [InlineData("OS", ConsoleKey.F4)]
    public void ParseCsiSequence_ApplicationFunctionKeys_CorrectKeys(string seq, ConsoleKey expectedKey)
    {
        var result = ParseCsi(seq);

        Assert.NotNull(result);
        Assert.Equal(expectedKey, result.Value.Key);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Invalid / unrecognized sequences
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("")]          // empty
    [InlineData("x")]         // no CSI introducer
    [InlineData("[X")]        // unknown final char
    [InlineData("[99~")]      // unknown tilde parameter
    public void ParseCsiSequence_InvalidSequences_ReturnsNull(string seq)
    {
        var result = ParseCsi(seq);

        Assert.Null(result);
    }

    // ══════════════════════════════════════════════════════════════════
    //  SS3 cursor keys (application mode: ESC O A/B/C/D)
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("OA", ConsoleKey.UpArrow)]
    [InlineData("OB", ConsoleKey.DownArrow)]
    [InlineData("OC", ConsoleKey.RightArrow)]
    [InlineData("OD", ConsoleKey.LeftArrow)]
    public void ParseCsiSequence_Ss3CursorKeys_CorrectKeys(string seq, ConsoleKey expectedKey)
    {
        var result = ParseCsi(seq);

        Assert.NotNull(result);
        Assert.Equal(expectedKey, result.Value.Key);
        AssertModifiers(result.Value, shift: false, alt: false, ctrl: false);
    }

    // ══════════════════════════════════════════════════════════════════
    //  F5–F12 via CSI ~ sequences
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("[15~", ConsoleKey.F5)]
    [InlineData("[17~", ConsoleKey.F6)]
    [InlineData("[18~", ConsoleKey.F7)]
    [InlineData("[19~", ConsoleKey.F8)]
    [InlineData("[20~", ConsoleKey.F9)]
    [InlineData("[21~", ConsoleKey.F10)]
    [InlineData("[23~", ConsoleKey.F11)]
    [InlineData("[24~", ConsoleKey.F12)]
    public void ParseCsiSequence_F5ThroughF12_CorrectKeys(string seq, ConsoleKey expectedKey)
    {
        var result = ParseCsi(seq);

        Assert.NotNull(result);
        Assert.Equal(expectedKey, result.Value.Key);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Modifier leak fix: bare CSI ~ sequences carry NO modifiers
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("[2~", ConsoleKey.Insert)]   // was erroneously Shift
    [InlineData("[3~", ConsoleKey.Delete)]   // was erroneously Alt
    [InlineData("[4~", ConsoleKey.End)]      // was erroneously Shift+Alt
    [InlineData("[5~", ConsoleKey.PageUp)]   // was erroneously Ctrl
    [InlineData("[6~", ConsoleKey.PageDown)] // was erroneously Ctrl+Shift
    public void ParseCsiSequence_BareTildeSequences_NoModifiers(string seq, ConsoleKey expectedKey)
    {
        var result = ParseCsi(seq);

        Assert.NotNull(result);
        Assert.Equal(expectedKey, result.Value.Key);
        AssertModifiers(result.Value, shift: false, alt: false, ctrl: false);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════

    /// <summary>Invokes the CSI parser.</summary>
    private static ConsoleKeyInfo? ParseCsi(string seq) =>
        CsiParser.Parse(seq);

    private static void AssertModifiers(ConsoleKeyInfo key, bool shift, bool alt, bool ctrl)
    {
        Assert.Equal(shift, key.Modifiers.HasFlag(ConsoleModifiers.Shift));
        Assert.Equal(alt, key.Modifiers.HasFlag(ConsoleModifiers.Alt));
        Assert.Equal(ctrl, key.Modifiers.HasFlag(ConsoleModifiers.Control));
    }
}
