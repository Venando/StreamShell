using System.Text;

namespace StreamShell;

/// <summary>
/// Linux-aware <see cref="ITerminal"/> that correctly parses multi-byte
/// VT/xterm CSI escape sequences (Shift+Arrow, Ctrl+Arrow, etc.) which
/// <c>System.Console.ReadKey()</c> mishandles on Linux by returning
/// <see cref="ConsoleKey.Escape"/> followed by the raw sequence bytes as
/// individual key presses.
/// </summary>
internal sealed class LinuxTerminal : ITerminal
{
    private readonly Queue<ConsoleKeyInfo> _pendingKeys = new();

    // ══════════════════════════════════════════════════════════════════
    //  ITerminal — Key Input
    // ══════════════════════════════════════════════════════════════════

    public bool KeyAvailable => _pendingKeys.Count > 0 || Console.KeyAvailable;

    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        // Drain re-queued keys first (from a prior failed CSI parse)
        if (_pendingKeys.Count > 0)
            return _pendingKeys.Dequeue();

        var key = Console.ReadKey(intercept);

        // On Linux, Console.ReadKey may return Escape as the leading byte of
        // a VT escape sequence whose remaining bytes were not consumed by
        // the runtime's CSI parser (e.g. Shift+Arrow → ESC [ 1 ; 2 D).
        if (key.Key != ConsoleKey.Escape)
            return key;

        // Small window for the terminal to flush the rest of the CSI sequence.
        // 5 ms is far below human reaction time (~200 ms) so user-typed chars
        // won't be conflated with a prefixed escape sequence.
        Thread.Sleep(5);

        if (!Console.KeyAvailable)
            return key; // genuine Escape key press

        // Read the trailing bytes of the potential CSI sequence
        var trailing = new List<ConsoleKeyInfo>();
        while (Console.KeyAvailable)
            trailing.Add(Console.ReadKey(intercept: true));

        if (trailing.Count == 0)
            return key;

        // CSI sequences always start with '[' (Control Sequence Introducer)
        // or 'O' (SS3 — single-shift three, used by some terminal modes for F-keys).
        char intro = trailing[0].KeyChar;
        if (intro != '[' && intro != 'O')
        {
            // Not a CSI sequence — user typed ESC then something else.
            // Re-queue the consumed keys so nobody loses input.
            foreach (var t in trailing)
                _pendingKeys.Enqueue(t);
            return key;
        }

        // Reconstruct the CSI sequence from KeyChar values
        var seq = new StringBuilder();
        foreach (var k in trailing)
        {
            if (k.KeyChar != '\0')
                seq.Append(k.KeyChar);
        }

        var parsed = ParseCsiSequence(seq.ToString());
        if (parsed is null)
        {
            // Could not parse — re-queue and treat as plain ESC
            foreach (var t in trailing)
                _pendingKeys.Enqueue(t);
            return key;
        }

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

        // CSI introducer
        char intro = seq[0];
        if (intro != '[' && intro != 'O')
            return null;

        // Final character (terminator) — always the last byte
        char final = seq[^1];

        // Parameter string between intro and final, exclusive
        string paramStr = seq.Length > 2 ? seq.Substring(1, seq.Length - 2) : "";

        // Parse up to two numeric parameters
        int p1 = 0, p2 = 0;
        if (paramStr.Length > 0)
        {
            string[] parts = paramStr.Split(';');
            if (parts.Length > 0) int.TryParse(parts[0], out p1);
            if (parts.Length > 1) int.TryParse(parts[1], out p2);
        }

        // XTerm modifier encoding for extended CSI sequences:
        //   When param 1 == 1, param 2 encodes modifiers:
        //     2=Shift  3=Alt  4=Shift+Alt  5=Ctrl  6=Ctrl+Shift  7=Ctrl+Alt  8=Ctrl+Shift+Alt
        //   Otherwise, modifier is derived from param 1 directly (for sequences
        //   where param 1 is the modifier code itself, e.g. "2~" for Shift+Insert).
        int modParam = p1 == 1 ? p2 : p1;

        bool shift = false, alt = false, ctrl = false;
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

        // Map final character and (optional) first parameter to ConsoleKey
        ConsoleKey? key = MapCsiToConsoleKey(intro, final, p1);

        if (key is null)
            return null;

        return new ConsoleKeyInfo('\0', key.Value, shift, alt, ctrl);
    }

    /// <summary>Maps a CSI final character to <see cref="ConsoleKey"/>.</summary>
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
                'Z' => ConsoleKey.Tab,             // Shift+Tab
                '~' => MapTildeToConsoleKey(p1),   // Home/End/Insert/Del/PgUp/PgDn variants
                _ => null
            };
        }

        if (intro == 'O')
        {
            return final switch
            {
                'H' => ConsoleKey.Home,   // application-mode Home
                'F' => ConsoleKey.End,    // application-mode End
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
    /// Maps the numeric parameter of a CSI ~ sequence to <see cref="ConsoleKey"/>.
    /// Common XTerm encodings: 1=Home, 2=Insert, 3=Delete, 4=End, 5=PgUp, 6=PgDn.
    /// </summary>
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

    // ══════════════════════════════════════════════════════════════════
    //  ITerminal — Cursor & Output  (delegated to System.Console)
    // ══════════════════════════════════════════════════════════════════

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
