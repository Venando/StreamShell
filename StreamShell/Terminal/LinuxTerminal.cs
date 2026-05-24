using System.Runtime.InteropServices;

namespace StreamShell;

/// <summary>
/// Linux terminal implementation that reads raw bytes from stdin via P/Invoke
/// and parses VT/xterm escape sequences into <see cref="ConsoleKeyInfo"/>.
///
/// Bypasses <c>Console.ReadKey()</c> entirely — on Linux, .NET may split
/// extended CSI sequences (Ctrl+Arrows, Shift+Arrows) internally, returning
/// ESC standalone and trapping the remaining bytes in its internal buffer.
/// Reading raw bytes avoids this entirely.
/// </summary>
internal sealed class LinuxTerminal : ITerminal, IDisposable
{
    private const int STDIN_FILENO = 0;
    private Termios _originalTermios;
    private bool _rawEnabled;
    private bool _useConsoleReadKey; // fallback for non-terminal environments
    private bool _disposed;

    // ══════════════════════════════════════════════════════════════════
    //  Lifecycle
    // ══════════════════════════════════════════════════════════════════

    public LinuxTerminal()
    {
        EnableRawMode();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RestoreTerminal();
    }

    // ══════════════════════════════════════════════════════════════════
    //  ITerminal — Key Input (raw stdin path)
    // ══════════════════════════════════════════════════════════════════

    public bool KeyAvailable
    {
        get
        {
            if (_useConsoleReadKey)
                return Console.KeyAvailable;

            var fds = new pollfd[1];
            fds[0].fd = STDIN_FILENO;
            fds[0].events = POLLIN;
            return poll(fds, 1, 0) > 0;
        }
    }

    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        if (_useConsoleReadKey)
            return Console.ReadKey(intercept);

        // Read first byte. VMIN=0, VTIME=1 → immediate if data
        // available (caller checks KeyAvailable first).
        int b = ReadByte();
        if (b < 0) return default; // no data (shouldn't happen if KeyAvailable)

        // ESC → multi-byte escape sequence
        if (b == 0x1B)
        {
            var seq = ReadEscapeSequence();
            if (seq is not null)
                return seq.Value;
            // Standalone ESC
            return new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false);
        }

        return MapSingleByte((byte)b);
    }

    // ══════════════════════════════════════════════════════════════════
    //  ITerminal — Window / Cursor (delegated to Console)
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
            catch (ArgumentOutOfRangeException) { }
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
            catch (ArgumentOutOfRangeException) { }
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

    // ══════════════════════════════════════════════════════════════════
    //  Raw Mode
    // ══════════════════════════════════════════════════════════════════

    private void EnableRawMode()
    {
        if (_rawEnabled) return;

        if (tcgetattr(STDIN_FILENO, ref _originalTermios) == -1)
        {
            // Not a terminal (piped input, CI, etc.) — fall back to
            // Console.ReadKey which handles this gracefully.
            _useConsoleReadKey = true;
            return;
        }

        var raw = _originalTermios;

        // Only disable input-processing flags — we need raw reads
        // but must keep output processing (OPOST) intact so that
        // \n → \r\n translation and cursor positioning still work.
        // cfmakeraw() clears OPOST too, which breaks Spectre.Console
        // rendering by leaving the cursor at random columns.
        raw.c_iflag &= ~(IGNBRK | BRKINT | PARMRK | ISTRIP | INLCR | IGNCR | ICRNL | IXON);
        raw.c_lflag &= ~(ECHO | ECHONL | ICANON | ISIG | IEXTEN);
        raw.c_cflag &= ~(CSIZE | PARENB);
        raw.c_cflag |= CS8;

        // Non-blocking reads with brief timeout for multi-byte sequences:
        // VMIN=0, VTIME=1 → read() returns immediately when data is
        // available, or blocks up to 100ms if no data.
        raw.c_cc[VMIN] = 0;
        raw.c_cc[VTIME] = 1;

        if (tcsetattr(STDIN_FILENO, TCSAFLUSH, ref raw) == -1)
            return;

        _rawEnabled = true;
    }

    private void RestoreTerminal()
    {
        if (!_rawEnabled) return;
        tcsetattr(STDIN_FILENO, TCSAFLUSH, ref _originalTermios);
        _rawEnabled = false;
    }

    // ══════════════════════════════════════════════════════════════════
    //  Byte Reading
    // ══════════════════════════════════════════════════════════════════

    /// <summary>Reads a single byte from stdin. Returns -1 if no data.</summary>
    private static int ReadByte()
    {
        Span<byte> buf = stackalloc byte[1];
        IntPtr n = read(STDIN_FILENO, ref buf[0], (IntPtr)1);
        return (long)n <= 0 ? -1 : buf[0];
    }

    // ══════════════════════════════════════════════════════════════════
    //  Escape Sequence Parser
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads an escape sequence after the initial ESC byte has been consumed.
    /// Tries to read up to 5 additional bytes over ~15ms to collect the
    /// full CSI/SS3 sequence, then parses it.
    /// </summary>
    private static ConsoleKeyInfo? ReadEscapeSequence()
    {
        var seq = new byte[6]; // max 6 bytes after ESC (e.g. [1;8D = 5 bytes)
        int len = 0;

        // Read up to 5 bytes after ESC. The terminal sends the entire
        // sequence atomically — these reads return immediately.
        for (int i = 0; i < 5; i++)
        {
            int b = ReadByte();
            if (b < 0) break;
            seq[len++] = (byte)b;

            // First byte is always the CSI/SS3 introducer ([ = 0x5B
            // or O = 0x4F) — skip terminator check for it.
            // After that, break when we hit the terminator (0x40-0x7E).
            if (len > 1 && b >= 0x40 && b <= 0x7E) break;
        }

        if (len == 0)
            return null; // standalone ESC

        return ParseEscapeSequence(seq.AsSpan(0, len));
    }

    /// <summary>
    /// Parses a CSI/SS3 escape sequence into a ConsoleKeyInfo.
    /// The span contains bytes after the initial ESC (0x1B).
    /// </summary>
    private static ConsoleKeyInfo? ParseEscapeSequence(Span<byte> seq)
    {
        if (seq.Length == 0) return null;

        char intro = (char)seq[0];
        if (intro != '[' && intro != 'O')
        {
            // Alt+key: ESC followed by a single printable character
            if (seq.Length == 1 && seq[0] >= 0x20)
                return MapAltChar((char)seq[0]);
            return null;
        }

        // Extract final character and parameter string
        char final = (char)seq[^1];
        string paramStr = seq.Length > 2
            ? System.Text.Encoding.ASCII.GetString(seq.Slice(1, seq.Length - 2))
            : "";

        // Parse parameters
        int p1 = 0, p2 = 0;
        if (paramStr.Length > 0)
        {
            string[] parts = paramStr.Split(';');
            if (parts.Length > 0) int.TryParse(parts[0], out p1);
            if (parts.Length > 1) int.TryParse(parts[1], out p2);
        }

        // Decode modifiers
        bool shift = false, alt = false, ctrl = false;
        if (paramStr.Contains(';'))
        {
            int modParam = p1 == 1 ? p2 : p1;
            (shift, alt, ctrl) = DecodeXtermModifier(modParam);
        }
        else if (p1 >= 2 && p1 <= 8 && final != '~')
        {
            // Bare modifier format (Linux console): CSI mod letter
            (shift, alt, ctrl) = DecodeXtermModifier(p1);
        }

        // Map to ConsoleKey
        ConsoleKey? key = MapSequenceToKey(intro, final, p1);
        if (key is null)
            return null;

        return new ConsoleKeyInfo('\0', key.Value, shift, alt, ctrl);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Key Mapping
    // ══════════════════════════════════════════════════════════════════

    private static ConsoleKeyInfo MapSingleByte(byte b)
    {
        return b switch
        {
            0x0D or 0x0A => new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false),
            0x09 => new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false),
            0x20 => new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false),
            0x7F => new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false),
            >= 0x01 and <= 0x1A => CtrlLetter(b), // Ctrl+A through Ctrl+Z
            _ => new ConsoleKeyInfo((char)b, (ConsoleKey)b, false, false, false),
        };
    }

    private static ConsoleKeyInfo CtrlLetter(byte ctrlByte)
    {
        char letter = (char)(ctrlByte + 'a' - 1);
        ConsoleKey key = ConsoleKey.A + (ctrlByte - 1);
        return new ConsoleKeyInfo(letter, key, false, false, true);
    }

    private static ConsoleKeyInfo MapAltChar(char c)
    {
        ConsoleKey key = c >= 'a' && c <= 'z'
            ? ConsoleKey.A + (c - 'a')
            : (ConsoleKey)c;
        return new ConsoleKeyInfo(c, key, false, true, false);
    }

    private static ConsoleKey? MapSequenceToKey(char intro, char final, int p1)
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
                '~' => MapTildeKeyCode(p1),
                _ => null,
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
                _ => null,
            };
        }

        return null;
    }

    private static ConsoleKey? MapTildeKeyCode(int code)
    {
        return code switch
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
            _ => null,
        };
    }

    private static (bool shift, bool alt, bool ctrl) DecodeXtermModifier(int mod)
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
            _ => (false, false, false),
        };
    }

    // ══════════════════════════════════════════════════════════════════
    //  P/Invoke — termios
    // ══════════════════════════════════════════════════════════════════

    private const int NCCS = 32;

    // termios flags
    private const uint IGNBRK  = 1 << 0;
    private const uint BRKINT  = 1 << 1;
    private const uint PARMRK  = 1 << 3;
    private const uint ISTRIP  = 1 << 5;
    private const uint INLCR   = 1 << 6;
    private const uint IGNCR   = 1 << 7;
    private const uint ICRNL   = 1 << 8;
    private const uint IXON    = 1 << 10;
    private const uint ECHO    = 1 << 3;
    private const uint ECHONL  = 1 << 6;
    private const uint ICANON  = 1 << 1;
    private const uint ISIG    = 1 << 0;
    private const uint IEXTEN  = 1 << 15;
    private const uint CSIZE   = 0x30;
    private const uint CS8     = 0x30;
    private const uint PARENB  = 1 << 8;

    private const int VMIN  = 6;
    private const int VTIME = 5;
    private const int TCSAFLUSH = 2;

    // poll() constants
    private const short POLLIN = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct Termios
    {
        public uint c_iflag;
        public uint c_oflag;
        public uint c_cflag;
        public uint c_lflag;
        public byte c_line;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = NCCS)]
        public byte[] c_cc;

        public uint c_ispeed;
        public uint c_ospeed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct pollfd
    {
        public int fd;
        public short events;
        public short revents;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int tcgetattr(int fd, ref Termios termios);

    [DllImport("libc", SetLastError = true)]
    private static extern int tcsetattr(int fd, int optional_actions, ref Termios termios);

    [DllImport("libc", SetLastError = true)]
    private static extern int poll([In, Out] pollfd[] fds, int nfds, int timeout);

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr read(int fd, ref byte buf, IntPtr count);
}
