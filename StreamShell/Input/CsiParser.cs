namespace StreamShell;

/// <summary>
/// Parses VT/xterm CSI (Control Sequence Introducer) escape sequences
/// and returns the corresponding <see cref="ConsoleKeyInfo"/> with
/// correct modifiers.
///
/// Handles standard and extended XTerm modifier encoding:
///   <c>ESC [ A</c>       → UpArrow
///   <c>ESC [ 1 ; 2 D</c> → LeftArrow + Shift
///   <c>ESC [ 5 D</c>     → LeftArrow + Ctrl  (bare modifier, Linux console)
///   <c>ESC [ 1 5 ~</c>   → F5 (via CSI ~ encoding)
///   <c>ESC O H</c>       → Home (application mode)
/// </summary>
internal static class CsiParser
{
    /// <summary>
    /// Parses an XTerm-style CSI sequence (everything after the ESC).
    /// </summary>
    /// <param name="seq">The CSI sequence starting with '[' or 'O',
    /// e.g. "[1;2D" for Shift+LeftArrow.</param>
    /// <returns>The parsed key, or null if the sequence is unrecognized.</returns>
    public static ConsoleKeyInfo? Parse(string seq)
    {
        if (seq.Length < 2)
            return null;

        char intro = seq[0];
        if (intro != '[' && intro != 'O')
            return null;

        char final = seq[^1];

        string paramStr = seq.Length > 2 ? seq.Substring(1, seq.Length - 2) : "";

        int p1 = 0, p2 = 0;
        if (paramStr.Length > 0)
        {
            string[] parts = paramStr.Split(';');
            if (parts.Length > 0) int.TryParse(parts[0], out p1);
            if (parts.Length > 1) int.TryParse(parts[1], out p2);
        }

        // Modifier extraction: two formats exist —
        //   Extended CSI:  CSI 1 ; mod letter     (modern terminals)
        //   Bare modifier: CSI mod letter          (Linux console, old xterm)
        // Tilde sequences (~) use p1 as a key code, never as a modifier.
        bool shift = false, alt = false, ctrl = false;
        if (paramStr.Contains(';'))
        {
            int modParam = p1 == 1 ? p2 : p1;
            (shift, alt, ctrl) = DecodeModifier(modParam);
        }
        else if (p1 >= 2 && p1 <= 8 && final != '~')
        {
            // Bare modifier format: single parameter is the modifier.
            // Only interpret as modifier for cursor keys (A–F, H) —
            // tilde sequences use p1 as a key code (e.g. "2~" → Insert).
            (shift, alt, ctrl) = DecodeModifier(p1);
        }

        ConsoleKey? key = MapToConsoleKey(intro, final, p1);
        if (key is null)
            return null;

        return new ConsoleKeyInfo('\0', key.Value, shift, alt, ctrl);
    }

    private static ConsoleKey? MapToConsoleKey(char intro, char final, int p1)
    {
        if (intro == '[')
        {
            return final switch
            {
                'A' => ConsoleKey.UpArrow,
                'B' => ConsoleKey.DownArrow,
                'C' => ConsoleKey.RightArrow,
                'D' => ConsoleKey.LeftArrow,
                'H' => ConsoleKey.Home,
                'F' => ConsoleKey.End,
                'Z' => ConsoleKey.Tab,
                '~' => MapTilde(p1),
                _ => null
            };
        }

        if (intro == 'O')
        {
            return final switch
            {
                'A' => ConsoleKey.UpArrow,
                'B' => ConsoleKey.DownArrow,
                'C' => ConsoleKey.RightArrow,
                'D' => ConsoleKey.LeftArrow,
                'H' => ConsoleKey.Home,
                'F' => ConsoleKey.End,
                'P' => ConsoleKey.F1,
                'Q' => ConsoleKey.F2,
                'R' => ConsoleKey.F3,
                'S' => ConsoleKey.F4,
                _ => null
            };
        }

        return null;
    }

    /// <summary>
    /// Decodes an xterm modifier parameter into Shift/Alt/Ctrl flags.
    /// The xterm modifier encoding is an enumeration, NOT a bitmask:
    /// 2=Shift, 3=Alt, 4=Shift+Alt, 5=Ctrl, 6=Ctrl+Shift,
    /// 7=Ctrl+Alt, 8=Ctrl+Shift+Alt.
    /// </summary>
    private static (bool shift, bool alt, bool ctrl) DecodeModifier(int mod)
    {
        return mod switch
        {
            2 => (true,  false, false),
            3 => (false, true,  false),
            4 => (true,  true,  false),
            5 => (false, false, true),
            6 => (true,  false, true),
            7 => (false, true,  true),
            8 => (true,  true,  true),
            _ => (false, false, false)
        };
    }

    private static ConsoleKey? MapTilde(int p1)
    {
        return p1 switch
        {
            1 or 7 => ConsoleKey.Home,
            2 => ConsoleKey.Insert,
            3 => ConsoleKey.Delete,
            4 or 8 => ConsoleKey.End,
            5 => ConsoleKey.PageUp,
            6 => ConsoleKey.PageDown,
            11 => ConsoleKey.F1,
            12 => ConsoleKey.F2,
            13 => ConsoleKey.F3,
            14 => ConsoleKey.F4,
            15 => ConsoleKey.F5,
            17 => ConsoleKey.F6,
            18 => ConsoleKey.F7,
            19 => ConsoleKey.F8,
            20 => ConsoleKey.F9,
            21 => ConsoleKey.F10,
            23 => ConsoleKey.F11,
            24 => ConsoleKey.F12,
            _ => null
        };
    }
}
