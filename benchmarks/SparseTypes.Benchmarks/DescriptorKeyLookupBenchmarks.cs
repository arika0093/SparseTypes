using BenchmarkDotNet.Attributes;
using SparseTypes;

[SparseModel]
public partial class BenchDescriptorKeyedItem
{
    [SparseKey(Unassigned = 0)]
    public int Id { get; set; }

    [SparseTemporaryKey]
    public Guid? TemporaryKey { get; set; }
}

[SparseModel]
public partial class BenchDescriptorKeyedHolder
{
    public List<BenchDescriptorKeyedItem> Items { get; set; } = new();
}

[MemoryDiagnoser]
public class DescriptorKeyLookupBenchmarks
{
    [Params(16, 512)]
    public int Size { get; set; }

    private const int Reads = 100;

    private SparseTypes.Generated.IArrayDescriptor _array = null!;
    private int _assignedKey;
    private Guid _temporaryKey;

    [GlobalSetup]
    public void Setup()
    {
        var model = new BenchDescriptorKeyedHolder
        {
            Items = Enumerable.Range(0, Size).Select(index => new BenchDescriptorKeyedItem { Id = index, TemporaryKey = Guid.NewGuid() }).ToList(),
        };
        _assignedKey = Size - 1;
        _temporaryKey = model.Items[_assignedKey].TemporaryKey!.Value;
        var session = model.CreateEditSession();
        if (!session.Descriptors.TryGet(nameof(BenchDescriptorKeyedHolder.Items), out var items))
        {
            throw new InvalidOperationException("The keyed sequence descriptor must resolve.");
        }

        _array = items.Array ?? throw new InvalidOperationException("The keyed sequence must expose an array descriptor.");
    }

    [Benchmark(Description = "100 repeated descriptor assigned-key lookups")]
    public int RepeatedAssignedKeyLookups()
    {
        var total = 0;
        for (var index = 0; index < Reads; index++)
        {
            total += _array.IndexOfKey(_assignedKey);
        }

        return total;
    }

    [Benchmark(Description = "100 repeated descriptor temporary-key lookups")]
    public int RepeatedTemporaryKeyLookups()
    {
        var total = 0;
        for (var index = 0; index < Reads; index++)
        {
            total += _array.IndexOfTemporaryKey(_temporaryKey);
        }

        return total;
    }
}
