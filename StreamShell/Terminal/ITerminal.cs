namespace StreamShell;

/// <summary>
/// Abstracts the minimal System.Console API surface needed by
/// <see cref="UserInputHandler"/>, <see cref="ConsoleRenderer"/>,
/// and <see cref="ConsoleAppHost"/>.
///
/// The default implementation wraps System.Console directly.
/// Test implementations provide controlled values and capture output.
/// </summary>
public interface ITerminal
{
    /// <summary>Console.KeyAvailable — whether a key press is waiting.</summary>
    bool KeyAvailable { get; }

    /// <summary>Console.ReadKey — reads the next key event.</summary>
    ConsoleKeyInfo ReadKey(bool intercept);

    /// <summary>Console.WindowWidth — current console window width in characters.</summary>
    int WindowWidth { get; }

    /// <summary>Console.WindowHeight — current console window height in lines.</summary>
    int WindowHeight { get; }

    /// <summary>Console.BufferHeight — current buffer height in lines.</summary>
    int BufferHeight { get; }

    /// <summary>Console.CursorTop — get or set the cursor row.</summary>
    int CursorTop { get; set; }

    /// <summary>Console.CursorLeft — get or set the cursor column.</summary>
    int CursorLeft { get; set; }

    /// <summary>Console.SetCursorPosition — positions the cursor.</summary>
    void SetCursorPosition(int left, int top);

    /// <summary>Console.Write(string) — writes text without a trailing newline.</summary>
    void Write(string text);

    /// <summary>Console.WriteLine() — writes an empty line.</summary>
    void WriteLine();

    /// <summary>
    /// Subscribes to a specific key combination. When matched, the handler is invoked
    /// and the key is consumed — it will NOT be added to the normal input queue.
    /// Returns an <see cref="IDisposable"/> that removes the subscription when disposed.
    /// </summary>
    IDisposable SubscribeKey(KeyCombination combination, Action<ConsoleKeyInfo> handler);

    /// <summary>
    /// Subscribes to keys matching a predicate. When matched, the handler is invoked
    /// and the key is consumed — it will NOT be added to the normal input queue.
    /// Returns an <see cref="IDisposable"/> that removes the subscription when disposed.
    /// </summary>
    IDisposable SubscribeKey(Func<ConsoleKeyInfo, bool> predicate, Action<ConsoleKeyInfo> handler);
}
