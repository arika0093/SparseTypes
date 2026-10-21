namespace SparseTypes.Generator.Shared;

/// <summary>Emits the dictionary patch surface plus Apply and Between algebra.</summary>
internal static class SparseDictionaryPatchEmitter
{
    internal static void EmitDictionaryPatch(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType = null,
        string? implementationNamespace = null,
        SparseOperationTarget? target = null
    )
    {
        var patchName = SparseKeyedCollectionEmitter.CollectionPatchName(member);
        var keyType = SparseKeyedCollectionEmitter.KeyType(member);
        var valueType = SparseKeyedCollectionEmitter.DictionaryValueType(member);
        var dictType = SparseKeyedCollectionEmitter.DictionaryType(member);
        var runtime = dialect.RuntimeNamespace;
        var operation = runtime + "FragmentOperation<" + dictType + ">";
        var kind = runtime + "FragmentOperationKind";
        var optionalDict = runtime + "Optional<" + dictType + ">";
        var facade = dialect.RuntimeFacade;
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var hasPatch = SparseKeyedCollectionEmitter.HasValuePatch(member);
        var editedValueType = hasPatch
            ? SparseKeyedCollectionEmitter.ValuePatchType(member)
            : valueType;

        SparseMemberOperationSplit? split = null;
        SharedIndentedBuilder operations = code;
        if (target is not null)
        {
            var operationsType = SparseOperationTarget.MemberOperationsType(
                target.PatchOperationsType,
                member
            );
            operations = target.OpenMemberOperations(
                SparseNaming.EscapeIdentifier(member.Property.Name) + "PatchOperations",
                SparseNaming.EscapeIdentifier(member.Property.Name) + "PatchOperations"
            );
            split = new SparseMemberOperationSplit(code, operations, operationsType, "self.");
        }
        // Public-first order for relocated patches: internal surface helpers
        // trail public mutators and algebra stubs.
        using var surfaceDeferred = split is null
            ? null
            : new SharedIndentedBuilder(code.CancellationToken);
        EmitDictionarySurface(
            code,
            member,
            patchName,
            keyType,
            valueType,
            dictType,
            runtime,
            operation,
            kind,
            comparer,
            hasPatch,
            editedValueType,
            implementationNamespace,
            split,
            surfaceDeferred
        );
        if (surfaceDeferred is not null)
        {
            SparseDictionaryRemovalIndexEmitter.Emit(
                surfaceDeferred,
                keyType,
                comparer,
                implementationNamespace
            );
        }
        else
        {
            SparseDictionaryRemovalIndexEmitter.Emit(
                code,
                keyType,
                comparer,
                implementationNamespace
            );
        }
        EmitDictionaryApply(
            operations,
            member,
            keyType,
            valueType,
            dictType,
            runtime,
            kind,
            optionalDict,
            comparer,
            hasPatch,
            split
        );
        EmitDictionaryBetween(
            operations,
            member,
            patchName,
            keyType,
            valueType,
            runtime,
            operation,
            optionalDict,
            facade,
            comparer,
            hasPatch,
            split
        );
        SparseDictionaryComposeEmitter.EmitDictionaryCompose(
            operations,
            member,
            patchName,
            keyType,
            valueType,
            dictType,
            runtime,
            operation,
            kind,
            comparer,
            hasPatch,
            split
        );
        if (split is not null)
        {
            code.AppendLine(
                "/// <summary>Inverts this dictionary patch relative to the baseline it was applied to.</summary>"
            );
            code.AppendLine(
                "public "
                    + patchName
                    + " Invert("
                    + optionalDict
                    + " baseline) => "
                    + split.OperationsType
                    + ".Invert(this, baseline);"
            );
            operations.AppendLine(
                "/// <summary>Inverts a dictionary patch relative to the baseline it was applied to.</summary>"
            );
            operations.AppendLine(
                "internal static "
                    + patchName
                    + " Invert("
                    + patchName
                    + " self, "
                    + optionalDict
                    + " baseline) => Between(Apply(self, baseline), baseline);"
            );
        }
        else
        {
            code.AppendLine("/// <summary>Inverts this dictionary patch.</summary>");
            code.AppendLine("/// <param name=\"baseline\">Baseline applied to.</param>");
            code.AppendLine("/// <returns>A patch undoing this patch.</returns>");
            code.AppendLine(
                "public "
                    + patchName
                    + " Invert("
                    + optionalDict
                    + " baseline) => Between(Apply(baseline), baseline);"
            );
        }
        SparseDictionaryRebaseEmitter.EmitDictionaryRebase(
            operations,
            member,
            patchName,
            keyType,
            valueType,
            runtime,
            kind,
            optionalDict,
            facade,
            comparer,
            hasPatch,
            editedValueType,
            dialect,
            split
        );
        if (modelType is not null)
        {
            SparseChangePayloadCollectionExportEmitter.AppendCollectionExport(
                operations,
                member,
                dialect,
                modelType,
                implementationNamespace,
                split
            );
        }

        if (surfaceDeferred is not null)
        {
            code.Append(surfaceDeferred.ToString());
        }
        if (target is not null)
        {
            target.CloseMemberOperations(
                SparseNaming.EscapeIdentifier(member.Property.Name) + "PatchOperations"
            );
        }
        code.CloseBlock();
    }

    internal static void EmitDictionarySurface(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string patchName,
        string keyType,
        string valueType,
        string dictType,
        string runtime,
        string operation,
        string kind,
        string comparer,
        bool hasPatch,
        string editedValueType,
        string? implementationNamespace = null,
        SparseMemberOperationSplit? split = null,
        SharedIndentedBuilder? surfaceDeferred = null
    )
    {
        // Shared removal prefix; null keeps the legacy private kernels.
        var sharedRemoval = string.IsNullOrEmpty(implementationNamespace)
            ? null
            : SparseDictionaryRemovalIndexEmitter.SharedPrefix(keyType, implementationNamespace!);
        // Public-first order: internal backing fields and helpers move to the
        // deferred builder trailing public mutators and algebra stubs.
        var internalCode = surfaceDeferred ?? code;
        code.AppendLine(
            "/// <summary>Dictionary patch for member '" + member.Property.Name + "'.</summary>"
        );
        code.AppendLine("public sealed class " + patchName);
        code.OpenBlock();
        // Relocated operation bodies reach state through these fields.
        internalCode.AppendLine("internal " + operation + " __whole;");
        internalCode.AppendLine(
            "internal global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">? __set;"
        );
        internalCode.AppendLine(
            "internal global::System.Collections.Generic.List<" + keyType + ">? __removed;"
        );
        internalCode.AppendLine("internal int[]? __removedLookup;");
        internalCode.AppendLine(
            hasPatch
                ? "internal global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + editedValueType
                    + ">? __edited;"
                : "internal global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">? __edited;"
        );
        code.AppendLine("/// <summary>Whether this dictionary patch carries no changes.</summary>");
        code.AppendLine("public bool IsEmpty => __whole.Kind == " + kind + ".Keep");
        code.AppendLine(
            "&& (__set is null || __set.Count == 0) && (__removed is null || __removed.Count == 0) && (__edited is null || __edited.Count == 0);"
        );
        internalCode.AppendLine("internal bool __SparseIsEmpty() => IsEmpty;");
        code.AppendLine("/// <summary>Replaces the whole dictionary.</summary>");
        code.AppendLine("/// <param name=\"value\">Replacement values.</param>");
        code.AppendLine("public void Set(" + dictType + " value)");
        code.OpenBlock();
        code.AppendLine(
            "__whole = "
                + operation
                + ".Set(value); __set = null; __removed = null; __removedLookup = null; __edited = null;"
        );
        code.CloseBlock();
        code.AppendLine("/// <summary>Removes the whole dictionary.</summary>");
        code.AppendLine("public void Remove()");
        code.OpenBlock();
        code.AppendLine(
            "__whole = "
                + operation
                + ".Remove; __set = null; __removed = null; __removedLookup = null; __edited = null;"
        );
        code.CloseBlock();
        if (!SparseKeyedCollectionEmitter.IsInterfaceMember(member))
        {
            code.AppendLine("/// <summary>Creates a dictionary patch from values.</summary>");
            code.AppendLine("/// <param name=\"value\">Replacement values.</param>");
            code.AppendLine("/// <returns>A patch carrying the values.</returns>");
            code.AppendLine(
                "public static implicit operator "
                    + patchName
                    + "("
                    + dictType
                    + " value) => new "
                    + patchName
                    + " { __whole = "
                    + operation
                    + ".Set(value) };"
            );
        }
        internalCode.AppendLine("internal void EnsureGranular(string operation)");
        internalCode.OpenBlock();
        internalCode.AppendLine(
            "if (__whole.Kind != "
                + kind
                + ".Keep) throw new global::System.InvalidOperationException(\"Cannot apply '\" + operation + \"' when the whole dictionary is set.\");"
        );
        internalCode.CloseBlock();
        EmitDictionaryMutators(
            split?.Operations ?? code,
            member,
            patchName,
            keyType,
            valueType,
            comparer,
            sharedRemoval,
            hasPatch,
            implementationNamespace,
            split
        );
        // Internal sparse setter for ChangeSet.ToPatch; installs an
        // already-built value patch.
        if (hasPatch)
        {
            internalCode.AppendLine(
                "internal void __SparseSetEdited("
                    + keyType
                    + " key, "
                    + SparseKeyedCollectionEmitter.ValuePatchType(member)
                    + " patch)"
            );
            internalCode.OpenBlock();
            internalCode.AppendLine("EnsureGranular(\"SetEdited\");");
            internalCode.AppendLine(
                "if (patch is null) throw new global::System.ArgumentNullException(nameof(patch));"
            );
            internalCode.AppendLine(
                "__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + SparseKeyedCollectionEmitter.ValuePatchType(member)
                    + ">("
                    + comparer
                    + ");"
            );
            internalCode.AppendLine("__edited[key] = patch;");
            internalCode.CloseBlock();
        }
    }

    /// <summary>Emits the dictionary mutators (SetEntry/RemoveEntry/Edit/UpdateEntry).</summary>
    /// <remarks>Delegates to <c>SparseDictionaryMutatorsEmitter</c>; kept here for call-site stability.</remarks>
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
    ) =>
        SparseDictionaryMutatorsEmitter.EmitDictionaryMutators(
            code,
            member,
            patchName,
            keyType,
            valueType,
            comparer,
            sharedRemoval,
            hasPatch,
            implementationNamespace,
            split
        );

    /// <summary>Aliases nested dictionary-patch fields by reference for relocated bodies.</summary>
    internal static void AppendDictionaryFieldAliases(SharedIndentedBuilder code) =>
        SparseDictionaryMutatorsEmitter.AppendDictionaryFieldAliases(code);

    internal static void EmitDictionaryApply(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string keyType,
        string valueType,
        string dictType,
        string runtime,
        string kind,
        string optionalDict,
        string comparer,
        bool hasPatch,
        SparseMemberOperationSplit? split = null
    )
    {
        var patchName = SparseKeyedCollectionEmitter.CollectionPatchName(member);
        if (split is not null)
        {
            split.Shell.AppendLine(
                "/// <summary>Applies this dictionary patch to a member value.</summary>"
            );
            split.Shell.AppendLine(
                "public "
                    + optionalDict
                    + " Apply("
                    + optionalDict
                    + " current) => "
                    + split.OperationsType
                    + ".Apply(this, current);"
            );
            code.AppendLine("/// <summary>Applies a dictionary patch to a member value.</summary>");
            code.AppendLine(
                "internal static "
                    + optionalDict
                    + " Apply("
                    + patchName
                    + " self, "
                    + optionalDict
                    + " current)"
            );
            code.OpenBlock();
            AppendDictionaryFieldAliases(code);
        }
        else
        {
            // Apply.
            code.AppendLine("/// <summary>Applies this dictionary patch.</summary>");
            code.AppendLine("/// <param name=\"current\">Value to apply to.</param>");
            code.AppendLine("/// <returns>The value with the patch applied.</returns>");
            code.AppendLine("public " + optionalDict + " Apply(" + optionalDict + " current)");
            code.OpenBlock();
        }
        code.AppendLine("if (__whole.Kind != " + kind + ".Keep) return __whole.Apply(current);");
        code.AppendLine(
            "if (" + (split is null ? string.Empty : "self.") + "IsEmpty) return current;"
        );
        code.AppendLine(
            "if (!current.IsPresent || (object?)current.Value is null) throw new global::System.InvalidOperationException(\"Cannot apply granular dictionary operations to a missing dictionary.\");"
        );
        code.AppendLine(
            "var result = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(("
                + "global::System.Collections.Generic.IDictionary<"
                + keyType
                + ", "
                + valueType
                + ">"
                + ")current.Value!, "
                + comparer
                + ");"
        );
        code.AppendLine(
            "if (__removed is not null) foreach (var k in __removed) result.Remove(k);"
        );
        if (hasPatch)
        {
            var valueFragment = SparseKeyedCollectionEmitter.ValueFragmentType(member);
            code.AppendLine("if (__edited is not null) foreach (var kv in __edited)");
            code.OpenBlock();
            code.AppendLine(
                "if (!result.TryGetValue(kv.Key, out var existing)) throw new global::System.InvalidOperationException(\"Cannot edit a missing entry.\");"
            );
            code.AppendLine(
                "var applied = kv.Value.Apply("
                    + runtime
                    + "Optional<"
                    + valueFragment
                    + "?>.Present("
                    + valueFragment
                    + ".From(existing)));"
            );
            code.AppendLine(
                "if (!applied.IsPresent || applied.Value is null) throw new global::System.InvalidOperationException(\"Entry edit removed the entry. Use RemoveEntry instead.\");"
            );
            code.AppendLine("result[kv.Key] = applied.Value!.ToModel();");
            code.CloseBlock();
        }
        else
        {
            code.AppendLine(
                "if (__edited is not null) foreach (var kv in __edited) { if (!result.ContainsKey(kv.Key)) throw new global::System.InvalidOperationException(\"Cannot update a missing entry.\"); result[kv.Key] = kv.Value; }"
            );
        }

        code.AppendLine(
            "if (__set is not null) foreach (var kv in __set) result[kv.Key] = kv.Value;"
        );
        code.AppendLine(
            "return "
                + runtime
                + "Optional<"
                + dictType
                + ">.Present("
                + SparseKeyedCollectionEmitter.ConvertDictionaryToMember(member, "result")
                + ");"
        );
        code.CloseBlock();
    }

    internal static void EmitDictionaryBetween(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string patchName,
        string keyType,
        string valueType,
        string runtime,
        string operation,
        string optionalDict,
        string facade,
        string comparer,
        bool hasPatch,
        SparseMemberOperationSplit? split = null
    )
    {
        if (split is not null)
        {
            split.Shell.AppendLine(
                "/// <summary>Derives a dictionary patch between two member values.</summary>"
            );
            split.Shell.AppendLine(
                "public static "
                    + patchName
                    + " Between("
                    + optionalDict
                    + " before, "
                    + optionalDict
                    + " after) => "
                    + split.OperationsType
                    + ".Between(before, after);"
            );
            code.AppendLine(
                "/// <summary>Derives a dictionary patch between two member values.</summary>"
            );
        }
        else
        {
            code.AppendLine("/// <summary>Derives a dictionary patch between values.</summary>");
            code.AppendLine("/// <param name=\"before\">Value before.</param>");
            code.AppendLine("/// <param name=\"after\">Value after.</param>");
            code.AppendLine("/// <returns>A patch turning before into after.</returns>");
        }
        // Between.
        code.AppendLine(
            (split is null ? "public static " : "internal static ")
                + patchName
                + " Between("
                + optionalDict
                + " before, "
                + optionalDict
                + " after)"
        );
        code.OpenBlock();
        code.AppendLine("var patch = new " + patchName + "();");
        code.AppendLine("if (before.IsPresent != after.IsPresent)");
        code.OpenBlock();
        code.AppendLine(
            "patch.__whole = after.IsPresent ? "
                + operation
                + ".Set(after.Value) : "
                + operation
                + ".Remove;"
        );
        code.AppendLine("return patch;");
        code.CloseBlock();
        code.AppendLine("if (!before.IsPresent) return patch;");
        code.AppendLine(
            "if (global::System.Object.ReferenceEquals(before.Value, after.Value)) return patch;"
        );
        code.AppendLine("if ((object?)before.Value is null || (object?)after.Value is null)");
        code.OpenBlock();
        code.AppendLine(
            "if (!"
                + facade
                + ".AreDictionaryEqual<"
                + keyType
                + ", "
                + valueType
                + ">(before.Value, after.Value)) patch.__whole = "
                + operation
                + ".Set(after.Value);"
        );
        code.AppendLine("return patch;");
        code.CloseBlock();
        code.AppendLine(
            "if ("
                + facade
                + ".AreDictionaryEqual<"
                + keyType
                + ", "
                + valueType
                + ">(before.Value, after.Value)) return patch;"
        );
        code.AppendLine(
            "var beforeDict = before.Value is global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + "> __beforeDirect && "
                + "global::System.Object.Equals(__beforeDirect.Comparer, "
                + comparer
                + ") ? __beforeDirect : new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(("
                + "global::System.Collections.Generic.IDictionary<"
                + keyType
                + ", "
                + valueType
                + ">)before.Value!, "
                + comparer
                + ");"
        );
        code.AppendLine(
            "var afterDict = after.Value is global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + "> __afterDirect && "
                + "global::System.Object.Equals(__afterDirect.Comparer, "
                + comparer
                + ") ? __afterDirect : new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(("
                + "global::System.Collections.Generic.IDictionary<"
                + keyType
                + ", "
                + valueType
                + ">)after.Value!, "
                + comparer
                + ");"
        );
        code.AppendLine(
            "foreach (var k in beforeDict.Keys) if (!afterDict.ContainsKey(k)) (patch.__removed ??= new global::System.Collections.Generic.List<"
                + keyType
                + ">()).Add(k);"
        );
        if (hasPatch)
        {
            var valueFragment = SparseKeyedCollectionEmitter.ValueFragmentType(member);
            var valuePatch = SparseKeyedCollectionEmitter.ValuePatchType(member);
            var prefix = SparseKeyedCollectionEmitter.ValuePatchPrefix(member);
            code.AppendLine("foreach (var kv in afterDict)");
            code.OpenBlock();
            code.AppendLine(
                "if (!beforeDict.TryGetValue(kv.Key, out var b)) { (patch.__set ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + "))[kv.Key] = kv.Value; continue; }"
            );
            code.AppendLine(
                "var nested = "
                    + valuePatch
                    + "."
                    + prefix
                    + "Between("
                    + runtime
                    + "Optional<"
                    + valueFragment
                    + "?>.Present("
                    + valueFragment
                    + ".From(b!)), "
                    + runtime
                    + "Optional<"
                    + valueFragment
                    + "?>.Present("
                    + valueFragment
                    + ".From(kv.Value)));"
            );
            code.AppendLine(
                "if (!nested.__SparseIsEmpty()) (patch.__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valuePatch
                    + ">("
                    + comparer
                    + "))[kv.Key] = nested;"
            );
            code.CloseBlock();
        }
        else
        {
            code.AppendLine("foreach (var kv in afterDict)");
            code.OpenBlock();
            code.AppendLine(
                "if (!beforeDict.TryGetValue(kv.Key, out var b)) { (patch.__set ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + "))[kv.Key] = kv.Value; continue; }"
            );
            code.AppendLine(
                "if (!"
                    + facade
                    + ".AreEqual<"
                    + valueType
                    + ">(b, kv.Value)) (patch.__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + "))[kv.Key] = kv.Value;"
            );
            code.CloseBlock();
        }

        code.AppendLine("return patch;");
        code.CloseBlock();
    }
}
