using System.Threading;

namespace SparseTypes;

internal sealed class SparsePathStorage
{
    private readonly SparsePathStorage? _parent;
    private readonly int _parentLength;
    private readonly SparsePathSegment _segment;
    private SparsePathSegment[]? _materialized;

    internal SparsePathStorage(SparsePathStorage? parent, int parentLength, SparsePathSegment segment)
    {
        _parent = parent;
        _parentLength = parentLength;
        _segment = segment;
        Length = parentLength + 1;
    }

    internal int Length { get; }

    internal SparsePathSegment[]? TryGetMaterializedSegments() => Volatile.Read(ref _materialized);

    internal SparsePathStorage GetAtLength(int length)
    {
        var storage = this;
        while (storage.Length > length)
        {
            storage = storage._parent!;
        }

        return storage;
    }

    internal SparsePathSegment[] GetSegments() => Materialize();

    private SparsePathSegment[] Materialize()
    {
        var existing = Volatile.Read(ref _materialized);
        if (existing is not null)
        {
            return existing;
        }

        var segments = new SparsePathSegment[Length];
        var remaining = Length;
        var storage = this;
        while (remaining > 0)
        {
            segments[--remaining] = storage._segment;
            if (remaining == 0)
            {
                break;
            }

            storage = storage._parent!.GetAtLength(storage._parentLength);
        }

        return Interlocked.CompareExchange(ref _materialized, segments, null) ?? segments;
    }
}
