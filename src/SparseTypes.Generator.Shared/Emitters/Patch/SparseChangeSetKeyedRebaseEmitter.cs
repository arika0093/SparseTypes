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
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedTransitionEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetMatchEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetPatchSyncEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetRebaseEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetTransitionEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Sparse per-key rebase for keyed sequences.</summary>
internal static class SparseChangeSetKeyedRebaseEmitter
{
    /// <summary>Emits sparse per-key rebase for a keyed member.</summary>
    internal static void AppendKeyedRebaseSparse(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseMemberModel member,
        string esc,
        string lit,
        string runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var id = member.Id;
        var keyType = KeyTypeOf(member);
        var elementType = ElementTypeOf(member);
        var elementCs = ElementChangeSetOf(member);
        var elementFrag = ElementFragmentOf(member);
        var optElement = runtime + "Optional<" + elementType + ">";
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var facade = dialect.RuntimeFacade;
        var conflict = dialect.ConflictType;
        var conflictKind = dialect.ConflictKindType;
        var trans = TransNameFor(members, member);
        var hasTempRebase = SparseKeyedCollectionEmitter.HasTemporaryKey(member);
        var redactedAssignments = new List<string>
        {
            "__rh" + id + " = true;",
            "__rwhole" + id + " = " + KeyedWholeFlag(member) + ";",
            "__rwb" + id + " = " + KeyedWholeBefore(member) + ";",
            "__rwa" + id + " = " + KeyedWholeAfter(member) + ";",
            "__ritems" + id + " = " + KeyedItems(member) + ";",
            "__rbO" + id + " = " + KeyedBeforeOrder(member) + ";",
            "__raO" + id + " = " + KeyedAfterOrder(member) + ";",
        };
        if (hasTempRebase)
        {
            redactedAssignments.Add("__rtbO" + id + " = " + KeyedTempBeforeOrder(member) + ";");
            redactedAssignments.Add("__rtaO" + id + " = " + KeyedTempAfterOrder(member) + ";");
        }
        SparseChangeSetMemberRebaseEmitter.AppendRedactedGuard(
            code,
            member,
            lit,
            runtime,
            conflict,
            dialect,
            HasField(member),
            redactedAssignments.ToArray()
        );
        code.AppendLine("if (" + HasField(member) + " && !__red" + id + ")");
        code.OpenBlock();
        code.AppendLine("var __curM" + id + " = __cur." + esc + ";");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member) && !hasTempRebase)
        {
            code.AppendLine(
                "if ("
                    + KeyedBeforeOrder(member)
                    + " is not null) foreach (var __baselineKey in "
                    + KeyedBeforeOrder(member)
                    + ") if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__baselineKey")
                    + ") throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed ChangeSet.\");"
            );
        }
        // Replay via member patch rebase is not sparse; report member conflict
        // unless already applied, else keep whole.
        code.AppendLine("if (" + KeyedWholeFlag(member) + ")");
        code.OpenBlock();
        code.AppendLine(
            "if (Fragment.__SparseEqual_"
                + id
                + "("
                + KeyedWholeAfter(member)
                + ", __curM"
                + id
                + ")) { }"
        );
        code.AppendLine(
            "else if (Fragment.__SparseEqual_"
                + id
                + "("
                + KeyedWholeBefore(member)
                + ", __curM"
                + id
                + ")) { __rh"
                + id
                + " = true; __rwhole"
                + id
                + " = true; __rwb"
                + id
                + " = __curM"
                + id
                + "; __rwa"
                + id
                + " = "
                + KeyedWholeAfter(member)
                + "; }"
        );
        code.AppendLine(
            "else { __conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + lit
                + "), "
                + conflictKind
                + ".Nested, "
                + BoxExpression(dialect, KeyedWholeBefore(member))
                + ", "
                + BoxExpression(dialect, KeyedWholeAfter(member))
                + ", "
                + BoxExpression(dialect, "__curM" + id)
                + ", \"The member conflicts with a concurrent change.\")); }"
        );
        code.CloseBlock();
        code.AppendLine(
            "else if (!__curM" + id + ".IsPresent || (object?)__curM" + id + ".Value is null)"
        );
        code.OpenBlock();
        code.AppendLine(
            "__conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + lit
                + "), "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Missing, "
                + BoxExpression(dialect, "__curM" + id)
                + ", \"The member conflicts with a concurrent change.\"));"
        );
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        // Current map + order (transient, not retained).
        code.AppendLine(
            "var __cmap"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLine(
            "var __corder"
                + id
                + " = new global::System.Collections.Generic.List<"
                + keyType
                + ">();"
        );
        // Current-position index built in the same pass as the map/order so
        // per-item position lookups below stay indexed instead of scanning.
        code.AppendLine(
            "var __cpos"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", int>("
                + comparer
                + ");"
        );
        if (hasTempRebase)
        {
            SparseChangeSetKeyedTempRebaseEmitter.AppendTempCurrentWalk(
                code,
                member,
                elementType,
                id
            );
        }
        else
        {
            code.AppendLine(
                "foreach (var __e in __curM"
                    + id
                    + ".Value!) { var __ck = __SparseKeyOf_ChangeSet_"
                    + id
                    + "(__e); "
                    + (
                        SparseKeyedCollectionEmitter.HasUnassignedKey(member)
                            ? "if ("
                                + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                                    member,
                                    "__ck"
                                )
                                + ") throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the current baseline of a keyed ChangeSet.\"); "
                            : ""
                    )
                    + SparseKeyedCollectionEmitter.AddUniqueEntry("__cmap" + id, "__ck", "__e")
                    + " __cpos"
                    + id
                    + "[__ck] = __corder"
                    + id
                    + ".Count; __corder"
                    + id
                    + ".Add(__ck); }"
            );
        }

        code.AppendLine(
            "var __rebaseCurrentOrder"
                + id
                + " = new global::System.Collections.Generic.List<"
                + keyType
                + ">(__corder"
                + id
                + ");"
        );
        code.AppendLine(
            "var __rlist"
                + id
                + " = new global::System.Collections.Generic.List<"
                + trans
                + ".Item>();"
        );
        code.AppendLine("bool __any" + id + " = false;");
        code.AppendLine(
            "if ("
                + KeyedItems(member)
                + " is not null) foreach (var __it in "
                + KeyedItems(member)
                + ")"
        );
        code.OpenBlock();
        if (hasTempRebase)
        {
            SparseChangeSetKeyedTempRebaseEmitter.AppendTempItemBranch(
                code,
                member,
                trans,
                runtime,
                elementCs,
                elementFrag,
                elementType,
                conflict,
                conflictKind,
                lit,
                id
            );
        }
        // Added: replay when absent; already-applied when equal; conflict
        // otherwise.
        code.AppendLine("if (__it.IsAdded)");
        code.OpenBlock();
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine(
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__it.Key")
                    + ") { __any"
                    + id
                    + " = true; "
                    + optElement
                    + " __ua = "
                    + optElement
                    + ".Present(__it.After.Value!); "
                    + runtime
                    + "Optional<"
                    + elementFrag
                    + "?> __uea = "
                    + runtime
                    + "Optional<"
                    + elementFrag
                    + "?>.Present("
                    + elementFrag
                    + ".From(__it.After.Value!)); var __uedit = "
                    + elementCs
                    + ".Between(default, __uea); __rlist"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__it.Key, __it.TemporaryKey, default, __ua, -1, __corder"
                    + id
                    + ".Count, true, false, false, false, __uedit, false)); } else "
                    + "if (!__cmap"
                    + id
                    + ".ContainsKey(__it.Key)) { __any"
                    + id
                    + " = true; "
                    + optElement
                    + " __na = "
                    + optElement
                    + ".Present(__it.After.Value!); "
                    + runtime
                    + "Optional<"
                    + elementFrag
                    + "?> __nea = "
                    + runtime
                    + "Optional<"
                    + elementFrag
                    + "?>.Present("
                    + elementFrag
                    + ".From(__it.After.Value!)); var __nedit = "
                    + elementCs
                    + ".Between(default, __nea); __rlist"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__it.Key, __it.TemporaryKey, default, __na, -1, __corder"
                    + id
                    + ".Count, true, false, false, false, __nedit, false)); __cmap"
                    + id
                    + "[__it.Key] = __it.After.Value!; __cpos"
                    + id
                    + "[__it.Key] = __corder"
                    + id
                    + ".Count; __corder"
                    + id
                    + ".Add(__it.Key); }"
            );
        }
        else
        {
            code.AppendLine(
                "if (!__cmap"
                    + id
                    + ".ContainsKey(__it.Key)) { __any"
                    + id
                    + " = true; "
                    + optElement
                    + " __na = "
                    + optElement
                    + ".Present(__it.After.Value!); "
                    + runtime
                    + "Optional<"
                    + elementFrag
                    + "?> __nea = "
                    + runtime
                    + "Optional<"
                    + elementFrag
                    + "?>.Present("
                    + elementFrag
                    + ".From(__it.After.Value!)); var __nedit = "
                    + elementCs
                    + ".Between(default, __nea); __rlist"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__it.Key, __it.TemporaryKey, default, __na, -1, __corder"
                    + id
                    + ".Count, true, false, false, false, __nedit, false)); __cmap"
                    + id
                    + "[__it.Key] = __it.After.Value!; __cpos"
                    + id
                    + "[__it.Key] = __corder"
                    + id
                    + ".Count; __corder"
                    + id
                    + ".Add(__it.Key); }"
            );
        }

        code.AppendLine(
            "else if (!"
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__cmap"
                + id
                + "[__it.Key])), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__it.After.Value!))).IsEmpty)"
        );
        code.AppendLine(
            "{ __conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + lit
                + ").Key(__it.Key), "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Present((object?)__it.After.Value), "
                + runtime
                + "Optional<object?>.Present((object?)__cmap"
                + id
                + "[__it.Key]), \"The keyed element conflicts with a concurrent change.\")); }"
        );
        code.CloseBlock();
        // Removed: already-applied when absent; replay when still base;
        // conflict otherwise.
        code.AppendLine("else if (__it.IsRemoved)");
        code.OpenBlock();
        code.AppendLine("if (!__cmap" + id + ".ContainsKey(__it.Key)) { }");
        code.AppendLine(
            "else if (!"
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__cmap"
                + id
                + "[__it.Key])), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__it.Before.Value!))).IsEmpty)"
        );
        code.AppendLine(
            "{ __conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + lit
                + ").Key(__it.Key), "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Present((object?)__it.Before.Value), "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Present((object?)__cmap"
                + id
                + "[__it.Key]), \"The keyed element conflicts with a concurrent change.\")); }"
        );
        code.AppendLine(
            "else { __any"
                + id
                + " = true; int __bi = __cpos"
                + id
                + "[__it.Key]; __rlist"
                + id
                + ".Add(new "
                + trans
                + ".Item(__it.Key, __it.TemporaryKey, __it.Before, default, __bi, -1, false, true, false, false, __it.Edit, false)); __cmap"
                + id
                + ".Remove(__it.Key); }"
        );
        code.CloseBlock();
        // Edited: nested rebase distinguishes clean/already-applied/conflict.
        code.AppendLine("else if (__it.IsEdited)");
        code.OpenBlock();
        code.AppendLine("if (!__cmap" + id + ".TryGetValue(__it.Key, out var __cev))");
        code.AppendLine(
            "{ __conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + lit
                + ").Key(__it.Key), "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Present((object?)__it.Before.Value), "
                + runtime
                + "Optional<object?>.Present((object?)__it.After.Value), "
                + runtime
                + "Optional<object?>.Missing, \"The keyed element conflicts with a concurrent change.\")); }"
        );
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine(
            "var __nr = __it.Edit.RebaseOnto("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__cev)));"
        );
        code.AppendLine(
            "foreach (var __cc in __nr.Conflicts) __conflicts.Add(__cc.WithPathPrefix(__SparseRootPath.Member("
                + lit
                + ").Key(__it.Key)));"
        );
        code.AppendLine("if (__nr.Conflicts.Count == 0 && !__nr.Rebased.IsEmpty)");
        code.OpenBlock();
        code.AppendLine(
            "var __applied = __nr.Rebased.ToPatch().Apply("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__cev)));"
        );
        code.AppendLine("if (__applied.IsPresent && __applied.Value is not null)");
        code.OpenBlock();
        code.AppendLine("var __um = __applied.Value!.ToModel(); __any" + id + " = true;");
        code.AppendLine(
            runtime
                + "Optional<"
                + elementType
                + "> __nb = "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__cev); "
                + runtime
                + "Optional<"
                + elementType
                + "> __na2 = "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__um);"
        );
        code.AppendLine("int __bi2 = __cpos" + id + "[__it.Key];");
        code.AppendLine(
            "__rlist"
                + id
                + ".Add(new "
                + trans
                + ".Item(__it.Key, __it.TemporaryKey, __nb, __na2, __bi2, __bi2, false, false, true, __it.IsReordered, __nr.Rebased, false)); __cmap"
                + id
                + "[__it.Key] = __um;"
        );
        code.CloseBlock();
        code.CloseBlock();
        code.CloseBlock();
        code.CloseBlock();
        // Reorder-only: keep when current order still matches base rank; else
        // coincidentally ordered.
        code.AppendLine("else if (__it.IsReordered)");
        code.OpenBlock();
        code.AppendLine(
            "if (__cmap"
                + id
                + ".ContainsKey(__it.Key)) { __any"
                + id
                + " = true; int __bi3 = __cpos"
                + id
                + "[__it.Key]; __rlist"
                + id
                + ".Add(new "
                + trans
                + ".Item(__it.Key, __it.TemporaryKey, __it.Before, __it.After, __bi3, __bi3, false, false, false, true, __it.Edit, false)); }"
        );
        code.CloseBlock();
        code.CloseBlock();
        // Order reconciliation: no local order change preserves current order
        // (concurrent adds kept).
        code.AppendLine(
            "global::System.Collections.Generic.List<"
                + keyType
                + ">? __nbO"
                + id
                + " = null; global::System.Collections.Generic.List<"
                + keyType
                + ">? __naO"
                + id
                + " = null;"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine("bool __unassignedOnlyAdds" + id + " = false;");
            code.AppendLine(
                "if ("
                    + KeyedBeforeOrder(member)
                    + " is not null && "
                    + KeyedAfterOrder(member)
                    + " is not null) { var __ordinaryAfter = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(); foreach (var __orderKey in "
                    + KeyedAfterOrder(member)
                    + ") if (!"
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__orderKey")
                    + ") __ordinaryAfter.Add(__orderKey); if ("
                    + facade
                    + ".KeyOrderEquals<"
                    + keyType
                    + ">("
                    + KeyedBeforeOrder(member)
                    + ", __ordinaryAfter)) foreach (var __candidate in "
                    + KeyedItems(member)
                    + " ?? new global::System.Collections.Generic.List<"
                    + trans
                    + ".Item>()) if (__candidate.IsAdded && "
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__candidate.Key")
                    + ") { __unassignedOnlyAdds"
                    + id
                    + " = true; break; } }"
            );
            code.AppendLine(
                "if (__unassignedOnlyAdds"
                    + id
                    + ") { __nbO"
                    + id
                    + " = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(__corder"
                    + id
                    + "); __naO"
                    + id
                    + " = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(__corder"
                    + id
                    + "); foreach (var __newItem in __rlist"
                    + id
                    + ") if (__newItem.IsAdded && "
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__newItem.Key")
                    + ") __naO"
                    + id
                    + ".Add(__newItem.Key); }"
            );
        }
        code.AppendLine(
            "if ("
                + (
                    SparseKeyedCollectionEmitter.HasUnassignedKey(member)
                        ? "__unassignedOnlyAdds" + id + " || ("
                        : ""
                )
                + KeyedBeforeOrder(member)
                + " is not null && "
                + KeyedAfterOrder(member)
                + " is not null && "
                + facade
                + ".KeyOrderEquals<"
                + keyType
                + ">("
                + KeyedBeforeOrder(member)
                + ", "
                + KeyedAfterOrder(member)
                + "))"
                + (SparseKeyedCollectionEmitter.HasUnassignedKey(member) ? ")" : "")
                + " { "
                + (
                    SparseKeyedCollectionEmitter.HasUnassignedKey(member)
                        ? "if (!__unassignedOnlyAdds" + id + ") { "
                        : ""
                )
                + "__nbO"
                + id
                + " = new global::System.Collections.Generic.List<"
                + keyType
                + ">(__corder"
                + id
                + "); __naO"
                + id
                + " = new global::System.Collections.Generic.List<"
                + keyType
                + ">(__corder"
                + id
                + ");"
                + (SparseKeyedCollectionEmitter.HasUnassignedKey(member) ? " }" : "")
                + " }"
        );
        code.AppendLine(
            "else if ("
                + (
                    SparseKeyedCollectionEmitter.HasUnassignedKey(member)
                        ? "!__unassignedOnlyAdds" + id + " && "
                        : ""
                )
                + KeyedBeforeOrder(member)
                + " is not null && "
                + KeyedAfterOrder(member)
                + " is not null)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if ("
                + facade
                + ".KeyOrderEquals<"
                + keyType
                + ">("
                + KeyedBeforeOrder(member)
                + ", __rebaseCurrentOrder"
                + id
                + ")) { __nbO"
                + id
                + " = new global::System.Collections.Generic.List<"
                + keyType
                + ">(__rebaseCurrentOrder"
                + id
                + "); __naO"
                + id
                + " = new global::System.Collections.Generic.List<"
                + keyType
                + ">("
                + KeyedAfterOrder(member)
                + "); }"
        );
        code.AppendLine(
            "else if (!"
                + facade
                + ".KeyOrderEquals<"
                + keyType
                + ">("
                + KeyedAfterOrder(member)
                + ", __rebaseCurrentOrder"
                + id
                + ")) { __conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + lit
                + "), "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Present((object?)"
                + KeyedBeforeOrder(member)
                + "), "
                + runtime
                + "Optional<object?>.Present((object?)"
                + KeyedAfterOrder(member)
                + "), "
                + runtime
                + "Optional<object?>.Present((object?)__rebaseCurrentOrder"
                + id
                + "), \"The collection order conflicts with a concurrent change.\")); }"
        );
        code.CloseBlock();
        if (hasTempRebase)
        {
            SparseChangeSetKeyedTempRebaseEmitter.AppendTempOrderOutput(code, id);
        }
        code.AppendLine(
            "if (__any"
                + id
                + ") { __rh"
                + id
                + " = true; __ritems"
                + id
                + " = __rlist"
                + id
                + "; __rbO"
                + id
                + " = __nbO"
                + id
                + "; __raO"
                + id
                + " = __naO"
                + id
                + "; }"
        );
        code.CloseBlock();
        code.CloseBlock();
    }
}
