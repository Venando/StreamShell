namespace StreamShell;

/// <summary>
/// Default <see cref="ITerminal"/> implementation that wraps
/// <c>System.Console</c> directly.
/// </summary>
internal sealed class SystemTerminal : ITerminal
{
    private readonly KeySubscriptionManager _subscriber = new();

    public bool KeyAvailable => Console.KeyAvailable;

    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        while (true)
        {
            var key = Console.ReadKey(intercept);
            if (!_subscriber.TryHandle(key))
                return key;
            // Subscriber consumed the key — read the next one
        }
    }

    public IDisposable SubscribeKey(KeyCombination combination, Action<ConsoleKeyInfo> handler)
        => _subscriber.Add(combination, handler);

    public IDisposable SubscribeKey(Func<ConsoleKeyInfo, bool> predicate, Action<ConsoleKeyInfo> handler)
        => _subscriber.Add(predicate, handler);

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
}
