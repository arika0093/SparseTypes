using BenchmarkDotNet.Attributes;
using SparseTypes;

[MemoryDiagnoser]
public class SparseObservableMembershipBenchmarks
{
    private object[] _removed = [];
    private Dictionary<object, int> _membership = null!;
    private Dictionary<object, byte> _proxies = null!;

    [Params(512, 4096)]
    public int ModelSize { get; set; }

    [Params(8, 128, 512)]
    public int RemovalBatch { get; set; }

    [Params(false, true)]
    public bool RepeatedReferences { get; set; }

    [Params(false, true)]
    public bool MembershipIndexValid { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        if (RemovalBatch > ModelSize)
        {
            throw new InvalidOperationException("Removal batch must fit in the model.");
        }

        var models = new object[ModelSize];
        var shared = new object();
        for (var index = 0; index < models.Length; index++)
        {
            models[index] = RepeatedReferences ? shared : new object();
        }

        _removed = models[..RemovalBatch];
        _membership = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        _proxies = new Dictionary<object, byte>(ReferenceEqualityComparer.Instance);

        for (var index = 0; index < models.Length; index++)
        {
            var item = models[index];
            _membership.TryGetValue(item, out var count);
            _membership[item] = count + 1;
            _proxies[item] = 0;
        }

        foreach (var item in _removed)
        {
            var count = _membership[item];
            if (count == 1)
            {
                _membership.Remove(item);
            }
            else
            {
                _membership[item] = count - 1;
            }
        }
    }

    [Benchmark(Baseline = true, Description = "Range pruning with removed-key set")]
    public int Prune_WithRemovedSet()
    {
        var removedKeys = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var item in _removed)
        {
            removedKeys.Add(item);
        }

        HashSet<object>? live = MembershipIndexValid ? null : CreateLiveSet();
        foreach (var key in removedKeys)
        {
            var isPresent = MembershipIndexValid ? _membership.ContainsKey(key) : live!.Contains(key);
            if (!isPresent)
            {
                _proxies.Remove(key);
            }
        }

        return RestoreProxies();
    }

    [Benchmark(Description = "Range pruning without removed-key set")]
    public int Prune_WithoutRemovedSet()
    {
        HashSet<object>? live = MembershipIndexValid ? null : CreateLiveSet();
        foreach (var key in _removed)
        {
            var isPresent = MembershipIndexValid ? _membership.ContainsKey(key) : live!.Contains(key);
            if (!isPresent)
            {
                _proxies.Remove(key);
            }
        }

        return RestoreProxies();
    }

    private int RestoreProxies()
    {
        var count = _proxies.Count;
        foreach (var item in _removed)
        {
            _proxies[item] = 0;
        }

        return count;
    }

    private HashSet<object> CreateLiveSet()
    {
        var live = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var item in _membership.Keys)
        {
            live.Add(item);
        }

        return live;
    }
}
