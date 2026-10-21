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

/// <summary>Projects and reuses immutable sparse keyed views.</summary>
internal static class SparseChangeSetKeyedProjectionEmitter
{
    internal static void AppendProjection(
        SharedIndentedBuilder shell,
        SparseMemberModel member,
        string prop,
        string trans,
        string cacheName,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target
    )
    {
        var code = target is null ? shell : target.ChangeSetOperations;
        var keyType = KeyTypeOf(member);
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var elementType = ElementTypeOf(member);
        var elementCs = ElementChangeSetOf(member);
        var facade = dialect.RuntimeFacade;
        // Sparse projection: derive typed transition from canonical
        // sparse storage without full member snapshots.
        if (target is null)
        {
            code.AppendLine("private " + trans + " __SparseProject_" + member.Id + "()");
        }
        else
        {
            code.AppendLine(
                "/// <summary>Projects the typed keyed transition from canonical sparse storage.</summary>"
            );
            code.AppendLine(
                "internal static " + trans + " __SparseProject_" + member.Id + "(ChangeSet self)"
            );
        }
        code.OpenBlock();
        if (target is not null)
        {
            AppendMemberStateAliases(code, member);
        }
        code.AppendLine(
            "if (__sparse_hasWhole) return __SparseBuild_"
                + member.Id
                + "(__SparseBefore_"
                + member.Id
                + (target is null ? "(), __SparseAfter_" : "(self), __SparseAfter_")
                + member.Id
                + (target is null ? "());" : "(self));")
        );
        code.AppendLine("if (!" + HasField(member) + ")");
        code.OpenBlock();
        code.AppendLine(
            "return new "
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
        code.CloseBlock();
        var wholeBefore = KeyedWholeBefore(member);
        var wholeAfter = KeyedWholeAfter(member);
        if (SparseChangeSetTransitionEmitter.NeedsSnapshot(member))
        {
            wholeBefore = "__SparseSnapshot_" + member.Id + "(" + wholeBefore + ")";
            wholeAfter = "__SparseSnapshot_" + member.Id + "(" + wholeAfter + ")";
        }
        code.AppendLine(
            "if ("
                + KeyedWholeFlag(member)
                + ") return __SparseBuild_"
                + member.Id
                + "("
                + wholeBefore
                + ", "
                + wholeAfter
                + ");"
        );
        code.AppendLine(
            "var __stored = "
                + KeyedItems(member)
                + " ?? new global::System.Collections.Generic.List<"
                + trans
                + ".Item>();"
        );
        code.AppendLine("int __addedCount = 0, __removedCount = 0, __editedCount = 0;");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine("bool __hasUnassignedEdit = false;");
            code.AppendLine(
                "foreach (var __it in __stored) { if (__it.IsAdded) __addedCount++; else if (__it.IsRemoved) __removedCount++; else if (__it.IsEdited) { if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__it.Key")
                    + ") __hasUnassignedEdit = true; else __editedCount++; } }"
            );
            code.AppendLine("if (__hasUnassignedEdit) __editedCount++;");
        }
        else
        {
            code.AppendLine(
                "foreach (var __it in __stored) { if (__it.IsAdded) __addedCount++; else if (__it.IsRemoved) __removedCount++; else if (__it.IsEdited) __editedCount++; }"
            );
        }
        code.AppendLine(
            "var __added = new global::System.Collections.Generic.List<"
                + elementType
                + ">(__addedCount); var __removed = new global::System.Collections.Generic.List<"
                + elementType
                + ">(__removedCount); var __edited = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementCs
                + ">(__editedCount, "
                + comparer
                + ");"
        );
        code.AppendLine(
            "foreach (var __it in __stored) { if (__it.IsAdded) __added.Add(__it.After.Value!); else if (__it.IsRemoved) __removed.Add(__it.Before.Value!); else if (__it.IsEdited) __edited[__it.Key] = __it.Edit; }"
        );
        code.AppendLine(
            "var __bO = "
                + KeyedBeforeOrder(member)
                + " ?? new global::System.Collections.Generic.List<"
                + keyType
                + ">(); var __aO = "
                + KeyedAfterOrder(member)
                + " ?? new global::System.Collections.Generic.List<"
                + keyType
                + ">();"
        );
        code.AppendLine(
            "bool __oc = "
                + KeyedBeforeOrder(member)
                + " is not null && "
                + KeyedAfterOrder(member)
                + " is not null && ("
                + KeyedOrderChangedExpression(member, facade)
                + ");"
        );
        code.AppendLine(
            "return new "
                + trans
                + "(default, default, __added, __removed, __edited, __bO, __aO, __oc, __stored, false);"
        );
        code.CloseBlock();
        var projectCall = target is null
            ? "__SparseProject_" + member.Id + "()"
            : target.ChangeSetOperationsType + ".__SparseProject_" + member.Id + "(this)";
        shell.AppendLine(
            "/// <summary>Gets the typed keyed transition for member '"
                + member.Property.Name
                + "'.</summary>"
        );
        SparseChangePayloadJsonBridgeEmitter.AppendBareJsonIgnore(shell, dialect.EmitJson);
        shell.AppendLine("public " + trans + " " + prop);
        shell.OpenBlock();
        shell.AppendLine("get");
        shell.OpenBlock();
        // Whole endpoints expose mutable container snapshots and must stay
        // independent.
        shell.AppendLine(
            "if (__sparse_hasWhole || " + KeyedWholeFlag(member) + ") return " + projectCall + ";"
        );
        shell.AppendLine("return " + cacheName + " ?? (" + cacheName + " = " + projectCall + ");");
        shell.CloseBlock();
        shell.CloseBlock();
    }
}
