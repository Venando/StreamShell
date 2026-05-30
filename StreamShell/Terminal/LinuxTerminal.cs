using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace StreamShell;

/// <summary>
/// Linux terminal implementation that continuously harvests raw bytes from stdin via P/Invoke
/// on a dedicated background thread and parses VT/xterm escape sequences.
/// </summary>
internal sealed class LinuxTerminal : ITerminal, IDisposable
{
    private const int STDIN_FILENO = 0;
    private Termios _originalTermios;
    private bool _rawEnabled;
    private bool _useConsoleReadKey; // fallback for non-terminal environments
    private bool _disposed;

    // Threading & Buffering primitives
    private readonly BlockingCollection<ConsoleKeyInfo> _inputBuffer = new();
    private Thread? _inputThread;
    private CancellationTokenSource? _cts;

    // Key subscription registry — checked before enqueue
    private readonly KeySubscriptionManager _subscriber = new();

    // ══════════════════════════════════════════════════════════════════
    //  Lifecycle
    // ══════════════════════════════════════════════════════════════════

    public LinuxTerminal()
    {
        EnableRawMode();
        if (!_useConsoleReadKey)
        {
            StartInputReader();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        _inputBuffer.CompleteAdding();

        if (_inputThread != null && _inputThread.IsAlive)
        {
            _inputThread.Join(500); 
        }

        RestoreTerminal();
        _cts?.Dispose();
        _inputBuffer.Dispose();
    }

    private void StartInputReader()
    {
        _cts = new CancellationTokenSource();
        _inputThread = new Thread(InputLoop)
        {
            IsBackground = true,
            Name = "LinuxTerminalInputReader"
        };
        _inputThread.Start();
    }

    // ══════════════════════════════════════════════════════════════════
    //  Background Input Loop (Producer Thread)
    // ══════════════════════════════════════════════════════════════════

    private void InputLoop()
    {
        try
        {
            var token = _cts!.Token;
            var fds = new PollFd[1];
            fds[0].fd = STDIN_FILENO;
            fds[0].events = POLLIN;

            while (!token.IsCancellationRequested)
            {
                // Poll stdin for up to 50ms to keep thread responsive to cancellation
                int pollRet = poll(fds, 1, 50);
                if (pollRet < 0)
                {
                    Thread.Sleep(10); // Interrupted/Error: throttle tight loop
                    continue;
                }
                if (pollRet == 0)
                {
                    continue; // Timeout, loop again
                }

                // Check for exceptional error/hangup conditions on descriptor
                if ((fds[0].revents & (POLLERR | POLLHUP | POLLNVAL)) != 0)
                {
                    Thread.Sleep(50); // Safe backing off on stream detach
                    continue;
                }

                // Bug 1 Fix: Pure blocking single-byte read mode.
                int b = ReadByteBlocking();
                if (b < 0)
                {
                    Thread.Sleep(10); // Safe throttle on empty read/interrupt
                    continue;
                }

                if (b == 0x1B) // ESC → multi-byte escape sequence
                {
                    var seq = ReadEscapeSequence();
                    if (seq is not null)
                    {
                        // Subscription check: if a subscriber handles this key, don't enqueue it.
                        if (!_subscriber.TryHandle(seq.Value))
                            _inputBuffer.Add(seq.Value, token);
                    }
                    else
                    {
                        // Standalone ESC
                        var esc = new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false);
                        if (!_subscriber.TryHandle(esc))
                            _inputBuffer.Add(esc, token);
                    }
                }
                else
                {
                    var mapped = MapSingleByte((byte)b);
                    if (!_subscriber.TryHandle(mapped))
                        _inputBuffer.Add(mapped, token);
                }
            }
        }
        catch (OperationCanceledException) { /* Clean thread exit */ }
        catch (Exception) { /* Handle unexpected errors gracefully */ }
    }

    // ══════════════════════════════════════════════════════════════════
    //  ITerminal — Key Input (Consumer Path)
    // ══════════════════════════════════════════════════════════════════

    public bool KeyAvailable => _useConsoleReadKey ? Console.KeyAvailable : _inputBuffer.Count > 0;

    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        if (_useConsoleReadKey)
            return Console.ReadKey(intercept);

        try
        {
            return _inputBuffer.Take();
        }
        catch (InvalidOperationException)
        {
            return default;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Key Subscription API
    // ══════════════════════════════════════════════════════════════════

    public IDisposable SubscribeKey(KeyCombination combination, Action<ConsoleKeyInfo> handler)
        => _subscriber.Add(combination, handler);

    public IDisposable SubscribeKey(Func<ConsoleKeyInfo, bool> predicate, Action<ConsoleKeyInfo> handler)
        => _subscriber.Add(predicate, handler);

    // ══════════════════════════════════════════════════════════════════
    //  ITerminal — Window / Cursor (delegated to Console)
    // ══════════════════════════════════════════════════════════════════

    public int WindowWidth => GetConsoleProperty(() => Console.WindowWidth, 80);
    public int WindowHeight => GetConsoleProperty(() => Console.WindowHeight, 24);
    public int BufferHeight => GetConsoleProperty(() => Console.BufferHeight, 30);

    private int _trackedCursorLeft = 0;
    private int _trackedCursorTop = 0;

    public int CursorLeft
    {
        get => _trackedCursorLeft;
        set 
        { 
            _trackedCursorLeft = Math.Max(0, value);
            
            // Escape sequence \x1b[{col}G (CHA - Cursor Horizontal Absolute)
            // Moves the cursor to an absolute column position (1-indexed)
            Console.Write($"\x1b[{_trackedCursorLeft + 1}G"); 
        }
    }

    public int CursorTop
    {
        get => _trackedCursorTop;
        set 
        { 
            _trackedCursorTop = Math.Max(0, value);
            
            // Escape sequence \x1b[{row}d (VPA - Vertical Line Position Absolute)
            // Moves the cursor to an absolute row position (1-indexed)
            Console.Write($"\x1b[{_trackedCursorTop + 1}d"); 
        }
    }

    public void SetCursorPosition(int left, int top)
    {
        _trackedCursorLeft = Math.Max(0, left);
        _trackedCursorTop = Math.Max(0, top);
        
        // Escape sequence \x1b[{row};{col}H (CUP - Cursor Position)
        // Moves the cursor to both coordinates simultaneously (1-indexed)
        Console.Write($"\x1b[{_trackedCursorTop + 1};{_trackedCursorLeft + 1}H");
    }

    public void Write(string text)
    {
        try { Console.Write(text); } catch (IOException) {}
    }

    public void WriteLine()
    {
        try { Console.WriteLine(); } catch (IOException) {}
    }

    private static T GetConsoleProperty<T>(Func<T> getter, T fallback)
    {
        try { return getter(); } catch (Exception) { return fallback; }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Raw Mode Configuration
    // ══════════════════════════════════════════════════════════════════

    private void EnableRawMode()
    {
        if (_rawEnabled) return;

        if (tcgetattr(STDIN_FILENO, ref _originalTermios) == -1)
        {
            _useConsoleReadKey = true; // Fallback for IDE, non-TTY streams
            return;
        }

        var raw = _originalTermios;

        // Strip input processing flags
        raw.c_iflag &= ~(IGNBRK | BRKINT | PARMRK | ISTRIP | INLCR | IGNCR | ICRNL | IXON);
        // Bug 2 Fix: Clear out specific bits properly using correct group logic mapping
        raw.c_lflag &= ~(ECHO | ECHONL | ICANON | ISIG | IEXTEN);
        raw.c_cflag &= ~(CSIZE | PARENB);
        raw.c_cflag |= CS8;

        // Bug 1 Fix: Change parameters to standard blocking primitive setup
        raw.c_cc[VMIN] = 1;
        raw.c_cc[VTIME] = 0;

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
    //  Byte Reading Primitives
    // ══════════════════════════════════════════════════════════════════

    /// <summary>Bug 4 — Explicit blocking primitive.</summary>
    private static int ReadByteBlocking()
    {
        Span<byte> buf = stackalloc byte[1];
        IntPtr n = read(STDIN_FILENO, ref buf[0], (IntPtr)1);
        return (long)n <= 0 ? -1 : buf[0];
    }

    // ══════════════════════════════════════════════════════════════════
    //  Escape Sequence Parser
    // ══════════════════════════════════════════════════════════════════

    private static ConsoleKeyInfo? ReadEscapeSequence()
    {
        var seq = new byte[6]; 
        int len = 0;
        var fds = new PollFd[1];
        fds[0].fd = STDIN_FILENO;
        fds[0].events = POLLIN;

        for (int i = 0; i < 5; i++)
        {
            // Bug 3 Fix: Timeout window reduced to exactly 20ms using local poll validation
            if (poll(fds, 1, 20) <= 0) 
                break;

            if ((fds[0].revents & (POLLERR | POLLHUP | POLLNVAL)) != 0)
                break;

            int b = ReadByteBlocking();
            if (b < 0) break;
            seq[len++] = (byte)b;

            if (len > 1 && b >= 0x40 && b <= 0x7E) 
                break;
        }

        if (len == 0)
            return null;

        return ParseEscapeSequence(seq.AsSpan(0, len));
    }

    private static ConsoleKeyInfo? ParseEscapeSequence(Span<byte> seq)
    {
        if (seq.Length == 0) return null;

        char intro = (char)seq[0];
        if (intro != '[' && intro != 'O')
        {
            if (seq.Length == 1 && seq[0] >= 0x20)
                return MapAltChar((char)seq[0]);

            // Ctrl+Alt+letter: terminal sends ESC + control byte (0x01-0x1A).
            // Without this, the control byte is silently consumed and ESC becomes
            // a standalone Escape key, which triggers ResetState() and erases input.
            if (seq.Length == 1 && seq[0] >= 0x01 && seq[0] <= 0x1A)
                return MapAltCtrl(seq[0]);

            return null;
        }

        char final = (char)seq[^1];
        string paramStr = seq.Length > 2
            ? System.Text.Encoding.ASCII.GetString(seq.Slice(1, seq.Length - 2))
            : "";

        int p1 = 0, p2 = 0;
        if (paramStr.Length > 0)
        {
            string[] parts = paramStr.Split(';');
            if (parts.Length > 0) int.TryParse(parts[0], out p1);
            if (parts.Length > 1) int.TryParse(parts[1], out p2);
        }

        bool shift = false, alt = false, ctrl = false;
        if (paramStr.Contains(';'))
        {
            int modParam = p1 == 1 ? p2 : p1;
            (shift, alt, ctrl) = DecodeXtermModifier(modParam);
        }
        else if (p1 >= 2 && p1 <= 8 && final != '~')
        {
            (shift, alt, ctrl) = DecodeXtermModifier(p1);
        }

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
            >= 0x01 and <= 0x1A => CtrlLetter(b),
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
        bool shift = c >= 'A' && c <= 'Z';
        char lower = shift ? char.ToLowerInvariant(c) : c;
        ConsoleKey key = lower >= 'a' && lower <= 'z'
            ? ConsoleKey.A + (lower - 'a')
            : (ConsoleKey)lower;
        return new ConsoleKeyInfo(c, key, shift, true, false);
    }

    /// <summary>
    /// Maps ESC + control byte (0x01-0x1A) to Ctrl+Alt+letter.
    /// The terminal encodes Ctrl+Alt+letter as ESC followed by the
    /// control byte (e.g., Ctrl+Alt+A = ESC 0x01).
    /// </summary>
    private static ConsoleKeyInfo MapAltCtrl(byte ctrlByte)
    {
        char letter = (char)(ctrlByte + 'a' - 1);
        ConsoleKey key = ConsoleKey.A + (ctrlByte - 1);
        return new ConsoleKeyInfo(letter, key, false, true, true);
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
    //  P/Invoke — Structural Header Constant Alignments
    // ══════════════════════════════════════════════════════════════════

    private const int NCCS = 32;

    // Bug 2 Fix: Verified hex mappings strictly conforming to x86_64 <bits/termios.h>
    private const uint IGNBRK  = 0x00000001; 
    private const uint BRKINT  = 0x00000002; 
    private const uint PARMRK  = 0x00000008; 
    private const uint ISTRIP  = 0x00000020; 
    private const uint INLCR   = 0x00000040; 
    private const uint IGNCR   = 0x00000080; 
    private const uint ICRNL   = 0x00000100; 
    private const uint IXON    = 0x00000400; 

    private const uint CSIZE   = 0x00000030; 
    private const uint CS8     = 0x00000030; 
    private const uint PARENB  = 0x00000100; 

    private const uint ISIG    = 0x00000001; 
    private const uint ICANON  = 0x00000002; 
    private const uint ECHO    = 0x00000008; 
    private const uint ECHONL  = 0x00000040; 
    private const uint IEXTEN  = 0x00008000; 

    private const int VMIN  = 6;
    private const int VTIME = 5;
    private const int TCSAFLUSH = 2;
    private const short POLLIN = 1;
    private const short POLLERR = 8;
    private const short POLLHUP = 16;
    private const short POLLNVAL = 32;

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
    private struct PollFd
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
    private static extern int poll([In, Out] PollFd[] fds, int nfds, int timeout);

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr read(int fd, ref byte buf, IntPtr count);
}