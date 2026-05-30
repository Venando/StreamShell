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

    /// <summary>
    /// Set by the reader the instant it pulls a key while the OS source still
    /// has more queued, and cleared the instant the source reports dry. It lets
    /// a consumer that drains the buffer faster than the reader fills it tell an
    /// in-flight burst (e.g. a paste) from genuine end-of-input — restoring the
    /// synchronous <c>Console.KeyAvailable</c> signal the old design relied on.
    /// </summary>
    private volatile bool _sourceHasMore;

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
                    // A key was waiting, so the OS input buffer may still hold the
                    // rest of a burst. Flag that more input is in flight before
                    // enqueueing, so a faster consumer treats a momentarily empty
                    // buffer as "burst continuing" rather than "input ended". The
                    // flag is cleared the moment TryReadKey reports the source dry.
                    _sourceHasMore = true;

                    // Subscription check on the producer thread: if a subscriber
                    // handles this key, it is consumed and never enqueued.
                    if (!_subscriber.TryHandle(key))
                        _inputBuffer.Add(key, token);
                }
                else
                {
                    // Source drained: the burst (if any) has ended.
                    _sourceHasMore = false;

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

    /// <summary>
    /// Reports whether more input is coming, blocking only while a burst is
    /// actively draining from the OS into the buffer.
    ///
    /// The reader enqueues keys asynchronously, so an empty buffer right now may
    /// still be receiving an in-flight burst — e.g. the tail of a multi-line
    /// paste that conhost injects as many individual key events. Returns true
    /// immediately if a key is already buffered; false immediately if the reader
    /// reports the source drained (so isolated keystrokes and submits stay
    /// latency-free); otherwise waits up to <paramref name="timeoutMs"/> for the
    /// next burst key to arrive. Callers use this instead of the instantaneous
    /// <see cref="KeyAvailable"/> snapshot, which races the producer thread.
    /// </summary>
    public bool WaitForKeyAvailable(int timeoutMs)
    {
        if (_inputBuffer.Count > 0)
            return true;
        // No buffered key and the reader reports the source dry → input has
        // genuinely ended; don't wait.
        if (!_sourceHasMore || timeoutMs <= 0 || _cts.IsCancellationRequested)
            return false;

        // A burst is still streaming from the OS into the buffer: give the next
        // key a moment to land so the whole burst is consumed in a single pass.
        SpinWait.SpinUntil(
            () => _inputBuffer.Count > 0 || !_sourceHasMore || _cts.IsCancellationRequested,
            timeoutMs);
        return _inputBuffer.Count > 0;
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
