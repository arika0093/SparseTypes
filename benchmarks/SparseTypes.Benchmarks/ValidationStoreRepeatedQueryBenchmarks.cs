using BenchmarkDotNet.Attributes;
using SparseTypes;

public sealed class BenchRepeatedValidationQueryModel { }

[MemoryDiagnoser]
public class ValidationStoreRepeatedQueryBenchmarks
{
    private const int ReadsPerOperation = 32;

    [Params(256, 4096)]
    public int PathCount { get; set; }

    private SparseValidationStore<BenchRepeatedValidationQueryModel> _store = null!;
    private SparsePath _subtree;
    private string _propertyName = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        var root = SparsePath.Root<BenchRepeatedValidationQueryModel>();
        _subtree = root.Member("Parent");
        _propertyName = _subtree.ToString();
        _store = new SparseValidationStore<BenchRepeatedValidationQueryModel>();

        var matchingPathCount = Math.Max(1, PathCount / 20);
        for (var index = 0; index < PathCount; index++)
        {
            var path = index < matchingPathCount ? _subtree.Member("Field" + index) : root.Member("Other" + index);
            _store.SetErrors(path, "benchmark", ["error"]);
        }
    }

    [Benchmark(Description = "Repeated typed subtree query")]
    public int GetPathErrors_Repeated()
    {
        var total = 0;
        for (var index = 0; index < ReadsPerOperation; index++)
        {
            total += _store.GetPathErrors(_subtree).Count;
        }

        return total;
    }

    [Benchmark(Description = "Repeated property-name subtree query")]
    public int GetErrors_Repeated()
    {
        var total = 0;
        for (var index = 0; index < ReadsPerOperation; index++)
        {
            foreach (var _ in _store.GetErrors(_propertyName))
            {
                total++;
            }
        }

        return total;
    }

    [Benchmark(Description = "Repeated all-errors query")]
    public int GetAllErrors_Repeated()
    {
        var total = 0;
        for (var index = 0; index < ReadsPerOperation; index++)
        {
            total += _store.GetAllErrors().Count;
        }

        return total;
    }
}
