using System.Collections.Immutable;

namespace SparseTypes.Generator.Shared;

// Per-member adapter retention for read-only views: one cached adapter per
// collection member is kept while the backing source reference is unchanged,
// so repeated reads on a long-lived view stop reallocating wrappers per
// getter call. Split from the view emitter to keep both files focused.
internal class SparseReadOnlyViewRetention(ImmutableArray<string> memberNames)
{
    private readonly System.Collections.Generic.HashSet<string> _taken = new(
        memberNames,
        System.StringComparer.Ordinal
    );
    private readonly System.Collections.Generic.List<(string Source, string Adapter)> _names = [];
    private readonly System.Collections.Generic.List<string> _types = [];

    /// <summary>Reserves field names for one cached source/adapter pair.</summary>
    /// <param name="property">The view property the pair belongs to.</param>
    /// <param name="adapterType">The concrete adapter type to declare.</param>
    public AdapterFieldNames Enter(string property, string adapterType)
    {
        var source = new System.Text.StringBuilder("__sparse_source_" + property);
        while (!_taken.Add(source.ToString()))
        {
            source.Insert(0, '_');
        }

        var adapter = new System.Text.StringBuilder("__sparse_adapter_" + property);
        while (!_taken.Add(adapter.ToString()))
        {
            adapter.Insert(0, '_');
        }

        var sourceName = source.ToString();
        var adapterName = adapter.ToString();
        _names.Add((sourceName, adapterName));
        _types.Add(adapterType);
        return new AdapterFieldNames(sourceName, adapterName);
    }

    /// <summary>Emits the collected backing-source/adapter field declarations.</summary>
    /// <param name="code">The builder receiving the declarations.</param>
    /// <param name="indent">The member-level indent of the emitted type.</param>
    public void Flush(SharedIndentedBuilder code, int indent)
    {
        for (var index = 0; index < _names.Count; index++)
        {
            code.AppendLineAt(indent, "private object? " + _names[index].Source + ";");
            code.AppendLineAt(
                indent,
                "private " + _types[index] + "? " + _names[index].Adapter + ";"
            );
        }

        _names.Clear();
        _types.Clear();
    }

    /// <summary>Field names of one cached source/adapter pair.</summary>
    internal readonly record struct AdapterFieldNames
    {
        /// <summary>Initializes the field-name pair.</summary>
        /// <param name="source">The backing-source field name.</param>
        /// <param name="adapter">The cached-adapter field name.</param>
        public AdapterFieldNames(string source, string adapter)
        {
            Source = source;
            Adapter = adapter;
        }

        /// <summary>Gets the backing-source field name.</summary>
        public string Source { get; }

        /// <summary>Gets the cached-adapter field name.</summary>
        public string Adapter { get; }
    }
}
