using System.Collections.Concurrent;
using System.Diagnostics;

namespace StreamShell.Tests;

// ═════════════════════════════════════════════════════════════════════
//  SystemTerminal — Background Reader / Subscription Tests
//
//  SystemTerminal harvests keys on a dedicated background thread, filters
//  out keys consumed by subscriptions, and buffers the rest for the consumer.
//  These tests drive it through an injected IConsoleKeySource so no real,
//  attached console is required. Because the reader runs on its own thread,
//  assertions wait for propagation via WaitUntil instead of fixed sleeps.
// ═════════════════════════════════════════════════════════════════════

public class SystemTerminalTests
{
    // ── Test double: a key source we can push keys into ──────────────
    private sealed class FakeKeySource : IConsoleKeySource
    {
        private readonly ConcurrentQueue<ConsoleKeyInfo> _queue = new();

        public void Push(ConsoleKeyInfo key) => _queue.Enqueue(key);

        public bool TryReadKey(out ConsoleKeyInfo key) => _queue.TryDequeue(out key);
    }

    private static SystemTerminal NewTerminal(FakeKeySource source) =>
        new(source, idlePollMs: 1);

    private static ConsoleKeyInfo Key(
        ConsoleKey k, char c = '\0', bool shift = false, bool alt = false, bool ctrl = false)
        => new(c, k, shift, alt, ctrl);

    /// <summary>Polls a condition until true or the timeout elapses.</summary>
    private static bool WaitUntil(Func<bool> condition, int timeoutMs = 2000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            Thread.Sleep(2);
        }
        return condition();
    }

    // ══════════════════════════════════════════════════════════════════
    //  Basic buffering
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void PushedKey_BecomesAvailable_AndIsReadBack()
    {
        var source = new FakeKeySource();
        using var terminal = NewTerminal(source);

        source.Push(Key(ConsoleKey.A, 'a'));

        Assert.True(WaitUntil(() => terminal.KeyAvailable), "key never became available");

        var read = terminal.ReadKey(intercept: true);
        Assert.Equal(ConsoleKey.A, read.Key);
        Assert.Equal('a', read.KeyChar);
    }

    [Fact]
    public void MultipleKeys_AreReadBack_InFifoOrder()
    {
        var source = new FakeKeySource();
        using var terminal = NewTerminal(source);

        // Simulate a paste burst: many key events arriving back-to-back.
        foreach (char c in "hello")
            source.Push(Key((ConsoleKey)char.ToUpperInvariant(c), c));

        Assert.True(WaitUntil(() => terminal.KeyAvailable));

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 5; i++)
        {
            Assert.True(WaitUntil(() => terminal.KeyAvailable));
            sb.Append(terminal.ReadKey(intercept: true).KeyChar);
        }

        Assert.Equal("hello", sb.ToString());
    }

    [Fact]
    public void NoKeys_KeyAvailableIsFalse()
    {
        var source = new FakeKeySource();
        using var terminal = NewTerminal(source);

        // Give the reader thread a chance to spin a few idle cycles.
        Thread.Sleep(20);

        Assert.False(terminal.KeyAvailable);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Subscription filtering (the whole point of the architecture)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void SubscribedCombination_IsConsumed_AndNeverBuffered()
    {
        var source = new FakeKeySource();
        using var terminal = NewTerminal(source);

        int handlerCalls = 0;
        terminal.SubscribeKey(
            KeyCombination.Alt(ConsoleKey.D),
            _ => Interlocked.Increment(ref handlerCalls));

        source.Push(Key(ConsoleKey.D, alt: true));

        // Handler fires WITHOUT the consumer ever calling ReadKey — proving the
        // key is filtered on the producer thread, decoupled from the render loop.
        Assert.True(WaitUntil(() => Volatile.Read(ref handlerCalls) == 1),
            "subscription handler was not invoked");
        Assert.False(terminal.KeyAvailable, "consumed key leaked into the buffer");
    }

    [Fact]
    public void NonMatchingKey_PassesThrough_WhenSubscriptionExists()
    {
        var source = new FakeKeySource();
        using var terminal = NewTerminal(source);

        int handlerCalls = 0;
        terminal.SubscribeKey(
            KeyCombination.Alt(ConsoleKey.D),
            _ => Interlocked.Increment(ref handlerCalls));

        // Alt+F does not match the Alt+D subscription.
        source.Push(Key(ConsoleKey.F, alt: true));

        Assert.True(WaitUntil(() => terminal.KeyAvailable));
        Assert.Equal(ConsoleKey.F, terminal.ReadKey(intercept: true).Key);
        Assert.Equal(0, Volatile.Read(ref handlerCalls));
    }

    [Fact]
    public void PredicateSubscription_ConsumesMatchingKeys()
    {
        var source = new FakeKeySource();
        using var terminal = NewTerminal(source);

        int handlerCalls = 0;
        terminal.SubscribeKey(
            k => k.Modifiers.HasFlag(ConsoleModifiers.Control),
            _ => Interlocked.Increment(ref handlerCalls));

        source.Push(Key(ConsoleKey.X, ctrl: true));      // consumed by predicate
        source.Push(Key(ConsoleKey.Y, 'y'));             // passes through

        Assert.True(WaitUntil(() => terminal.KeyAvailable));
        Assert.Equal(ConsoleKey.Y, terminal.ReadKey(intercept: true).Key);
        Assert.Equal(1, Volatile.Read(ref handlerCalls));
    }

    [Fact]
    public void DisposingSubscriptionToken_StopsFiltering()
    {
        var source = new FakeKeySource();
        using var terminal = NewTerminal(source);

        int handlerCalls = 0;
        IDisposable token = terminal.SubscribeKey(
            KeyCombination.Alt(ConsoleKey.D),
            _ => Interlocked.Increment(ref handlerCalls));

        token.Dispose();

        source.Push(Key(ConsoleKey.D, alt: true));

        // With the subscription removed, the key now flows through to the buffer.
        Assert.True(WaitUntil(() => terminal.KeyAvailable));
        Assert.Equal(ConsoleKey.D, terminal.ReadKey(intercept: true).Key);
        Assert.Equal(0, Volatile.Read(ref handlerCalls));
    }

    [Fact]
    public void SubscriptionHandler_RunsOffTheCallingThread()
    {
        var source = new FakeKeySource();
        using var terminal = NewTerminal(source);

        int callerThreadId = Environment.CurrentManagedThreadId;
        int handlerThreadId = 0;
        terminal.SubscribeKey(
            KeyCombination.Alt(ConsoleKey.D),
            _ => Volatile.Write(ref handlerThreadId, Environment.CurrentManagedThreadId));

        source.Push(Key(ConsoleKey.D, alt: true));

        Assert.True(WaitUntil(() => Volatile.Read(ref handlerThreadId) != 0));
        Assert.NotEqual(callerThreadId, Volatile.Read(ref handlerThreadId));
    }

    // ══════════════════════════════════════════════════════════════════
    //  Lifecycle
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var source = new FakeKeySource();
        var terminal = NewTerminal(source);

        terminal.Dispose();
        terminal.Dispose(); // must not throw
    }

    [Fact]
    public void ReadKey_AfterDispose_ReturnsDefault_WithoutBlocking()
    {
        var source = new FakeKeySource();
        var terminal = NewTerminal(source);
        terminal.Dispose();

        // Buffer is empty and completed: Take would throw, ReadKey swallows it.
        var read = terminal.ReadKey(intercept: true);
        Assert.Equal(default, read);
    }

    [Fact]
    public void Dispose_StopsTheReaderThread_FromConsumingMoreKeys()
    {
        var source = new FakeKeySource();
        var terminal = NewTerminal(source);
        terminal.Dispose();

        // Pushing after disposal must not be observed (thread has stopped).
        source.Push(Key(ConsoleKey.A, 'a'));
        Thread.Sleep(20);

        Assert.True(source.TryReadKey(out _), "reader thread kept draining after Dispose");
    }
}
