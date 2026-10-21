namespace SparseTypes.Generator.Shared;

/// <summary>Emits dictionary patch mutators.</summary>
internal static class SparseDictionaryMutatorsEmitter
{
    /// <summary>Emits the dictionary mutators (SetEntry/RemoveEntry/Edit/UpdateEntry).</summary>
    /// <remarks>With a null split the mutators stay instance members of the nested patch (legacy single-file emission).</remarks>
    internal static void EmitDictionaryMutators(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string patchName,
        string keyType,
        string valueType,
        string comparer,
        string? sharedRemoval,
        bool hasPatch,
        string? implementationNamespace,
        SparseMemberOperationSplit? split
    )
    {
        var relocated = split is not null;
        var guard = relocated ? "self." : string.Empty;
        if (relocated)
        {
            split!.Shell.AppendLine("/// <summary>Sets an entry of this dictionary patch.</summary>");
            split.Shell.AppendLine(
                "public void SetEntry(" + keyType + " key, " + valueType + " value) => " + split.OperationsType + ".SetEntry(this, key, value);"
            );
            code.AppendLine("/// <summary>Sets an entry of a dictionary patch.</summary>");
            code.AppendLine("internal static void SetEntry(" + patchName + " self, " + keyType + " key, " + valueType + " value)");
            code.OpenBlock();
            AppendDictionaryFieldAliases(code);
        }
        else
        {
            code.AppendLine("/// <summary>Sets an entry of this dictionary patch.</summary>");
            code.AppendLine("public void SetEntry(" + keyType + " key, " + valueType + " value)");
            code.OpenBlock();
        }
        code.AppendLine(guard + "EnsureGranular(\"SetEntry\");");
        code.AppendLine(
            sharedRemoval is null
                ? "if (__removed is not null) __SparseCancelRemoval(key);"
                : "if (__removed is not null) " + sharedRemoval + ".CancelRemoval(__removed!, ref __removedLookup, key);"
        );
        code.AppendLine("if (__edited is not null) __edited.Remove(key);");
        code.AppendLine("__set ??= new global::System.Collections.Generic.Dictionary<" + keyType + ", " + valueType + ">(" + comparer + ");");
        code.AppendLine("__set[key] = value;");
        code.CloseBlock();
        if (relocated)
        {
            split!.Shell.AppendLine("/// <summary>Removes an entry of this dictionary patch by key.</summary>");
            split.Shell.AppendLine("public void RemoveEntry(" + keyType + " key) => " + split.OperationsType + ".RemoveEntry(this, key);");
            code.AppendLine("/// <summary>Removes an entry of a dictionary patch by key.</summary>");
            code.AppendLine("internal static void RemoveEntry(" + patchName + " self, " + keyType + " key)");
            code.OpenBlock();
            AppendDictionaryFieldAliases(code);
        }
        else
        {
            code.AppendLine("/// <summary>Removes an entry by key.</summary>");
            code.AppendLine("public void RemoveEntry(" + keyType + " key)");
            code.OpenBlock();
        }
        code.AppendLine(guard + "EnsureGranular(\"RemoveEntry\");");
        code.AppendLine("if (__set is not null) { if (__set.Remove(key)) return; }");
        code.AppendLine("if (__edited is not null) __edited.Remove(key);");
        code.AppendLine("__removed ??= new global::System.Collections.Generic.List<" + keyType + ">();");
        SparseDictionaryRemovalIndexEmitter.EmitAdd(code, comparer, keepReservedIndex: true, keyType, implementationNamespace);
        code.CloseBlock();
        if (hasPatch)
        {
            var valuePatch = SparseKeyedCollectionEmitter.ValuePatchType(member);
            if (relocated)
            {
                split!.Shell.AppendLine("/// <summary>Gets the edit patch for an entry of this dictionary patch.</summary>");
                split.Shell.AppendLine("public " + valuePatch + " Edit(" + keyType + " key) => " + split.OperationsType + ".Edit(this, key);");
                code.AppendLine("/// <summary>Gets the edit patch for an entry of a dictionary patch.</summary>");
                code.AppendLine("internal static " + valuePatch + " Edit(" + patchName + " self, " + keyType + " key)");
                code.OpenBlock();
                AppendDictionaryFieldAliases(code);
            }
            else
            {
                code.AppendLine("/// <summary>Gets the edit patch for an entry.</summary>");
                code.AppendLine("public " + valuePatch + " Edit(" + keyType + " key)");
                code.OpenBlock();
            }
            code.AppendLine(guard + "EnsureGranular(\"Edit\");");
            code.AppendLine(
                sharedRemoval is null
                    ? "if (__removed is not null && __SparseContainsRemoved(key)) throw new global::System.InvalidOperationException(\"Cannot edit a removed entry.\");"
                    : "if (__removed is not null && "
                        + sharedRemoval
                        + ".ContainsRemoved(__removed!, __removedLookup, key)) throw new global::System.InvalidOperationException(\"Cannot edit a removed entry.\");"
            );
            code.AppendLine(
                "if (__set is not null && __set.ContainsKey(key)) throw new global::System.InvalidOperationException(\"Cannot edit an entry set in the same patch. Update the set value directly.\");"
            );
            code.AppendLine("__edited ??= new global::System.Collections.Generic.Dictionary<" + keyType + ", " + valuePatch + ">(" + comparer + ");");
            code.AppendLine("if (!__edited.TryGetValue(key, out var patch)) { patch = new " + valuePatch + "(); __edited[key] = patch; }");
            code.AppendLine("return patch;");
            code.CloseBlock();
        }
        else
        {
            if (relocated)
            {
                split!.Shell.AppendLine("/// <summary>Updates an entry of this dictionary patch.</summary>");
                split.Shell.AppendLine(
                    "public void UpdateEntry("
                        + keyType
                        + " key, "
                        + valueType
                        + " value) => "
                        + split.OperationsType
                        + ".UpdateEntry(this, key, value);"
                );
                code.AppendLine("/// <summary>Updates an entry of a dictionary patch.</summary>");
                code.AppendLine("internal static void UpdateEntry(" + patchName + " self, " + keyType + " key, " + valueType + " value)");
                code.OpenBlock();
                AppendDictionaryFieldAliases(code);
            }
            else
            {
                code.AppendLine("/// <summary>Updates an entry of this dictionary patch.</summary>");
                code.AppendLine("public void UpdateEntry(" + keyType + " key, " + valueType + " value)");
                code.OpenBlock();
            }
            code.AppendLine(guard + "EnsureGranular(\"UpdateEntry\");");
            code.AppendLine(
                sharedRemoval is null
                    ? "if (__removed is not null && __SparseContainsRemoved(key)) throw new global::System.InvalidOperationException(\"Cannot update a removed entry. Set it again instead.\");"
                    : "if (__removed is not null && "
                        + sharedRemoval
                        + ".ContainsRemoved(__removed!, __removedLookup, key)) throw new global::System.InvalidOperationException(\"Cannot update a removed entry. Set it again instead.\");"
            );
            code.AppendLine("if (__set is not null && __set.ContainsKey(key)) { __set[key] = value; return; }");
            code.AppendLine("__edited ??= new global::System.Collections.Generic.Dictionary<" + keyType + ", " + valueType + ">(" + comparer + ");");
            code.AppendLine("__edited[key] = value;");
            code.CloseBlock();
        }
    }

    /// <summary>Aliases nested dictionary-patch fields by reference for relocated bodies.</summary>
    internal static void AppendDictionaryFieldAliases(SharedIndentedBuilder code)
    {
        code.AppendLine("ref var __whole = ref self.__whole;");
        code.AppendLine("ref var __set = ref self.__set;");
        code.AppendLine("ref var __removed = ref self.__removed;");
        code.AppendLine("ref var __removedLookup = ref self.__removedLookup;");
        code.AppendLine("ref var __edited = ref self.__edited;");
    }
}
