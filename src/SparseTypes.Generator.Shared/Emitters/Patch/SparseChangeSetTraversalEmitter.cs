using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Caller-owned path-traversal vocabulary for change-set enumeration.</summary>
/// <remarks>When set, generated <c>EnumerateChanges</c> walks nested transitions through one push/pop segment buffer instead of rebuilding prefixed paths at every level.</remarks>
/// <param name="SegmentType">The segment value type held by the scratch buffer.</param>
/// <param name="MemberSegment">Builds a member segment expression from a member-name literal.</param>
/// <param name="KeySegment">Builds a key segment expression from a key expression.</param>
/// <param name="TemporaryKeySegment">Builds a temporary-identity segment expression from a Guid expression.</param>
/// <param name="PathFromSegments">Builds a path expression from a root-type expression and a segment-sequence expression.</param>
internal sealed record SparsePathTraversalContracts(
    string SegmentType,
    Func<string, string> MemberSegment,
    Func<string, string> KeySegment,
    Func<string, string> TemporaryKeySegment,
    Func<string, string, string> PathFromSegments
);

/// <summary>Push/pop traversal for flattened change-set enumeration.</summary>
/// <remarks>
/// The legacy enumeration materializes a child result list and re-prefixes every entry at each ancestor, copying path lengths 1 through D for one leaf under D levels. The traversal instead threads one caller-owned sink plus a segment scratch buffer through the nesting: each level pushes its own segments, leaves materialize the final path once, and intermediate lists never exist. Entry order, endpoints, and kinds match the legacy walk exactly; only the intermediate allocations disappear.
/// </remarks>
internal static class SparseChangeSetTraversalEmitter
{
    /// <summary>Builds the scratch buffer list type for a traversal contract.</summary>
    internal static string ScratchListType(SparsePathTraversalContracts traversal) =>
        "global::System.Collections.Generic.List<" + traversal.SegmentType + ">";

    /// <summary>Builds the leaf-sink delegate type for boxed endpoints plus a kind index.</summary>
    internal static string SinkType(string optionalObject) =>
        "global::System.Action<" + optionalObject + ", " + optionalObject + ", int>";

    /// <summary>Appends the root traversal walk shared by both placements.</summary>
    /// <remarks>The caller emits the method shell, the empty/whole guards, and the return; this fills the member walk.</remarks>
    internal static void AppendRootWalk(
        SharedIndentedBuilder code,
        string rootModel,
        SparsePathTraversalContracts traversal,
        SparseOperationTarget? target
    )
    {
        var scratch = ScratchListType(traversal);
        code.AppendLineAt(3, "var __sparse_scratch = new " + scratch + "();");
        var self = target is null ? string.Empty : "self.";
        code.AppendLineAt(
            3,
            self
                + "__SparseCollectInto((__sparse_before, __sparse_after, __sparse_kind) => changes.Add(new ChangeInfo("
                + traversal.PathFromSegments("typeof(" + rootModel + ")", "__sparse_scratch")
                + ", __sparse_before, __sparse_after, (ChangeKind)__sparse_kind)), __sparse_scratch);"
        );
    }

    /// <summary>Appends the per-model recursive collector plus its leaf helpers.</summary>
    /// <remarks>Instance placement serves both standalone and relocated roots: nested transitions are always instance objects.</remarks>
    internal static void AppendCollect(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        Dictionary<int, string> propertyNames,
        string optionalObject,
        SparsePathTraversalContracts traversal
    )
    {
        code.AppendLineAt(
            2,
            "/// <summary>Collects flattened transitions into the caller's sink (enumeration infrastructure).</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"__sparse_sink\">Receives boxed endpoints plus the change-kind index for each leaf.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"__sparse_scratch\">Push/pop segment buffer owned by the root enumeration.</param>"
        );
        code.AppendLineAt(
            2,
            "internal void __SparseCollectInto("
                + SinkType(optionalObject)
                + " __sparse_sink, "
                + ScratchListType(traversal)
                + " __sparse_scratch)"
        );
        code.AppendLineAt(2, "{");
        // A whole nested transition collapses to one entry at the pushed
        // member path, matching the legacy nested enumeration. The root
        // whole case returns from the entry method before reaching this
        // collector.
        code.AppendLineAt(3, "if (__sparse_hasWhole)");
        code.AppendLineAt(3, "{");
        code.IndentOffset++;
        AppendSinkBoxed(
            code,
            BoxValue(
                optionalObject,
                "__sparse_wholeBefore.IsPresent",
                "__sparse_wholeBefore.Value"
            ),
            BoxValue(optionalObject, "__sparse_wholeAfter.IsPresent", "__sparse_wholeAfter.Value"),
            KindOf("__sparse_wholeBefore", "__sparse_wholeAfter")
        );
        code.AppendLineAt(3, "return;");
        code.IndentOffset--;
        code.AppendLineAt(3, "}");
        foreach (var member in members)
        {
            var property = SparseNaming.EscapeIdentifier(propertyNames[member.Id]);
            var literal = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(
                member.Property.Name,
                true
            );
            var changed = IsNested(member) ? "!" + property + ".IsEmpty" : property + ".IsChanged";
            code.AppendLineAt(3, "if (" + changed + ")");
            code.AppendLineAt(3, "{");
            code.IndentOffset++;
            if (IsNested(member))
            {
                AppendNestedCollect(code, property, literal, traversal);
            }
            else if (IsKeyed(member))
            {
                AppendKeyedCollect(code, member, property, literal, optionalObject, traversal);
            }
            else if (IsDict(member))
            {
                AppendDictionaryCollect(code, member, property, literal, optionalObject, traversal);
            }
            else if (IsSet(member))
            {
                AppendSetCollect(code, member, property, literal, optionalObject, traversal);
            }
            else
            {
                AppendValueCollect(
                    code,
                    property,
                    literal,
                    "value" + member.Id,
                    optionalObject,
                    traversal
                );
            }

            code.IndentOffset--;
            code.AppendLineAt(3, "}");
        }

        code.AppendLineAt(2, "}");
    }

    private static void AppendPush(SharedIndentedBuilder code, string segment)
    {
        code.AppendLineAt(3, "__sparse_scratch.Add(" + segment + ");");
    }

    private static void AppendPop(SharedIndentedBuilder code)
    {
        code.AppendLineAt(3, "__sparse_scratch.RemoveAt(__sparse_scratch.Count - 1);");
    }

    private static void AppendSinkBoxed(
        SharedIndentedBuilder code,
        string before,
        string after,
        string kind
    )
    {
        code.AppendLineAt(3, "__sparse_sink(" + before + ", " + after + ", " + kind + ");");
    }

    private static string BoxValue(string optionalObject, string check, string value) =>
        "("
        + check
        + " ? "
        + optionalObject
        + ".Present((object?)"
        + value
        + ") : "
        + optionalObject
        + ".Missing)";

    private static string KindOf(string beforeSource, string afterSource) =>
        "("
        + beforeSource
        + ".IsPresent == "
        + afterSource
        + ".IsPresent ? (int)ChangeKind.Changed : "
        + beforeSource
        + ".IsPresent ? (int)ChangeKind.Removed : (int)ChangeKind.Added)";

    private static void AppendLeafChange(
        SharedIndentedBuilder code,
        string optionalObject,
        string segment,
        string source,
        string beforeMember,
        string afterMember
    )
    {
        AppendPush(code, segment);
        AppendSinkBoxed(
            code,
            BoxValue(
                optionalObject,
                source + "." + beforeMember + ".IsPresent",
                source + "." + beforeMember + ".Value"
            ),
            BoxValue(
                optionalObject,
                source + "." + afterMember + ".IsPresent",
                source + "." + afterMember + ".Value"
            ),
            KindOf(source + "." + beforeMember, source + "." + afterMember)
        );
        AppendPop(code);
    }

    private static void AppendValueCollect(
        SharedIndentedBuilder code,
        string property,
        string literal,
        string local,
        string optionalObject,
        SparsePathTraversalContracts traversal
    )
    {
        code.AppendLineAt(3, "var " + local + " = " + property + ";");
        code.AppendLineAt(3, "if (" + local + ".IsChanged)");
        code.AppendLineAt(3, "{");
        code.IndentOffset++;
        AppendLeafChange(
            code,
            optionalObject,
            traversal.MemberSegment(literal),
            local,
            "Before",
            "After"
        );
        code.IndentOffset--;
        code.AppendLineAt(3, "}");
    }

    private static void AppendNestedCollect(
        SharedIndentedBuilder code,
        string property,
        string literal,
        SparsePathTraversalContracts traversal
    )
    {
        AppendPush(code, traversal.MemberSegment(literal));
        code.AppendLineAt(3, property + ".__SparseCollectInto(__sparse_sink, __sparse_scratch);");
        AppendPop(code);
    }

    private static void AppendKeyedCollect(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string property,
        string literal,
        string optionalObject,
        SparsePathTraversalContracts traversal
    )
    {
        var transition = "__sparse_keyed_transition_" + member.Id;
        var item = "__sparse_keyed_item_" + member.Id;
        var itemPath = "__sparse_keyed_path_" + member.Id;
        var order = "__sparse_keyed_order_" + member.Id;
        code.AppendLineAt(3, "var " + transition + " = " + property + ";");
        code.AppendLineAt(
            3,
            "if (" + transition + ".Before.IsPresent || " + transition + ".After.IsPresent)"
        );
        code.AppendLineAt(3, "{");
        code.IndentOffset++;
        AppendLeafChange(
            code,
            optionalObject,
            traversal.MemberSegment(literal),
            transition,
            "Before",
            "After"
        );
        code.IndentOffset--;
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "else");
        code.AppendLineAt(3, "{");
        code.IndentOffset++;
        // The member segment stays pushed across items and the order entry,
        // so item leaves materialize member-plus-item paths with one member
        // push instead of rebuilding the member prefix per item.
        AppendPush(code, traversal.MemberSegment(literal));
        code.AppendLineAt(3, "foreach (var " + item + " in " + transition + ")");
        code.AppendLineAt(3, "{");
        code.IndentOffset++;
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            // Temporary entries address by Guid; assigned entries keep key
            // paths, matching the legacy enumeration.
            code.AppendLineAt(
                3,
                "var "
                    + itemPath
                    + " = "
                    + item
                    + ".TemporaryKey.HasValue ? "
                    + traversal.TemporaryKeySegment(item + ".TemporaryKey.Value")
                    + " : "
                    + traversal.KeySegment(item + ".Key")
                    + ";"
            );
        }
        else
        {
            code.AppendLineAt(
                3,
                "var " + itemPath + " = " + traversal.KeySegment(item + ".Key") + ";"
            );
        }

        code.AppendLineAt(3, "if (" + item + ".IsAdded || " + item + ".IsRemoved)");
        code.AppendLineAt(3, "{");
        code.IndentOffset++;
        AppendLeafChange(code, optionalObject, itemPath, item, "Before", "After");
        code.IndentOffset--;
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "if (" + item + ".IsEdited)");
        code.AppendLineAt(3, "{");
        code.IndentOffset++;
        AppendPush(code, itemPath);
        code.AppendLineAt(3, item + ".Edit.__SparseCollectInto(__sparse_sink, __sparse_scratch);");
        AppendPop(code);
        code.IndentOffset--;
        code.AppendLineAt(3, "}");
        code.IndentOffset--;
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "if (!global::System.Linq.Enumerable.SequenceEqual("
                + transition
                + ".BeforeOrder, "
                + transition
                + ".AfterOrder))"
        );
        code.AppendLineAt(3, "{");
        code.IndentOffset++;
        code.AppendLineAt(
            3,
            "var " + order + " = " + optionalObject + ".Present(" + transition + ".BeforeOrder);"
        );
        code.AppendLineAt(
            3,
            "var "
                + order
                + "After = "
                + optionalObject
                + ".Present("
                + transition
                + ".AfterOrder);"
        );
        AppendSinkBoxed(code, order, order + "After", "(int)ChangeKind.Order");
        code.IndentOffset--;
        code.AppendLineAt(3, "}");
        AppendPop(code);
        code.IndentOffset--;
        code.AppendLineAt(3, "}");
    }

    private static void AppendDictionaryCollect(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string property,
        string literal,
        string optionalObject,
        SparsePathTraversalContracts traversal
    )
    {
        var transition = "__sparse_dictionary_transition_" + member.Id;
        code.AppendLineAt(3, "if (!__sparse_hasWhole && !" + KeyedWholeFlag(member) + ")");
        code.AppendLineAt(3, "{");
        code.IndentOffset++;
        code.AppendLineAt(3, "if (" + KeyedItems(member) + " is not null)");
        code.AppendLineAt(3, "{");
        code.IndentOffset++;
        AppendPush(code, traversal.MemberSegment(literal));
        AppendDictionaryItemsCollect(code, member, KeyedItems(member), optionalObject, traversal);
        AppendPop(code);
        code.IndentOffset--;
        code.AppendLineAt(3, "}");
        code.IndentOffset--;
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "else");
        code.AppendLineAt(3, "{");
        code.IndentOffset++;
        code.AppendLineAt(3, "var " + transition + " = " + property + ";");
        code.AppendLineAt(
            3,
            "if (" + transition + ".Before.IsPresent || " + transition + ".After.IsPresent)"
        );
        code.AppendLineAt(3, "{");
        code.IndentOffset++;
        AppendLeafChange(
            code,
            optionalObject,
            traversal.MemberSegment(literal),
            transition,
            "Before",
            "After"
        );
        code.IndentOffset--;
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "else");
        code.AppendLineAt(3, "{");
        code.IndentOffset++;
        AppendPush(code, traversal.MemberSegment(literal));
        AppendDictionaryItemsCollect(code, member, transition, optionalObject, traversal);
        AppendPop(code);
        code.IndentOffset--;
        code.AppendLineAt(3, "}");
        code.IndentOffset--;
        code.AppendLineAt(3, "}");
    }

    private static void AppendDictionaryItemsCollect(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string items,
        string optionalObject,
        SparsePathTraversalContracts traversal
    )
    {
        var item = "__sparse_dictionary_item_" + member.Id;
        var itemPath = "__sparse_dictionary_path_" + member.Id;
        code.AppendLineAt(3, "foreach (var " + item + " in " + items + ")");
        code.AppendLineAt(3, "{");
        code.IndentOffset++;
        code.AppendLineAt(3, "var " + itemPath + " = " + traversal.KeySegment(item + ".Key") + ";");
        code.AppendLineAt(3, "if (" + item + ".IsAdded || " + item + ".IsRemoved)");
        code.AppendLineAt(3, "{");
        code.IndentOffset++;
        AppendLeafChange(code, optionalObject, itemPath, item, "Before", "After");
        code.IndentOffset--;
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "if (" + item + ".IsEdited)");
        code.AppendLineAt(3, "{");
        code.IndentOffset++;
        if (member.Collection.ValueType?.IsFragmentModel == true)
        {
            AppendPush(code, itemPath);
            code.AppendLineAt(
                3,
                item + ".Edit.__SparseCollectInto(__sparse_sink, __sparse_scratch);"
            );
            AppendPop(code);
        }
        else
        {
            AppendLeafChange(code, optionalObject, itemPath, item, "Before", "After");
        }

        code.IndentOffset--;
        code.AppendLineAt(3, "}");
        code.IndentOffset--;
        code.AppendLineAt(3, "}");
    }

    private static void AppendSetCollect(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string property,
        string literal,
        string optionalObject,
        SparsePathTraversalContracts traversal
    )
    {
        // Set-element membership deltas at audit granularity, matching the
        // legacy enumeration.
        var transition = "__sparse_set_transition_" + member.Id;
        var added = "__sparse_set_added_" + member.Id;
        var removed = "__sparse_set_removed_" + member.Id;
        code.AppendLineAt(3, "var " + transition + " = " + property + ";");
        code.AppendLineAt(3, "if (" + transition + ".IsChanged)");
        code.AppendLineAt(3, "{");
        code.IndentOffset++;
        code.AppendLineAt(
            3,
            "if (!"
                + transition
                + ".Before.IsPresent || !"
                + transition
                + ".After.IsPresent || (object?)"
                + transition
                + ".Before.Value is null || (object?)"
                + transition
                + ".After.Value is null)"
        );
        code.AppendLineAt(3, "{");
        code.IndentOffset++;
        AppendLeafChange(
            code,
            optionalObject,
            traversal.MemberSegment(literal),
            transition,
            "Before",
            "After"
        );
        code.IndentOffset--;
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "else");
        code.AppendLineAt(3, "{");
        code.IndentOffset++;
        // The member segment covers both membership loops below.
        AppendPush(code, traversal.MemberSegment(literal));
        code.AppendLineAt(3, "foreach (var " + added + " in " + transition + ".Added)");
        code.AppendLineAt(3, "{");
        code.IndentOffset++;
        AppendPush(code, traversal.KeySegment(added));
        AppendSinkBoxed(
            code,
            optionalObject + ".Missing",
            optionalObject + ".Present((object?)" + added + ")",
            "(int)ChangeKind.Added"
        );
        AppendPop(code);
        code.IndentOffset--;
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "foreach (var " + removed + " in " + transition + ".Removed)");
        code.AppendLineAt(3, "{");
        code.IndentOffset++;
        AppendPush(code, traversal.KeySegment(removed));
        AppendSinkBoxed(
            code,
            optionalObject + ".Present((object?)" + removed + ")",
            optionalObject + ".Missing",
            "(int)ChangeKind.Removed"
        );
        AppendPop(code);
        code.IndentOffset--;
        code.AppendLineAt(3, "}");
        AppendPop(code);
        code.IndentOffset--;
        code.AppendLineAt(3, "}");
        code.IndentOffset--;
        code.AppendLineAt(3, "}");
    }
}
