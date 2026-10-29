namespace SparseTypes.Generator.Shared;

/// <summary>Temporary-identity fragments for keyed-sequence ChangeSet rebase.</summary>
/// <remarks>Correlates temporary-keyed change items against the current state's temporary map instead of the assigned-key map.</remarks>
internal static class SparseChangeSetKeyedTempRebaseEmitter
{
    /// <summary>Emits the temp-aware current-state walk for a keyed rebase.</summary>
    internal static void AppendTempCurrentWalk(SharedIndentedBuilder code, SparseMemberModel member, string elementType, int id)
    {
        var tempOf = "__SparseTemporaryKeyOf_ChangeSet_" + id;
        code.AppendLine("var __tcur" + id + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, " + elementType + ">();");
        code.AppendLine("var __tcorder" + id + " = new global::System.Collections.Generic.List<global::System.Guid>();");
        // Temporary current-position index built in the same pass as the
        // temp map/order; assigned positions reuse the __cpos table so both
        // stay aligned with __corder.
        code.AppendLine("var __tpos" + id + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, int>();");
        code.AppendLine(
            "foreach (var __e in __curM"
                + id
                + ".Value!) { var __ck = __SparseKeyOf_ChangeSet_"
                + id
                + "(__e); if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__ck")
                + ") { var __ct = "
                + tempOf
                + "(__e); if (!__ct.HasValue) throw new global::System.InvalidOperationException(\"An unassigned keyed element is missing its temporary identity.\"); if (__ct.Value == global::System.Guid.Empty) throw new global::System.InvalidOperationException(\"An unassigned keyed element has a reserved temporary identity.\"); "
                + SparseKeyedCollectionEmitter.AddUniqueTempEntry("__tcur" + id, "__ct.Value", "__e")
                + " __tpos"
                + id
                + "[__ct.Value] = __tcorder"
                + id
                + ".Count; __tcorder"
                + id
                + ".Add(__ct.Value); __cpos"
                + id
                + "[__ck] = __corder"
                + id
                + ".Count; __corder"
                + id
                + ".Add(__ck); } else { "
                + SparseKeyedCollectionEmitter.AddUniqueEntry("__cmap" + id, "__ck", "__e")
                + " __cpos"
                + id
                + "[__ck] = __corder"
                + id
                + ".Count; __corder"
                + id
                + ".Add(__ck); } }"
        );
    }

    /// <summary>Emits the per-item temporary branch of a keyed rebase.</summary>
    internal static void AppendTempItemBranch(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string trans,
        string runtime,
        string elementCs,
        string elementFrag,
        string elementType,
        string conflict,
        string conflictKind,
        string lit,
        int id
    )
    {
        var optElement = runtime + "Optional<" + elementType + ">";
        var optElementFrag = runtime + "Optional<" + elementFrag + "?>";
        var rootPath = "__SparseRootPath.Member(" + lit + ")";
        // Temporary items rebase against the current temporary map; assigned
        // entries never match here even when they retain a temporary value.
        code.AppendLine("if (" + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__it.Key") + " && __it.TemporaryKey.HasValue)");
        code.OpenBlock();
        code.AppendLine("var __tt = __it.TemporaryKey.Value;");
        // Added: replay when absent; already-applied when equal; conflict
        // otherwise.
        code.AppendLine("if (__it.IsAdded)");
        code.OpenBlock();
        code.AppendLine(
            "if (__tcur"
                + id
                + ".TryGetValue(__tt, out var __tcurAdded) && "
                + elementCs
                + ".Between("
                + optElementFrag
                + ".Present("
                + elementFrag
                + ".From(__tcurAdded)), "
                + optElementFrag
                + ".Present("
                + elementFrag
                + ".From(__it.After.Value!))).IsEmpty) { }"
        );
        code.AppendLine(
            "else if (__tcur"
                + id
                + ".ContainsKey(__tt)) { __conflicts.Add(new "
                + conflict
                + "("
                + rootPath
                + ".TemporaryKey(__tt), "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Present((object?)__it.After.Value), "
                + runtime
                + "Optional<object?>.Present((object?)__tcur"
                + id
                + "[__tt]), \"The keyed element conflicts with a concurrent change.\")); }"
        );
        code.AppendLine(
            "else { __any"
                + id
                + " = true; "
                + optElement
                + " __tua = "
                + optElement
                + ".Present(__it.After.Value!); "
                + optElementFrag
                + " __tuea = "
                + optElementFrag
                + ".Present("
                + elementFrag
                + ".From(__it.After.Value!)); var __tuedit = "
                + elementCs
                + ".Between(default, __tuea); __rlist"
                + id
                + ".Add(new "
                + trans
                + ".Item(__it.Key, __tt, default, __tua, -1, (__preserveTempAddOrder"
                + id
                + " && __corder"
                + id
                + ".Count > 0 ? __it.AfterIndex : __corder"
                + id
                + ".Count), true, false, false, false, __tuedit, false)); }"
        );
        code.CloseBlock();
        // Removed: already-applied when absent; replay when still base;
        // conflict otherwise.
        code.AppendLine("else if (__it.IsRemoved)");
        code.OpenBlock();
        code.AppendLine("if (!__tcur" + id + ".ContainsKey(__tt)) { }");
        code.AppendLine(
            "else if (!"
                + elementCs
                + ".Between("
                + optElementFrag
                + ".Present("
                + elementFrag
                + ".From(__tcur"
                + id
                + "[__tt])), "
                + optElementFrag
                + ".Present("
                + elementFrag
                + ".From(__it.Before.Value!))).IsEmpty)"
        );
        code.AppendLine(
            "{ __conflicts.Add(new "
                + conflict
                + "("
                + rootPath
                + ".TemporaryKey(__tt), "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Present((object?)__it.Before.Value), "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Present((object?)__tcur"
                + id
                + "[__tt]), \"The keyed element conflicts with a concurrent change.\")); }"
        );
        code.AppendLine(
            "else { __any"
                + id
                + " = true; int __tbi = __tpos"
                + id
                + "[__tt]; __rlist"
                + id
                + ".Add(new "
                + trans
                + ".Item(__it.Key, __tt, __it.Before, default, __tbi, -1, false, true, false, false, __it.Edit, false)); __tcur"
                + id
                + ".Remove(__tt); }"
        );
        code.CloseBlock();
        // Edited: nested rebase against the current temporary element.
        code.AppendLine("else if (__it.IsEdited)");
        code.OpenBlock();
        code.AppendLine("if (!__tcur" + id + ".TryGetValue(__tt, out var __tcev))");
        code.AppendLine(
            "{ __conflicts.Add(new "
                + conflict
                + "("
                + rootPath
                + ".TemporaryKey(__tt), "
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
        code.AppendLine("var __tnr = __it.Edit.RebaseOnto(" + optElementFrag + ".Present(" + elementFrag + ".From(__tcev)));");
        code.AppendLine("foreach (var __cc in __tnr.Conflicts) __conflicts.Add(__cc.WithPathPrefix(" + rootPath + ".TemporaryKey(__tt)));");
        code.AppendLine("if (__tnr.Conflicts.Count == 0 && !__tnr.Rebased.IsEmpty)");
        code.OpenBlock();
        code.AppendLine("var __tapplied = __tnr.Rebased.ToPatch().Apply(" + optElementFrag + ".Present(" + elementFrag + ".From(__tcev)));");
        code.AppendLine("if (__tapplied.IsPresent && __tapplied.Value is not null)");
        code.OpenBlock();
        code.AppendLine("var __tum = __tapplied.Value!.ToModel(); __any" + id + " = true;");
        code.AppendLine(optElement + " __tnb = " + optElement + ".Present(__tcev); " + optElement + " __tna2 = " + optElement + ".Present(__tum);");
        code.AppendLine("int __tbi2 = __tpos" + id + "[__tt];");
        code.AppendLine(
            "__rlist"
                + id
                + ".Add(new "
                + trans
                + ".Item(__it.Key, __tt, __tnb, __tna2, __tbi2, __tbi2, false, false, true, __it.IsReordered, __tnr.Rebased, false)); __tcur"
                + id
                + "[__tt] = __tum;"
        );
        code.CloseBlock();
        code.CloseBlock();
        code.CloseBlock();
        code.CloseBlock();
        // Reorder-only: keep when present.
        code.AppendLine("else if (__it.IsReordered)");
        code.OpenBlock();
        code.AppendLine(
            "if (__tcur"
                + id
                + ".ContainsKey(__tt)) { __any"
                + id
                + " = true; int __tbi3 = __tpos"
                + id
                + "[__tt]; __rlist"
                + id
                + ".Add(new "
                + trans
                + ".Item(__it.Key, __tt, __it.Before, __it.After, __tbi3, __tbi3, false, false, false, true, __it.Edit, false)); }"
        );
        code.CloseBlock();
        code.AppendLine("continue;");
        code.CloseBlock();
    }

    /// <summary>Emits rebased temporary orders from the current temp order.</summary>
    internal static void AppendTempOrderOutput(SharedIndentedBuilder code, SparseMemberModel member, int id)
    {
        code.AppendLine("if (__preserveTempAddOrder" + id + ")");
        code.OpenBlock();
        code.AppendLine("__rtbO" + id + " = new global::System.Collections.Generic.List<global::System.Guid>(__tcorder" + id + ");");
        code.AppendLine(
            "__rtaO"
                + id
                + " = new global::System.Collections.Generic.List<global::System.Guid>("
                + SparseChangeSetBasicsEmitter.KeyedTempAfterOrder(member)
                + ");"
        );
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        // Concurrent changes keep the established replay order: current temporary items first, then additions.
        code.AppendLine("__rtbO" + id + " = new global::System.Collections.Generic.List<global::System.Guid>(__tcorder" + id + ");");
        code.AppendLine("__rtaO" + id + " = new global::System.Collections.Generic.List<global::System.Guid>(__tcorder" + id + ");");
        code.AppendLine("var __rtaOseen" + id + " = new global::System.Collections.Generic.HashSet<global::System.Guid>(__tcorder" + id + ");");
        code.AppendLine(
            "foreach (var __it in __rlist"
                + id
                + ") if (__it.IsAdded && __it.TemporaryKey.HasValue && __rtaOseen"
                + id
                + ".Add(__it.TemporaryKey.Value)) __rtaO"
                + id
                + ".Add(__it.TemporaryKey.Value);"
        );
        code.CloseBlock();
    }
}
