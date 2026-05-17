namespace StreamShell;

public class ThreadSafeQueue<T>
{
    private readonly LinkedList<T> _list = new();
    private readonly object _lock = new();

    // Gets the current number of elements
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _list.Count;
            }
        }
    }

    // Adds an item to the back of the queue (Standard FIFO)
    public void Enqueue(T item)
    {
        lock (_lock)
        {
            _list.AddLast(item);
        }
    }

    // Adds an item directly to the front of the queue (LIFO / Cut-in-line)
    public void EnqueueAsFirst(T item)
    {
        lock (_lock)
        {
            _list.AddFirst(item);
        }
    }

    // Tries to remove and return the item at the front of the queue
    public bool TryDequeue(out T item)
    {
        lock (_lock)
        {
            if (_list.Count == 0)
            {
                item = default!;
                return false;
            }

            item = _list.First.Value;
            _list.RemoveFirst();
            return true;
        }
    }
}
