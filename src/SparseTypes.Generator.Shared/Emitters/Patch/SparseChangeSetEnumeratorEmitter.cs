using System.Collections.Generic;
using System.Collections.Immutable;
using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetEmitter;

namespace SparseTypes.Generator.Shared;

internal static class SparseChangeSetEnumeratorEmitter
{
    internal static void Append(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null,
        string? modelType = null
    )
    {
        ComputePublicNames(members, out var propertyNames, out _);
        var runtime = dialect.RuntimeNamespace;
        var optionalObject = runtime + "Optional<object?>";
        var pathType = SparseFragmentPatchEmitter.GetPathType(dialect);
        var recordType = dialect.TransitionContracts?.ChangeRecordType ?? "ChangeInfo";
        var kindType = dialect.TransitionContracts?.ChangeKindType ?? "ChangeKind";
        if (dialect.TransitionContracts is null)
        {
            AppendChangeInfoTypes(code, optionalObject, pathType);
        }
        else
        {
            AppendChangeKindCompatibilityFacade(code, kindType);
        }
        if (target is not null)
        {
            var ops = target.ChangeSetOperations;
            AppendValueHelpers(
                ops,
                runtime,
                optionalObject,
                pathType,
                recordType,
                kindType,
                modelType,
                dialect.TransitionContracts?.OptionalBoxType
            );
            AppendMethodShellStub(code, target, recordType);
            AppendMethod(
                ops,
                members,
                propertyNames,
                optionalObject,
                recordType,
                kindType,
                modelType,
                dialect.PathTraversal,
                target
            );
            SparseChangeSetFindEmitter.AppendShellStub(
                code,
                dialect,
                modelType,
                target,
                recordType
            );
            SparseChangeSetFindEmitter.Append(
                ops,
                dialect,
                modelType,
                members,
                propertyNames,
                recordType,
                target
            );
        }
        else
        {
            AppendValueHelpers(
                code,
                runtime,
                optionalObject,
                pathType,
                recordType,
                kindType,
                modelType,
                dialect.TransitionContracts?.OptionalBoxType
            );
            AppendMethod(
                code,
                members,
                propertyNames,
                optionalObject,
                recordType,
                kindType,
                modelType,
                dialect.PathTraversal,
                target
            );
            SparseChangeSetFindEmitter.Append(
                code,
                dialect,
                modelType,
                members,
                propertyNames,
                recordType,
                target
            );
        }
    }

    /// <summary>Appends the internal push/pop traversal after public members.</summary>
    /// <remarks>The orchestrator calls this after path enumeration so internal helpers trail public members.</remarks>
    internal static void AppendTraversal(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        if (dialect.PathTraversal is null)
        {
            return;
        }

        ComputePublicNames(members, out var propertyNames, out _);
        var optionalObject = dialect.RuntimeNamespace + "Optional<object?>";
        SparseChangeSetTraversalEmitter.AppendCollect(
            code,
            members,
            propertyNames,
            optionalObject,
            dialect.PathTraversal
        );
    }

    private static void AppendMethodShellStub(
        SharedIndentedBuilder code,
        SparseOperationTarget target,
        string recordType
    )
    {
        code.AppendLine(
            "/// <summary>Enumerates flattened value transitions, including keyed collection order changes.</summary>"
        );
        code.AppendLine(
            "public global::System.Collections.Generic.IEnumerable<"
                + recordType
                + "> EnumerateChanges() => "
                + target.ChangeSetOperationsType
                + ".EnumerateChanges(this);"
        );
    }

    private static void AppendChangeInfoTypes(
        SharedIndentedBuilder code,
        string optionalObject,
        string pathType
    )
    {
        code.AppendLine("/// <summary>Classifies a flattened change-set entry.</summary>");
        code.AppendLine("public enum ChangeKind");
        code.OpenBlock();
        code.AppendLine("/// <summary>A value changed while remaining present.</summary>");
        code.AppendLine("Changed = 0,");
        code.AppendLine("/// <summary>A value was added.</summary>");
        code.AppendLine("Added = 1,");
        code.AppendLine("/// <summary>A value was removed.</summary>");
        code.AppendLine("Removed = 2,");
        code.AppendLine("/// <summary>A keyed collection order changed.</summary>");
        code.AppendLine("Order = 3,");
        code.CloseBlock();
        code.AppendLine(
            "/// <summary>A path and its before and after values in a change set.</summary>"
        );
        code.AppendLine("public sealed class ChangeInfo");
        code.OpenBlock();
        // Public-first order: public properties precede the internal
        // constructor.
        code.AppendLine("/// <summary>Gets the changed member path.</summary>");
        code.AppendLine("public " + pathType + " Path { get; }");
        code.AppendLine("/// <summary>Gets the wire-compatible path text.</summary>");
        code.AppendLine("public string PathText => Path.ToString();");
        code.AppendLine("/// <summary>Gets the presence-aware value before the change.</summary>");
        code.AppendLine("public " + optionalObject + " Before { get; }");
        code.AppendLine("/// <summary>Gets the presence-aware value after the change.</summary>");
        code.AppendLine("public " + optionalObject + " After { get; }");
        code.AppendLine("/// <summary>Gets the kind of change.</summary>");
        code.AppendLine("public ChangeKind Kind { get; }");
        code.AppendLine(
            "internal ChangeInfo("
                + pathType
                + " path, "
                + optionalObject
                + " before, "
                + optionalObject
                + " after, ChangeKind kind)"
        );
        code.OpenBlock();
        code.AppendLine("Path = path;");
        code.AppendLine("Before = before;");
        code.AppendLine("After = after;");
        code.AppendLine("Kind = kind;");
        code.CloseBlock();
        code.CloseBlock();
    }

    private static void AppendChangeKindCompatibilityFacade(
        SharedIndentedBuilder code,
        string kindType
    )
    {
        code.AppendLine(
            "/// <summary>Named change-kind values for compatibility with earlier generated APIs.</summary>"
        );
        code.AppendLine("public static class ChangeKind");
        code.OpenBlock();
        code.AppendLine("/// <summary>A value changed while remaining present.</summary>");
        code.AppendLine("public const " + kindType + " Changed = " + kindType + ".Changed;");
        code.AppendLine("/// <summary>A value was added.</summary>");
        code.AppendLine("public const " + kindType + " Added = " + kindType + ".Added;");
        code.AppendLine("/// <summary>A value was removed.</summary>");
        code.AppendLine("public const " + kindType + " Removed = " + kindType + ".Removed;");
        code.AppendLine("/// <summary>A keyed collection order changed.</summary>");
        code.AppendLine("public const " + kindType + " Order = " + kindType + ".Order;");
        code.CloseBlock();
    }

    private static void AppendValueHelpers(
        SharedIndentedBuilder code,
        string runtime,
        string optionalObject,
        string pathType,
        string recordType,
        string kindType,
        string? modelType,
        string? optionalBoxType
    )
    {
        var optional = runtime + "Optional";
        code.AppendLine(
            "/// <summary>Model-rooted path factory for changes and rebase conflicts.</summary>"
        );
        code.AppendLine(
            "internal static readonly "
                + pathType
                + " __SparseRootPath = "
                + pathType
                + ".Root(typeof("
                + (modelType ?? "global::System.Object")
                + "));"
        );
        if (optionalBoxType is null)
        {
            code.AppendLine(
                "private static "
                    + optionalObject
                    + " __SparseBox<T>("
                    + optional
                    + "<T> value) => value.IsPresent ? "
                    + optionalObject
                    + ".Present(value.Value) : "
                    + optionalObject
                    + ".Missing;"
            );
        }
        code.AppendLine(
            "private static "
                + recordType
                + " __SparseCreateChangeInfo<T>("
                + pathType
                + " path, "
                + optional
                + "<T> before, "
                + optional
                + "<T> after)"
        );
        code.OpenBlock();
        code.AppendLine(
            "var kind = before.IsPresent == after.IsPresent ? "
                + kindType
                + ".Changed : before.IsPresent ? "
                + kindType
                + ".Removed : "
                + kindType
                + ".Added;"
        );
        code.AppendLine(
            "return new "
                + recordType
                + "(path, "
                + (
                    optionalBoxType is null
                        ? "__SparseBox(before), __SparseBox(after)"
                        : optionalBoxType
                            + ".ToObject(before), "
                            + optionalBoxType
                            + ".ToObject(after)"
                )
                + ", kind);"
        );
        code.CloseBlock();
    }

    private static void AppendMethod(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        Dictionary<int, string> propertyNames,
        string optionalObject,
        string recordType,
        string kindType,
        string? modelType,
        SparsePathTraversalContracts? traversal,
        SparseOperationTarget? target = null
    )
    {
        var rootModel = modelType ?? "global::System.Object";
        if (target is not null)
        {
            code.AppendLine(
                "/// <summary>Enumerates flattened value transitions, including keyed collection order changes.</summary>"
            );
            code.AppendLine("/// <param name=\"self\">The change set to enumerate.</param>");
            code.AppendLine("/// <returns>The flattened transitions.</returns>");
            code.AppendLine(
                "internal static global::System.Collections.Generic.IEnumerable<"
                    + recordType
                    + "> EnumerateChanges(ChangeSet self)"
            );
            code.OpenBlock();
            AppendSelfAliases(code, members, includeMemberState: traversal is null);
        }
        else
        {
            code.AppendLine(
                "/// <summary>Enumerates flattened value transitions, including keyed collection order changes.</summary>"
            );
            code.AppendLine(
                "/// <remarks>A whole-root presence transition emits a single root entry that replaces member entries; nested whole-child presence transitions appear under the parent member path alongside other member entries. Presence-derived kinds apply: missing to present-null reads as <c>Added</c>, present-null to missing as <c>Removed</c>, and present-null to present-value as <c>Changed</c>. Each entry carries a canonical <c>SparsePath</c>: member segments, typed key segments for keyed and dictionary entries, index segments for positions, and set-membership segments carrying the element value. The wire text exposed by <c>PathText</c> keeps the historical grammar: member segments joined by <c>.</c>; keyed, dictionary and set entries as <c>Name[\"key\"]</c> with the key JSON-escaped; order transitions at the member path with kind <c>Order</c>.</remarks>"
            );
            code.AppendLine(
                "public global::System.Collections.Generic.IEnumerable<"
                    + recordType
                    + "> EnumerateChanges()"
            );
            code.OpenBlock();
        }
        code.AppendLine(target is null ? "if (IsEmpty)" : "if (self.IsEmpty)");
        code.OpenBlock();
        code.AppendLine("return global::System.Array.Empty<" + recordType + ">();");
        code.CloseBlock();
        code.AppendLine(
            "var changes = new global::System.Collections.Generic.List<" + recordType + ">();"
        );
        // Root presence transitions carry no member values but are nonempty
        // history; report them instead of silently dropping them.
        code.AppendLine("var __sparse_prefix = __SparseRootPath;");
        code.AppendLine("if (__sparse_hasWhole)");
        code.OpenBlock();
        code.AppendLine(
            "changes.Add(__SparseCreateChangeInfo(__sparse_prefix, __sparse_wholeBefore, __sparse_wholeAfter));"
        );
        code.AppendLine("return changes;");
        code.CloseBlock();
        if (traversal is not null)
        {
            SparseChangeSetTraversalEmitter.AppendRootWalk(
                code,
                rootModel,
                traversal,
                target,
                recordType,
                kindType
            );
        }
        else
        {
            foreach (var member in members)
            {
                // Relocated bodies read transitions through the facade instance.
                var property =
                    (target is null ? string.Empty : "self.")
                    + SparseNaming.EscapeIdentifier(propertyNames[member.Id]);
                var memberPath = "__sparse_member_" + member.Id;
                var literal = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(
                    member.Property.Name,
                    true
                );
                var changed = IsNested(member)
                    ? "!" + property + ".IsEmpty"
                    : property + ".IsChanged";
                code.AppendLine("if (" + changed + ")");
                code.OpenBlock();
                code.Indent();
                code.AppendLine(
                    "var " + memberPath + " = __sparse_prefix.Member(" + literal + ");"
                );
                if (IsNested(member))
                {
                    AppendNestedChanges(
                        code,
                        property,
                        memberPath,
                        "nested" + member.Id,
                        recordType
                    );
                }
                else if (IsKeyed(member))
                {
                    AppendKeyedChanges(
                        code,
                        member,
                        property,
                        memberPath,
                        optionalObject,
                        recordType,
                        kindType
                    );
                }
                else if (IsDict(member))
                {
                    AppendDictionaryChanges(code, member, property, memberPath, recordType);
                }
                else if (IsSet(member))
                {
                    AppendSetChanges(
                        code,
                        member,
                        property,
                        memberPath,
                        optionalObject,
                        recordType,
                        kindType
                    );
                }
                else
                {
                    AppendValueChange(code, property, memberPath, "value" + member.Id);
                }
                code.Unindent();
                code.CloseBlock();
            }
        }

        code.AppendLine("return changes;");
        code.CloseBlock();
    }

    private static void AppendValueChange(
        SharedIndentedBuilder code,
        string property,
        string memberPath,
        string local
    )
    {
        code.AppendLine("var " + local + " = " + property + ";");
        code.AppendLine("if (" + local + ".IsChanged)");
        code.OpenBlock();
        code.AppendLine(
            "changes.Add(__SparseCreateChangeInfo("
                + memberPath
                + ", "
                + local
                + ".Before, "
                + local
                + ".After));"
        );
        code.CloseBlock();
    }

    private static void AppendSetChanges(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string property,
        string memberPath,
        string optionalObject,
        string recordType,
        string kindType
    )
    {
        // Set-element membership deltas at audit granularity.
        var transition = "__sparse_set_transition_" + member.Id;
        var added = "__sparse_set_added_" + member.Id;
        var removed = "__sparse_set_removed_" + member.Id;
        code.AppendLine("var " + transition + " = " + property + ";");
        code.AppendLine("if (" + transition + ".IsChanged)");
        code.OpenBlock();
        code.AppendLine(
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
        code.OpenBlock();
        code.AppendLine(
            "changes.Add(__SparseCreateChangeInfo("
                + memberPath
                + ", "
                + transition
                + ".Before, "
                + transition
                + ".After));"
        );
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine("foreach (var " + added + " in " + transition + ".Added)");
        code.AppendLine(
            "changes.Add(new "
                + recordType
                + "("
                + memberPath
                + ".Key("
                + added
                + "), "
                + optionalObject
                + ".Missing, "
                + optionalObject
                + ".Present((object?)"
                + added
                + "), "
                + kindType
                + ".Added));"
        );
        code.AppendLine("foreach (var " + removed + " in " + transition + ".Removed)");
        code.AppendLine(
            "changes.Add(new "
                + recordType
                + "("
                + memberPath
                + ".Key("
                + removed
                + "), "
                + optionalObject
                + ".Present((object?)"
                + removed
                + "), "
                + optionalObject
                + ".Missing, "
                + kindType
                + ".Removed));"
        );
        code.CloseBlock();
        code.CloseBlock();
    }

    private static void AppendNestedChanges(
        SharedIndentedBuilder code,
        string property,
        string memberPath,
        string suffix,
        string recordType
    )
    {
        var change = "__sparse_nested_change_" + suffix;
        code.AppendLine("foreach (var " + change + " in " + property + ".EnumerateChanges())");
        code.OpenBlock();
        code.AppendLine(
            "changes.Add(new "
                + recordType
                + "("
                + memberPath
                + ".Append("
                + change
                + ".Path), "
                + change
                + ".Before, "
                + change
                + ".After, "
                + change
                + ".Kind));"
        );
        code.CloseBlock();
    }

    private static void AppendKeyedChanges(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string property,
        string memberPath,
        string optionalObject,
        string recordType,
        string kindType
    )
    {
        var transition = "__sparse_keyed_transition_" + member.Id;
        var item = "__sparse_keyed_item_" + member.Id;
        var itemPath = "__sparse_keyed_path_" + member.Id;
        var order = "__sparse_keyed_order_" + member.Id;
        code.AppendLine("var " + transition + " = " + property + ";");
        code.AppendLine(
            "if (" + transition + ".Before.IsPresent || " + transition + ".After.IsPresent)"
        );
        code.OpenBlock();
        code.AppendLine(
            "changes.Add(__SparseCreateChangeInfo("
                + memberPath
                + ", "
                + transition
                + ".Before, "
                + transition
                + ".After));"
        );
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine("foreach (var " + item + " in " + transition + ")");
        code.OpenBlock();
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            // Temporary entries address by Guid; assigned entries keep key
            // paths.
            code.AppendLine(
                "var "
                    + itemPath
                    + " = "
                    + item
                    + ".TemporaryKey.HasValue ? "
                    + memberPath
                    + ".TemporaryKey("
                    + item
                    + ".TemporaryKey.Value) : "
                    + memberPath
                    + ".Key("
                    + item
                    + ".Key);"
            );
        }
        else
        {
            code.AppendLine("var " + itemPath + " = " + memberPath + ".Key(" + item + ".Key);");
        }

        code.AppendLine(
            "if ("
                + item
                + ".IsAdded || "
                + item
                + ".IsRemoved) changes.Add(__SparseCreateChangeInfo("
                + itemPath
                + ", "
                + item
                + ".Before, "
                + item
                + ".After));"
        );
        code.AppendLine("if (" + item + ".IsEdited)");
        code.OpenBlock();
        AppendNestedChanges(code, item + ".Edit", itemPath, "keyed" + member.Id, recordType);
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine(
            "if (!global::System.Linq.Enumerable.SequenceEqual("
                + transition
                + ".BeforeOrder, "
                + transition
                + ".AfterOrder))"
        );
        code.OpenBlock();
        code.AppendLine(
            "var " + order + " = " + optionalObject + ".Present(" + transition + ".BeforeOrder);"
        );
        code.AppendLine(
            "var "
                + order
                + "After = "
                + optionalObject
                + ".Present("
                + transition
                + ".AfterOrder);"
        );
        code.AppendLine(
            "changes.Add(new "
                + recordType
                + "("
                + memberPath
                + ", "
                + order
                + ", "
                + order
                + "After, "
                + kindType
                + ".Order));"
        );
        code.CloseBlock();
        code.CloseBlock();
    }

    private static void AppendDictionaryChanges(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string property,
        string memberPath,
        string recordType
    )
    {
        var transition = "__sparse_dictionary_transition_" + member.Id;
        code.AppendLine("if (!__sparse_hasWhole && !" + KeyedWholeFlag(member) + ")");
        code.OpenBlock();
        code.AppendLine("if (" + KeyedItems(member) + " is not null)");
        code.OpenBlock();
        AppendDictionaryItems(code, member, KeyedItems(member), memberPath, recordType);
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine("var " + transition + " = " + property + ";");
        code.AppendLine(
            "if (" + transition + ".Before.IsPresent || " + transition + ".After.IsPresent)"
        );
        code.OpenBlock();
        code.AppendLine(
            "changes.Add(__SparseCreateChangeInfo("
                + memberPath
                + ", "
                + transition
                + ".Before, "
                + transition
                + ".After));"
        );
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        AppendDictionaryItems(code, member, transition, memberPath, recordType);
        code.CloseBlock();
        code.CloseBlock();
    }

    private static void AppendDictionaryItems(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string items,
        string memberPath,
        string recordType
    )
    {
        var item = "__sparse_dictionary_item_" + member.Id;
        var itemPath = "__sparse_dictionary_path_" + member.Id;
        code.AppendLine("foreach (var " + item + " in " + items + ")");
        code.OpenBlock();
        code.AppendLine("var " + itemPath + " = " + memberPath + ".Key(" + item + ".Key);");
        code.AppendLine(
            "if ("
                + item
                + ".IsAdded || "
                + item
                + ".IsRemoved) changes.Add(__SparseCreateChangeInfo("
                + itemPath
                + ", "
                + item
                + ".Before, "
                + item
                + ".After));"
        );
        code.AppendLine("if (" + item + ".IsEdited)");
        code.OpenBlock();
        if (member.Collection.ValueType?.IsFragmentModel == true)
        {
            AppendNestedChanges(
                code,
                item + ".Edit",
                itemPath,
                "dictionary" + member.Id,
                recordType
            );
        }
        else
        {
            code.AppendLine(
                "changes.Add(__SparseCreateChangeInfo("
                    + itemPath
                    + ", "
                    + item
                    + ".Before, "
                    + item
                    + ".After));"
            );
        }

        code.CloseBlock();
        code.CloseBlock();
    }
}
