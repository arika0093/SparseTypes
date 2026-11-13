using BenchmarkDotNet.Attributes;
using SparseTypes;
using SparseTypes.Generated;

[MemoryDiagnoser]
public class EditSessionSnapshotSharingBenchmarks
{
    private const int CollectionSize = 1000;

    [Params(1, 10, 100)]
    public int FieldCount { get; set; }

    private BenchSnapshotLarge.EditSession _session = null!;
    private SparsePath[] _queries = [];

    [GlobalSetup]
    public void Setup()
    {
        var model = new BenchSnapshotLarge
        {
            Title = "before",
            Count = 1,
            Items = Enumerable
                .Range(0, CollectionSize)
                .Select(index => new BenchFieldStateItem
                {
                    Id = "k-" + index,
                    Name = "n-" + index,
                    Count = index,
                })
                .ToList(),
        };
        _session = model.CreateEditSession();
        _session.Observable.Title = "after";
        _queries = [SparsePath.Root<BenchSnapshotLarge>().Member("Title"), SparsePath.Root<BenchSnapshotLarge>().Member("Count")];
    }

    [Benchmark(Baseline = true, Description = "Fresh live capture per field")]
    public int FreshCapturePerField()
    {
        var hits = 0;
        for (var index = 0; index < FieldCount; index++)
        {
            var snapshot = _session.CaptureStateSnapshot();
            if (snapshot.FieldState(_queries[index % _queries.Length]).IsChanged)
            {
                hits++;
            }
        }

        return hits;
    }

    [Benchmark(Description = "One shared indexed snapshot")]
    public int SharedSnapshot()
    {
        var snapshot = _session.CaptureStateSnapshot();
        var hits = 0;
        for (var index = 0; index < FieldCount; index++)
        {
            if (snapshot.FieldState(_queries[index % _queries.Length]).IsChanged)
            {
                hits++;
            }
        }

        return hits;
    }
}
