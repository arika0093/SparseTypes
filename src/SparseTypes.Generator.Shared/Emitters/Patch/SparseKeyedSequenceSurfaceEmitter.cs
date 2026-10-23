namespace SparseTypes.Generator.Shared;

/// <summary>Emits the keyed-sequence patch surface: declaration plus Add/Remove/Edit/Update/SetOrder mutators.</summary>
internal static class SparseKeyedSequenceSurfaceEmitter
{
    internal static void EmitKeyedSequencePatch(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        bool emitChangePayload = true,
        string? modelType = null,
        string? implementationNamespace = null,
        SparseOperationTarget? target = null
    )
    {
        var patchName = SparseKeyedCollectionEmitter.CollectionPatchName(member);
        var elementType = SparseKeyedCollectionEmitter.ElementType(member);
        var keyType = SparseKeyedCollectionEmitter.KeyType(member);
        var listType = SparseKeyedCollectionEmitter.MemberListType(member);
        var runtime = dialect.RuntimeNamespace;
        var operation = runtime + "FragmentOperation<" + listType + ">";
        var kind = runtime + "FragmentOperationKind";
        var optionalList = runtime + "Optional<" + listType + ">";
        var facade = dialect.RuntimeFacade;
        var hasPatch = SparseKeyedCollectionEmitter.HasElementPatch(member);
        var editedValueType = hasPatch ? SparseKeyedCollectionEmitter.ElementPatchType(member) : elementType;
        var comparer = "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        // Shared removal prefix; null keeps the legacy private kernels.
        var sharedRemoval = string.IsNullOrEmpty(implementationNamespace)
            ? null
            : SparseDictionaryRemovalIndexEmitter.SharedPrefix(keyType, implementationNamespace!);

        code.AppendLine("/// <summary>Keyed collection patch for member '" + member.Property.Name + "'.</summary>");
        code.AppendLine("public sealed class " + patchName);
        code.OpenBlock();
        // Public-first order for relocated patches: internal backing fields
        // and helpers trail public mutators and algebra stubs.
        using var surfaceDeferred = target is null ? null : new SharedIndentedBuilder(code.CancellationToken);
        var internalCode = surfaceDeferred ?? code;
        // Relocated operation bodies reach state through these fields, so they
        // are internal rather than private.
        internalCode.AppendLine("internal " + operation + " __whole;");
        internalCode.AppendLine("internal global::System.Collections.Generic.List<" + elementType + ">? __added;");
        internalCode.AppendLine("internal global::System.Collections.Generic.List<" + keyType + ">? __removed;");
        internalCode.AppendLine("internal global::System.Collections.Generic.Dictionary<" + keyType + ", " + editedValueType + ">? __edited;");
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            SparseKeyedSequenceTempSurfaceEmitter.AppendTempFields(internalCode, editedValueType);
        }
        internalCode.AppendLine("internal global::System.Collections.Generic.List<" + keyType + ">? __order;");
        SparseMemberOperationSplit? split = null;
        SharedIndentedBuilder operations = code;
        using var deferredKeyOf = target is null ? null : new SharedIndentedBuilder(code.CancellationToken);
        if (target is not null)
        {
            var operationsType = SparseOperationTarget.MemberOperationsType(target.PatchOperationsType, member);
            operations = target.OpenMemberOperations(
                SparseNaming.EscapeIdentifier(member.Property.Name) + "PatchOperations",
                SparseNaming.EscapeIdentifier(member.Property.Name) + "PatchOperations"
            );
            split = new SparseMemberOperationSplit(code, operations, operationsType, "self.");
            // Private key helper trails the internal operation bodies so the
            // member operation class stays public/internal-first ordered.
            SparseKeyedCollectionEmitter.EmitKeyOf(deferredKeyOf!, member, elementType);
        }
        else
        {
            SparseKeyedCollectionEmitter.EmitKeyOf(code, member, elementType);
        }
        code.AppendLine("/// <summary>Whether this keyed patch carries no changes.</summary>");
        code.AppendLine("public bool IsEmpty => __whole.Kind == " + kind + ".Keep");
        code.AppendLine("&& (__added is null || __added.Count == 0) && (__removed is null || __removed.Count == 0)");
        code.AppendLine(
            "&& (__edited is null || __edited.Count == 0) && (__order is null || __order.Count == 0)"
                + (
                    SparseKeyedCollectionEmitter.HasTemporaryKey(member)
                        ? SparseKeyedSequenceTempSurfaceEmitter.TempIsEmptyFragment(string.Empty)
                        : ""
                )
                + ";"
        );
        internalCode.AppendLine("internal bool __SparseIsEmpty() => IsEmpty;");
        // Whole operations (public group).
        code.AppendLine("/// <summary>Replaces the whole collection.</summary>");
        code.AppendLine("/// <param name=\"value\">Replacement values.</param>");
        code.AppendLine("public void Set(" + listType + " value)");
        code.OpenBlock();
        code.AppendLine("__whole = " + operation + ".Set(value);");
        code.AppendLine("__added = null; __removed = null; __edited = null; __order = null;");
        code.CloseBlock();
        code.AppendLine("/// <summary>Removes the whole collection.</summary>");
        code.AppendLine("public void Remove()");
        code.OpenBlock();
        code.AppendLine("__whole = " + operation + ".Remove;");
        code.AppendLine("__added = null; __removed = null; __edited = null; __order = null;");
        code.CloseBlock();
        if (!SparseKeyedCollectionEmitter.IsInterfaceMember(member))
        {
            code.AppendLine("/// <summary>Creates a keyed patch from values.</summary>");
            code.AppendLine("/// <param name=\"value\">Replacement values.</param>");
            code.AppendLine("/// <returns>A patch carrying the values.</returns>");
            code.AppendLine(
                "public static implicit operator "
                    + patchName
                    + "("
                    + listType
                    + " value) => new "
                    + patchName
                    + " { __whole = "
                    + operation
                    + ".Set(value) };"
            );
        }
        if (surfaceDeferred is not null)
        {
            SparseKeyedRemovalIndexEmitter.Emit(surfaceDeferred, keyType, comparer, implementationNamespace);
        }
        else
        {
            SparseKeyedRemovalIndexEmitter.Emit(code, keyType, comparer, implementationNamespace);
        }
        // Relocated mutator bodies reuse this guard through the facade
        // instance.
        internalCode.AppendLine("internal void EnsureGranular(string operation)");
        internalCode.OpenBlock();
        internalCode.AppendLine(
            "if (__whole.Kind != "
                + kind
                + ".Keep) throw new global::System.InvalidOperationException(\"Cannot apply '\" + operation + \"' when the whole collection is set. Clear the whole operation first.\");"
        );
        internalCode.CloseBlock();
        EmitKeyedMutators(
            operations,
            internalCode,
            member,
            patchName,
            elementType,
            keyType,
            editedValueType,
            comparer,
            facade,
            sharedRemoval,
            hasPatch,
            implementationNamespace,
            split
        );
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            SparseKeyedSequenceTempSurfaceEmitter.AppendTempMutators(code, member, patchName, elementType, hasPatch, split);
        }
        if (hasPatch)
        {
            internalCode.AppendLine(
                "internal void __SparseSetEdited(" + keyType + " key, " + SparseKeyedCollectionEmitter.ElementPatchType(member) + " patch)"
            );
            internalCode.OpenBlock();
            internalCode.AppendLine("EnsureGranular(\"SetEdited\");");
            internalCode.AppendLine("if (patch is null) throw new global::System.ArgumentNullException(nameof(patch));");
            internalCode.AppendLine(
                "__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + SparseKeyedCollectionEmitter.ElementPatchType(member)
                    + ">("
                    + comparer
                    + ");"
            );
            internalCode.AppendLine("__edited[key] = patch;");
            internalCode.CloseBlock();
            if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
            {
                internalCode.AppendLine(
                    "internal void __SparseSetEditedByTemporaryKey(global::System.Guid temporaryKey, "
                        + SparseKeyedCollectionEmitter.ElementPatchType(member)
                        + " patch)"
                );
                internalCode.OpenBlock();
                internalCode.AppendLine("EnsureGranular(\"SetEditedByTemporaryKey\");");
                internalCode.AppendLine("if (patch is null) throw new global::System.ArgumentNullException(nameof(patch));");
                internalCode.AppendLine(
                    "if (temporaryKey == global::System.Guid.Empty) throw new global::System.ArgumentException(\"Temporary identity must not be empty.\", nameof(temporaryKey));"
                );
                internalCode.AppendLine(
                    "__editedTemp ??= new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                        + SparseKeyedCollectionEmitter.ElementPatchType(member)
                        + ">();"
                );
                internalCode.AppendLine("__editedTemp[temporaryKey] = patch;");
                internalCode.CloseBlock();
            }
        }
        SparseKeyedSequenceApplyEmitter.EmitKeyedApply(operations, member, elementType, keyType, listType, hasPatch, comparer, runtime, split);
        SparseKeyedSequenceApplyEmitter.EmitKeyedBetween(
            operations,
            member,
            patchName,
            elementType,
            keyType,
            listType,
            hasPatch,
            comparer,
            facade,
            runtime,
            split
        );
        SparseKeyedSequenceComposeEmitter.EmitKeyedCompose(
            operations,
            member,
            patchName,
            elementType,
            keyType,
            listType,
            hasPatch,
            comparer,
            runtime,
            split
        );
        if (split is not null)
        {
            code.AppendLine("/// <summary>Inverts this collection patch relative to the baseline it was applied to.</summary>");
            code.AppendLine("/// <param name=\"baseline\">Baseline applied to.</param>");
            code.AppendLine("/// <returns>A patch undoing this patch.</returns>");
            code.AppendLine("public " + patchName + " Invert(" + optionalList + " baseline) => " + split.OperationsType + ".Invert(this, baseline);");
            operations.AppendLine("/// <summary>Inverts a collection patch relative to the baseline it was applied to.</summary>");
            operations.AppendLine(
                "internal static "
                    + patchName
                    + " Invert("
                    + patchName
                    + " self, "
                    + optionalList
                    + " baseline) => Between(Apply(self, baseline), baseline);"
            );
        }
        else
        {
            code.AppendLine("/// <summary>Inverts this patch relative to its baseline.</summary>");
            code.AppendLine("/// <param name=\"baseline\">Baseline applied to.</param>");
            code.AppendLine("/// <returns>A patch undoing this patch.</returns>");
            code.AppendLine("public " + patchName + " Invert(" + optionalList + " baseline) => Between(Apply(baseline), baseline);");
        }
        if (dialect.EmitPatchRebase)
        {
            SparseKeyedSequenceRebaseEmitter.EmitKeyedRebase(
                operations,
                member,
                patchName,
                elementType,
                keyType,
                listType,
                hasPatch,
                comparer,
                facade,
                dialect,
                split
            );
        }
        if (emitChangePayload && modelType is not null)
        {
            SparseChangePayloadCollectionExportEmitter.AppendCollectionExport(operations, member, dialect, modelType, implementationNamespace, split);
        }

        if (surfaceDeferred is not null)
        {
            code.Append(surfaceDeferred.ToString());
        }
        if (target is not null)
        {
            if (deferredKeyOf is not null)
            {
                operations.Append(deferredKeyOf.ToString());
            }

            target.CloseMemberOperations(SparseNaming.EscapeIdentifier(member.Property.Name) + "PatchOperations");
        }
        code.CloseBlock();
    }

    /// <summary>Emits the keyed mutators (Add/Remove/Edit/Update/SetOrder).</summary>
    /// <remarks>With a null split the mutators stay instance members of the nested patch (legacy single-file emission).</remarks>
    internal static void EmitKeyedMutators(
        SharedIndentedBuilder code,
        SharedIndentedBuilder internalCode,
        SparseMemberModel member,
        string patchName,
        string elementType,
        string keyType,
        string editedValueType,
        string comparer,
        string facade,
        string? sharedRemoval,
        bool hasPatch,
        string? implementationNamespace,
        SparseMemberOperationSplit? split
    )
    {
        var relocated = split is not null;
        var guard = relocated ? "self." : string.Empty;
        // Add.
        if (relocated)
        {
            split!.Shell.AppendLine("/// <summary>Adds an element to this keyed patch.</summary>");
            split.Shell.AppendLine("public void Add(" + elementType + " element) => " + split.OperationsType + ".Add(this, element);");
            code.AppendLine("/// <summary>Adds an element to a keyed patch.</summary>");
            code.AppendLine("internal static void Add(" + patchName + " self, " + elementType + " element)");
            code.OpenBlock();
            AppendFieldAliases(code, member);
        }
        else
        {
            code.AppendLine("/// <summary>Adds an element to this keyed patch.</summary>");
            code.AppendLine("public void Add(" + elementType + " element)");
            code.OpenBlock();
        }
        code.AppendLine("if ((object?)element is null) throw new global::System.ArgumentNullException(nameof(element));");
        code.AppendLine(guard + "EnsureGranular(\"Add\");");
        code.AppendLine("var key = " + SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(element);");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine(
                "if (!"
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key")
                    + " && __added is not null) foreach (var existing in __added) if ("
                    + comparer
                    + ".Equals("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(existing), key)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection patch.\");"
            );
        }
        else
        {
            code.AppendLine(
                "if (__added is not null) foreach (var existing in __added) if ("
                    + comparer
                    + ".Equals("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(existing), key)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection patch.\");"
            );
        }
        code.AppendLine(
            "if (__edited is not null"
                + (
                    SparseKeyedCollectionEmitter.HasUnassignedKey(member)
                        ? " && !" + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key")
                        : ""
                )
                + " && __edited.ContainsKey(key!)) throw new global::System.InvalidOperationException(\"Key is already edited in this patch.\");"
        );
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            // Temporary adds stay separately addressable: duplicate
            // identities fail at record time instead of colliding at apply.
            code.AppendLine(
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key")
                    + ") { var __addTemp = "
                    + SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member)
                    + "(element); if (__addTemp.HasValue && __addTemp.Value != global::System.Guid.Empty && __added is not null) foreach (var existing in __added) if ("
                    + SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member)
                    + "(existing) == __addTemp.Value) throw new global::System.InvalidOperationException(\"Duplicate temporary key in keyed collection patch.\"); }"
            );
        }
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine(
                sharedRemoval is null
                    ? "if (!"
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key")
                        + " && __removed is not null) __SparseCancelRemoval(key);"
                    : "if (!"
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key")
                        + " && __removed is not null) "
                        + sharedRemoval
                        + ".CancelRemoval(__removed!, ref __removedLookup, key);"
            );
        }
        else
        {
            code.AppendLine(
                sharedRemoval is null
                    ? "if (__removed is not null) __SparseCancelRemoval(key);"
                    : "if (__removed is not null) " + sharedRemoval + ".CancelRemoval(__removed!, ref __removedLookup, key);"
            );
        }
        code.AppendLine("__added ??= new global::System.Collections.Generic.List<" + elementType + ">();");
        code.AppendLine("__added.Add(element);");
        code.CloseBlock();
        // Remove.
        if (relocated)
        {
            split!.Shell.AppendLine("/// <summary>Removes an element from this keyed patch by key.</summary>");
            split.Shell.AppendLine("public void Remove(" + keyType + " key) => " + split.OperationsType + ".Remove(this, key);");
            code.AppendLine("/// <summary>Removes an element from a keyed patch by key.</summary>");
            code.AppendLine("internal static void Remove(" + patchName + " self, " + keyType + " key)");
            code.OpenBlock();
            AppendFieldAliases(code, member);
        }
        else
        {
            code.AppendLine("/// <summary>Removes an element by key.</summary>");
            code.AppendLine("public void Remove(" + keyType + " key)");
            code.OpenBlock();
        }
        code.AppendLine(guard + "EnsureGranular(\"Remove\");");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine(
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key")
                    + ") throw new global::System.InvalidOperationException(\"An unassigned element cannot be removed by key.\");"
            );
        }

        code.AppendLine(
            "if (__added is not null) { for (var i = __added.Count - 1; i >= 0; i--) if ("
                + comparer
                + ".Equals("
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                + "(__added[i]), key)) { __added.RemoveAt(i); return; } }"
        );
        code.AppendLine("if (__edited is not null) __edited.Remove(key);");
        code.AppendLine("__removed ??= new global::System.Collections.Generic.List<" + keyType + ">();");
        SparseDictionaryRemovalIndexEmitter.EmitAdd(code, comparer, keepReservedIndex: false, keyType, implementationNamespace);
        code.CloseBlock();
        // Edit / Update.
        if (hasPatch)
        {
            var elementPatch = SparseKeyedCollectionEmitter.ElementPatchType(member);
            if (relocated)
            {
                split!.Shell.AppendLine("/// <summary>Gets the edit patch for an element of this keyed patch.</summary>");
                split.Shell.AppendLine("public " + elementPatch + " Edit(" + keyType + " key) => " + split.OperationsType + ".Edit(this, key);");
                code.AppendLine("/// <summary>Gets the edit patch for an element of a keyed patch.</summary>");
                code.AppendLine("internal static " + elementPatch + " Edit(" + patchName + " self, " + keyType + " key)");
                code.OpenBlock();
                AppendFieldAliases(code, member);
            }
            else
            {
                code.AppendLine("/// <summary>Gets the edit patch for an element.</summary>");
                code.AppendLine("/// <param name=\"key\">Key to edit.</param>");
                code.AppendLine("/// <returns>The edit patch.</returns>");
                code.AppendLine("public " + elementPatch + " Edit(" + keyType + " key)");
                code.OpenBlock();
            }
            code.AppendLine(guard + "EnsureGranular(\"Edit\");");
            if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
            {
                code.AppendLine(
                    "if ("
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key")
                        + ") throw new global::System.InvalidOperationException(\"An unassigned element cannot be edited by key.\");"
                );
            }

            code.AppendLine(
                sharedRemoval is null
                    ? "if (__removed is not null && __SparseContainsRemoved(key)) throw new global::System.InvalidOperationException(\"Cannot edit a removed element. Add it again instead.\");"
                    : "if (__removed is not null && "
                        + sharedRemoval
                        + ".ContainsRemoved(__removed!, __removedLookup, key)) throw new global::System.InvalidOperationException(\"Cannot edit a removed element. Add it again instead.\");"
            );
            code.AppendLine(
                "if (__added is not null) foreach (var a in __added) if ("
                    + comparer
                    + ".Equals("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(a), key)) throw new global::System.InvalidOperationException(\"Cannot edit an element added in the same patch. Update the added value directly.\");"
            );
            code.AppendLine(
                "__edited ??= new global::System.Collections.Generic.Dictionary<" + keyType + ", " + editedValueType + "> (" + comparer + ");"
            );
            code.AppendLine("if (!__edited.TryGetValue(key, out var patch)) { patch = new " + elementPatch + "(); __edited[key] = patch; }");
            code.AppendLine("return patch;");
            code.CloseBlock();
        }
        else
        {
            if (relocated)
            {
                split!.Shell.AppendLine("/// <summary>Updates an element of this keyed patch in place.</summary>");
                split.Shell.AppendLine("public void Update(" + elementType + " element) => " + split.OperationsType + ".Update(this, element);");
                code.AppendLine("/// <summary>Updates an element of a keyed patch in place.</summary>");
                code.AppendLine("internal static void Update(" + patchName + " self, " + elementType + " element)");
                code.OpenBlock();
                AppendFieldAliases(code, member);
            }
            else
            {
                code.AppendLine("/// <summary>Updates an element in place.</summary>");
                code.AppendLine("public void Update(" + elementType + " element)");
                code.OpenBlock();
            }
            code.AppendLine("if ((object?)element is null) throw new global::System.ArgumentNullException(nameof(element));");
            code.AppendLine(guard + "EnsureGranular(\"Update\");");
            code.AppendLine("var key = " + SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(element);");
            if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
            {
                // Unassigned elements with an identity update by Guid; the
                // dedicated mutator owns that path so identity never collides.
                var updateByTemp = split is not null
                    ? split.OperationsType + ".UpdateByTemporaryKey(self, element)"
                    : "UpdateByTemporaryKey(element)";
                code.AppendLine(
                    "if ("
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key")
                        + " && "
                        + SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member)
                        + "(element).HasValue) { "
                        + updateByTemp
                        + "; return; }"
                );
            }
            if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
            {
                code.AppendLine(
                    "if ("
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key")
                        + ") throw new global::System.InvalidOperationException(\"An unassigned element cannot be updated by key; add it as a new item instead.\");"
                );
            }

            code.AppendLine(
                sharedRemoval is null
                    ? "if (__removed is not null && __SparseContainsRemoved(key)) throw new global::System.InvalidOperationException(\"Cannot update a removed element. Add it again instead.\");"
                    : "if (__removed is not null && "
                        + sharedRemoval
                        + ".ContainsRemoved(__removed!, __removedLookup, key)) throw new global::System.InvalidOperationException(\"Cannot update a removed element. Add it again instead.\");"
            );
            code.AppendLine(
                "if (__added is not null) for (var i = 0; i < __added.Count; i++) if ("
                    + comparer
                    + ".Equals("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(__added[i]), key)) { __added[i] = element; return; }"
            );
            code.AppendLine(
                "__edited ??= new global::System.Collections.Generic.Dictionary<" + keyType + ", " + editedValueType + "> (" + comparer + ");"
            );
            code.AppendLine("__edited[key] = element;");
            code.CloseBlock();
        }

        if (relocated)
        {
            split!.Shell.AppendLine("/// <summary>Replaces the key order of this keyed patch.</summary>");
            split.Shell.AppendLine(
                "public void SetOrder(global::System.Collections.Generic.IEnumerable<"
                    + keyType
                    + "> keys) => "
                    + split.OperationsType
                    + ".SetOrder(this, keys);"
            );
            code.AppendLine("/// <summary>Replaces the key order of a keyed patch.</summary>");
            code.AppendLine(
                "internal static void SetOrder(" + patchName + " self, global::System.Collections.Generic.IEnumerable<" + keyType + "> keys)"
            );
            code.OpenBlock();
            AppendFieldAliases(code, member);
        }
        else
        {
            code.AppendLine("/// <summary>Replaces the key order.</summary>");
            code.AppendLine("/// <param name=\"keys\">Replacement order.</param>");
            code.AppendLine("public void SetOrder(global::System.Collections.Generic.IEnumerable<" + keyType + "> keys)");
            code.OpenBlock();
        }
        code.AppendLine("if (keys is null) throw new global::System.ArgumentNullException(nameof(keys));");
        code.AppendLine(guard + "EnsureGranular(\"SetOrder\");");
        code.AppendLine("var list = new global::System.Collections.Generic.List<" + keyType + ">(keys);");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine(
                "var __seenOrder = new global::System.Collections.Generic.HashSet<"
                    + keyType
                    + ">("
                    + comparer
                    + "); foreach (var __key in list) if (!"
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__key")
                    + " && !__seenOrder.Add(__key)) throw new global::System.InvalidOperationException(\"Duplicate order key.\");"
            );
        }
        else
        {
            code.AppendLine(facade + ".EnsureUniqueKeys<" + keyType + ">(list);");
        }

        code.AppendLine("__order = list;");
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            // Explicit key orders carry no temporary fidelity; later applies
            // fall back to positional legacy flow for bare sentinel slots.
            code.AppendLine("__orderTemp = null;");
        }
        code.CloseBlock();

        EmitChangeSetConversionMutators(
            internalCode,
            member,
            patchName,
            elementType,
            keyType,
            editedValueType,
            comparer,
            sharedRemoval,
            implementationNamespace,
            split
        );
    }

    private static void EmitChangeSetConversionMutators(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string patchName,
        string elementType,
        string keyType,
        string editedValueType,
        string comparer,
        string? sharedRemoval,
        string? implementationNamespace,
        SparseMemberOperationSplit? split
    )
    {
        var relocated = split is not null;
        var implementation = relocated ? split!.Operations : code;
        var receiver = relocated ? "self." : string.Empty;
        var keyOf = SparseKeyedCollectionEmitter.KeyOfMethod(member);
        var hasUnassigned = SparseKeyedCollectionEmitter.HasUnassignedKey(member);
        var hasTemporaryKey = SparseKeyedCollectionEmitter.HasTemporaryKey(member);
        var tempOf = hasTemporaryKey ? SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member) : null;
        var unassigned = hasUnassigned ? SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key") : null;

        void Begin(string name, string parameters, string arguments)
        {
            if (split is null)
            {
                implementation.AppendLine("internal void " + name + "(" + parameters + ")");
            }
            else
            {
                code.AppendLine(
                    "internal void " + name + "(" + parameters + ") => " + split.OperationsType + "." + name + "(this, " + arguments + ");"
                );
                implementation.AppendLine("internal static void " + name + "(" + patchName + " self, " + parameters + ")");
            }

            implementation.OpenBlock();
            if (relocated)
            {
                AppendFieldAliases(implementation, member);
            }
        }

        Begin(
            "__SparseAddForChangeSet",
            elementType
                + " element, global::System.Collections.Generic.HashSet<"
                + keyType
                + "> addedKeys"
                + (hasTemporaryKey ? ", global::System.Collections.Generic.HashSet<global::System.Guid> addedTemporaryKeys" : ""),
            "element, addedKeys" + (hasTemporaryKey ? ", addedTemporaryKeys" : "")
        );
        implementation.AppendLine("if ((object?)element is null) throw new global::System.ArgumentNullException(nameof(element));");
        implementation.AppendLine(receiver + "EnsureGranular(\"Add\");");
        implementation.AppendLine("var key = " + keyOf + "(element);");
        if (hasUnassigned)
        {
            implementation.AppendLine(
                "if (!"
                    + unassigned
                    + " && !addedKeys.Add(key)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection patch.\");"
            );
        }
        else
        {
            implementation.AppendLine(
                "if (!addedKeys.Add(key)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection patch.\");"
            );
        }

        implementation.AppendLine(
            "if (__edited is not null"
                + (hasUnassigned ? " && !" + unassigned : "")
                + " && __edited.ContainsKey(key!)) throw new global::System.InvalidOperationException(\"Key is already edited in this patch.\");"
        );
        if (hasTemporaryKey)
        {
            implementation.AppendLine(
                "if ("
                    + unassigned
                    + ") { var __temporaryKey = "
                    + tempOf
                    + "(element); if (__temporaryKey.HasValue && __temporaryKey.Value != global::System.Guid.Empty && !addedTemporaryKeys.Add(__temporaryKey.Value)) throw new global::System.InvalidOperationException(\"Duplicate temporary key in keyed collection patch.\"); }"
            );
        }

        if (hasUnassigned)
        {
            implementation.AppendLine(
                sharedRemoval is null
                    ? "if (!" + unassigned + " && __removed is not null) __SparseCancelRemoval(key);"
                    : "if (!" + unassigned + " && __removed is not null) " + sharedRemoval + ".CancelRemoval(__removed!, ref __removedLookup, key);"
            );
        }
        else
        {
            implementation.AppendLine(
                sharedRemoval is null
                    ? "if (__removed is not null) __SparseCancelRemoval(key);"
                    : "if (__removed is not null) " + sharedRemoval + ".CancelRemoval(__removed!, ref __removedLookup, key);"
            );
        }

        implementation.AppendLine("__added ??= new global::System.Collections.Generic.List<" + elementType + ">();");
        implementation.AppendLine("__added.Add(element);");
        implementation.CloseBlock();

        Begin(
            "__SparseRemoveForChangeSet",
            keyType + " key, global::System.Collections.Generic.HashSet<" + keyType + "> addedKeys",
            "key, addedKeys"
        );
        implementation.AppendLine(receiver + "EnsureGranular(\"Remove\");");
        if (hasUnassigned)
        {
            implementation.AppendLine(
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key")
                    + ") throw new global::System.InvalidOperationException(\"An unassigned element cannot be removed by key.\");"
            );
        }

        implementation.AppendLine("if (addedKeys.Contains(key) && __added is not null)");
        implementation.OpenBlock();
        implementation.AppendLine(
            "for (var i = __added.Count - 1; i >= 0; i--) if ("
                + comparer
                + ".Equals("
                + keyOf
                + "(__added[i]), key)) { __added.RemoveAt(i); addedKeys.Remove(key); return; }"
        );
        implementation.CloseBlock();
        implementation.AppendLine("if (__edited is not null) __edited.Remove(key);");
        implementation.AppendLine("__removed ??= new global::System.Collections.Generic.List<" + keyType + ">();");
        SparseDictionaryRemovalIndexEmitter.EmitAdd(implementation, comparer, keepReservedIndex: false, keyType, implementationNamespace);
        implementation.CloseBlock();

        if (hasTemporaryKey)
        {
            Begin(
                "__SparseRemoveByTemporaryKeyForChangeSet",
                "global::System.Guid temporaryKey, global::System.Collections.Generic.HashSet<global::System.Guid> addedTemporaryKeys, global::System.Collections.Generic.HashSet<global::System.Guid> removedTemporaryKeys",
                "temporaryKey, addedTemporaryKeys, removedTemporaryKeys"
            );
            implementation.AppendLine(receiver + "EnsureGranular(\"RemoveByTemporaryKey\");");
            implementation.AppendLine(
                "if (temporaryKey == global::System.Guid.Empty) throw new global::System.ArgumentException(\"Temporary identity must not be empty.\", nameof(temporaryKey));"
            );
            implementation.AppendLine("if (addedTemporaryKeys.Contains(temporaryKey) && __added is not null)");
            implementation.OpenBlock();
            implementation.AppendLine(
                "for (var i = __added.Count - 1; i >= 0; i--) if ("
                    + tempOf
                    + "(__added[i]) == temporaryKey) { __added.RemoveAt(i); addedTemporaryKeys.Remove(temporaryKey); return; }"
            );
            implementation.CloseBlock();
            implementation.AppendLine("if (__editedTemp is not null) __editedTemp.Remove(temporaryKey);");
            implementation.AppendLine(
                "if (!removedTemporaryKeys.Add(temporaryKey)) throw new global::System.InvalidOperationException(\"Element is already removed in this patch.\");"
            );
            implementation.AppendLine("__removedTemp ??= new global::System.Collections.Generic.List<global::System.Guid>();");
            implementation.AppendLine("__removedTemp.Add(temporaryKey);");
            implementation.CloseBlock();
        }

        if (!SparseKeyedCollectionEmitter.HasElementPatch(member))
        {
            if (hasTemporaryKey)
            {
                Begin(
                    "__SparseUpdateByTemporaryKeyForChangeSet",
                    elementType
                        + " element, global::System.Collections.Generic.HashSet<global::System.Guid> addedTemporaryKeys, global::System.Collections.Generic.HashSet<global::System.Guid> removedTemporaryKeys",
                    "element, addedTemporaryKeys, removedTemporaryKeys"
                );
                implementation.AppendLine("if ((object?)element is null) throw new global::System.ArgumentNullException(nameof(element));");
                implementation.AppendLine(receiver + "EnsureGranular(\"UpdateByTemporaryKey\");");
                implementation.AppendLine("var __temporaryKey = " + tempOf + "(element);");
                implementation.AppendLine(
                    "if (!__temporaryKey.HasValue || __temporaryKey.Value == global::System.Guid.Empty) throw new global::System.InvalidOperationException(\"UpdateByTemporaryKey requires an unassigned element with a temporary identity.\");"
                );
                implementation.AppendLine(
                    "if (removedTemporaryKeys.Contains(__temporaryKey.Value)) throw new global::System.InvalidOperationException(\"Cannot update a removed element. Add it again instead.\");"
                );
                implementation.AppendLine("if (addedTemporaryKeys.Contains(__temporaryKey.Value) && __added is not null)");
                implementation.OpenBlock();
                implementation.AppendLine(
                    "for (var i = 0; i < __added.Count; i++) if ("
                        + tempOf
                        + "(__added[i]) == __temporaryKey.Value) { __added[i] = element; return; }"
                );
                implementation.CloseBlock();
                implementation.AppendLine(
                    "__editedTemp ??= new global::System.Collections.Generic.Dictionary<global::System.Guid, " + editedValueType + ">();"
                );
                implementation.AppendLine("__editedTemp[__temporaryKey.Value] = element;");
                implementation.CloseBlock();
            }

            var updateParams =
                elementType
                + " element, global::System.Collections.Generic.HashSet<"
                + keyType
                + "> addedKeys"
                + (
                    hasTemporaryKey
                        ? ", global::System.Collections.Generic.HashSet<global::System.Guid> addedTemporaryKeys, global::System.Collections.Generic.HashSet<global::System.Guid> removedTemporaryKeys"
                        : ""
                );
            var updateArgs = "element, addedKeys" + (hasTemporaryKey ? ", addedTemporaryKeys, removedTemporaryKeys" : "");
            Begin("__SparseUpdateForChangeSet", updateParams, updateArgs);
            implementation.AppendLine("if ((object?)element is null) throw new global::System.ArgumentNullException(nameof(element));");
            implementation.AppendLine(receiver + "EnsureGranular(\"Update\");");
            implementation.AppendLine("var key = " + keyOf + "(element);");
            if (hasTemporaryKey)
            {
                var temporaryUpdateArgs = "element, addedTemporaryKeys, removedTemporaryKeys";
                implementation.AppendLine(
                    "if ("
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key")
                        + " && "
                        + tempOf
                        + "(element).HasValue) { "
                        + (
                            relocated
                                ? split!.OperationsType + ".__SparseUpdateByTemporaryKeyForChangeSet(self, "
                                : "__SparseUpdateByTemporaryKeyForChangeSet("
                        )
                        + temporaryUpdateArgs
                        + "); return; }"
                );
            }
            if (hasUnassigned)
            {
                implementation.AppendLine(
                    "if ("
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key")
                        + ") throw new global::System.InvalidOperationException(\"An unassigned element cannot be updated by key; add it as a new item instead.\");"
                );
            }
            var containsRemoved = sharedRemoval is null
                ? "__SparseContainsRemoved(key)"
                : sharedRemoval + ".ContainsRemoved(__removed!, __removedLookup, key)";
            implementation.AppendLine(
                "if (__removed is not null && "
                    + containsRemoved
                    + ") throw new global::System.InvalidOperationException(\"Cannot update a removed element. Add it again instead.\");"
            );
            implementation.AppendLine("if (addedKeys.Contains(key) && __added is not null)");
            implementation.OpenBlock();
            implementation.AppendLine(
                "for (var i = 0; i < __added.Count; i++) if ("
                    + comparer
                    + ".Equals("
                    + keyOf
                    + "(__added[i]), key)) { __added[i] = element; return; }"
            );
            implementation.CloseBlock();
            implementation.AppendLine(
                "__edited ??= new global::System.Collections.Generic.Dictionary<" + keyType + ", " + editedValueType + ">(" + comparer + ");"
            );
            implementation.AppendLine("__edited[key] = element;");
            implementation.CloseBlock();
        }
    }

    /// <summary>Aliases nested keyed-patch fields by reference for relocated bodies.</summary>
    /// <remarks>Relocated mutator and algebra bodies operate on the facade instance through these aliases.</remarks>
    internal static void AppendFieldAliases(SharedIndentedBuilder code, SparseMemberModel? member = null)
    {
        code.AppendLine("ref var __whole = ref self.__whole;");
        code.AppendLine("ref var __added = ref self.__added;");
        code.AppendLine("ref var __removed = ref self.__removed;");
        code.AppendLine("ref var __removedLookup = ref self.__removedLookup;");
        code.AppendLine("ref var __edited = ref self.__edited;");
        code.AppendLine("ref var __order = ref self.__order;");
        if (member.HasValue && SparseKeyedCollectionEmitter.HasTemporaryKey(member.Value))
        {
            SparseKeyedSequenceTempSurfaceEmitter.AppendTempFieldAliases(code);
        }
    }
}
