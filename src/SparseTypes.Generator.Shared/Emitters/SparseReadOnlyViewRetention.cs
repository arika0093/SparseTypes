using System.Collections.Immutable;

namespace SparseTypes.Generator.Shared;

// Per-member adapter retention for read-only views: one cached adapter per
// collection member is kept while the backing source reference is unchanged,
// so repeated reads on a long-lived view stop reallocating wrappers per
// getter call. Split from the view emitter to keep both files focused.
internal class SparseReadOnlyViewRetention(ImmutableArray<string> memberNames)
{
    private readonly System.Collections.Generic.HashSet<string> _taken = new(memberNames, System.StringComparer.Ordinal);
    private readonly System.Collections.Generic.List<(string Cache, string Holder, string Adapter)> _names = [];

    /// <summary>Reserves a cache field and holder type for one source/adapter pair.</summary>
    /// <param name="property">The view property the pair belongs to.</param>
    /// <param name="adapterType">The concrete adapter type to declare.</param>
    public AdapterFieldNames Enter(string property, string adapterType)
    {
        var cache = new System.Text.StringBuilder("__sparse_cache_" + property);
        while (!_taken.Add(cache.ToString()))
        {
            cache.Insert(0, '_');
        }

        var holder = new System.Text.StringBuilder("__SparseReadOnlyCache_" + property);
        while (!_taken.Add(holder.ToString()))
        {
            holder.Insert(0, '_');
        }

        var cacheName = cache.ToString();
        var holderName = holder.ToString();
        _names.Add((cacheName, holderName, adapterType));
        return new AdapterFieldNames(cacheName, holderName);
    }

    /// <summary>Emits immutable cache holders and their atomically published references.</summary>
    /// <param name="code">The builder receiving the declarations.</param>
    public void Flush(SharedIndentedBuilder code)
    {
        for (var index = 0; index < _names.Count; index++)
        {
            var (cache, holder, adapter) = _names[index];
            code.AppendLine("private sealed class " + holder);
            code.OpenBlock();
            code.AppendLine("public " + holder + "(object source, " + adapter + " adapter) { Source = source; Adapter = adapter; }");
            code.AppendLine("public readonly object Source;");
            code.AppendLine("public readonly " + adapter + " Adapter;");
            code.CloseBlock();
            code.AppendLine("private volatile " + holder + "? " + cache + ";");
        }

        _names.Clear();
    }

    /// <summary>Field and holder names for one atomically published cache value.</summary>
    internal readonly record struct AdapterFieldNames
    {
        /// <summary>Initializes the cache field and holder names.</summary>
        /// <param name="cache">The cache field name.</param>
        /// <param name="holder">The holder type name.</param>
        public AdapterFieldNames(string cache, string holder)
        {
            Cache = cache;
            Holder = holder;
        }

        /// <summary>Gets the cache field name.</summary>
        public string Cache { get; }

        /// <summary>Gets the holder type name.</summary>
        public string Holder { get; }
    }
}
