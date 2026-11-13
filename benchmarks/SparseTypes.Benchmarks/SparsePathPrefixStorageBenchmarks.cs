using BenchmarkDotNet.Attributes;
using SparseTypes;

[MemoryDiagnoser]
public class SparsePathPrefixStorageBenchmarks
{
    [ThreadStatic]
    private static System.Text.StringBuilder? _cachedBuilder;

    private SparsePathSegment[] _legacySegments = [];
    private SparsePath _path;

    [Params(8, 32, 128, 512)]
    public int Depth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _path = SparsePath.Root<SparsePathPrefixStorageBenchmarks>();
        _legacySegments = new SparsePathSegment[Depth];
        for (var index = 0; index < Depth; index++)
        {
            _legacySegments[index] = SparsePathSegment.Member("M" + index);
            _path = _path.Append(_legacySegments[index]);
        }
        _ = _path.Segments;
    }

    [Benchmark(Baseline = true, Description = "Repeated append construction")]
    public int Append_Current()
    {
        var path = SparsePath.Root<SparsePathPrefixStorageBenchmarks>();
        for (var index = 0; index < Depth; index++)
        {
            path = path.Append(_legacySegments[index]);
        }

        return path.Depth;
    }

    [Benchmark(Description = "Repeated append with array-copy baseline")]
    public int Append_LegacyArrayCopy() => BuildLegacySegments().Length;

    [Benchmark(Description = "Repeated append followed by path hash")]
    public int AppendAndHash_Current()
    {
        var path = SparsePath.Root<SparsePathPrefixStorageBenchmarks>();
        for (var index = 0; index < Depth; index++)
        {
            path = path.Append(_legacySegments[index]);
        }

        return path.GetHashCode();
    }

    [Benchmark(Description = "Array-copy append followed by path hash")]
    public int AppendAndHash_LegacyArrayCopy()
    {
        var segments = BuildLegacySegments();
        var hash = typeof(SparsePathPrefixStorageBenchmarks).GetHashCode();
        foreach (var segment in segments)
        {
            hash = unchecked((hash * 397) ^ segment.GetHashCode());
        }

        return hash;
    }

    private SparsePathSegment[] BuildLegacySegments()
    {
        var segments = Array.Empty<SparsePathSegment>();
        for (var index = 0; index < Depth; index++)
        {
            var combined = new SparsePathSegment[index + 1];
            Array.Copy(segments, combined, index);
            combined[index] = _legacySegments[index];
            segments = combined;
        }

        return segments;
    }

    private static string RenderMembers(SparsePathSegment[] segments)
    {
        var builder = _cachedBuilder ?? new System.Text.StringBuilder();
        _cachedBuilder = null;
        try
        {
            foreach (var segment in segments)
            {
                if (builder.Length > 0)
                {
                    builder.Append('.');
                }

                builder.Append(segment.Name);
            }

            return builder.ToString();
        }
        finally
        {
            if (builder.Capacity <= 1024)
            {
                builder.Clear();
                _cachedBuilder ??= builder;
            }
        }
    }

    [Benchmark(Description = "Interleaved append, hash, enumeration, and formatting")]
    public int AppendAndReadInterleaved_Current()
    {
        var path = SparsePath.Root<SparsePathPrefixStorageBenchmarks>();
        var checksum = 0;
        for (var index = 0; index < Depth; index++)
        {
            path = path.Append(_legacySegments[index]);
            checksum = unchecked((checksum * 397) ^ path.GetHashCode());
            foreach (var segment in path.Segments)
            {
                checksum = unchecked((checksum * 397) ^ segment.GetHashCode());
            }

            checksum = unchecked((checksum * 397) ^ path.ToString().Length);
        }

        return checksum;
    }

    [Benchmark(Description = "Interleaved array-copy append, hash, enumeration, and formatting")]
    public int AppendAndReadInterleaved_LegacyArrayCopy()
    {
        var segments = Array.Empty<SparsePathSegment>();
        var checksum = 0;
        for (var index = 0; index < Depth; index++)
        {
            var combined = new SparsePathSegment[index + 1];
            Array.Copy(segments, combined, index);
            combined[index] = _legacySegments[index];
            segments = combined;
            var path = new LegacyPath(typeof(SparsePathPrefixStorageBenchmarks), segments);
            checksum = unchecked((checksum * 397) ^ path.GetHashCode());

            foreach (var segment in path.Segments)
            {
                checksum = unchecked((checksum * 397) ^ segment.GetHashCode());
            }

            checksum = unchecked((checksum * 397) ^ path.ToString().Length);
        }

        return checksum;
    }

    [Benchmark(Description = "Read materialized path hash")]
    public int Read_GetHashCode() => _path.GetHashCode();

    [Benchmark(Description = "Read materialized path segments")]
    public int Read_EnumerateSegments()
    {
        var hash = 0;
        foreach (var segment in _path.Segments)
        {
            hash = unchecked((hash * 397) ^ segment.GetHashCode());
        }

        return hash;
    }

    [Benchmark(Description = "Read contiguous array hash baseline")]
    public int Read_LegacyHash()
    {
        var hash = typeof(SparsePathPrefixStorageBenchmarks).GetHashCode();
        foreach (var segment in _legacySegments)
        {
            hash = unchecked((hash * 397) ^ segment.GetHashCode());
        }

        return hash;
    }

    [Benchmark(Description = "Read contiguous array segments baseline")]
    public int Read_LegacyEnumerateSegments()
    {
        var hash = 0;
        foreach (var segment in _legacySegments)
        {
            hash = unchecked((hash * 397) ^ segment.GetHashCode());
        }

        return hash;
    }

    private readonly struct LegacyPath(Type rootType, SparsePathSegment[] segments)
    {
        private readonly Type _rootType = rootType;
        private readonly SparsePathSegment[] _segments = segments;

        public LegacySegments Segments => new(_segments);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = _rootType.GetHashCode();
                foreach (var segment in _segments)
                {
                    hash = (hash * 397) ^ segment.GetHashCode();
                }

                return hash;
            }
        }

        public override string ToString() => RenderMembers(_segments);
    }

    private readonly struct LegacySegments(SparsePathSegment[] segments)
    {
        public Enumerator GetEnumerator() => new(segments);

        public struct Enumerator(SparsePathSegment[] segments)
        {
            private readonly SparsePathSegment[] _segments = segments;
            private int _index = -1;

            public readonly SparsePathSegment Current =>
                (uint)_index < (uint)_segments.Length ? _segments[_index] : throw new InvalidOperationException();

            public bool MoveNext()
            {
                if (_index < _segments.Length - 1)
                {
                    _index++;
                    return true;
                }

                _index = _segments.Length;
                return false;
            }
        }
    }
}
