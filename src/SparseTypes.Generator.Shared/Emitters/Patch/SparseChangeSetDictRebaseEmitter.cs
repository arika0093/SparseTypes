using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetBetweenEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetComposeEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetDictBetweenEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetDictComposeEmitter;
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

/// <summary>Sparse per-key rebase for dictionaries.</summary>
internal static class SparseChangeSetDictRebaseEmitter
{
    /// <summary>Emits sparse per-key rebase for a dictionary member.</summary>
    internal static void AppendDictRebaseSparse(
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
        var valueType = ValueTypeOf(member);
        var optValue = runtime + "Optional<" + valueType + ">";
        var facade = dialect.RuntimeFacade;
        var conflict = dialect.ConflictType;
        var conflictKind = dialect.ConflictKindType;
        var hasPatch = member.Collection.ValueType?.IsFragmentModel == true;
        var valueFrag = hasPatch ? ValueFragmentOf(member) : null;
        var trans = TransNameFor(members, member);
        SparseChangeSetMemberRebaseEmitter.AppendRedactedGuard(
            code,
            member,
            lit,
            runtime,
            conflict,
            dialect,
            HasField(member),
            [
                "__rh" + id + " = true;",
                "__rwhole" + id + " = " + KeyedWholeFlag(member) + ";",
                "__rwb" + id + " = " + KeyedWholeBefore(member) + ";",
                "__rwa" + id + " = " + KeyedWholeAfter(member) + ";",
                "__ritems" + id + " = " + KeyedItems(member) + ";",
            ]
        );
        code.AppendLine("if (" + HasField(member) + " && !__red" + id + ")");
        code.OpenBlock();
        code.AppendLine("var __curM" + id + " = __cur." + esc + ";");
        code.AppendLine("if (" + KeyedWholeFlag(member) + ")");
        code.OpenBlock();
        code.AppendLine("if (Fragment.__SparseEqual_" + id + "(" + KeyedWholeAfter(member) + ", __curM" + id + ")) { }");
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
        code.AppendLine("else if (!__curM" + id + ".IsPresent || (object?)__curM" + id + ".Value is null)");
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
        code.AppendLine("var __rlist" + id + " = new global::System.Collections.Generic.List<" + trans + ".Item>();");
        code.AppendLine("bool __any" + id + " = false;");
        code.AppendLine("if (" + KeyedItems(member) + " is not null) foreach (var __it in " + KeyedItems(member) + ")");
        code.OpenBlock();
        code.AppendLine("if (__it.IsAdded)");
        code.OpenBlock();
        code.AppendLine("if (!__curM" + id + ".Value!.ContainsKey(__it.Key!))");
        code.OpenBlock();
        code.AppendLine("__any" + id + " = true;");
        if (hasPatch)
        {
            code.AppendLine("__rlist" + id + ".Add(new " + trans + ".Item(__it.Key!, default, __it.After, true, false, false, __it.Edit, false));");
        }
        else
        {
            code.AppendLine("__rlist" + id + ".Add(new " + trans + ".Item(__it.Key!, default, __it.After, true, false, false, false));");
        }

        code.CloseBlock();
        code.AppendLine("else if (!" + facade + ".AreEqual(__curM" + id + ".Value![__it.Key!], __it.After.Value))");
        code.AppendLine(
            "{ __conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + lit
                + ").Key(__it.Key!), "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Present((object?)__it.After.Value), "
                + runtime
                + "Optional<object?>.Present((object?)__curM"
                + id
                + ".Value![__it.Key!]), \"The dictionary entry conflicts with a concurrent change.\")); }"
        );
        code.CloseBlock();
        code.AppendLine("else if (__it.IsRemoved)");
        code.OpenBlock();
        code.AppendLine("if (!__curM" + id + ".Value!.ContainsKey(__it.Key!)) { }");
        code.AppendLine("else if (!" + facade + ".AreEqual(__curM" + id + ".Value![__it.Key!], __it.Before.Value))");
        code.AppendLine(
            "{ __conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + lit
                + ").Key(__it.Key!), "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Present((object?)__it.Before.Value), "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Present((object?)__curM"
                + id
                + ".Value![__it.Key!]), \"The dictionary entry conflicts with a concurrent change.\")); }"
        );
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine("__any" + id + " = true;");
        if (hasPatch)
        {
            code.AppendLine("__rlist" + id + ".Add(new " + trans + ".Item(__it.Key!, __it.Before, default, false, true, false, __it.Edit, false));");
        }
        else
        {
            code.AppendLine("__rlist" + id + ".Add(new " + trans + ".Item(__it.Key!, __it.Before, default, false, true, false, false));");
        }

        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        if (hasPatch)
        {
            code.AppendLine("if (!__curM" + id + ".Value!.TryGetValue(__it.Key!, out var __cev))");
            code.AppendLine(
                "{ __conflicts.Add(new "
                    + conflict
                    + "(__SparseRootPath.Member("
                    + lit
                    + ").Key(__it.Key!), "
                    + conflictKind
                    + ".Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)__it.Before.Value), "
                    + runtime
                    + "Optional<object?>.Present((object?)__it.After.Value), "
                    + runtime
                    + "Optional<object?>.Missing, \"The dictionary entry conflicts with a concurrent change.\")); }"
            );
            code.AppendLine("else");
            code.OpenBlock();
            code.AppendLine("var __nr = __it.Edit.RebaseOnto(" + runtime + "Optional<" + valueFrag + "?>.Present(" + valueFrag + ".From(__cev)));");
            code.AppendLine(
                "foreach (var __cc in __nr.Conflicts) __conflicts.Add(__cc.WithPathPrefix(__SparseRootPath.Member(" + lit + ").Key(__it.Key!)));"
            );
            code.AppendLine("if (__nr.Conflicts.Count == 0 && !__nr.Rebased.IsEmpty)");
            code.OpenBlock();
            code.AppendLine(
                "var __applied = __nr.Rebased.ToPatch().Apply(" + runtime + "Optional<" + valueFrag + "?>.Present(" + valueFrag + ".From(__cev)));"
            );
            code.AppendLine("if (__applied.IsPresent && __applied.Value is not null)");
            code.OpenBlock();
            code.AppendLine("__any" + id + " = true;");
            code.AppendLine(
                optValue + " __nb = " + optValue + ".Present(__cev); " + optValue + " __na2 = " + optValue + ".Present(__applied.Value!.ToModel());"
            );
            code.AppendLine("__rlist" + id + ".Add(new " + trans + ".Item(__it.Key!, __nb, __na2, false, false, true, __nr.Rebased, false));");
            code.CloseBlock();
            code.CloseBlock();
            code.CloseBlock();
        }
        else
        {
            code.AppendLine("if (!__curM" + id + ".Value!.TryGetValue(__it.Key!, out var __cev2))");
            code.AppendLine(
                "{ __conflicts.Add(new "
                    + conflict
                    + "(__SparseRootPath.Member("
                    + lit
                    + ").Key(__it.Key!), "
                    + conflictKind
                    + ".Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)__it.Before.Value), "
                    + runtime
                    + "Optional<object?>.Present((object?)__it.After.Value), "
                    + runtime
                    + "Optional<object?>.Missing, \"The dictionary entry conflicts with a concurrent change.\")); }"
            );
            code.AppendLine("else if (" + facade + ".AreEqual(__cev2, __it.After.Value)) { }");
            code.AppendLine("else if (!" + facade + ".AreEqual(__cev2, __it.Before.Value))");
            code.AppendLine(
                "{ __conflicts.Add(new "
                    + conflict
                    + "(__SparseRootPath.Member("
                    + lit
                    + ").Key(__it.Key!), "
                    + conflictKind
                    + ".Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)__it.Before.Value), "
                    + runtime
                    + "Optional<object?>.Present((object?)__it.After.Value), "
                    + runtime
                    + "Optional<object?>.Present((object?)__cev2), \"The dictionary entry conflicts with a concurrent change.\")); }"
            );
            code.AppendLine("else");
            code.OpenBlock();
            code.AppendLine("__any" + id + " = true;");
            code.AppendLine("__rlist" + id + ".Add(new " + trans + ".Item(__it.Key!, __it.Before, __it.After, false, false, true, false));");
            code.CloseBlock();
        }
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("if (__any" + id + ") { __rh" + id + " = true; __ritems" + id + " = __rlist" + id + "; }");
        code.CloseBlock();
        code.CloseBlock();
    }
}
