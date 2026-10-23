using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;

namespace SparseTypes;

/// <summary>A read-only view of the segments in a path.</summary>
/// <remarks>The default value is empty. Direct indexing and enumeration allocate no objects.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public readonly struct SparsePathSegments : IReadOnlyList<SparsePathSegment>
{
    private readonly SparsePathSegment[]? _segments;
    private readonly int _length;

    internal SparsePathSegments(SparsePathSegment[] segments, int length)
    {
        _segments = segments;
        _length = length;
    }

    /// <summary>Gets the number of segments.</summary>
    public int Count => _length;

    /// <summary>Gets the segment at the specified index.</summary>
    public SparsePathSegment this[int index] =>
        (_segments is not null && (uint)index < (uint)_length ? _segments : Array.Empty<SparsePathSegment>())[index];

    /// <summary>Returns an enumerator over the segments.</summary>
    public Enumerator GetEnumerator() => new(_segments, _length);

    IEnumerator<SparsePathSegment> IEnumerable<SparsePathSegment>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Enumerates the segments of a path without allocating an enumerator object.</summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public struct Enumerator : IEnumerator<SparsePathSegment>
    {
        private readonly SparsePathSegment[]? _segments;
        private readonly int _length;
        private int _index;

        internal Enumerator(SparsePathSegment[]? segments, int length)
        {
            _segments = segments;
            _length = length;
            _index = -1;
        }

        /// <inheritdoc />
        public readonly SparsePathSegment Current =>
            _segments is not null && (uint)_index < (uint)_length ? _segments[_index] : throw new InvalidOperationException();

        readonly object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            if (_segments is not null && _index < _length - 1)
            {
                _index++;
                return true;
            }
            _index = _length;
            return false;
        }

        /// <inheritdoc />
        public void Reset() => _index = -1;

        /// <inheritdoc />
        public readonly void Dispose() { }
    }
}
