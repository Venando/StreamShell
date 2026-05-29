namespace StreamShell;

/// <summary>
/// Abstracts the raw key-reading primitive that <see cref="SystemTerminal"/>'s
/// background reader pulls from. Extracting this lets tests drive the terminal
/// without a real, attached console (where <c>Console.KeyAvailable</c> and
/// <c>Console.ReadKey</c> throw when input is redirected).
/// </summary>
internal interface IConsoleKeySource
{
    /// <summary>
    /// Non-blocking read. Returns true and sets <paramref name="key"/> when a
    /// key was available and consumed; returns false when none is waiting.
    /// Implementations must not throw — redirected/headless input is reported
    /// as "no key available" so the reader thread idles instead of crashing.
    /// </summary>
    bool TryReadKey(out ConsoleKeyInfo key);
}

/// <summary>
/// Default <see cref="IConsoleKeySource"/> backed by <c>System.Console</c>.
/// </summary>
internal sealed class ConsoleKeySource : IConsoleKeySource
{
    public bool TryReadKey(out ConsoleKeyInfo key)
    {
        key = default;
        try
        {
            if (!Console.KeyAvailable)
                return false;

            key = Console.ReadKey(intercept: true);
            return true;
        }
        catch (InvalidOperationException)
        {
            // Input is redirected or no console is attached. Treat as no input —
            // the terminal simply produces no keys in this environment.
            return false;
        }
    }
}
