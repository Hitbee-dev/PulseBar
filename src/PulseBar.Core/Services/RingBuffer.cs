namespace PulseBar.Core.Services;

/// <summary>Fixed-capacity ring buffer; oldest items are overwritten. Thread-safe.</summary>
public sealed class RingBuffer<T>
{
    private readonly T[] _items;
    private readonly object _gate = new();
    private int _next;
    private int _count;

    public RingBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _items = new T[capacity];
    }

    public int Capacity => _items.Length;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    public void Add(T item)
    {
        lock (_gate)
        {
            _items[_next] = item;
            _next = (_next + 1) % _items.Length;
            if (_count < _items.Length)
            {
                _count++;
            }
        }
    }

    /// <summary>Snapshot in insertion order (oldest first).</summary>
    public T[] ToArray()
    {
        lock (_gate)
        {
            var result = new T[_count];
            var start = _count < _items.Length ? 0 : _next;
            for (var i = 0; i < _count; i++)
            {
                result[i] = _items[(start + i) % _items.Length];
            }

            return result;
        }
    }
}
