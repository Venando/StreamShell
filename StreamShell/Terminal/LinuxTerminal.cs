using System.Text;

namespace StreamShell;

/// <summary>
/// Linux-aware <see cref="ITerminal"/> that intercepts <see cref="ConsoleKey.Escape"/>
/// returned by <c>Console.ReadKey()</c> and checks whether it was actually the start
/// of a VT/xterm CSI escape sequence that the runtime's parser failed to handle.
///
/// On Linux, the .NET runtime's internal CSI parser may consume the leading ESC byte
/// from extended sequences (Shift+Arrow emits <c>ESC [ 1 ; 2 D</c>) but buffer the
/// remaining bytes internally rather than returning them as a complete key.  After
/// the ESC is returned, <c>Console.KeyAvailable</c> is still true, and subsequent
/// <c>Console.ReadKey()</c> calls return the trailing bytes as individual key presses.
///
/// This terminal consumes those trailing bytes, reconstructs the CSI sequence, and
/// returns the correct <see cref="ConsoleKeyInfo"/> with proper modifiers.
/// </summary>
internal sealed class LinuxTerminal : ITerminal
{
    // ══════════════════════════════════════════════════════════════════
    //  ITerminal — Key Input
    // ══════════════════════════════════════════════════════════════════

    public bool KeyAvailable => Console.KeyAvailable;

    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        var key = Console.ReadKey(intercept);

        // Fast path: non-ESC keys are returned as-is.
        // The .NET runtime handles standard printable chars, Enter, Tab,
        // Backspace, Ctrl+letter, and basic arrow keys correctly on Linux.
        if (key.Key != ConsoleKey.Escape)
            return key;

        // ESC received.  On Linux this may be the leading byte of a CSI
        // sequence whose remaining bytes .NET buffered but could not parse
        // into a complete key.  If there are pending bytes, consume and
        // try to parse them as an extended CSI sequence.
        //
        // No explicit sleep is needed — .NET's internal read already
        // consumed the burst of bytes; Console.KeyAvailable reflects its
        // internal buffer state, so the check is instantaneous.
        if (!Console.KeyAvailable)
            return key; // genuine Escape key press

        // Read trailing bytes of the potential CSI sequence
        // Use a small list — CSI sequences are at most ~10 bytes
        var trailing = new List<ConsoleKeyInfo>(8);
        while (Console.KeyAvailable)
            trailing.Add(Console.ReadKey(intercept: true));

        if (trailing.Count == 0)
            return key;

        // CSI sequences always start with '[' or 'O' (SS3)
        char intro = trailing[0].KeyChar;
        if (intro != '[' && intro != 'O')
        {
            // Not a CSI sequence — a real user action (ESC + keypress).
            // Reconstructed as Alt+key if the second byte is printable.
            // Otherwise, return plain ESC (trailing bytes are lost, but
            // this only happens for exotic terminal sequences).
            if (trailing.Count == 1 && trailing[0].KeyChar >= ' ')
            {
                char c = trailing[0].KeyChar;
                // Alt+Enter: ESC followed by \r
                if (trailing[0].Key == ConsoleKey.Enter)
                    return new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, true, false);
                // Alt+letter
                if (c >= 'a' && c <= 'z')
                    return new ConsoleKeyInfo(c, ConsoleKey.A + (c - 'a'), false, true, false);
                if (c >= 'A' && c <= 'Z')
                    return new ConsoleKeyInfo(c, (ConsoleKey)c, false, true, false);
                // Alt+digit or other printable
                return new ConsoleKeyInfo(c, (ConsoleKey)c, false, true, false);
            }
            return key;
        }

        // Reconstruct the CSI sequence from KeyChar values
        var seq = new StringBuilder(trailing.Count);
        foreach (var k in trailing)
        {
            if (k.KeyChar != '\0')
                seq.Append(k.KeyChar);
        }

        var parsed = ParseCsiSequence(seq.ToString());
        if (parsed is null)
            return key; // unrecognized CSI — treat as plain ESC

        return parsed.Value;
    }

    // ══════════════════════════════════════════════════════════════════
    //  CSI Sequence Parser
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parses an XTerm-style CSI sequence (everything after the ESC).
    /// </summary>
    /// <param name="seq">The CSI sequence string starting with '[' or 'O',
    /// e.g. "[1;2D" for Shift+LeftArrow.</param>
    internal static ConsoleKeyInfo? ParseCsiSequence(string seq)
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

        // Only extract modifiers when ';' is present (extended CSI).
        // Bare numbers like "2~" are key codes, not modifiers.
        bool shift = false, alt = false, ctrl = false;
        if (paramStr.Contains(';'))
        {
            int modParam = p1 == 1 ? p2 : p1;
            switch (modParam)
            {
                case 2: shift = true; break;
                case 3: alt = true; break;
                case 4: shift = true; alt = true; break;
                case 5: ctrl = true; break;
                case 6: ctrl = true; shift = true; break;
                case 7: ctrl = true; alt = true; break;
                case 8: ctrl = true; shift = true; alt = true; break;
            }
        }

        ConsoleKey? key = MapCsiToConsoleKey(intro, final, p1);
        if (key is null)
            return null;

        return new ConsoleKeyInfo('\0', key.Value, shift, alt, ctrl);
    }

    private static ConsoleKey? MapCsiToConsoleKey(char intro, char final, int p1)
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
                '~' => MapTildeToConsoleKey(p1),
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

    private static ConsoleKey? MapTildeToConsoleKey(int p1)
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

    // ══════════════════════════════════════════════════════════════════
    //  ITerminal — Dimensions  (delegated to System.Console)
    // ══════════════════════════════════════════════════════════════════

    public int WindowWidth
    {
        get
        {
            try { return Console.WindowWidth; }
            catch (IOException) { return 80; }
        }
    }

    public int WindowHeight
    {
        get
        {
            try { return Console.WindowHeight; }
            catch (IOException) { return 24; }
        }
    }

    public int BufferHeight
    {
        get
        {
            try { return Console.BufferHeight; }
            catch (IOException) { return 30; }
        }
    }

    public int CursorTop
    {
        get
        {
            try { return Console.CursorTop; }
            catch (IOException) { return 0; }
        }
        set
        {
            try { Console.CursorTop = value; }
            catch (IOException) { }
        }
    }

    public int CursorLeft
    {
        get
        {
            try { return Console.CursorLeft; }
            catch (IOException) { return 0; }
        }
        set
        {
            try { Console.CursorLeft = value; }
            catch (IOException) { }
        }
    }

    public void SetCursorPosition(int left, int top)
    {
        try { Console.SetCursorPosition(left, top); }
        catch (IOException) { }
        catch (ArgumentOutOfRangeException) { }
    }

    public void Write(string text)
    {
        try { Console.Write(text); }
        catch (IOException) { }
    }

    public void WriteLine()
    {
        try { Console.WriteLine(); }
        catch (IOException) { }
    }
}
