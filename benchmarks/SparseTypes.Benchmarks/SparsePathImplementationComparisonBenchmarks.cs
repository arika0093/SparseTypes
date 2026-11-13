using BenchmarkDotNet.Attributes;
using SparseTypes;

[MemoryDiagnoser]
public class SparsePathSingleAppendBenchmarks
{
    private SparsePath _prefix;
    private SparsePathSegment _segment;

    [Params(1, 4)]
    public int Depth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _prefix = SparsePath.Root<SparsePathSingleAppendBenchmarks>();
        for (var index = 0; index < Depth; index++)
        {
            _prefix = _prefix.Member("M" + index);
        }

        _segment = SparsePathSegment.Member("Appended");
    }

    [Benchmark]
    public SparsePath AppendOneSegment() => _prefix.Append(_segment);
}

[MemoryDiagnoser]
public class SparsePathInterleavedAppendBenchmarks
{
    private SparsePathSegment[] _segments = [];

    [Params(128)]
    public int Depth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _segments = new SparsePathSegment[Depth];
        for (var index = 0; index < Depth; index++)
        {
            _segments[index] = SparsePathSegment.Member("M" + index);
        }
    }

    [Benchmark]
    public int AppendAndReadInterleaved()
    {
        var path = SparsePath.Root<SparsePathInterleavedAppendBenchmarks>();
        var checksum = 0;
        for (var index = 0; index < Depth; index++)
        {
            path = path.Append(_segments[index]);
            checksum = unchecked((checksum * 397) ^ path.GetHashCode());
            foreach (var segment in path.Segments)
            {
                checksum = unchecked((checksum * 397) ^ segment.GetHashCode());
            }

            checksum = unchecked((checksum * 397) ^ path.ToString().Length);
        }

        return checksum;
    }
}
