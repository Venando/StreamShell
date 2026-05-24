using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace StreamShell;

/// <summary>
/// Linux-aware <see cref="ITerminal"/> that reads raw bytes from stdin
/// and parses VT/xterm escape sequences, control characters, and printable
/// input into <see cref="ConsoleKeyInfo"/> values with correct modifiers.
///
/// Bypasses <c>System.Console.ReadKey()</c> entirely because the runtime's
/// CSI parser on Linux mishandles extended escape sequences (Shift+Arrow,
/// Ctrl+Arrow, etc.) by returning them as individual key presses rather than
/// as single modifier-aware keys.
/// </summary>
internal sealed class LinuxTerminal : ITerminal
{
    private readonly Stream _stdin;
    private readonly byte[] _readBuf = new byte[64];
    private static bool _rawModeSet;

    // ── termios / raw-mode P/Invoke ─────────────────────────────────

    private const int TCSANOW = 0;
    private const uint ICANON = 0x0002;
    private const uint ECHO   = 0x0008;
    private const uint ISIG   = 0x0001;
    private const uint IXON   = 0x0400;
    private const uint ICRNL  = 0x0100;
    private const byte VMIN   = 6;
    private const byte VTIME  = 5;

    [DllImport("libc", SetLastError = true)]
    private static extern int tcgetattr(int fd, ref Termios termios_p);

    [DllImport("libc", SetLastError = true)]
    private static extern int tcsetattr(int fd, int optional_actions, ref Termios termios_p);

    [StructLayout(LayoutKind.Sequential)]
    private struct Termios
    {
        public uint c_iflag;
        public uint c_oflag;
        public uint c_cflag;
        public uint c_lflag;
        public byte c_line;
        // cc_t is byte[32] on x86_64 Linux — use MarshalAs to avoid unsafe
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] c_cc;
    }

    private static Termios _savedTermios;

    public LinuxTerminal()
    {
        _stdin = Console.OpenStandardInput();
        EnsureRawMode();
    }

    private void EnsureRawMode()
    {
        if (_rawModeSet) return;
        _rawModeSet = true;

        // Get stdin fd from the .NET FileStream handle
        int fd = 0; // STDIN_FILENO
        try
        {
            if (_stdin is FileStream fs && fs.SafeFileHandle is not null)
            {
                nint handle = fs.SafeFileHandle.DangerousGetHandle();
                if (handle != nint.Zero)
                    fd = (int)handle;
            }
        }
        catch { /* fallback to fd 0 */ }

        // Initialize arrays before P/Invoke writes into them
        _savedTermios = new Termios { c_cc = new byte[32] };
        tcgetattr(fd, ref _savedTermios);

        // Build raw settings (clone the cc array so we don't mutate _savedTermios)
        var raw = new Termios
        {
            c_iflag = _savedTermios.c_iflag,
            c_oflag = _savedTermios.c_oflag,
            c_cflag = _savedTermios.c_cflag,
            c_lflag = _savedTermios.c_lflag,
            c_line = _savedTermios.c_line,
            c_cc = new byte[32]
        };
        Array.Copy(_savedTermios.c_cc, raw.c_cc, 32);
        raw.c_lflag &= ~(ICANON | ECHO /* | ISIG */); // keep ISIG for Ctrl+C handling
        raw.c_iflag &= ~(IXON | ICRNL);               // disable flow control, CR→NL translation
        raw.c_cc[VMIN] = 1;   // read at least 1 byte
        raw.c_cc[VTIME] = 0;  // no timeout (blocking read)
        tcsetattr(fd, TCSANOW, ref raw);
    }

    /// <summary>Restores the original terminal settings.</summary>
    internal static void RestoreTerminal()
    {
        if (!_rawModeSet) return;
        _rawModeSet = false;

        int fd = 0;
        try
        {
            tcsetattr(fd, TCSANOW, ref _savedTermios);
        }
        catch
        {
            // Best effort
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  ITerminal — Key Input
    // ══════════════════════════════════════════════════════════════════

    public bool KeyAvailable => Console.KeyAvailable;

    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        int first = _stdin.ReadByte();
        if (first < 0)
            return default;

        byte b = (byte)first;

        // ── Escape sequences ──────────────────────────────────────────
        if (b == 0x1b)
            return ReadEscapeSequence();

        // ── Tab ───────────────────────────────────────────────────────
        if (b == 0x09)
            return Key('\t', ConsoleKey.Tab);

        // ── Enter / Ctrl+Enter / Shift+Enter ──────────────────────────
        // All variants send \r (0x0D) in legacy terminal protocol.
        // The modifier cannot be detected without Kitty Keyboard Protocol.
        if (b == 0x0d)
            return Key('\r', ConsoleKey.Enter);

        // ── Backspace (0x7F = DEL on Linux, 0x08 = BS) ───────────────
        if (b == 0x7f || b == 0x08)
            return Key('\b', ConsoleKey.Backspace);

        // ── Ctrl+A..Ctrl+Z  (0x01..0x1A) ──────────────────────────────
        // Ctrl+D (0x04) = quit; Ctrl+C (0x03) = copy, etc.
        if (b >= 0x01 && b <= 0x1a)
        {
            ConsoleKey ck = ConsoleKey.A + (b - 0x01);
            return new ConsoleKeyInfo((char)b, ck, false, false, true);
        }

        // ── Printable ASCII ───────────────────────────────────────────
        if (b >= 0x20 && b < 0x7f)
        {
            char c = (char)b;
            return new ConsoleKeyInfo(c, (ConsoleKey)c, false, false, false);
        }

        // ── Fallback: unknown byte ────────────────────────────────────
        return new ConsoleKeyInfo((char)b, (ConsoleKey)b, false, false, false);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Escape Sequence Handling
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads and parses a sequence that starts with ESC (0x1B).
    /// Sets a short read timeout so we don't block forever waiting
    /// for bytes that aren't coming (plain ESC press).
    /// </summary>
    private ConsoleKeyInfo ReadEscapeSequence()
    {
        int oldTimeout = _stdin.ReadTimeout;
        _stdin.ReadTimeout = 25; // ncurses ESCDELAY recommendation

        try
        {
            int b = _stdin.ReadByte();
            if (b < 0)
                return Key('\x1b', ConsoleKey.Escape);

            byte second = (byte)b;

            // ── CSI sequence (ESC [ …) ────────────────────────────────
            if (second == (byte)'[')
            {
                string? seq = ReadCsiBody((byte)'[');
                if (seq is not null)
                {
                    var parsed = ParseCsiSequence(seq);
                    if (parsed is not null)
                        return parsed.Value;
                }
                return Key('\x1b', ConsoleKey.Escape);
            }

            // ── SS3 sequence (ESC O …) ────────────────────────────────
            if (second == (byte)'O')
            {
                string? seq = ReadCsiBody((byte)'O');
                if (seq is not null)
                {
                    var parsed = ParseCsiSequence(seq);
                    if (parsed is not null)
                        return parsed.Value;
                }
                return Key('\x1b', ConsoleKey.Escape);
            }

            // ── Alt+key (ESC followed by a printable character) ───────
            // .NET convention: ESC + letter = Alt+letter
            if (second >= 0x20 && second < 0x7f)
            {
                // Alt+Enter: ESC \r → use Alt modifier
                if (second == 0x0d)
                    return new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, true, false);

                char c = (char)second;
                if (c >= 'a' && c <= 'z')
                    return new ConsoleKeyInfo(c, (ConsoleKey)(c - 0x20), false, true, false);
                return new ConsoleKeyInfo(c, (ConsoleKey)c, false, true, false);
            }

            return Key('\x1b', ConsoleKey.Escape);
        }
        catch (IOException)
        {
            // Timeout — plain ESC press
            return Key('\x1b', ConsoleKey.Escape);
        }
        finally
        {
            _stdin.ReadTimeout = oldTimeout;
        }
    }

    /// <summary>
    /// Reads the body of a CSI/SS3 sequence (everything after the introducer)
    /// until a terminator byte (0x40–0x7E) or timeout.
    /// Returns the full sequence string including the introducer, or null on failure.
    /// </summary>
    private string? ReadCsiBody(byte introducer)
    {
        int total = 1;
        _readBuf[0] = introducer;

        while (total < _readBuf.Length)
        {
            try
            {
                int nb = _stdin.ReadByte();
                if (nb < 0)
                    break;

                byte next = (byte)nb;
                _readBuf[total++] = next;

                // CSI terminator range: 0x40 ('@') to 0x7E ('~')
                if (next >= 0x40 && next <= 0x7E)
                    return Encoding.ASCII.GetString(_readBuf, 0, total);
            }
            catch (IOException)
            {
                break; // timeout mid-sequence
            }
        }

        return null;
    }

    // ══════════════════════════════════════════════════════════════════
    //  CSI Sequence Parser (same logic, exposed for tests)
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
    //  Helpers
    // ══════════════════════════════════════════════════════════════════

    private static ConsoleKeyInfo Key(char ch, ConsoleKey key) =>
        new(ch, key, false, false, false);

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
