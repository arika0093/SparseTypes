namespace SparseTypes.Generator.Shared;

/// <summary>Temporary-identity surface for keyed-sequence patches.</summary>
/// <remarks>Temporary removals and edits need Guid-keyed operation logs because unassigned permanent keys collide.</remarks>
internal static class SparseKeyedSequenceTempSurfaceEmitter
{
    /// <summary>Emits temporary operation-log fields for a keyed patch.</summary>
    internal static void AppendTempFields(SharedIndentedBuilder code, string editedValueType)
    {
        code.AppendLine("internal global::System.Collections.Generic.List<global::System.Guid>? __removedTemp;");
        code.AppendLine("internal global::System.Collections.Generic.Dictionary<global::System.Guid, " + editedValueType + ">? __editedTemp;");
        code.AppendLine("internal global::System.Collections.Generic.List<global::System.Guid?>? __orderTemp;");
    }

    /// <summary>Aliases temporary fields for relocated operation bodies.</summary>
    internal static void AppendTempFieldAliases(SharedIndentedBuilder code)
    {
        code.AppendLine("ref var __removedTemp = ref self.__removedTemp;");
        code.AppendLine("ref var __editedTemp = ref self.__editedTemp;");
        code.AppendLine("ref var __orderTemp = ref self.__orderTemp;");
    }

    /// <summary>Emits the emptiness contribution of temporary operations.</summary>
    internal static string TempIsEmptyFragment(string guard) =>
        " && ("
        + guard
        + "__removedTemp is null || "
        + guard
        + "__removedTemp.Count == 0)"
        + " && ("
        + guard
        + "__editedTemp is null || "
        + guard
        + "__editedTemp.Count == 0)"
        + " && ("
        + guard
        + "__orderTemp is null || "
        + guard
        + "__orderTemp.Count == 0)";

    /// <summary>Emits temporary mutators for a keyed patch.</summary>
    internal static void AppendTempMutators(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string patchName,
        string elementType,
        bool hasPatch,
        SparseMemberOperationSplit? split
    )
    {
        var relocated = split is not null;
        var operations = relocated ? split!.Operations : code;
        var guard = relocated ? "self." : string.Empty;
        var tempOf = SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member);
        AppendRemoveByTemporaryKey(operations, code, split, patchName, guard, relocated, tempOf);
        if (hasPatch)
        {
            AppendEditByTemporaryKey(
                operations,
                code,
                split,
                patchName,
                SparseKeyedCollectionEmitter.ElementPatchType(member),
                guard,
                relocated,
                tempOf
            );
        }
        else
        {
            AppendUpdateByTemporaryKey(operations, code, split, patchName, elementType, guard, relocated, tempOf);
        }
    }

    private static void AppendRemoveByTemporaryKey(
        SharedIndentedBuilder operations,
        SharedIndentedBuilder code,
        SparseMemberOperationSplit? split,
        string patchName,
        string guard,
        bool relocated,
        string tempOf
    )
    {
        if (relocated)
        {
            split!.Shell.AppendLine("/// <summary>Removes an unassigned element from this keyed patch by temporary identity.</summary>");
            split.Shell.AppendLine(
                "public void RemoveByTemporaryKey(global::System.Guid temporaryKey) => "
                    + split.OperationsType
                    + ".RemoveByTemporaryKey(this, temporaryKey);"
            );
            operations.AppendLine("/// <summary>Removes an unassigned element from a keyed patch by temporary identity.</summary>");
            operations.AppendLine("internal static void RemoveByTemporaryKey(" + patchName + " self, global::System.Guid temporaryKey)");
            operations.OpenBlock();
            SparseKeyedSequenceSurfaceEmitter.AppendFieldAliases(operations);
            AppendTempFieldAliases(operations);
        }
        else
        {
            code.AppendLine("/// <summary>Removes an unassigned element by temporary identity.</summary>");
            code.AppendLine("/// <param name=\"temporaryKey\">The temporary identity.</param>");
            code.AppendLine("public void RemoveByTemporaryKey(global::System.Guid temporaryKey)");
            code.OpenBlock();
        }
        var target = relocated ? operations : code;
        target.AppendLine(guard + "EnsureGranular(\"RemoveByTemporaryKey\");");
        target.AppendLine(
            "if (temporaryKey == global::System.Guid.Empty) throw new global::System.ArgumentException(\"Temporary identity must not be empty.\", nameof(temporaryKey));"
        );
        target.AppendLine(
            "if (__added is not null) { for (var i = __added.Count - 1; i >= 0; i--) if ("
                + tempOf
                + "(__added[i]) == temporaryKey) { __added.RemoveAt(i); return; } }"
        );
        target.AppendLine("if (__editedTemp is not null) __editedTemp.Remove(temporaryKey);");
        target.AppendLine(
            "if (__removedTemp is not null && __removedTemp.Contains(temporaryKey)) throw new global::System.InvalidOperationException(\"Element is already removed in this patch.\");"
        );
        target.AppendLine("__removedTemp ??= new global::System.Collections.Generic.List<global::System.Guid>();");
        target.AppendLine("__removedTemp.Add(temporaryKey);");
        target.CloseBlock();
    }

    private static void AppendEditByTemporaryKey(
        SharedIndentedBuilder operations,
        SharedIndentedBuilder code,
        SparseMemberOperationSplit? split,
        string patchName,
        string elementPatch,
        string guard,
        bool relocated,
        string tempOf
    )
    {
        if (relocated)
        {
            split!.Shell.AppendLine("/// <summary>Gets the edit patch for an unassigned element of this keyed patch.</summary>");
            split.Shell.AppendLine(
                "public "
                    + elementPatch
                    + " EditByTemporaryKey(global::System.Guid temporaryKey) => "
                    + split.OperationsType
                    + ".EditByTemporaryKey(this, temporaryKey);"
            );
            operations.AppendLine("/// <summary>Gets the edit patch for an unassigned element of a keyed patch.</summary>");
            operations.AppendLine(
                "internal static " + elementPatch + " EditByTemporaryKey(" + patchName + " self, global::System.Guid temporaryKey)"
            );
            operations.OpenBlock();
            SparseKeyedSequenceSurfaceEmitter.AppendFieldAliases(operations);
            AppendTempFieldAliases(operations);
        }
        else
        {
            code.AppendLine("/// <summary>Gets the edit patch for an unassigned element.</summary>");
            code.AppendLine("/// <param name=\"temporaryKey\">The temporary identity.</param>");
            code.AppendLine("/// <returns>The edit patch.</returns>");
            code.AppendLine("public " + elementPatch + " EditByTemporaryKey(global::System.Guid temporaryKey)");
            code.OpenBlock();
        }
        var target = relocated ? operations : code;
        target.AppendLine(guard + "EnsureGranular(\"EditByTemporaryKey\");");
        target.AppendLine(
            "if (temporaryKey == global::System.Guid.Empty) throw new global::System.ArgumentException(\"Temporary identity must not be empty.\", nameof(temporaryKey));"
        );
        target.AppendLine(
            "if (__removedTemp is not null && __removedTemp.Contains(temporaryKey)) throw new global::System.InvalidOperationException(\"Cannot edit a removed element. Add it again instead.\");"
        );
        target.AppendLine(
            "if (__added is not null) foreach (var a in __added) if ("
                + tempOf
                + "(a) == temporaryKey) throw new global::System.InvalidOperationException(\"Cannot edit an element added in the same patch. Update the added value directly.\");"
        );
        target.AppendLine("__editedTemp ??= new global::System.Collections.Generic.Dictionary<global::System.Guid, " + elementPatch + ">();");
        target.AppendLine(
            "if (!__editedTemp.TryGetValue(temporaryKey, out var patch)) { patch = new " + elementPatch + "(); __editedTemp[temporaryKey] = patch; }"
        );
        target.AppendLine("return patch;");
        target.CloseBlock();
    }

    private static void AppendUpdateByTemporaryKey(
        SharedIndentedBuilder operations,
        SharedIndentedBuilder code,
        SparseMemberOperationSplit? split,
        string patchName,
        string elementType,
        string guard,
        bool relocated,
        string tempOf
    )
    {
        if (relocated)
        {
            split!.Shell.AppendLine("/// <summary>Updates an unassigned element of this keyed patch in place.</summary>");
            split.Shell.AppendLine(
                "public void UpdateByTemporaryKey(" + elementType + " element) => " + split.OperationsType + ".UpdateByTemporaryKey(this, element);"
            );
            operations.AppendLine("/// <summary>Updates an unassigned element of a keyed patch in place.</summary>");
            operations.AppendLine("internal static void UpdateByTemporaryKey(" + patchName + " self, " + elementType + " element)");
            operations.OpenBlock();
            SparseKeyedSequenceSurfaceEmitter.AppendFieldAliases(operations);
            AppendTempFieldAliases(operations);
        }
        else
        {
            code.AppendLine("/// <summary>Updates an unassigned element in place.</summary>");
            code.AppendLine("public void UpdateByTemporaryKey(" + elementType + " element)");
            code.OpenBlock();
        }
        var target = relocated ? operations : code;
        target.AppendLine("if ((object?)element is null) throw new global::System.ArgumentNullException(nameof(element));");
        target.AppendLine(guard + "EnsureGranular(\"UpdateByTemporaryKey\");");
        target.AppendLine("var __temp = " + tempOf + "(element);");
        target.AppendLine(
            "if (!__temp.HasValue || __temp.Value == global::System.Guid.Empty) throw new global::System.InvalidOperationException(\"UpdateByTemporaryKey requires an unassigned element with a temporary identity.\");"
        );
        target.AppendLine(
            "if (__removedTemp is not null && __removedTemp.Contains(__temp.Value)) throw new global::System.InvalidOperationException(\"Cannot update a removed element. Add it again instead.\");"
        );
        target.AppendLine(
            "if (__added is not null) for (var i = 0; i < __added.Count; i++) if ("
                + tempOf
                + "(__added[i]) == __temp.Value) { __added[i] = element; return; }"
        );
        target.AppendLine("__editedTemp ??= new global::System.Collections.Generic.Dictionary<global::System.Guid, " + elementType + ">();");
        target.AppendLine("__editedTemp[__temp.Value] = element;");
        target.CloseBlock();
    }
}
