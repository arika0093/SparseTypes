namespace SparseTypes.Generator.Shared;

/// <summary>Temporary-identity three-way merge for keyed-sequence patch rebase.</summary>
/// <remarks>Replays temporary operations against newer state by Guid instead of by permanent key.</remarks>
internal static class SparseKeyedSequenceTempRebaseEmitter
{
    /// <summary>Emits temporary side maps for base/current/desired states.</summary>
    internal static void AppendTempStateMaps(SharedIndentedBuilder code, SparseMemberModel member, string elementType, int id)
    {
        var tempOf = SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member);
        var keyOf = SparseKeyedCollectionEmitter.KeyOfMethod(member);
        var isUnassigned = SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k");
        code.AppendLine("var __baseTemp" + id + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, " + elementType + ">();");
        code.AppendLine(
            "var __currentTemp" + id + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, " + elementType + ">();"
        );
        code.AppendLine(
            "var __desiredTemp" + id + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, " + elementType + ">();"
        );
        code.AppendLine(
            "if (baseState.IsPresent && (object?)baseState.Value is not null) foreach (var item in baseState.Value!) { var k = "
                + keyOf
                + "(item); if ("
                + isUnassigned
                + ") { var __t = "
                + tempOf
                + "(item); if (!__t.HasValue) throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed collection operation.\"); if (__t.Value == global::System.Guid.Empty) throw new global::System.InvalidOperationException(\"An unassigned keyed element has a reserved temporary identity.\"); "
                + SparseKeyedCollectionEmitter.AddUniqueTempEntry("__baseTemp" + id, "__t.Value", "item")
                + " } }"
        );
        code.AppendLine(
            "if (currentState.IsPresent && (object?)currentState.Value is not null) foreach (var item in currentState.Value!) { var k = "
                + keyOf
                + "(item); if ("
                + isUnassigned
                + ") { var __t = "
                + tempOf
                + "(item); if (!__t.HasValue) throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed collection operation.\"); if (__t.Value == global::System.Guid.Empty) throw new global::System.InvalidOperationException(\"An unassigned keyed element has a reserved temporary identity.\"); "
                + SparseKeyedCollectionEmitter.AddUniqueTempEntry("__currentTemp" + id, "__t.Value", "item")
                + " } }"
        );
        code.AppendLine(
            "if (desired.IsPresent && (object?)desired.Value is not null) foreach (var item in desired.Value!) { var k = "
                + keyOf
                + "(item); if ("
                + isUnassigned
                + ") { var __t = "
                + tempOf
                + "(item); if (!__t.HasValue) throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed collection operation.\"); if (__t.Value == global::System.Guid.Empty) throw new global::System.InvalidOperationException(\"An unassigned keyed element has a reserved temporary identity.\"); "
                + SparseKeyedCollectionEmitter.AddUniqueTempEntry("__desiredTemp" + id, "__t.Value", "item")
                + " } }"
        );
    }

    /// <summary>Emits the temporary three-way merge for patch Rebase.</summary>
    internal static void AppendTempThreeWay(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string elementType,
        bool hasPatch,
        string facade,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        int id
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var conflictType = dialect.ConflictType;
        var conflictKindType = dialect.ConflictKindType;
        var tempOf = SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member);
        code.AppendLine(
            "var __localTempAdded" + id + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, " + elementType + ">();"
        );
        code.AppendLine(
            "if (local.__added is not null) foreach (var __touchedItem in local.__added) { var __ttk = "
                + tempOf
                + "(__touchedItem); if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(__touchedItem)")
                + " && __ttk.HasValue && __ttk.Value != global::System.Guid.Empty) __localTempAdded"
                + id
                + "[__ttk.Value] = __touchedItem; }"
        );
        code.AppendLine(
            "var __tempUnion" + id + " = new global::System.Collections.Generic.HashSet<global::System.Guid>(__baseTemp" + id + ".Keys);"
        );
        code.AppendLine("__tempUnion" + id + ".UnionWith(__currentTemp" + id + ".Keys);");
        code.AppendLine("__tempUnion" + id + ".UnionWith(__desiredTemp" + id + ".Keys);");
        code.AppendLine("__tempUnion" + id + ".UnionWith(__localTempAdded" + id + ".Keys);");
        code.AppendLine("if (local.__removedTemp is not null) __tempUnion" + id + ".UnionWith(local.__removedTemp);");
        code.AppendLine("if (local.__editedTemp is not null) __tempUnion" + id + ".UnionWith(local.__editedTemp.Keys);");
        code.AppendLine("foreach (var __tt in __tempUnion" + id + ")");
        code.OpenBlock();
        code.AppendLine(
            "var __tinBase = __baseTemp"
                + id
                + ".TryGetValue(__tt, out var __tb); var __tinCurrent = __currentTemp"
                + id
                + ".TryGetValue(__tt, out var __tc); var __tinDesired = __desiredTemp"
                + id
                + ".TryGetValue(__tt, out var __td);"
        );
        code.AppendLine(
            "bool __tbaseEqualsCurrent = __tinBase == __tinCurrent && (!__tinBase || " + facade + ".AreEqual((object?)__tb, (object?)__tc));"
        );
        code.AppendLine(
            "bool __tdesiredEqualsCurrent = __tinDesired == __tinCurrent && (!__tinDesired || " + facade + ".AreEqual((object?)__td, (object?)__tc));"
        );
        code.AppendLine("if (__tbaseEqualsCurrent)");
        code.OpenBlock();
        code.AppendLine(
            "if (local.__removedTemp is not null && local.__removedTemp.Contains(__tt)) { (result.__removedTemp ??= new global::System.Collections.Generic.List<global::System.Guid>()).Add(__tt); }"
        );
        code.AppendLine(
            "else if (__localTempAdded"
                + id
                + ".TryGetValue(__tt, out var __trebasedAdded)) (result.__added ??= new global::System.Collections.Generic.List<"
                + elementType
                + ">()).Add(__trebasedAdded);"
        );
        if (hasPatch)
        {
            var elementPatch = SparseKeyedCollectionEmitter.ElementPatchType(member);
            code.AppendLine(
                "else if (local.__editedTemp is not null && local.__editedTemp.TryGetValue(__tt, out var __tedit) && !__tedit.__SparseIsEmpty()) { (result.__editedTemp ??= new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                    + elementPatch
                    + ">())[__tt] = __tedit; }"
            );
        }
        else
        {
            code.AppendLine(
                "else if (local.__editedTemp is not null && local.__editedTemp.TryGetValue(__tt, out var __tupd)) { (result.__editedTemp ??= new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                    + elementType
                    + ">())[__tt] = __tupd; }"
            );
        }
        code.CloseBlock();
        code.AppendLine("else if (__tdesiredEqualsCurrent) { }");
        if (hasPatch)
        {
            var elementPatch = SparseKeyedCollectionEmitter.ElementPatchType(member);
            var elementFragment = SparseKeyedCollectionEmitter.ElementFragmentType(member);
            var prefix = SparseKeyedCollectionEmitter.ElementPatchPrefix(member);
            code.AppendLine(
                "else if (__tinBase && __tinCurrent && __tinDesired && local.__editedTemp is not null && local.__editedTemp.TryGetValue(__tt, out var __tlocalEdit))"
            );
            code.OpenBlock();
            code.AppendLine(
                "var __tnested = "
                    + elementPatch
                    + "."
                    + prefix
                    + "Rebase("
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(__tb!)), __tlocalEdit, "
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(__tc!)));"
            );
            code.AppendLine(
                "if (!__tnested.HasConflicts && !__tnested.Rebased.__SparseIsEmpty()) (result.__editedTemp ??= new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                    + elementPatch
                    + ">())[__tt] = __tnested.Rebased;"
            );
            code.AppendLine(
                "else foreach (var __tnc in __tnested.Conflicts) conflicts.Add(__tnc.WithPathPrefix(__SparseRootPath.TemporaryKey(__tt)));"
            );
            code.CloseBlock();
        }
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine(
            "conflicts.Add(new "
                + conflictType
                + "(__SparseRootPath.TemporaryKey(__tt), "
                + conflictKindType
                + ".Nested, "
                + runtime
                + "Optional<object?>.Present((object?)__tb), "
                + runtime
                + "Optional<object?>.Present((object?)__td), "
                + runtime
                + "Optional<object?>.Present((object?)__tc), \"The keyed element conflicts with a concurrent change.\"));"
        );
        code.CloseBlock();
        code.CloseBlock();
    }
}
