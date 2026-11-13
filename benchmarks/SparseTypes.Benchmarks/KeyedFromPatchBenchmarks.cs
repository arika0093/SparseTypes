using BenchmarkDotNet.Attributes;
using SparseTypes;

public enum KeyedFromPatchShape
{
    OneEdit,
    AllEdits,
}

[MemoryDiagnoser]
public class KeyedFromPatchBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(KeyedFromPatchShape.OneEdit, KeyedFromPatchShape.AllEdits)]
    public KeyedFromPatchShape Shape { get; set; }

    private Optional<BenchKeyedServerHolder.Fragment?> _baseline;
    private Optional<BenchKeyedServerHolder.Fragment?> _after;
    private BenchKeyedServerHolder.Patch _patch = null!;

    [GlobalSetup]
    public void Setup()
    {
        var beforeItems = new List<BenchKeyedServer>(Size);
        var afterItems = new List<BenchKeyedServer>(Size);
        for (var index = 0; index < Size; index++)
        {
            beforeItems.Add(
                new BenchKeyedServer
                {
                    Id = "server-" + index,
                    Name = "name-" + index,
                    Count = index,
                }
            );
            var edit = Shape == KeyedFromPatchShape.AllEdits || index == 0;
            afterItems.Add(
                new BenchKeyedServer
                {
                    Id = "server-" + index,
                    Name = edit ? "changed-" + index : "name-" + index,
                    Count = edit ? index + 1 : index,
                }
            );
        }

        _baseline = Optional<BenchKeyedServerHolder.Fragment?>.Present(
            BenchKeyedServerHolder.Fragment.From(new BenchKeyedServerHolder { Items = beforeItems })
        );
        _after = Optional<BenchKeyedServerHolder.Fragment?>.Present(
            BenchKeyedServerHolder.Fragment.From(new BenchKeyedServerHolder { Items = afterItems })
        );
        _patch = BenchKeyedServerHolder.Patch.Between(_baseline, _after);
        if (!BenchKeyedServerHolder.Patch.Between(FromPatch().ToPatch().Apply(_baseline), _after).IsEmpty)
        {
            throw new InvalidOperationException("FromPatch must preserve the baseline and requested keyed endpoint.");
        }
    }

    [Benchmark]
    public BenchKeyedServerHolder.ChangeSet FromPatch() => BenchKeyedServerHolder.ChangeSet.FromPatch(_baseline, _patch, DateTimeOffset.UnixEpoch);
}
