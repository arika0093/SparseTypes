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
        var hasContracts = SparseChangeSetContractEmitter.HasContracts(dialect);
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
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets whether this transition contains no changes.");
        code.AppendLine("public bool IsEmpty { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets whether this transition contains any changes.");
        code.AppendLine("public bool IsChanged => !IsEmpty;");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets the value before the transition.");
        code.AppendLine("public " + optList + " Before { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets the value after the transition.");
        code.AppendLine("public " + optList + " After { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets the elements added by the transition.");
        code.AppendLine("public " + readOnlyList + elementType + "> Added { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets the elements removed by the transition.");
        code.AppendLine("public " + readOnlyList + elementType + "> Removed { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets the entries edited by the transition.");
        code.AppendLine("public " + readOnlyDict + keyType + ", " + elementCs + "> Edited { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets the key order before the transition.");
        code.AppendLine("public " + readOnlyList + keyType + "> BeforeOrder { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets the key order after the transition.");
        code.AppendLine("public " + readOnlyList + keyType + "> AfterOrder { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets whether the key order changed.");
        code.AppendLine("public bool OrderChanged { get; }");
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine("public global::System.Collections.Generic.IEnumerator<Item> GetEnumerator() => _items.GetEnumerator();");
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine("global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();");
        code.AppendLine("/// <summary>A single typed keyed item change.</summary>");
        code.AppendLine("/// <remarks>Empty lookup results expose <see cref=\"IsEmpty\"/> and are never enumerated.</remarks>");
        code.AppendLine("public sealed class Item" + SparseChangeSetContractEmitter.KeyedItemBases(dialect, member));
        code.OpenBlock();
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets the entry key.");
        code.AppendLine("public " + keyType + " Key { get; }");
        SparseChangeSetKeyedTempTransitionEmitter.AppendItemTemporaryKeyProperty(code, member, hasContracts);
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets the value before the transition.");
        code.AppendLine("public " + optElement + " Before { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets the value after the transition.");
        code.AppendLine("public " + optElement + " After { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets the index before the transition.");
        code.AppendLine("public int BeforeIndex { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets the index after the transition.");
        code.AppendLine("public int AfterIndex { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets whether the entry was added.");
        code.AppendLine("public bool IsAdded { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets whether the entry was removed.");
        code.AppendLine("public bool IsRemoved { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets whether the entry was edited.");
        code.AppendLine("public bool IsEdited { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets whether the entry was reordered.");
        code.AppendLine("public bool IsReordered { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets the nested transition for the edited entry.");
        code.AppendLine("public " + elementCs + " Edit { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(
            code,
            hasContracts,
            "Whether this item carries no semantic change for the requested key."
        );
        code.AppendLine("public bool IsEmpty { get; }");
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(code, hasContracts, "Gets whether this item contains any changes.");
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
        SparseChangeSetContractEmitter.AppendInterfaceMemberDocumentation(
            code,
            hasContracts,
            "Looks up the typed change for a stable key; never returns null."
        );
        if (!hasContracts)
        {
            code.AppendLine(
                "/// <remarks>Unchanged or unknown keys return <see cref=\"Item.Empty\"/> (allocation-light singleton shared across lookups). "
                    + "The unassigned sentinel is never a stable lookup: it always returns <see cref=\"Item.Empty\"/>. "
                    + "Non-empty results are the same instances produced by enumeration. "
                    + "BeforeIndex/AfterIndex are absolute collection indexes; IsReordered observes surviving-key relative rank.</remarks>"
            );
            code.AppendLine("/// <param name=\"key\">The stable key to look up.</param>");
            code.AppendLine("/// <returns>The typed change for the key.</returns>");
        }
        code.AppendLine("public Item GetChange(" + keyType + " key)");
        code.OpenBlock();
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine("if (" + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key") + ") return Item.Empty;");
        }
        code.AppendLine("if (_items.Count == 0) return Item.Empty;");
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
            SparseChangeSetKeyedTempTransitionEmitter.AppendGetTemporaryChange(code, member, hasContracts);
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
        code.AppendLine(
            "internal static "
                + trans
                + " __SparseEmpty { get; } = new "
                + trans
                + "(default, default, new global::System.Collections.Generic.List<"
                + elementType
                + ">(), new global::System.Collections.Generic.List<"
                + elementType
                + ">(), new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementCs
                + ">("
                + comparer
                + "), new global::System.Collections.Generic.List<"
                + keyType
                + ">(), new global::System.Collections.Generic.List<"
                + keyType
                + ">(), false, new global::System.Collections.Generic.List<"
                + trans
                + ".Item>(), true);"
        );
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
