using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetBetweenEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetComposeEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetDictBetweenEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetDictRebaseEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetDictTransitionEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedBetweenEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedComposeEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedRebaseEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedTransitionEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetMatchEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetPatchSyncEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetRebaseEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetTransitionEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Per-key sparse compose for dictionaries.</summary>
/// <remarks>Merge-time seam: pure entry-set composition delegates to the Generated-Once kernel helpers.</remarks>
internal static class SparseChangeSetDictComposeEmitter
{
    /// <summary>Emits per-key sparse compose for a dictionary member.</summary>
    internal static void AppendDictComposeSparse(
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
        var comparer = "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var facade = dialect.RuntimeFacade;
        var hasPatch = member.Collection.ValueType?.IsFragmentModel == true;
        var valueType = hasPatch ? member.Collection.ValueType!.Value.NonNullableName : null;
        var valueCs = hasPatch ? ValueChangeSetOf(member) : null;
        var valueFrag = hasPatch ? ValueFragmentOf(member) : null;
        var trans = TransNameFor(members, member);
        code.AppendLine("bool __cb" + id + "_has = false;");
        code.AppendLine("bool __cb" + id + "_whole = false;");
        code.AppendLine(opt + " __cb" + id + "_wb = default;");
        code.AppendLine(opt + " __cb" + id + "_wa = default;");
        code.AppendLine("global::System.Collections.Generic.List<" + trans + ".Item>? __cb" + id + "_items = null;");
        code.OpenBlock();
        code.AppendLine("var __first" + id + "_has = " + HasField(member) + ";");
        code.AppendLine("var __second" + id + "_has = next." + HasField(member) + ";");
        code.AppendLine("if (!__first" + id + "_has)");
        code.OpenBlock();
        code.AppendLine("__cb" + id + "_has = __second" + id + "_has; __cb" + id + "_whole = next." + KeyedWholeFlag(member) + ";");
        code.AppendLine("__cb" + id + "_wb = next." + KeyedWholeBefore(member) + "; __cb" + id + "_wa = next." + KeyedWholeAfter(member) + ";");
        code.AppendLine("__cb" + id + "_items = next." + KeyedItems(member) + ";");
        code.CloseBlock();
        code.AppendLine("else if (!__second" + id + "_has)");
        code.OpenBlock();
        code.AppendLine("__cb" + id + "_has = true; __cb" + id + "_whole = " + KeyedWholeFlag(member) + ";");
        code.AppendLine("__cb" + id + "_wb = " + KeyedWholeBefore(member) + "; __cb" + id + "_wa = " + KeyedWholeAfter(member) + ";");
        code.AppendLine("__cb" + id + "_items = " + KeyedItems(member) + ";");
        code.CloseBlock();
        code.AppendLine("else if (" + KeyedWholeFlag(member) + " || next." + KeyedWholeFlag(member) + ")");
        code.OpenBlock();
        code.AppendLine("__cb" + id + "_has = true;");
        code.AppendLine("var __w1b = " + KeyedWholeBefore(member) + "; var __w1a = " + KeyedWholeAfter(member) + ";");
        code.AppendLine("var __w2b = next." + KeyedWholeBefore(member) + "; var __w2a = next." + KeyedWholeAfter(member) + ";");
        // Both whole: continuity on middle, net whole when non-empty.
        code.AppendLine("if (" + KeyedWholeFlag(member) + " && next." + KeyedWholeFlag(member) + ")");
        code.OpenBlock();
        code.AppendLine(
            "if (!Fragment.__SparseEqual_"
                + id
                + "(__w1a, __w2b)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLine("if (Fragment.__SparseEqual_" + id + "(__w1b, __w2a)) { __cb" + id + "_has = false; }");
        code.AppendLine("else { __cb" + id + "_whole = true; __cb" + id + "_wb = __w1b; __cb" + id + "_wa = __w2a; }");
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        // Mixed whole/granular member transitions are out of the sparse fast
        // path.
        code.AppendLine(
            "throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
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
            "if (" + KeyedItems(member) + " is not null) foreach (var __it in " + KeyedItems(member) + ") __map1" + id + "[__it.Key!] = __it;"
        );
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
        code.AppendLine(
            "if (next."
                + KeyedItems(member)
                + " is not null) foreach (var __it in next."
                + KeyedItems(member)
                + ") __map2"
                + id
                + "[__it.Key!] = __it;"
        );
        code.AppendLine(
            "var __keys" + id + " = new global::System.Collections.Generic.HashSet<" + keyType + ">(__map1" + id + ".Keys, " + comparer + ");"
        );
        code.AppendLine("__keys" + id + ".UnionWith(__map2" + id + ".Keys);");
        // Keys present in only one input always survive composition. Reserve
        // their count without allocating for fully overlapping cancellations.
        code.AppendLine(
            "var __capacity" + id + " = (__keys" + id + ".Count - __map1" + id + ".Count) + (__keys" + id + ".Count - __map2" + id + ".Count);"
        );
        var netType = "global::System.Collections.Generic.Dictionary<" + keyType + ", " + trans + ".Item>";
        code.AppendLine(
            "var __net"
                + id
                + " = __capacity"
                + id
                + " == 0 ? new "
                + netType
                + "("
                + comparer
                + ") : new "
                + netType
                + "(__capacity"
                + id
                + ", "
                + comparer
                + ");"
        );
        code.AppendLine("foreach (var __k in __keys" + id + ")");
        code.OpenBlock();
        code.AppendLine(
            "var __has1 = __map1" + id + ".TryGetValue(__k!, out var __a1); var __has2 = __map2" + id + ".TryGetValue(__k!, out var __a2);"
        );
        code.AppendLine("if (!__has1) { __net" + id + "[__k!] = __a2!; continue; }");
        code.AppendLine("if (!__has2) { __net" + id + "[__k!] = __a1!; continue; }");
        code.AppendLine("if (__a1!.IsAdded && __a2!.IsAdded)");
        code.AppendLine(
            "{ throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\"); }"
        );
        code.AppendLine("if (__a1!.IsAdded && __a2!.IsRemoved)");
        code.OpenBlock();
        code.AppendLine(
            "if (!"
                + facade
                + ".AreEqual(__a1.After.Value, __a2.Before.Value)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLine("continue;");
        code.CloseBlock();
        code.AppendLine("if (__a1!.IsRemoved && __a2!.IsAdded)");
        code.OpenBlock();
        code.AppendLine("if (" + facade + ".AreEqual(__a1.Before.Value, __a2.After.Value)) continue;");
        if (hasPatch)
        {
            code.AppendLine(
                runtime
                    + "Optional<"
                    + valueFrag
                    + "?> __eb = "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__a1.Before.Value!));"
            );
            code.AppendLine(
                runtime
                    + "Optional<"
                    + valueFrag
                    + "?> __ea = "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__a2.After.Value!));"
            );
            code.AppendLine("var __edit = " + valueCs + ".Between(__eb, __ea);");
            code.AppendLine("if (__edit.IsEmpty) continue;");
            code.AppendLine("__net" + id + "[__k!] = new " + trans + ".Item(__k!, __a1.Before, __a2.After, false, false, true, __edit, false);");
        }
        else
        {
            code.AppendLine("__net" + id + "[__k!] = new " + trans + ".Item(__k!, __a1.Before, __a2.After, false, false, true, false);");
        }
        code.AppendLine("continue;");
        code.CloseBlock();
        code.AppendLine("if (__a1!.IsRemoved || __a2!.IsAdded)");
        code.AppendLine(
            "{ throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\"); }"
        );
        code.AppendLine("if (__a1!.IsAdded && __a2!.IsEdited)");
        code.OpenBlock();
        if (hasPatch)
        {
            code.AppendLine("var __edit2 = __a1.Edit.Compose(__a2.Edit);");
            code.AppendLine("var __addedState = __edit2.ToPatch().Apply(default);");
            code.AppendLine(
                "if (!__addedState.IsPresent || __addedState.Value is null) throw new global::System.InvalidOperationException(\"Composed addition did not produce a value.\");"
            );
            code.AppendLine("var __addedValue = __addedState.Value!.ToModel();");
            code.AppendLine(
                "__net"
                    + id
                    + "[__k!] = new "
                    + trans
                    + ".Item(__k!, default, "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__addedValue), true, false, false, __edit2, false);"
            );
        }
        else
        {
            code.AppendLine(
                "if (!"
                    + facade
                    + ".AreEqual(__a1.After.Value, __a2.Before.Value)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
            );
            code.AppendLine("__net" + id + "[__k!] = new " + trans + ".Item(__k!, default, __a2.After, true, false, false, false);");
        }
        code.AppendLine("continue;");
        code.CloseBlock();
        code.AppendLine("if (__a1!.IsEdited && __a2!.IsRemoved)");
        code.OpenBlock();
        if (hasPatch)
        {
            code.AppendLine("var __removedEdit = __a1.Edit.Compose(__a2.Edit);");
            code.AppendLine("var __removedState = __removedEdit.Invert().ToPatch().Apply(default);");
            code.AppendLine(
                "if (!__removedState.IsPresent || __removedState.Value is null) throw new global::System.InvalidOperationException(\"Composed removal did not produce a value.\");"
            );
            code.AppendLine("var __removedValue = __removedState.Value!.ToModel();");
            code.AppendLine(
                "__net"
                    + id
                    + "[__k!] = new "
                    + trans
                    + ".Item(__k!, "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__removedValue), default, false, true, false, __removedEdit, false);"
            );
        }
        else
        {
            code.AppendLine(
                "if (!"
                    + facade
                    + ".AreEqual(__a1.After.Value, __a2.Before.Value)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
            );
            code.AppendLine("__net" + id + "[__k!] = new " + trans + ".Item(__k!, __a1.Before, default, false, true, false, false);");
        }
        code.AppendLine("continue;");
        code.CloseBlock();
        if (hasPatch)
        {
            code.AppendLine("if (__a1!.IsEdited && __a2!.IsEdited)");
            code.OpenBlock();
            code.AppendLine("var __cc = __a1.Edit.Compose(__a2.Edit);");
            code.AppendLine("if (__cc.IsEmpty) { continue; }");
            code.AppendLine("__net" + id + "[__k!] = new " + trans + ".Item(__k!, __a1.Before, __a2.After, false, false, true, __cc, false);");
            code.AppendLine("continue;");
            code.CloseBlock();
            code.AppendLine(
                "throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
            );
        }
        else
        {
            code.AppendLine("if (__a1!.IsEdited && __a2!.IsEdited)");
            code.OpenBlock();
            code.AppendLine(
                "if (!"
                    + facade
                    + ".AreEqual(__a1.After.Value, __a2.Before.Value)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
            );
            code.AppendLine("if (" + facade + ".AreEqual(__a1.Before.Value, __a2.After.Value)) { continue; }");
            code.AppendLine("__net" + id + "[__k!] = new " + trans + ".Item(__k!, __a1.Before, __a2.After, false, false, true, false);");
            code.AppendLine("continue;");
            code.CloseBlock();
            code.AppendLine(
                "throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
            );
        }
        code.CloseBlock();
        code.AppendLine("if (__net" + id + ".Count == 0) { }");
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine("__cb" + id + "_has = true;");
        code.AppendLine("var __elist" + id + " = new global::System.Collections.Generic.List<" + trans + ".Item>(__net" + id + ".Count);");
        code.AppendLine("foreach (var __kv in __net" + id + ") __elist" + id + ".Add(__kv.Value);");
        code.AppendLine("__cb" + id + "_items = __elist" + id + ";");
        code.CloseBlock();
        code.CloseBlock();
        code.CloseBlock();
    }
}
