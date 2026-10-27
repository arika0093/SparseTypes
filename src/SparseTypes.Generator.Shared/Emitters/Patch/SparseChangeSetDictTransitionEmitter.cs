using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Typed dictionary transition surface.</summary>
internal static class SparseChangeSetDictTransitionEmitter
{
    private const int MaxCombinedLookupCount = 2048;

    internal static void AppendDictTransition(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string prop,
        string trans,
        string cacheName,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var keyType = KeyTypeOf(member);
        var valueType = ValueTypeOf(member);
        var dictType = member.Property.Type.Name;
        var optDict = runtime + "Optional<" + dictType + ">";
        var optValue = runtime + "Optional<" + valueType + ">";
        var comparer = "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var readOnlyDict = "global::System.Collections.Generic.IReadOnlyDictionary<";
        var hasPatch = member.Collection.ValueType?.IsFragmentModel == true;
        var valueCs = hasPatch ? ValueChangeSetOf(member) : null;
        var valueFrag = hasPatch ? ValueFragmentOf(member) : null;
        var optionalValueFragment = hasPatch ? runtime + "Optional<" + valueFrag + "?>" : null;
        var editedType = hasPatch ? valueCs! : valueType;
        var hasContracts = SparseChangeSetContractEmitter.HasContracts(dialect);
        code.AppendLine("/// <summary>Typed dictionary transition for member '" + member.Property.Name + "'.</summary>");
        code.AppendLine(
            "public sealed class "
                + trans
                + " : global::System.Collections.Generic.IEnumerable<"
                + trans
                + ".Item>"
                + SparseChangeSetContractEmitter.DictionaryBases(dialect, member)
        );
        code.OpenBlock();
        // Public surface first, then the internal constructor, then private
        // storage.
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets whether this transition contains no changes.");
        code.AppendLine("public bool IsEmpty { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets whether this transition contains any changes.");
        code.AppendLine("public bool IsChanged => !IsEmpty;");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets the value before the transition.");
        code.AppendLine("public " + optDict + " Before { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets the value after the transition.");
        code.AppendLine("public " + optDict + " After { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets the entries added by the transition.");
        code.AppendLine("public " + readOnlyDict + keyType + ", " + valueType + "> Added { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets the entries removed by the transition.");
        code.AppendLine("public " + readOnlyDict + keyType + ", " + valueType + "> Removed { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets the entries edited by the transition.");
        code.AppendLine("public " + readOnlyDict + keyType + ", " + editedType + "> Edited { get; }");
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine("public global::System.Collections.Generic.IEnumerator<Item> GetEnumerator() => _items.GetEnumerator();");
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine("global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();");
        code.AppendLine("/// <summary>A single typed dictionary entry change.</summary>");
        code.AppendLine("public sealed class Item" + SparseChangeSetContractEmitter.DictionaryItemBases(dialect, member));
        code.OpenBlock();
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets the entry key.");
        code.AppendLine("public " + keyType + " Key { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets the value before the transition.");
        code.AppendLine("public " + optValue + " Before { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets the value after the transition.");
        code.AppendLine("public " + optValue + " After { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets whether the entry was added.");
        code.AppendLine("public bool IsAdded { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets whether the entry was removed.");
        code.AppendLine("public bool IsRemoved { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets whether the entry was edited.");
        code.AppendLine("public bool IsEdited { get; }");
        if (hasPatch)
        {
            SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets the nested transition for the edited entry.");
            code.AppendLine("public " + valueCs + " Edit { get; }");
        }
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(
            code,
            hasContracts,
            "Whether this entry carries no semantic change for the requested key."
        );
        code.AppendLine("public bool IsEmpty { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets whether this entry contains any changes.");
        code.AppendLine("public bool IsChanged => !IsEmpty;");
        code.AppendLine("/// <summary>Shared allocation-light empty entry; retains no value snapshots.</summary>");
        if (hasPatch)
        {
            code.AppendLine(
                "public static Item Empty { get; } = new Item(default!, default, default, false, false, false, "
                    + valueCs
                    + ".Between("
                    + optionalValueFragment
                    + ".Missing, "
                    + optionalValueFragment
                    + ".Missing), true);"
            );
        }
        else
        {
            code.AppendLine("public static Item Empty { get; } = new Item(default!, default, default, false, false, false, true);");
        }

        if (hasPatch)
        {
            code.AppendLine(
                "internal Item("
                    + keyType
                    + " key, "
                    + optValue
                    + " before, "
                    + optValue
                    + " after, bool isAdded, bool isRemoved, bool isEdited, "
                    + valueCs
                    + " edit, bool isEmpty)"
            );
        }
        else
        {
            code.AppendLine(
                "internal Item("
                    + keyType
                    + " key, "
                    + optValue
                    + " before, "
                    + optValue
                    + " after, bool isAdded, bool isRemoved, bool isEdited, bool isEmpty)"
            );
        }

        code.OpenBlock();
        code.AppendLine("Key = key;");
        code.AppendLine("Before = before;");
        code.AppendLine("After = after;");
        code.AppendLine("IsAdded = isAdded;");
        code.AppendLine("IsRemoved = isRemoved;");
        code.AppendLine("IsEdited = isEdited;");
        if (hasPatch)
        {
            code.AppendLine("Edit = edit;");
        }

        code.AppendLine("IsEmpty = isEmpty;");
        code.CloseBlock();
        code.CloseBlock();
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(
            code,
            hasContracts,
            "Looks up the typed change for a dictionary key; never returns null."
        );
        if (!hasContracts)
        {
            code.AppendLine(
                "/// <remarks>Unchanged or unknown keys return <see cref=\"Item.Empty\"/> (allocation-light singleton). "
                    + "Non-empty results are the same instances produced by enumeration.</remarks>"
            );
            code.AppendLine("/// <param name=\"key\">The dictionary key to look up.</param>");
            code.AppendLine("/// <returns>The typed change for the key.</returns>");
        }
        code.AppendLine("public Item GetChange(" + keyType + " key)");
        code.OpenBlock();
        code.AppendLine("if (_itemCount == 0) return Item.Empty;");
        AppendLookupBody(code);
        code.CloseBlock();
        SparseChangeSetContractEmitter.AppendDictionaryContractMembers(code, member, dialect);
        code.AppendLine(
            "internal "
                + trans
                + "("
                + optDict
                + " before, "
                + optDict
                + " after, global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + "> added, global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + "> removed, global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + editedType
                + "> edited, global::System.Collections.Generic.List<Item> items, bool isEmpty)"
        );
        code.OpenBlock();
        code.AppendLine("Before = before;");
        code.AppendLine("After = after;");
        code.AppendLine("Added = new global::System.Collections.ObjectModel.ReadOnlyDictionary<" + keyType + ", " + valueType + ">(added);");
        code.AppendLine("Removed = new global::System.Collections.ObjectModel.ReadOnlyDictionary<" + keyType + ", " + valueType + ">(removed);");
        code.AppendLine("Edited = new global::System.Collections.ObjectModel.ReadOnlyDictionary<" + keyType + ", " + editedType + ">(edited);");
        code.AppendLine("_items = items.AsReadOnly();");
        code.AppendLine("_itemCount = items.Count;");
        code.AppendLine("IsEmpty = isEmpty;");
        code.CloseBlock();
        code.AppendLine(
            "internal static "
                + trans
                + " __SparseEmpty { get; } = new "
                + trans
                + "(default, default, new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">("
                + comparer
                + "), new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">("
                + comparer
                + "), new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + editedType
                + ">("
                + comparer
                + "), new global::System.Collections.Generic.List<"
                + trans
                + ".Item>(), true);"
        );
        code.AppendLine("private readonly global::System.Collections.Generic.IReadOnlyList<Item> _items;");
        code.AppendLine("private global::System.Collections.Generic.Dictionary<" + keyType + ", Item>? _lookup;");
        code.AppendLine("private global::System.Collections.Generic.Dictionary<" + keyType + ", Item>?[]? _lookupPartitions;");
        code.AppendLine("private readonly int _itemCount;");
        AppendLookupHelpers(code, keyType, comparer);
        code.CloseBlock();
        SparseChangeSetDictBuildEmitter.AppendBuild(code, member, trans, dialect, target);
        SparseChangeSetDictProjectionEmitter.AppendProjection(code, member, prop, trans, cacheName, dialect, target);
    }

    private static void AppendLookupBody(SharedIndentedBuilder code)
    {
        code.AppendLine("var __lookup = _lookup;");
        code.AppendLine("if (__lookup is not null && __lookup.TryGetValue(key, out var __found)) return __found;");
        code.AppendLine("return __SparseGetUncachedChange(key);");
    }

    private static void AppendLookupHelpers(SharedIndentedBuilder code, string keyType, string comparer)
    {
        var dictionary = "global::System.Collections.Generic.Dictionary<" + keyType + ", Item>";
        code.AppendLine("private Item __SparseGetUncachedChange(" + keyType + " key)");
        code.OpenBlock();
        code.AppendLine("if (_itemCount > " + MaxCombinedLookupCount + ") return __SparseGetPartitionedChange(key);");
        code.AppendLine("var __lookup = _lookup ?? __SparseBuildLookup();");
        code.AppendLine("return __lookup.TryGetValue(key, out var __found) ? __found : Item.Empty;");
        code.CloseBlock();
        // Keep index construction and the large-diff path out of the hot public
        // lookup.
        code.AppendLine("private " + dictionary + " __SparseBuildLookup()");
        code.OpenBlock();
        code.AppendLine("var __lookup = new " + dictionary + "(_itemCount, " + comparer + ");");
        code.AppendLine("foreach (var __item in _items) __lookup[__item.Key] = __item;");
        code.AppendLine("_lookup = __lookup;");
        code.AppendLine("return __lookup;");
        code.CloseBlock();
        code.AppendLine("private Item __SparseGetPartitionedChange(" + keyType + " key)");
        code.OpenBlock();
        // The public lookup already tried the most recently used partition.
        code.AppendLine("var __lookup = _lookup;");
        code.AppendLine("Item? __found;");
        code.AppendLine("var __partitions = _lookupPartitions;");
        code.AppendLine("if (__partitions is not null)");
        code.OpenBlock();
        code.AppendLine("foreach (var __candidate in __partitions)");
        code.OpenBlock();
        code.AppendLine(
            "if (__candidate is not null && !global::System.Object.ReferenceEquals(__candidate, __lookup) && __candidate.TryGetValue(key, out __found))"
        );
        code.OpenBlock();
        code.AppendLine("_lookup = __candidate;");
        code.AppendLine("return __found;");
        code.CloseBlock();
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("int __kind = Added.ContainsKey(key) ? 0 : Removed.ContainsKey(key) ? 1 : Edited.ContainsKey(key) ? 2 : -1;");
        code.AppendLine("if (__kind < 0) return Item.Empty;");
        code.AppendLine("if (__partitions is null)");
        code.OpenBlock();
        code.AppendLine("__partitions = new " + dictionary + "?[3];");
        code.AppendLine("_lookupPartitions = __partitions;");
        code.CloseBlock();
        code.AppendLine("__lookup = __partitions[__kind];");
        code.AppendLine("if (__lookup is null)");
        code.OpenBlock();
        code.AppendLine("int __count = __kind == 0 ? Added.Count : __kind == 1 ? Removed.Count : Edited.Count;");
        code.AppendLine("__lookup = new " + dictionary + "(__count, " + comparer + ");");
        code.AppendLine("foreach (var __item in _items)");
        code.OpenBlock();
        code.AppendLine("int __itemKind = __item.IsAdded ? 0 : __item.IsRemoved ? 1 : __item.IsEdited ? 2 : -1;");
        code.AppendLine("if (__itemKind == __kind) __lookup[__item.Key] = __item;");
        code.CloseBlock();
        code.AppendLine("__partitions[__kind] = __lookup;");
        code.CloseBlock();
        code.AppendLine("_lookup = __lookup;");
        code.AppendLine("return __lookup.TryGetValue(key, out __found) ? __found : Item.Empty;");
        code.CloseBlock();
    }
}
