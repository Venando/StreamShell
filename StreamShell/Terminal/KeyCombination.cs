namespace StreamShell;

/// <summary>
/// Represents a specific key combination for subscription-based key handling.
/// Use the factory methods (<see cref="Alt"/>,
/// <see cref="Ctrl"/>, <see cref="Shift"/>) for concise construction.
/// </summary>
public readonly record struct KeyCombination(ConsoleKey Key, ConsoleModifiers Modifiers)
{
    /// <summary>Returns true when <paramref name="key"/> matches this combination exactly.</summary>
    public bool Matches(ConsoleKeyInfo key)
        => key.Key == Key && key.Modifiers == Modifiers;

    public static KeyCombination Alt(ConsoleKey key) => new(key, ConsoleModifiers.Alt);
    public static KeyCombination Ctrl(ConsoleKey key) => new(key, ConsoleModifiers.Control);
    public static KeyCombination Shift(ConsoleKey key) => new(key, ConsoleModifiers.Shift);
}
