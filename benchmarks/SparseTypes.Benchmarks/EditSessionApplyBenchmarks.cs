using BenchmarkDotNet.Attributes;
using SparseTypes;

[SparseModel]
public partial class BenchApplyChild
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

[SparseModel]
public partial class BenchApplyModel
{
    public string Title { get; set; } = string.Empty;

    public List<BenchApplyChild> Items { get; set; } = new();
}

[MemoryDiagnoser]
public class EditSessionApplyBenchmarks
{
    [Params(64, 512)]
    public int Size { get; set; }

    private BenchApplyModel _baseline = null!;
    private BenchApplyModel _edited = null!;
    private BenchApplyModel.ChangeSet _changes = null!;

    [GlobalSetup]
    public void Setup()
    {
        _baseline = CreateModel();
        _edited = CreateModel();
        _edited.Title = "updated";
        _edited.Items[Size / 2].Value = "updated";
        _changes = BenchApplyModel.ChangeSet.Between(_baseline, _edited);

        var inPlace = CreateModel();
        if (!_changes.TryApplyInPlace(inPlace, out _) || inPlace.Title != "updated")
        {
            throw new InvalidOperationException("In-place application must apply the benchmark change.");
        }

        var sessionModel = CreateModel();
        var session = sessionModel.CreateEditSession();
        if (!session.TryApplyInPlace(_changes, out _) || sessionModel.Title != "updated")
        {
            throw new InvalidOperationException("Edit-session application must apply the benchmark change.");
        }
    }

    [Benchmark(Baseline = true, Description = "ChangeSet materializes updated model")]
    public int TryApplyTo()
    {
        if (!_changes.TryApplyTo(CreateModel(), out var updated, out _) || updated is null)
        {
            throw new InvalidOperationException("The benchmark change did not apply.");
        }

        return updated.Items.Count + updated.Title.Length;
    }

    [Benchmark(Description = "ChangeSet applies directly")]
    public int TryApplyInPlace()
    {
        var model = CreateModel();
        if (!_changes.TryApplyInPlace(model, out _))
        {
            throw new InvalidOperationException("The benchmark change did not apply.");
        }

        return model.Items.Count + model.Title.Length;
    }

    [Benchmark(Description = "EditSession applies directly")]
    public int EditSessionTryApplyInPlace()
    {
        var model = CreateModel();
        var session = model.CreateEditSession();
        if (!session.TryApplyInPlace(_changes, out _))
        {
            throw new InvalidOperationException("The benchmark change did not apply.");
        }

        return model.Items.Count + model.Title.Length;
    }

    private BenchApplyModel CreateModel() =>
        new()
        {
            Title = "baseline",
            Items = Enumerable.Range(0, Size).Select(index => new BenchApplyChild { Id = "id-" + index, Value = "value-" + index }).ToList(),
        };
}
