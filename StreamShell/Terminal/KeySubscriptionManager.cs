using System;
using System.Collections.Generic;

namespace StreamShell;

/// <summary>
/// Thread-safe registry of key subscriptions. Used internally by terminal
/// implementations to check whether a parsed key should be consumed by a
/// subscriber instead of entering the normal input pipeline.
/// </summary>
internal sealed class KeySubscriptionManager
{
    private readonly object _lock = new();
    private readonly List<SubscriptionEntry> _subscriptions = new();

    private sealed record SubscriptionEntry(
        Func<ConsoleKeyInfo, bool> Predicate,
        Action<ConsoleKeyInfo> Handler);

    public IDisposable Add(KeyCombination combination, Action<ConsoleKeyInfo> handler)
    {
        var entry = new SubscriptionEntry(combination.Matches, handler);
        lock (_lock) _subscriptions.Add(entry);
        return new Token(this, entry);
    }

    public IDisposable Add(Func<ConsoleKeyInfo, bool> predicate, Action<ConsoleKeyInfo> handler)
    {
        var entry = new SubscriptionEntry(predicate, handler);
        lock (_lock) _subscriptions.Add(entry);
        return new Token(this, entry);
    }

    /// <summary>
    /// Checks each subscription in registration order. If a predicate matches,
    /// invokes its handler and returns true (key is consumed).
    /// Returns false if no subscriber handled the key.
    /// </summary>
    public bool TryHandle(ConsoleKeyInfo key)
    {
        lock (_lock)
        {
            foreach (var entry in _subscriptions)
            {
                if (entry.Predicate(key))
                {
                    entry.Handler(key);
                    return true;
                }
            }
        }
        return false;
    }

    private void Remove(SubscriptionEntry entry)
    {
        lock (_lock) _subscriptions.Remove(entry);
    }

    private sealed class Token : IDisposable
    {
        private readonly KeySubscriptionManager _manager;
        private readonly SubscriptionEntry _entry;
        private bool _disposed;

        public Token(KeySubscriptionManager manager, SubscriptionEntry entry)
        {
            _manager = manager;
            _entry = entry;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _manager.Remove(_entry);
        }
    }
}
