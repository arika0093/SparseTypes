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
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedRebaseEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedTransitionEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetMatchEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetPatchSyncEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetRebaseEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetTransitionEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Per-key sparse compose for keyed sequences.</summary>
internal static class SparseChangeSetKeyedComposeEmitter
{
    internal static void AppendKeyedComposeSparse(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseMemberModel member,
        string runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var id = member.Id;
        var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
        var keyType = KeyTypeOf(member);
        var elementType = ElementTypeOf(member);
        var elementCs = ElementChangeSetOf(member);
        var elementFrag = ElementFragmentOf(member);
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var trans = TransNameFor(members, member);
        code.AppendLine("bool __cb" + id + "_has = false;");
        code.AppendLine("bool __cb" + id + "_whole = false;");
        code.AppendLine(opt + " __cb" + id + "_wb = default;");
        code.AppendLine(opt + " __cb" + id + "_wa = default;");
        code.AppendLine(
            "global::System.Collections.Generic.List<"
                + trans
                + ".Item>? __cb"
                + id
                + "_items = null;"
        );
        code.AppendLine(
            "global::System.Collections.Generic.List<" + keyType + ">? __cb" + id + "_bO = null;"
        );
        code.AppendLine(
            "global::System.Collections.Generic.List<" + keyType + ">? __cb" + id + "_aO = null;"
        );
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            code.AppendLine(
                "global::System.Collections.Generic.List<global::System.Guid>? __cb"
                    + id
                    + "_tbO = null;"
            );
            code.AppendLine(
                "global::System.Collections.Generic.List<global::System.Guid>? __cb"
                    + id
                    + "_taO = null;"
            );
        }
        code.OpenBlock();
        var hasTempCompose = SparseKeyedCollectionEmitter.HasTemporaryKey(member);
        if (hasTempCompose)
        {
            SparseChangeSetKeyedTempMergeEmitter.AppendTempLocals(code, member, trans, id);
        }
        code.AppendLine("var __first" + id + "_has = " + HasField(member) + ";");
        code.AppendLine("var __second" + id + "_has = next." + HasField(member) + ";");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            SparseChangeSetKeyedTempMergeEmitter.AppendWholeBeforeGuard(
                code,
                member,
                id,
                hasTempCompose
            );
        }
        code.AppendLine("if (!__first" + id + "_has)");
        code.OpenBlock();
        code.AppendLine(
            "__cb"
                + id
                + "_has = __second"
                + id
                + "_has; __cb"
                + id
                + "_whole = next."
                + KeyedWholeFlag(member)
                + ";"
        );
        code.AppendLine(
            "__cb"
                + id
                + "_wb = next."
                + KeyedWholeBefore(member)
                + "; __cb"
                + id
                + "_wa = next."
                + KeyedWholeAfter(member)
                + ";"
        );
        code.AppendLine(
            "__cb"
                + id
                + "_items = next."
                + KeyedItems(member)
                + "; __cb"
                + id
                + "_bO = next."
                + KeyedBeforeOrder(member)
                + "; __cb"
                + id
                + "_aO = next."
                + KeyedAfterOrder(member)
                + ";"
        );
        code.CloseBlock();
        code.AppendLine("else if (!__second" + id + "_has)");
        code.OpenBlock();
        code.AppendLine(
            "__cb" + id + "_has = true; __cb" + id + "_whole = " + KeyedWholeFlag(member) + ";"
        );
        code.AppendLine(
            "__cb"
                + id
                + "_wb = "
                + KeyedWholeBefore(member)
                + "; __cb"
                + id
                + "_wa = "
                + KeyedWholeAfter(member)
                + ";"
        );
        code.AppendLine(
            "__cb"
                + id
                + "_items = "
                + KeyedItems(member)
                + "; __cb"
                + id
                + "_bO = "
                + KeyedBeforeOrder(member)
                + "; __cb"
                + id
                + "_aO = "
                + KeyedAfterOrder(member)
                + ";"
        );
        code.CloseBlock();
        // Whole member transitions (Missing/null/value): endpoint composition.
        code.AppendLine(
            "else if (" + KeyedWholeFlag(member) + " || next." + KeyedWholeFlag(member) + ")"
        );
        code.OpenBlock();
        code.AppendLine("__cb" + id + "_has = true;");
        code.AppendLine(
            "var __w1b = "
                + KeyedWholeBefore(member)
                + "; var __w1a = "
                + KeyedWholeAfter(member)
                + ";"
        );
        code.AppendLine(
            "var __w2b = next."
                + KeyedWholeBefore(member)
                + "; var __w2a = next."
                + KeyedWholeAfter(member)
                + ";"
        );
        code.AppendLine(
            "if (" + KeyedWholeFlag(member) + " && next." + KeyedWholeFlag(member) + ")"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (!Fragment.__SparseEqual_"
                + id
                + "(__w1a, __w2b)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLine(
            "if (Fragment.__SparseEqual_" + id + "(__w1b, __w2a)) { __cb" + id + "_has = false; }"
        );
        code.AppendLine(
            "else { __cb"
                + id
                + "_whole = true; __cb"
                + id
                + "_wb = __w1b; __cb"
                + id
                + "_wa = __w2a; }"
        );
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine(
            "throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            SparseChangeSetKeyedTempMergeEmitter.AppendGranularBeforeGuard(
                code,
                member,
                id,
                hasTempCompose
            );
        }
        // Granular + granular per-key merge.
        if (hasTempCompose)
        {
            SparseChangeSetKeyedTempMergeEmitter.AppendSegregateFirst(
                code,
                member,
                keyType,
                trans,
                comparer,
                id
            );
        }
        else
        {
            code.AppendLine(
                "var __map1"
                    + id
                    + " = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + trans
                    + ".Item>("
                    + KeyedItems(member)
                    + "?.Count ?? 0, "
                    + comparer
                    + ");"
            );
            code.AppendLine(
                "if ("
                    + KeyedItems(member)
                    + " is not null) foreach (var __it in "
                    + KeyedItems(member)
                    + ") __map1"
                    + id
                    + "[__it.Key] = __it;"
            );
        }
        if (!hasTempCompose)
        {
            code.AppendLine(
                "var __map2"
                    + id
                    + " = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + trans
                    + ".Item>("
                    + "next."
                    + KeyedItems(member)
                    + "?.Count ?? 0, "
                    + comparer
                    + ");"
            );
        }
        // Second-side segregation mirrors the first side: temp items merge by
        // Guid while legacy temp-less unassigned adds keep positional flow.
        if (hasTempCompose)
        {
            SparseChangeSetKeyedTempMergeEmitter.AppendSegregateSecond(
                code,
                member,
                keyType,
                trans,
                comparer,
                id
            );
        }
        else if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine(
                "var __unassignedNetItems"
                    + id
                    + " = new global::System.Collections.Generic.List<"
                    + trans
                    + ".Item>(); if (next."
                    + KeyedItems(member)
                    + " is not null) foreach (var __it in next."
                    + KeyedItems(member)
                    + ") { if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__it.Key")
                    + ") { if (!__it.IsAdded) throw new global::System.InvalidOperationException(\"An unassigned key cannot be used as a baseline change.\"); __unassignedNetItems"
                    + id
                    + ".Add(__it); } else __map2"
                    + id
                    + "[__it.Key] = __it; }"
            );
        }
        else
        {
            code.AppendLine(
                "if (next."
                    + KeyedItems(member)
                    + " is not null) foreach (var __it in next."
                    + KeyedItems(member)
                    + ") __map2"
                    + id
                    + "[__it.Key] = __it;"
            );
        }

        code.AppendLine(
            "var __keys"
                + id
                + " = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(__map1"
                + id
                + ".Keys, "
                + comparer
                + ");"
        );
        code.AppendLine("__keys" + id + ".UnionWith(__map2" + id + ".Keys);");
        // Keys occurring in only one input always survive composition.
        code.AppendLine(
            "var __net"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + trans
                + ".Item>((__keys"
                + id
                + ".Count - __map1"
                + id
                + ".Count) + (__keys"
                + id
                + ".Count - __map2"
                + id
                + ".Count), "
                + comparer
                + ");"
        );
        // Index only second-only keys that occur in the retained first order.
        var continuityDictionary =
            "global::System.Collections.Generic.Dictionary<" + keyType + ", " + trans + ".Item>";
        code.AppendLine(
            "static global::System.Collections.Generic.HashSet<"
                + keyType
                + ">? __IndexContinuity"
                + id
                + "(global::System.Collections.Generic.List<"
                + keyType
                + "> order, "
                + continuityDictionary
                + " first, "
                + continuityDictionary
                + " second, int capacity)"
        );
        code.OpenBlock();
        if (dialect.HashSetSupportsCapacity)
        {
            code.AppendLine(
                "global::System.Collections.Generic.HashSet<" + keyType + ">? present = null;"
            );
            code.AppendLine(
                "foreach (var key in order!) if (second.ContainsKey(key) && !first.ContainsKey(key)) (present ??= new global::System.Collections.Generic.HashSet<"
                    + keyType
                    + ">(capacity, "
                    + comparer
                    + ")).Add(key);"
            );
            code.AppendLine("return present;");
        }
        else
        {
            code.AppendLine(
                "global::System.Collections.Generic.List<" + keyType + ">? present = null;"
            );
            code.AppendLine(
                "foreach (var key in order!) if (second.ContainsKey(key) && !first.ContainsKey(key)) (present ??= new global::System.Collections.Generic.List<"
                    + keyType
                    + ">()).Add(key);"
            );
            code.AppendLine(
                "return present is null ? null : new global::System.Collections.Generic.HashSet<"
                    + keyType
                    + ">(present, "
                    + comparer
                    + ");"
            );
        }
        code.CloseBlock();
        code.AppendLine(
            "var __indexContinuity"
                + id
                + " = "
                + KeyedAfterOrder(member)
                + " is not null && "
                + KeyedAfterOrder(member)
                + ".Count > 16 && __keys"
                + id
                + ".Count - __map1"
                + id
                + ".Count > 4;"
        );
        code.AppendLine(
            "var __presentSecondOnly"
                + id
                + " = __indexContinuity"
                + id
                + " ? __IndexContinuity"
                + id
                + "("
                + KeyedAfterOrder(member)
                + "!, __map1"
                + id
                + ", __map2"
                + id
                + ", global::System.Math.Min("
                + KeyedAfterOrder(member)
                + "!.Count, __keys"
                + id
                + ".Count - __map1"
                + id
                + ".Count)) : null;"
        );
        code.AppendLine("foreach (var __k in __keys" + id + ")");
        code.OpenBlock();
        code.AppendLine(
            "var __has1 = __map1"
                + id
                + ".TryGetValue(__k, out var __a1); var __has2 = __map2"
                + id
                + ".TryGetValue(__k, out var __a2);"
        );
        // Directional continuity: second's expected before must hold in first's
        // after.
        code.AppendLine("if (!__has1)");
        code.OpenBlock();
        code.AppendLine("var __o1aChk = " + KeyedAfterOrder(member) + ";");
        code.AppendLine(
            "if (__o1aChk is not null) { bool __in1; if (__indexContinuity"
                + id
                + ") __in1 = __presentSecondOnly"
                + id
                + "?.Contains(__k) == true; else { __in1 = false; foreach (var __ok in __o1aChk) if ("
                + comparer
                + ".Equals(__ok, __k)) { __in1 = true; break; } } if (__a2!.IsAdded ? __in1 : !__in1) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\"); }"
        );
        code.AppendLine("__net" + id + "[__k] = __a2!; continue;");
        code.CloseBlock();
        code.AppendLine("if (!__has2) { __net" + id + "[__k] = __a1!; continue; }");
        // Both changed: validate continuity and merge by membership kind.
        // Added (Missing->after) cases.
        code.AppendLine("if (__a1!.IsAdded && __a2!.IsAdded)");
        code.AppendLine(
            "{ throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\"); }"
        );
        code.AppendLine("if (__a1!.IsAdded && __a2!.IsRemoved)");
        code.OpenBlock();
        code.AppendLine(
            "if (!"
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a1.After.Value!)), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a2.Before.Value!))).IsEmpty) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLine("continue;");
        code.CloseBlock();
        code.AppendLine("if (__a1!.IsRemoved && __a2!.IsAdded)");
        code.OpenBlock();
        code.AppendLine(
            "if ("
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a1.Before.Value!)), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a2.After.Value!))).IsEmpty) continue;"
        );
        code.AppendLine(
            runtime
                + "Optional<"
                + elementFrag
                + "?> __eb = "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a1.Before.Value!));"
        );
        code.AppendLine(
            runtime
                + "Optional<"
                + elementFrag
                + "?> __ea = "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a2.After.Value!));"
        );
        code.AppendLine("var __edit = " + elementCs + ".Between(__eb, __ea);");
        code.AppendLine(
            "__net"
                + id
                + "[__k] = new "
                + trans
                + ".Item(__k, null, __a1.Before, __a2.After, __a1.BeforeIndex, __a2.AfterIndex, false, false, true, false, __edit, false);"
        );
        code.AppendLine("continue;");
        code.CloseBlock();
        code.AppendLine("if (__a1!.IsRemoved || __a2!.IsAdded)");
        code.AppendLine(
            "{ throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\"); }"
        );
        // Added then edited: compose the nested transitions and materialize
        // only the final added value.
        code.AppendLine("if (__a1!.IsAdded && __a2!.IsEdited)");
        code.OpenBlock();
        code.AppendLine("var __edit2 = __a1.Edit.Compose(__a2.Edit);");
        code.AppendLine("var __addedState = __edit2.ToPatch().Apply(default);");
        code.AppendLine(
            "if (!__addedState.IsPresent || __addedState.Value is null) throw new global::System.InvalidOperationException(\"Composed addition did not produce an element value.\");"
        );
        code.AppendLine("var __addedValue = __addedState.Value!.ToModel();");
        code.AppendLine(
            "__net"
                + id
                + "[__k] = new "
                + trans
                + ".Item(__k, null, default, "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__addedValue), -1, __a2.AfterIndex, true, false, false, false, __edit2, false);"
        );
        code.AppendLine("continue;");
        code.CloseBlock();
        // Edited then removed: invert the composed transition to recover only
        // the required removed value.
        code.AppendLine("if ((__a1!.IsEdited || __a1!.IsReordered) && __a2!.IsRemoved)");
        code.OpenBlock();
        code.AppendLine(
            "var __removedEdit = __a1.IsEdited ? __a1.Edit.Compose(__a2.Edit) : __a2.Edit;"
        );
        code.AppendLine("var __removedState = __removedEdit.Invert().ToPatch().Apply(default);");
        code.AppendLine(
            "if (!__removedState.IsPresent || __removedState.Value is null) throw new global::System.InvalidOperationException(\"Composed removal did not produce an element value.\");"
        );
        code.AppendLine("var __removedValue = __removedState.Value!.ToModel();");
        code.AppendLine(
            "__net"
                + id
                + "[__k] = new "
                + trans
                + ".Item(__k, null, "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__removedValue), default, __a1.BeforeIndex >= 0 ? __a1.BeforeIndex : __a2.BeforeIndex, -1, false, true, false, false, __removedEdit, false);"
        );
        code.AppendLine("continue;");
        code.CloseBlock();
        // Edited/edited (or reorder-involved) continuity via nested compose;
        // scalar fallback via equality.
        code.AppendLine("if (__a1!.IsEdited && __a2!.IsEdited)");
        code.OpenBlock();
        code.AppendLine("var __cc = __a1.Edit.Compose(__a2.Edit);");
        code.AppendLine("if (__cc.IsEmpty) { continue; }");
        code.AppendLine(
            "__net"
                + id
                + "[__k] = new "
                + trans
                + ".Item(__k, null, __a1.Before, __a2.After, __a1.BeforeIndex, __a2.AfterIndex, false, false, true, __a1.IsReordered || __a2.IsReordered, __cc, false);"
        );
        code.AppendLine("continue;");
        code.CloseBlock();
        code.AppendLine("if (__a1!.IsEdited || __a2!.IsEdited)");
        code.OpenBlock();
        code.AppendLine(
            "if (__a1.IsEdited && __a2.IsReordered && !__a2.IsEdited) { __net"
                + id
                + "[__k] = new "
                + trans
                + ".Item(__k, null, __a1.Before, __a1.After, __a1.BeforeIndex, __a2.AfterIndex, false, false, true, true, __a1.Edit, false); continue; }"
        );
        code.AppendLine(
            "if (__a2.IsEdited && __a1.IsReordered && !__a1.IsEdited) { var __cc2 = __a2.Edit; __net"
                + id
                + "[__k] = new "
                + trans
                + ".Item(__k, null, __a2.Before, __a2.After, __a1.BeforeIndex, __a2.AfterIndex, false, false, true, true, __cc2, false); continue; }"
        );
        code.AppendLine(
            "throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.CloseBlock();
        // Reorder-only on both or one side: continuity holds (same values); net
        // keeps reorder flag.
        code.AppendLine(
            "if (__a1!.IsReordered || __a2!.IsReordered) { var __keep = __a2!.IsReordered || __a1!.IsReordered; var __b = __a1.Before.IsPresent ? __a1.Before : __a2.Before; var __a = __a2.After.IsPresent ? __a2.After : __a1.After; var __bi = __a1.BeforeIndex >= 0 ? __a1.BeforeIndex : __a2.BeforeIndex; var __ai = __a2.AfterIndex >= 0 ? __a2.AfterIndex : __a1.AfterIndex; __net"
                + id
                + "[__k] = new "
                + trans
                + ".Item(__k, null, __b, __a, __bi, __ai, false, false, false, true, "
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__b.Value!)), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a.Value!))), false); continue; }"
        );
        code.AppendLine(
            "throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.CloseBlock();
        if (hasTempCompose)
        {
            SparseChangeSetKeyedTempPairEmitter.AppendTempPairMerge(
                code,
                member,
                trans,
                runtime,
                elementCs,
                elementFrag,
                elementType,
                id
            );
        }
        SparseChangeSetKeyedComposeOrdersEmitter.AppendOrdersAndAssembly(
            code,
            members,
            member,
            dialect,
            hasTempCompose
        );
        code.CloseBlock();
        code.CloseBlock();
    }
}
