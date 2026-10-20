using BenchmarkDotNet.Attributes;
using SparseTypes;
using SparseTypes.Blazor;

// Repeated keyed-row resolution: one session.Field per row per field, as a
// dense form performs on every render. The setup pass builds the
// per-collection key index; measured passes are validated index hits, while
// the post-reorder benchmark measures the invalidation plus rebuild path.
[MemoryDiagnoser]
public class KeyedFieldResolutionBenchmarks
{
    [Params(10, 100, 1000, 10000)]
    public int Size { get; set; }

    private BenchFieldStateRoot _model = null!;
    private BenchFieldStateRoot.EditSession _session = null!;
    private SparsePath[] _namePaths = null!;
    private SparsePath[] _countPaths = null!;
    private SparsePath[] _idPaths = null!;

    [GlobalSetup]
    public void Setup()
    {
        _model = new BenchFieldStateRoot { Title = "bench" };
        _namePaths = new SparsePath[Size];
        _countPaths = new SparsePath[Size];
        _idPaths = new SparsePath[Size];
        for (var i = 0; i < Size; i++)
        {
            var key = $"k-{i:00000}";
            _namePaths[i] = SparsePath.Parse<BenchFieldStateRoot>($"Items[\"{key}\"].Name");
            _countPaths[i] = SparsePath.Parse<BenchFieldStateRoot>($"Items[\"{key}\"].Count");
            _idPaths[i] = SparsePath.Parse<BenchFieldStateRoot>($"Items[\"{key}\"].Id");
            _model.Items.Add(
                new BenchFieldStateItem
                {
                    Id = key,
                    Name = $"n-{i}",
                    Count = i,
                }
            );
        }

        _session = _model.CreateEditSession();

        // Prime the index so measured passes reflect steady rerenders; the
        // post-reorder benchmark below measures the rebuild path instead.
        foreach (var path in _namePaths)
        {
            _session.Field(path);
        }
    }

    [Benchmark(Description = "Field N keyed rows x1 field")]
    public int Field_AllRows_OneField()
    {
        var total = 0;
        foreach (var path in _namePaths)
        {
            total += _session.Field(path).FieldName.Length;
        }

        return total;
    }

    [Benchmark(Description = "Field N keyed rows x3 fields")]
    public int Field_AllRows_ThreeFields()
    {
        var total = 0;
        for (var index = 0; index < _namePaths.Length; index++)
        {
            total += _session.Field(_namePaths[index]).FieldName.Length;
            total += _session.Field(_countPaths[index]).FieldName.Length;
            total += _session.Field(_idPaths[index]).FieldName.Length;
        }

        return total;
    }

    [Benchmark(Description = "Field N keyed rows x3 fields after reorder")]
    public int Field_AllRows_AfterReorder()
    {
        _model.Items.Reverse();
        var total = 0;
        for (var index = 0; index < _namePaths.Length; index++)
        {
            total += _session.Field(_namePaths[index]).FieldName.Length;
            total += _session.Field(_countPaths[index]).FieldName.Length;
            total += _session.Field(_idPaths[index]).FieldName.Length;
        }

        return total;
    }

    [Benchmark(Description = "Field one keyed row")]
    public string Field_SingleRow() => _session.Field(_namePaths[0]).FieldName;
}
