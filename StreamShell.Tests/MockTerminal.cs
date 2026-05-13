namespace StreamShell.Tests;

/// <summary>
/// Mock <see cref="ITerminal"/> for testing <see cref="UserInputHandler"/>,
/// <see cref="ConsoleRenderer"/>, and <see cref="ConsoleAppHost"/>.
///
/// This mock:
/// - Controls <see cref="WindowWidth"/>, <see cref="BufferHeight"/> for predictable layout
/// - Queues <see cref="ConsoleKeyInfo"/> values via <see cref="EnqueueKey"/> for input simulation
/// - Captures all <see cref="Write"/>/<see cref="WriteLine"/>, cursor positioning, and <see cref="WindowWidth"/> reads
/// </summary>
internal sealed class MockTerminal : ITerminal
{
    // ── Input Simulation ─────────────────────────────────────────────
    private readonly Queue<ConsoleKeyInfo> _keys = new();

    /// <summary>Enqueue a key that will be returned by the next ReadKey call.</summary>
    public void EnqueueKey(ConsoleKeyInfo key) => _keys.Enqueue(key);

    /// <summary>Enqueue a simple character key (no modifiers).</summary>
    public void EnqueueChar(char c) =>
        _keys.Enqueue(new ConsoleKeyInfo(c, (ConsoleKey)c, false, false, false));

    /// <summary>Enqueue an Enter key (no modifiers).</summary>
    public void EnqueueEnter() =>
        _keys.Enqueue(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

    /// <summary>Enqueue a Shift+Enter.</summary>
    public void EnqueueShiftEnter() =>
        _keys.Enqueue(new ConsoleKeyInfo('\r', ConsoleKey.Enter, true, false, false));

    /// <summary>Enqueue a Tab key.</summary>
    public void EnqueueTab() =>
        _keys.Enqueue(new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false));

    /// <summary>Enqueue an Escape key.</summary>
    public void EnqueueEscape() =>
        _keys.Enqueue(new ConsoleKeyInfo((char)27, ConsoleKey.Escape, false, false, false));

    /// <summary>Enqueue a Ctrl+key combination (e.g. EnqueueCtrl(ConsoleKey.C)).</summary>
    public void EnqueueCtrl(ConsoleKey key) =>
        _keys.Enqueue(new ConsoleKeyInfo((char)(key - ConsoleKey.A + 1), key, false, false, true));

    /// <summary>Enqueue a Shift+arrow or other Shift modifier keys.</summary>
    public void EnqueueShiftArrow(ConsoleKey key) =>
        _keys.Enqueue(new ConsoleKeyInfo('\0', key, true, false, false));

    /// <summary>Enqueue a Ctrl+arrow key.</summary>
    public void EnqueueCtrlArrow(ConsoleKey key) =>
        _keys.Enqueue(new ConsoleKeyInfo('\0', key, false, false, true));

    /// <summary>Enqueue any raw key.</summary>
    public void EnqueueRaw(ConsoleKeyInfo key) => _keys.Enqueue(key);

    public bool KeyAvailable => _keys.Count > 0;
    public ConsoleKeyInfo ReadKey(bool intercept) => _keys.Dequeue();

    // ── Terminal Properties ──────────────────────────────────────────
    public int WindowWidth { get; set; } = 80;
    public int WindowHeight { get; set; } = 24;
    public int BufferHeight { get; set; } = 200;

    // ── Cursor Position ──────────────────────────────────────────────
    public int CursorTop { get; set; }
    public int CursorLeft { get; set; }

    public void SetCursorPosition(int left, int top)
    {
        CursorLeft = Math.Max(0, left);
        CursorTop = Math.Max(0, Math.Min(top, BufferHeight - 1));
        SetCursorCalls.Add((left, top));
    }

    // ── Output Capture ───────────────────────────────────────────────
    public List<string> WrittenLines { get; } = new();
    public List<string> WrittenTexts { get; } = new();
    public List<(int Left, int Top)> SetCursorCalls { get; } = new();

    public void Write(string text)
    {
        WrittenTexts.Add(text);
        CursorLeft += text.Length;
    }

    public void WriteLine()
    {
        WrittenLines.Add("");
        CursorTop++;
        CursorLeft = 0;
    }

    // ── Convenience ──────────────────────────────────────────────────
    public void ClearOutput()
    {
        WrittenLines.Clear();
        WrittenTexts.Clear();
        SetCursorCalls.Clear();
    }
}
