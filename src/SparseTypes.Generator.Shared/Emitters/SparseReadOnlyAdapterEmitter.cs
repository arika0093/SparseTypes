namespace SparseTypes.Generator.Shared;

internal static class SparseReadOnlyAdapterEmitter
{
    internal static void AppendCollectionAdapter(SharedIndentedBuilder code, string typeName)
    {
        code.AppendLine(
            "/// <summary>Streaming read-only list view over a live enumerable source.</summary>"
        );
        code.AppendLine(
            "/// <remarks>Sources must be finite, repeatable enumerables that stay stable during a view read (issue #172). <c>Count</c> is O(1) for <c>ICollection</c>/<c>IReadOnlyCollection</c> sources and a full enumeration otherwise; the indexer is O(1) for <c>IList</c>/<c>IReadOnlyList</c> sources and re-enumerates from the start otherwise, so a full indexed loop over a streaming source is quadratic and should be a <c>foreach</c> instead. Each access enumerates anew with no snapshot: single-pass sources drain and per-enumeration-varying sources reflect the latest enumeration.</remarks>"
        );
        code.AppendLine(
            "private sealed class "
                + typeName
                + "<TSource, TView> : global::System.Collections.Generic.IReadOnlyList<TView>"
        );
        code.OpenBlock();
        // Public-first ordering: constructor and public members precede
        // private state and helpers.
        code.AppendLine(
            "public "
                + typeName
                + "(global::System.Collections.Generic.IEnumerable<TSource> source, global::System.Func<TSource, TView> map)"
        );
        code.OpenBlock();
        code.AppendLine("_source = source;");
        code.AppendLine("_map = map;");
        code.CloseBlock();
        code.AppendLine(
            "public int Count => _source is global::System.Collections.Generic.ICollection<TSource> collection ? collection.Count : _source is global::System.Collections.Generic.IReadOnlyCollection<TSource> readOnly ? readOnly.Count : global::System.Linq.Enumerable.Count(_source);"
        );
        code.AppendLine(
            "public TView this[int index] => _source is global::System.Collections.Generic.IList<TSource> list ? _map(list[index]) : _source is global::System.Collections.Generic.IReadOnlyList<TSource> readOnly ? _map(readOnly[index]) : __SparseGet(index);"
        );
        code.AppendLine(
            "public global::System.Collections.Generic.IEnumerator<TView> GetEnumerator() => global::System.Linq.Enumerable.Select(_source, _map).GetEnumerator();"
        );
        code.AppendLine(
            "global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();"
        );
        code.AppendLine(
            "private readonly global::System.Collections.Generic.IEnumerable<TSource> _source;"
        );
        code.AppendLine("private readonly global::System.Func<TSource, TView> _map;");
        code.AppendLine("private TView __SparseGet(int index)");
        code.OpenBlock();
        code.AppendLine(
            "if (index < 0) throw new global::System.ArgumentOutOfRangeException(nameof(index));"
        );
        code.AppendLine("var current = 0;");
        code.AppendLine(
            "foreach (var item in _source) { if (current++ == index) return _map(item); }"
        );
        code.AppendLine("throw new global::System.ArgumentOutOfRangeException(nameof(index));");
        code.CloseBlock();
        code.CloseBlock();
    }

    internal static void AppendDictionaryAdapter(
        SharedIndentedBuilder code,
        string typeName,
        string collectionName
    )
    {
        code.AppendLine(
            "private sealed class "
                + typeName
                + "<TKey, TSource, TView> : global::System.Collections.Generic.IReadOnlyDictionary<TKey, TView> where TKey : notnull"
        );
        code.OpenBlock();
        // Public-first ordering: constructor and public members precede private
        // state.
        code.AppendLine(
            "public "
                + typeName
                + "(global::System.Collections.Generic.IReadOnlyDictionary<TKey, TSource> source, global::System.Func<TSource, TView> map)"
        );
        code.OpenBlock();
        code.AppendLine("_source = source;");
        code.AppendLine("_map = map;");
        code.CloseBlock();
        code.AppendLine("public int Count => _source.Count;");
        code.AppendLine(
            "public global::System.Collections.Generic.IEnumerable<TKey> Keys => new "
                + collectionName
                + "<TKey, TKey>(_source.Keys, static key => key);"
        );
        code.AppendLine(
            "public global::System.Collections.Generic.IEnumerable<TView> Values => global::System.Linq.Enumerable.Select(_source.Values, _map);"
        );
        code.AppendLine("public TView this[TKey key] => _map(_source[key]);");
        code.AppendLine("public bool ContainsKey(TKey key) => _source.ContainsKey(key);");
        code.AppendLine("public bool TryGetValue(TKey key, out TView value)");
        code.OpenBlock();
        code.AppendLine(
            "if (_source.TryGetValue(key, out var current)) { value = _map(current); return true; }"
        );
        code.AppendLine("value = default!;");
        code.AppendLine("return false;");
        code.CloseBlock();
        code.AppendLine(
            "public global::System.Collections.Generic.IEnumerator<global::System.Collections.Generic.KeyValuePair<TKey, TView>> GetEnumerator()"
        );
        code.OpenBlock();
        code.AppendLine(
            "foreach (var pair in _source) yield return new global::System.Collections.Generic.KeyValuePair<TKey, TView>(pair.Key, _map(pair.Value));"
        );
        code.CloseBlock();
        code.AppendLine(
            "global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();"
        );
        code.AppendLine(
            "private readonly global::System.Collections.Generic.IReadOnlyDictionary<TKey, TSource> _source;"
        );
        code.AppendLine("private readonly global::System.Func<TSource, TView> _map;");
        code.CloseBlock();
    }

    internal static void AppendDictionaryEntriesAdapter(SharedIndentedBuilder code, string typeName)
    {
        code.AppendLine(
            "private sealed class "
                + typeName
                + "<TKey, TSource, TKeyView, TValueView> : global::System.Collections.Generic.IReadOnlyCollection<global::System.Collections.Generic.KeyValuePair<TKeyView, TValueView>> where TKey : notnull"
        );
        code.OpenBlock();
        // Public-first ordering: constructor and public members precede private
        // state.
        code.AppendLine(
            "public "
                + typeName
                + "(global::System.Collections.Generic.IReadOnlyDictionary<TKey, TSource> source, global::System.Func<TKey, TKeyView> mapKey, global::System.Func<TSource, TValueView> mapValue)"
        );
        code.OpenBlock();
        code.AppendLine("_source = source;");
        code.AppendLine("_mapKey = mapKey;");
        code.AppendLine("_mapValue = mapValue;");
        code.CloseBlock();
        code.AppendLine("public int Count => _source.Count;");
        code.AppendLine(
            "public global::System.Collections.Generic.IEnumerator<global::System.Collections.Generic.KeyValuePair<TKeyView, TValueView>> GetEnumerator()"
        );
        code.OpenBlock();
        code.AppendLine(
            "foreach (var pair in _source) yield return new global::System.Collections.Generic.KeyValuePair<TKeyView, TValueView>(_mapKey(pair.Key), _mapValue(pair.Value));"
        );
        code.CloseBlock();
        code.AppendLine(
            "global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();"
        );
        code.AppendLine(
            "private readonly global::System.Collections.Generic.IReadOnlyDictionary<TKey, TSource> _source;"
        );
        code.AppendLine("private readonly global::System.Func<TKey, TKeyView> _mapKey;");
        code.AppendLine("private readonly global::System.Func<TSource, TValueView> _mapValue;");
        code.CloseBlock();
    }
}
