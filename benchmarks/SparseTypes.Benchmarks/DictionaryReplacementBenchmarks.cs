using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

[MemoryDiagnoser]
public class DictionaryReplacementBenchmarks
{
    [Params(1, 64)]
    public int EntryCount { get; set; }

    private Dictionary<int, object> _values = null!;
    private readonly object _replacement = new();

    [GlobalSetup]
    public void Setup()
    {
        _values = new Dictionary<int, object>(EntryCount);
        for (var key = 0; key < EntryCount; key++)
        {
            _values.Add(key, new object());
        }
    }

    [MemoryDiagnoser]
    public class RuntimeLockBenchmarks
    {
        private readonly object _monitor = new();
        private readonly System.Threading.Lock _lock = new();
        private int _value;

        [Benchmark(Baseline = true)]
        public int MonitorLock()
        {
            lock (_monitor)
            {
                return ++_value;
            }
        }

        [Benchmark]
        public int RuntimeLock()
        {
            lock (_lock)
            {
                return ++_value;
            }
        }
    }

    [Benchmark(Baseline = true)]
    public int TryGetRemoveAdd()
    {
        if (_values.TryGetValue(0, out _))
        {
            _values.Remove(0);
        }

        _values.Add(0, _replacement);
        return _values.Count;
    }

    [Benchmark]
    public int CollectionsMarshalRef()
    {
        ref var existing = ref CollectionsMarshal.GetValueRefOrAddDefault(_values, 0, out var found);
        if (!found)
        {
            throw new InvalidOperationException("The benchmark key must already exist.");
        }

        existing = _replacement;
        return _values.Count;
    }
}
