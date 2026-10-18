using BenchmarkDotNet.Attributes;
using SparseTypes;

[SparseModel]
public partial class RetentionBenchRow
{
    [SparseKey]
    public string Sku { get; set; } = string.Empty;

    public int Quantity { get; set; }
}

[SparseModel]
public partial class RetentionBenchSheet
{
    public List<RetentionBenchRow> Lines { get; set; } = new();

    public Dictionary<string, int> Scores { get; set; } = new();
}

// Read-only view adapter retention (#328): repeated getter reads on one
// long-lived Current view must reuse the cached adapter while the backing
// source reference is unchanged instead of allocating a wrapper per read.
[MemoryDiagnoser]
public class ReadOnlyViewRetentionBenchmarks
{
    private const int RowCount = 128;

    private RetentionBenchSheet.EditSession _session = null!;

    [GlobalSetup]
    public void Setup()
    {
        var sheet = new RetentionBenchSheet
        {
            Lines = Enumerable
                .Range(0, RowCount)
                .Select(index => new RetentionBenchRow { Sku = "sku-" + index, Quantity = index })
                .ToList(),
            Scores = Enumerable
                .Range(0, RowCount)
                .ToDictionary(index => "k-" + index, index => index),
        };
        _session = sheet.CreateEditSession();
    }

    [Benchmark(Description = "128 indexed reads on one retained list view")]
    public int RepeatedListIndexReads()
    {
        var view = _session.Current;
        var total = 0;
        for (var index = 0; index < view.Lines.Count; index++)
        {
            total += view.Lines[index].Quantity;
        }

        return total;
    }

    [Benchmark(Description = "128 getter reads on one retained list view")]
    public int RepeatedListGetterReads()
    {
        var view = _session.Current;
        var total = 0;
        for (var index = 0; index < RowCount; index++)
        {
            total += view.Lines.Count;
        }

        return total;
    }

    [Benchmark(Description = "128 key reads on one retained dictionary view")]
    public int RepeatedDictionaryReads()
    {
        var view = _session.Current;
        var total = 0;
        for (var index = 0; index < RowCount; index++)
        {
            total += view.Scores["k-" + index];
        }

        return total;
    }
}
