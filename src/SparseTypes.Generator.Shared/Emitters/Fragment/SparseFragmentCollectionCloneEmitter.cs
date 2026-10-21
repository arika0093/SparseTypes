using System.Collections.Generic;
using System.Linq;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits collection clone helpers and the portable <c>IReadOnlySet{T}</c> view.</summary>
/// <remarks>Only first-class shapes (arrays, lists, sets, dictionaries) get generated clone helpers.</remarks>
internal static class SparseFragmentCollectionCloneEmitter
{
    public static bool RequiresPortableSetView(bool bclHashSetImplementsReadOnlySet, IEnumerable<string?> namedTypeDefinitions) =>
        !bclHashSetImplementsReadOnlySet && namedTypeDefinitions.Any(definition => definition == SparseWellKnownNames.ReadOnlySetTypeDefinition);

    public static void AppendCollectionCloneHelpers(SharedIndentedBuilder code, bool includePortableSetView, bool hashSetSupportsCapacity)
    {
        var setCapacity = hashSetSupportsCapacity ? "__CloneCollectionCount(source), " : "";
        code.AppendLine(
            "private static int __CloneCollectionCount<T>(global::System.Collections.Generic.IEnumerable<T> source) => (source as global::System.Collections.Generic.ICollection<T>)?.Count ?? (source as global::System.Collections.Generic.IReadOnlyCollection<T>)?.Count ?? 0;"
        );
        code.AppendLine(
            "private static TSet __CloneSet<T, TSet>(global::System.Collections.Generic.IEnumerable<T> source, global::System.Collections.Generic.Dictionary<object, object> context, global::System.Func<T, global::System.Collections.Generic.Dictionary<object, object>, T>? cloneElement)"
        );
        code.OpenBlock();
        code.AppendLine("if (context.TryGetValue(source, out var existing))");
        code.OpenBlock();
        code.AppendLine("if (existing is TSet typed) return typed;");
        if (includePortableSetView)
        {
            code.AppendLine("if (existing is __SparseReadOnlySet<T> view && view.Inner is TSet inner) return inner;");
        }

        code.AppendLine("return (TSet)existing;");
        code.CloseBlock();
        code.AppendLine("global::System.Collections.Generic.ISet<T> clone;");
        code.AppendLine(
            "clone = new global::System.Collections.Generic.HashSet<T>("
                + setCapacity
                + "(source as global::System.Collections.Generic.HashSet<T>)?.Comparer);"
        );
        code.AppendLine("context.Add(source, clone);");
        // A HashSet copy constructor can retain a source's oversized capacity
        // and cross the LOH threshold. Union into the count-sized set instead.
        code.AppendLine("if (cloneElement is null) clone.UnionWith(source);");
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine("foreach (var item in source) clone.Add(cloneElement(item, context));");
        code.CloseBlock();
        code.AppendLine("return (TSet)(object)clone;");
        code.CloseBlock();
        if (includePortableSetView)
        {
            code.AppendLine(
                "private static TSet __CloneSetView<T, TSet>(global::System.Collections.Generic.IEnumerable<T> source, global::System.Collections.Generic.Dictionary<object, object> context, global::System.Func<T, global::System.Collections.Generic.Dictionary<object, object>, T>? cloneElement)"
            );
            code.OpenBlock();
            code.AppendLine("if (context.TryGetValue(source, out var existing))");
            code.OpenBlock();
            code.AppendLine("if (existing is TSet typed) return typed;");
            code.AppendLine("if (existing is global::System.Collections.Generic.ISet<T> existingSet)");
            code.OpenBlock();
            code.AppendLine("var bridged = new __SparseReadOnlySet<T>(existingSet);");
            code.AppendLine("context[source] = bridged;");
            code.AppendLine("return (TSet)(object)bridged;");
            code.CloseBlock();
            code.AppendLine("return (TSet)existing;");
            code.CloseBlock();
            code.AppendLine("global::System.Collections.Generic.ISet<T> clone;");
            code.AppendLine("if (source is __SparseReadOnlySet<T> sourceView) clone = sourceView.CloneEmpty();");
            code.AppendLine(
                "else clone = new global::System.Collections.Generic.HashSet<T>("
                    + setCapacity
                    + "(source as global::System.Collections.Generic.HashSet<T>)?.Comparer);"
            );
            code.AppendLine("var view = new __SparseReadOnlySet<T>(clone);");
            code.AppendLine("context.Add(source, view);");
            code.AppendLine("if (cloneElement is null) clone.UnionWith(source);");
            code.AppendLine("else");
            code.OpenBlock();
            code.AppendLine("foreach (var item in source) view.Add(cloneElement(item, context));");
            code.CloseBlock();
            code.AppendLine("return (TSet)(object)view;");
            code.CloseBlock();
        }
        code.AppendLine(
            "private static TDictionary __CloneDictionary<TKey, TValue, TDictionary>(global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<TKey, TValue>> source, global::System.Collections.Generic.Dictionary<object, object> context, global::System.Func<TKey, global::System.Collections.Generic.Dictionary<object, object>, TKey>? cloneKey, global::System.Func<TValue, global::System.Collections.Generic.Dictionary<object, object>, TValue>? cloneValue) where TKey : notnull"
        );
        code.OpenBlock();
        code.AppendLine("if (context.TryGetValue(source, out var existing)) return (TDictionary)existing;");
        code.AppendLine("global::System.Collections.Generic.IDictionary<TKey, TValue> clone;");
        code.AppendLine(
            "if (cloneKey is null && cloneValue is null && source is global::System.Collections.Generic.Dictionary<TKey, TValue> dictionary)"
        );
        code.OpenBlock();
        code.AppendLine("clone = new global::System.Collections.Generic.Dictionary<TKey, TValue>(dictionary, dictionary.Comparer);");
        code.AppendLine("context.Add(source, clone);");
        code.AppendLine("return (TDictionary)(object)clone;");
        code.CloseBlock();
        code.AppendLine(
            "if (source is global::System.Collections.Generic.SortedDictionary<TKey, TValue> sorted) clone = new global::System.Collections.Generic.SortedDictionary<TKey, TValue>(sorted.Comparer);"
        );
        code.AppendLine(
            "else if (source is global::System.Collections.Generic.SortedList<TKey, TValue> sortedList) clone = new global::System.Collections.Generic.SortedList<TKey, TValue>(sortedList.Comparer);"
        );
        code.AppendLine(
            "else clone = new global::System.Collections.Generic.Dictionary<TKey, TValue>(__CloneCollectionCount(source), (source as global::System.Collections.Generic.Dictionary<TKey, TValue>)?.Comparer);"
        );
        code.AppendLine("context.Add(source, clone);");
        code.AppendLine(
            "foreach (var pair in source) clone.Add(cloneKey is null ? pair.Key : cloneKey(pair.Key, context), cloneValue is null ? pair.Value : cloneValue(pair.Value, context));"
        );
        code.AppendLine("return (TDictionary)(object)clone;");
        code.CloseBlock();
        code.AppendLine(
            "private static TCollection __CloneArray<T, TCollection>(global::System.Collections.Generic.IEnumerable<T> source, global::System.Collections.Generic.Dictionary<object, object> context, global::System.Func<T, global::System.Collections.Generic.Dictionary<object, object>, T>? cloneElement)"
        );
        code.OpenBlock();
        code.AppendLine("if (context.TryGetValue(source, out var existing)) return (TCollection)existing;");
        code.AppendLine(
            "if (source is global::System.Collections.Generic.List<T>) return __CloneList<T, TCollection>(source, context, cloneElement);"
        );
        code.AppendLine(
            "if (source is global::System.Collections.Generic.HashSet<T>) return __CloneSet<T, TCollection>(source, context, cloneElement);"
        );
        code.AppendLine("if (cloneElement is null)");
        code.OpenBlock();
        code.AppendLine("var copy = global::System.Linq.Enumerable.ToArray(source);");
        code.AppendLine("context.Add(source, copy);");
        code.AppendLine("return (TCollection)(object)copy;");
        code.CloseBlock();
        code.AppendLine("var values = source as T[] ?? global::System.Linq.Enumerable.ToArray(source);");
        code.AppendLine("var clone = new T[values.Length];");
        code.AppendLine("context.Add(source, clone);");
        code.AppendLine("for (var index = 0; index < values.Length; index++) clone[index] = cloneElement(values[index], context);");
        code.AppendLine("return (TCollection)(object)clone;");
        code.CloseBlock();
        code.AppendLine(
            "private static TCollection __CloneList<T, TCollection>(global::System.Collections.Generic.IEnumerable<T> source, global::System.Collections.Generic.Dictionary<object, object> context, global::System.Func<T, global::System.Collections.Generic.Dictionary<object, object>, T>? cloneElement)"
        );
        code.OpenBlock();
        code.AppendLine("if (context.TryGetValue(source, out var existing)) return (TCollection)existing;");
        code.AppendLine("if (source is T[]) return __CloneArray<T, TCollection>(source, context, cloneElement);");
        code.AppendLine("var clone = new global::System.Collections.Generic.List<T>(__CloneCollectionCount(source));");
        code.AppendLine("context.Add(source, clone);");
        code.AppendLine("if (cloneElement is null) clone.AddRange(source);");
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine("foreach (var item in source) clone.Add(cloneElement(item, context));");
        code.CloseBlock();
        code.AppendLine("return (TCollection)(object)clone;");
        code.CloseBlock();
        if (includePortableSetView)
        {
            code.AppendLine(
                "private sealed class __SparseReadOnlySet<T> : global::System.Collections.Generic.ISet<T>, global::System.Collections.Generic.IReadOnlySet<T>"
            );
            code.OpenBlock();
            code.AppendLine("private readonly global::System.Collections.Generic.ISet<T> __inner;");
            code.AppendLine("public __SparseReadOnlySet(global::System.Collections.Generic.ISet<T> inner) => __inner = inner;");
            code.AppendLine("public global::System.Collections.Generic.ISet<T> Inner => __inner;");
            code.AppendLine("public int Count => __inner.Count;");
            code.AppendLine("public bool IsReadOnly => __inner.IsReadOnly;");
            code.AppendLine("public bool Add(T item) => __inner.Add(item);");
            code.AppendLine("void global::System.Collections.Generic.ICollection<T>.Add(T item) => __inner.Add(item);");
            code.AppendLine("public void Clear() => __inner.Clear();");
            code.AppendLine("public bool Contains(T item) => __inner.Contains(item);");
            code.AppendLine("public void CopyTo(T[] array, int arrayIndex) => __inner.CopyTo(array, arrayIndex);");
            code.AppendLine("public bool Remove(T item) => __inner.Remove(item);");
            code.AppendLine("public void ExceptWith(global::System.Collections.Generic.IEnumerable<T> other) => __inner.ExceptWith(other);");
            code.AppendLine("public void IntersectWith(global::System.Collections.Generic.IEnumerable<T> other) => __inner.IntersectWith(other);");
            code.AppendLine(
                "public bool IsProperSubsetOf(global::System.Collections.Generic.IEnumerable<T> other) => __inner.IsProperSubsetOf(other);"
            );
            code.AppendLine(
                "public bool IsProperSupersetOf(global::System.Collections.Generic.IEnumerable<T> other) => __inner.IsProperSupersetOf(other);"
            );
            code.AppendLine("public bool IsSubsetOf(global::System.Collections.Generic.IEnumerable<T> other) => __inner.IsSubsetOf(other);");
            code.AppendLine("public bool IsSupersetOf(global::System.Collections.Generic.IEnumerable<T> other) => __inner.IsSupersetOf(other);");
            code.AppendLine("public bool Overlaps(global::System.Collections.Generic.IEnumerable<T> other) => __inner.Overlaps(other);");
            code.AppendLine("public bool SetEquals(global::System.Collections.Generic.IEnumerable<T> other) => __inner.SetEquals(other);");
            code.AppendLine(
                "public void SymmetricExceptWith(global::System.Collections.Generic.IEnumerable<T> other) => __inner.SymmetricExceptWith(other);"
            );
            code.AppendLine("public void UnionWith(global::System.Collections.Generic.IEnumerable<T> other) => __inner.UnionWith(other);");
            code.AppendLine("public global::System.Collections.Generic.IEnumerator<T> GetEnumerator() => __inner.GetEnumerator();");
            code.AppendLine(
                "global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => __inner.GetEnumerator();"
            );
            code.AppendLine("public global::System.Collections.Generic.ISet<T> CloneEmpty()");
            code.OpenBlock();
            code.AppendLine(
                "if (__inner is global::System.Collections.Generic.HashSet<T> hash) return new global::System.Collections.Generic.HashSet<T>("
                    + (hashSetSupportsCapacity ? "hash.Count, " : "")
                    + "hash.Comparer);"
            );
            code.AppendLine("return new global::System.Collections.Generic.HashSet<T>(" + (hashSetSupportsCapacity ? "Count, null" : "") + ");");
            code.CloseBlock();
            code.CloseBlock();
        }
    }
}
