using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Projects and reuses sparse dictionary views.</summary>
internal static class SparseChangeSetDictProjectionEmitter
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
        // Sparse projection from canonical storage.
        if (target is null)
        {
            code.AppendLine("private " + trans + " __SparseProject_" + member.Id + "()");
        }
        else
        {
            code.AppendLine(
                "/// <summary>Projects the typed dictionary transition from canonical sparse storage.</summary>"
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
        AppendEmptyReturn(code, member, trans, comparer);
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
        // Granular: derive Added/Removed/Edited maps from stored items (no full
        // snapshots).
        AppendGranularReturn(code, member, trans, comparer);
        code.CloseBlock();
        var projectCall = target is null
            ? "__SparseProject_" + member.Id + "()"
            : target.ChangeSetOperationsType + ".__SparseProject_" + member.Id + "(this)";
        shell.AppendLine(
            "/// <summary>Gets the typed dictionary transition for member '"
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

    private static void AppendGranularReturn(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string trans,
        string comparer
    )
    {
        var keyType = KeyTypeOf(member);
        var valueType = ValueTypeOf(member);
        var hasPatch = member.Collection.ValueType?.IsFragmentModel == true;
        var editedType = hasPatch ? ValueChangeSetOf(member) : valueType;
        code.AppendLine(
            "var __stored = "
                + KeyedItems(member)
                + " ?? new global::System.Collections.Generic.List<"
                + trans
                + ".Item>();"
        );
        code.AppendLine("int __addedCount = 0, __removedCount = 0, __editedCount = 0;");
        code.AppendLine(
            "foreach (var __it in __stored) { if (__it.IsAdded) __addedCount++; else if (__it.IsRemoved) __removedCount++; else if (__it.IsEdited) __editedCount++; }"
        );
        code.AppendLine(
            "var __added = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(__addedCount, "
                + comparer
                + "); var __removed = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(__removedCount, "
                + comparer
                + "); var __edited = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + editedType
                + ">(__editedCount, "
                + comparer
                + ");"
        );
        if (hasPatch)
        {
            code.AppendLine(
                "foreach (var __it in __stored) { if (__it.IsAdded) __added[__it.Key] = __it.After.Value!; else if (__it.IsRemoved) __removed[__it.Key] = __it.Before.Value!; else if (__it.IsEdited) __edited[__it.Key] = __it.Edit; }"
            );
        }
        else
        {
            code.AppendLine(
                "foreach (var __it in __stored) { if (__it.IsAdded) __added[__it.Key] = __it.After.Value!; else if (__it.IsRemoved) __removed[__it.Key] = __it.Before.Value!; else if (__it.IsEdited) __edited[__it.Key] = __it.After.Value!; }"
            );
        }

        code.AppendLine(
            "return new "
                + trans
                + "(default, default, __added, __removed, __edited, __stored, false);"
        );
    }

    private static void AppendEmptyReturn(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string trans,
        string comparer
    )
    {
        var emptyEdited =
            (member.Collection.ValueType?.IsFragmentModel == true)
                ? ValueChangeSetOf(member)
                : ValueTypeOf(member);
        code.AppendLine(
            "return new "
                + trans
                + "(default, default, new global::System.Collections.Generic.Dictionary<"
                + KeyTypeOf(member)
                + ", "
                + ValueTypeOf(member)
                + ">("
                + comparer
                + "), new global::System.Collections.Generic.Dictionary<"
                + KeyTypeOf(member)
                + ", "
                + ValueTypeOf(member)
                + ">("
                + comparer
                + "), new global::System.Collections.Generic.Dictionary<"
                + KeyTypeOf(member)
                + ", "
                + emptyEdited
                + ">("
                + comparer
                + "), new global::System.Collections.Generic.List<"
                + trans
                + ".Item>(), true);"
        );
    }
}
