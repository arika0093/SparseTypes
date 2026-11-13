using BenchmarkDotNet.Attributes;
using SparseTypes;

[MemoryDiagnoser]
public class SparsePathBulkSuffixBenchmarks
{
    private SparsePath _suffix;
    private SparsePathSegment[] _segments = [];

    [Params(1, 2, 4)]
    public int Depth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _suffix = SparsePath.Root<SparsePathBulkSuffixBenchmarks>();
        _segments = new SparsePathSegment[Depth];
        for (var index = 0; index < Depth; index++)
        {
            _segments[index] = SparsePathSegment.Member("M" + index);
            _suffix = _suffix.Append(_segments[index]);
        }

        _ = _suffix.Segments;
    }

    [Benchmark(Baseline = true, Description = "Root append of materialized suffix")]
    public int AppendMaterializedSuffix_Current() => SparsePath.Root<string>().Append(_suffix).Depth;

    [Benchmark(Description = "Root append by array copy baseline")]
    public int AppendMaterializedSuffix_LegacyArrayCopy()
    {
        var combined = new SparsePathSegment[_segments.Length];
        Array.Copy(_segments, combined, _segments.Length);
        return combined.Length;
    }
}
