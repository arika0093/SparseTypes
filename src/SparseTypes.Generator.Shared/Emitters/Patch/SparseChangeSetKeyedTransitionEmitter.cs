using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetBetweenEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetComposeEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetDictBetweenEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetDictComposeEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetDictRebaseEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetDictTransitionEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedBetweenEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedComposeEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedRebaseEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetMatchEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetPatchSyncEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetRebaseEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetTransitionEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Typed keyed-sequence transition surface.</summary>
internal static class SparseChangeSetKeyedTransitionEmitter
{
    internal static void AppendKeyedTransition(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string prop,
        string trans,
        string cacheName,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null
    )
    {
        var shell = code;
        var runtime = dialect.RuntimeNamespace;
        var keyType = KeyTypeOf(member);
        var comparer = "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var elementType = ElementTypeOf(member);
        var listType = member.Property.Type.Name;
        var optList = runtime + "Optional<" + listType + ">";
        var optElement = runtime + "Optional<" + elementType + ">";
        var elementCs = ElementChangeSetOf(member);
        var elementFrag = ElementFragmentOf(member);
        var optionalElementFragment = runtime + "Optional<" + elementFrag + "?>";
        var readOnlyList = "global::System.Collections.Generic.IReadOnlyList<";
        var readOnlyDict = "global::System.Collections.Generic.IReadOnlyDictionary<";
        // Transition type.
        code.AppendLine("/// <summary>Typed keyed transition for member '" + member.Property.Name + "'.</summary>");
        code.AppendLine(
            "public sealed class "
                + trans
                + " : global::System.Collections.Generic.IEnumerable<"
                + trans
                + ".Item>"
                + SparseChangeSetContractEmitter.KeyedBases(dialect, member)
        );
        code.OpenBlock();
        // Public surface first, then the internal constructor, then private
        // storage.
        code.AppendLine("/// <summary>Gets whether this transition contains no changes.</summary>");
        code.AppendLine("public bool IsEmpty { get; }");
        code.AppendLine("/// <summary>Gets whether this transition contains any changes.</summary>");
        code.AppendLine("public bool IsChanged => !IsEmpty;");
        code.AppendLine("/// <summary>Gets the value before the transition.</summary>");
        code.AppendLine("public " + optList + " Before { get; }");
        code.AppendLine("/// <summary>Gets the value after the transition.</summary>");
        code.AppendLine("public " + optList + " After { get; }");
        code.AppendLine("/// <summary>Gets the elements added by the transition.</summary>");
        code.AppendLine("public " + readOnlyList + elementType + "> Added { get; }");
        code.AppendLine("/// <summary>Gets the elements removed by the transition.</summary>");
        code.AppendLine("public " + readOnlyList + elementType + "> Removed { get; }");
        code.AppendLine("/// <summary>Gets the entries edited by the transition.</summary>");
        code.AppendLine("public " + readOnlyDict + keyType + ", " + elementCs + "> Edited { get; }");
        code.AppendLine("/// <summary>Gets the key order before the transition.</summary>");
        code.AppendLine("public " + readOnlyList + keyType + "> BeforeOrder { get; }");
        code.AppendLine("/// <summary>Gets the key order after the transition.</summary>");
        code.AppendLine("public " + readOnlyList + keyType + "> AfterOrder { get; }");
        code.AppendLine("/// <summary>Gets whether the key order changed.</summary>");
        code.AppendLine("public bool OrderChanged { get; }");
        code.AppendLine("/// <summary>Gets the enumerator over changed entries.</summary>");
        code.AppendLine("/// <returns>The enumerator over changed entries.</returns>");
        code.AppendLine("public global::System.Collections.Generic.IEnumerator<Item> GetEnumerator() => _items.GetEnumerator();");
        code.AppendLine("global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();");
        code.AppendLine("/// <summary>A single typed keyed item change.</summary>");
        code.AppendLine("/// <remarks>Empty lookup results expose <see cref=\"IsEmpty\"/> and are never enumerated.</remarks>");
        code.AppendLine("public sealed class Item" + SparseChangeSetContractEmitter.KeyedItemBases(dialect, member));
        code.OpenBlock();
        code.AppendLine("/// <summary>Gets the entry key.</summary>");
        code.AppendLine("public " + keyType + " Key { get; }");
        SparseChangeSetKeyedTempTransitionEmitter.AppendItemTemporaryKeyProperty(code, member);
        code.AppendLine("/// <summary>Gets the value before the transition.</summary>");
        code.AppendLine("public " + optElement + " Before { get; }");
        code.AppendLine("/// <summary>Gets the value after the transition.</summary>");
        code.AppendLine("public " + optElement + " After { get; }");
        code.AppendLine("/// <summary>Gets the index before the transition.</summary>");
        code.AppendLine("public int BeforeIndex { get; }");
        code.AppendLine("/// <summary>Gets the index after the transition.</summary>");
        code.AppendLine("public int AfterIndex { get; }");
        code.AppendLine("/// <summary>Gets whether the entry was added.</summary>");
        code.AppendLine("public bool IsAdded { get; }");
        code.AppendLine("/// <summary>Gets whether the entry was removed.</summary>");
        code.AppendLine("public bool IsRemoved { get; }");
        code.AppendLine("/// <summary>Gets whether the entry was edited.</summary>");
        code.AppendLine("public bool IsEdited { get; }");
        code.AppendLine("/// <summary>Gets whether the entry was reordered.</summary>");
        code.AppendLine("public bool IsReordered { get; }");
        code.AppendLine("/// <summary>Gets the nested transition for the edited entry.</summary>");
        code.AppendLine("public " + elementCs + " Edit { get; }");
        code.AppendLine("/// <summary>Whether this item carries no semantic change for the requested key.</summary>");
        code.AppendLine("public bool IsEmpty { get; }");
        code.AppendLine("/// <summary>Gets whether this item contains any changes.</summary>");
        code.AppendLine("public bool IsChanged => !IsEmpty;");
        code.AppendLine("/// <summary>Shared allocation-light empty item; retains no element snapshots.</summary>");
        code.AppendLine(
            "public static Item Empty { get; } = new Item(default!, null, default, default, -1, -1, false, false, false, false, "
                + elementCs
                + ".Between("
                + optionalElementFragment
                + ".Missing, "
                + optionalElementFragment
                + ".Missing), true);"
        );
        code.AppendLine(
            "internal Item("
                + keyType
                + " key, global::System.Guid? temporaryKey, "
                + optElement
                + " before, "
                + optElement
                + " after, int beforeIndex, int afterIndex, bool isAdded, bool isRemoved, bool isEdited, bool isReordered, "
                + elementCs
                + " edit, bool isEmpty)"
        );
        code.OpenBlock();
        code.AppendLine("Key = key;");
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            code.AppendLine("TemporaryKey = temporaryKey;");
        }

        code.AppendLine("Before = before;");
        code.AppendLine("After = after;");
        code.AppendLine("BeforeIndex = beforeIndex;");
        code.AppendLine("AfterIndex = afterIndex;");
        code.AppendLine("IsAdded = isAdded;");
        code.AppendLine("IsRemoved = isRemoved;");
        code.AppendLine("IsEdited = isEdited;");
        code.AppendLine("IsReordered = isReordered;");
        code.AppendLine("Edit = edit;");
        code.AppendLine("IsEmpty = isEmpty;");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("/// <summary>Looks up the typed change for a stable key; never returns null.</summary>");
        code.AppendLine(
            "/// <remarks>Unchanged or unknown keys return <see cref=\"Item.Empty\"/> (allocation-light singleton shared across lookups). "
                + "The unassigned sentinel is never a stable lookup: it always returns <see cref=\"Item.Empty\"/>. "
                + "Non-empty results are the same instances produced by enumeration. "
                + "BeforeIndex/AfterIndex are absolute collection indexes; IsReordered observes surviving-key relative rank.</remarks>"
        );
        code.AppendLine("/// <param name=\"key\">The stable key to look up.</param>");
        code.AppendLine("/// <returns>The typed change for the key.</returns>");
        code.AppendLine("public Item GetChange(" + keyType + " key)");
        code.OpenBlock();
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine("if (" + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key") + ") return Item.Empty;");
        }
        code.AppendLine("var __lookup = _lookup;");
        code.AppendLine("if (__lookup is null)");
        code.OpenBlock();
        var lookupCapacity = "_items.Count";
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine("int __lookupCount = 0;");
            code.AppendLine(
                "for (int __index = 0; __index < _items.Count; __index++) if (!"
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "_items[__index].Key")
                    + ") __lookupCount++;"
            );
            lookupCapacity = "__lookupCount";
        }
        code.AppendLine(
            "__lookup = new global::System.Collections.Generic.Dictionary<" + keyType + ", Item>(" + lookupCapacity + ", " + comparer + ");"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine(
                "foreach (var __item in _items) if (!"
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__item.Key")
                    + ") __lookup[__item.Key] = __item;"
            );
        }
        else
        {
            code.AppendLine("foreach (var __item in _items) __lookup[__item.Key] = __item;");
        }

        code.AppendLine("_lookup = __lookup;");
        code.CloseBlock();
        code.AppendLine("return __lookup.TryGetValue(key, out var __found) ? __found : Item.Empty;");
        code.CloseBlock();
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            SparseChangeSetKeyedTempTransitionEmitter.AppendGetTemporaryChange(code, member);
        }
        SparseChangeSetContractEmitter.AppendKeyedContractMembers(code, member, dialect);
        code.AppendLine(
            "internal "
                + trans
                + "("
                + optList
                + " before, "
                + optList
                + " after, global::System.Collections.Generic.List<"
                + elementType
                + "> added, global::System.Collections.Generic.List<"
                + elementType
                + "> removed, global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementCs
                + "> edited, global::System.Collections.Generic.List<"
                + keyType
                + "> beforeOrder, global::System.Collections.Generic.List<"
                + keyType
                + "> afterOrder, bool orderChanged, global::System.Collections.Generic.List<Item> items, bool isEmpty)"
        );
        code.OpenBlock();
        code.AppendLine("Before = before;");
        code.AppendLine("After = after;");
        code.AppendLine("Added = added.AsReadOnly();");
        code.AppendLine("Removed = removed.AsReadOnly();");
        code.AppendLine("Edited = new global::System.Collections.ObjectModel.ReadOnlyDictionary<" + keyType + ", " + elementCs + ">(edited);");
        code.AppendLine("BeforeOrder = beforeOrder.AsReadOnly();");
        code.AppendLine("AfterOrder = afterOrder.AsReadOnly();");
        code.AppendLine("OrderChanged = orderChanged;");
        code.AppendLine("_items = items.AsReadOnly();");
        code.AppendLine("IsEmpty = isEmpty;");
        code.CloseBlock();
        code.AppendLine("private readonly global::System.Collections.Generic.IReadOnlyList<Item> _items;");
        code.AppendLine("private global::System.Collections.Generic.Dictionary<" + keyType + ", Item>? _lookup;");
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            SparseChangeSetKeyedTempTransitionEmitter.AppendTemporaryLookupField(code);
        }
        code.CloseBlock();
        // Key helper: stays on the facade for single-file emission; it moves
        // into the operation container (below) when relocating.
        if (target is null)
        {
            code.AppendLine("private static " + keyType + " __SparseKeyOf_ChangeSet_" + member.Id + "(" + elementType + " element)");
            code.OpenBlock();
            code.AppendLine(
                "if ((object?)element is null) throw new global::System.InvalidOperationException(\"Null elements have no stable key.\");"
            );
            code.AppendLine(KeyOfBody(member));
            code.CloseBlock();
            if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
            {
                SparseChangeSetKeyedTempTransitionEmitter.AppendTemporaryKeyExtractor(code, member, elementType, documented: false);
            }
        }
        if (target is not null)
        {
            // Projection algorithms move into the change-set operation
            // container; the typed shell above stays on the facade.
            code = target.ChangeSetOperations;
            code.AppendLine("/// <summary>Extracts the stable key for a keyed transition element.</summary>");
            code.AppendLine("private static " + keyType + " __SparseKeyOf_ChangeSet_" + member.Id + "(" + elementType + " element)");
            code.OpenBlock();
            code.AppendLine(
                "if ((object?)element is null) throw new global::System.InvalidOperationException(\"Null elements have no stable key.\");"
            );
            code.AppendLine(KeyOfBody(member));
            code.CloseBlock();
            if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
            {
                SparseChangeSetKeyedTempTransitionEmitter.AppendTemporaryKeyExtractor(code, member, elementType, documented: true);
            }
        }
        SparseChangeSetKeyedBuildEmitter.AppendBuild(code, member, trans, dialect, target);
        SparseChangeSetKeyedProjectionEmitter.AppendProjection(shell, member, prop, trans, cacheName, dialect, target);
    }

    internal static string KeyOfBody(SparseMemberModel member)
    {
        // The key type is the declared property type, which may itself be a
        // tuple or value object for composite identity.
        var keys = member.Collection.KeyPropertyNames;
        if (keys.Length == 1)
        {
            return "return element."
                + SparseNaming.EscapeIdentifier(keys[0])
                + (SparseKeyedCollectionEmitter.HasUnassignedKey(member) ? "!" : "")
                + ";";
        }

        return "throw new global::System.InvalidOperationException(\"Keyed collection has no usable key property.\");";
    }
}
