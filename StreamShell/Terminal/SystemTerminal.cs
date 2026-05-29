using System.Collections.Concurrent;

namespace StreamShell;

/// <summary>
/// Default <see cref="ITerminal"/> implementation for non-Linux platforms.
///
/// Mirrors <see cref="LinuxTerminal"/>'s producer/consumer architecture: a
/// dedicated background thread harvests keys from the console, filters out any
/// consumed by a <see cref="KeySubscriptionManager"/> subscription, and enqueues
/// the rest into a buffer. The consumer (<see cref="UserInputHandler"/>, which
/// runs on the render thread) drains that buffer via <see cref="ReadKey"/>.
///
/// This decoupling is deliberate: subscription handlers run on the reader thread,
/// never on the render thread, so a consumed hotkey can never block the message
/// stream. The previous synchronous design called <c>Console.ReadKey</c> again to
/// drain after a consumed key, which blocked the render loop until the next
/// keystroke. The split removes that failure mode by construction.
/// </summary>
internal sealed class SystemTerminal : ITerminal, IDisposable
{
    private readonly IConsoleKeySource _source;
    private readonly int _idlePollMs;
    private readonly KeySubscriptionManager _subscriber = new();
    private readonly BlockingCollection<ConsoleKeyInfo> _inputBuffer = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _inputThread;
    private bool _disposed;

    public SystemTerminal() : this(new ConsoleKeySource()) { }

    /// <summary>Test seam: inject a key source and a faster idle poll interval.</summary>
    internal SystemTerminal(IConsoleKeySource source, int idlePollMs = 5)
    {
        _source = source;
        _idlePollMs = idlePollMs;
        _inputThread = new Thread(InputLoop)
        {
            IsBackground = true,
            Name = "SystemTerminalInputReader"
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
            var token = _cts.Token;
            while (!token.IsCancellationRequested)
            {
                // Drain every key currently waiting in a tight loop, then idle.
                // During a burst (e.g. a paste, which conhost injects as many
                // individual key events) TryReadKey keeps returning true, so the
                // whole burst lands in the buffer before the consumer's next tick.
                if (_source.TryReadKey(out var key))
                {
                    // Subscription check on the producer thread: if a subscriber
                    // handles this key, it is consumed and never enqueued.
                    if (!_subscriber.TryHandle(key))
                        _inputBuffer.Add(key, token);
                }
                else
                {
                    // Nothing waiting. Windows console exposes no blocking poll
                    // primitive, so sleep briefly to avoid a hot spin while
                    // staying responsive to the next keystroke and cancellation.
                    if (token.WaitHandle.WaitOne(_idlePollMs))
                        break; // cancelled
                }
            }
        }
        catch (OperationCanceledException) { /* clean exit on Dispose */ }
        catch (ObjectDisposedException) { /* buffer disposed during shutdown */ }
        catch (Exception) { /* never let the reader thread tear down the process */ }
    }

    // ══════════════════════════════════════════════════════════════════
    //  ITerminal — Key Input (Consumer Path)
    // ══════════════════════════════════════════════════════════════════

    public bool KeyAvailable => _inputBuffer.Count > 0;

    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        try
        {
            return _inputBuffer.Take();
        }
        catch (InvalidOperationException)
        {
            // CompleteAdding was called (Dispose) and the buffer is empty.
            // (ObjectDisposedException derives from InvalidOperationException.)
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

    public int WindowWidth
    {
        get
        {
            try { return Console.WindowWidth; }
            catch (IOException) { return 80; } // headless/test fallback
        }
    }

    public int WindowHeight
    {
        get
        {
            try { return Console.WindowHeight; }
            catch (IOException) { return 24; } // headless/test fallback
        }
    }

    public int BufferHeight
    {
        get
        {
            try { return Console.BufferHeight; }
            catch (IOException) { return 30; } // headless/test fallback
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
    //  Lifecycle
    // ══════════════════════════════════════════════════════════════════

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _inputBuffer.CompleteAdding();

        if (_inputThread.IsAlive)
            _inputThread.Join(500);

        _cts.Dispose();
        _inputBuffer.Dispose();
    }
}
