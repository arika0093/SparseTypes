namespace SparseTypes.Generator.Shared;

/// <summary>Emits keyed-sequence Rebase algebra including the per-key three-way merge.</summary>
internal static class SparseKeyedSequenceRebaseEmitter
{
    internal static void EmitKeyedRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string patchName,
        string elementType,
        string keyType,
        string listType,
        bool hasPatch,
        string comparer,
        string facade,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseMemberOperationSplit? split = null
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var resultType = dialect.RebaseResult(patchName);
        var conflictType = dialect.ConflictType;
        var conflictKindType = dialect.ConflictKindType;
        if (split is not null)
        {
            split.Shell.AppendLine("/// <summary>Rebases this keyed patch onto a newer member value.</summary>");
            split.Shell.AppendLine(
                "public static "
                    + resultType
                    + " Rebase("
                    + runtime
                    + "Optional<"
                    + listType
                    + "> baseState, "
                    + patchName
                    + " local, "
                    + runtime
                    + "Optional<"
                    + listType
                    + "> currentState) => "
                    + split.OperationsType
                    + ".Rebase(baseState, local, currentState);"
            );
            code.AppendLine("/// <summary>Rebases a keyed patch onto a newer member value.</summary>");
        }
        code.AppendLine(
            (split is null ? "public static " : "internal static ")
                + resultType
                + " Rebase("
                + runtime
                + "Optional<"
                + listType
                + "> baseState, "
                + patchName
                + " local, "
                + runtime
                + "Optional<"
                + listType
                + "> currentState)"
        );
        code.OpenBlock();
        code.AppendLine("if (local is null) throw new global::System.ArgumentNullException(nameof(local));");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine(
                "if (baseState.IsPresent && (object?)baseState.Value is not null) foreach (var __baselineItem in baseState.Value!) if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                        member,
                        SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(__baselineItem)"
                    )
                    + ") throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed collection operation.\");"
            );
        }
        code.AppendLine("if (local.__SparseIsEmpty()) return " + resultType + ".Success(new " + patchName + "());");
        code.AppendLine("var result = new " + patchName + "();");
        code.AppendLine("var conflicts = new global::System.Collections.Generic.List<" + conflictType + ">();");
        code.AppendLine(runtime + "Optional<" + listType + "> desired;");
        code.AppendLine("try { desired = local.Apply(baseState); }");
        code.AppendLine(
            "catch (global::System.InvalidOperationException ex) { conflicts.Add(new "
                + conflictType
                + "(__SparseRootPath, "
                + conflictKindType
                + ".Nested, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Missing, ex.Message)); return new "
                + resultType
                + "(result, conflicts); }"
        );
        code.AppendLine("if (local.__whole.Kind != " + runtime + "FragmentOperationKind.Keep)");
        code.OpenBlock();
        code.AppendLine(
            "if ("
                + facade
                + ".AreEqual((object?)baseState.Value, (object?)currentState.Value)) { result.__whole = local.__whole; return new "
                + resultType
                + "(result, conflicts); }"
        );
        code.AppendLine(
            "var desiredState = desired; if ("
                + facade
                + ".AreEqual((object?)desiredState.Value, (object?)currentState.Value)) return "
                + resultType
                + ".Success(new "
                + patchName
                + "());"
        );
        code.AppendLine(
            "conflicts.Add(new "
                + conflictType
                + "(__SparseRootPath, "
                + conflictKindType
                + ".Nested, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Missing, \"The whole collection conflicts with a concurrent change.\"));"
        );
        code.AppendLine("return new " + resultType + "(result, conflicts);");
        code.CloseBlock();
        // Unchanged keys bypass nested rebase.
        var editedTempCopyFragment = string.Empty;
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            var tempEditedValueType = hasPatch ? SparseKeyedCollectionEmitter.ElementPatchType(member) : elementType;
            editedTempCopyFragment =
                " result.__removedTemp = local.__removedTemp is null ? null : new global::System.Collections.Generic.List<global::System.Guid>(local.__removedTemp); result.__editedTemp = local.__editedTemp is null ? null : new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                + tempEditedValueType
                + ">(local.__editedTemp);";
        }
        code.AppendLine(
            "if ("
                + facade
                + ".AreEqual((object?)baseState.Value, (object?)currentState.Value)) { result.__added = local.__added is null ? null : new global::System.Collections.Generic.List<"
                + elementType
                + ">(local.__added); result.__removed = local.__removed is null ? null : new global::System.Collections.Generic.List<"
                + keyType
                + ">(local.__removed); result.__edited = local.__edited is null ? null : new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + (hasPatch ? SparseKeyedCollectionEmitter.ElementPatchType(member) : elementType)
                + ">(local.__edited, "
                + comparer
                + "); result.__order = local.__order is null ? null : new global::System.Collections.Generic.List<"
                + keyType
                + ">(local.__order);"
                + editedTempCopyFragment
                + " return new "
                + resultType
                + "(result, conflicts); }"
        );
        code.AppendLine(
            "if (desired.IsPresent == currentState.IsPresent && "
                + facade
                + ".AreEqual((object?)desired.Value, (object?)currentState.Value)) return "
                + resultType
                + ".Success(new "
                + patchName
                + "());"
        );
        EmitKeyedRebasePerKey(code, member, elementType, keyType, hasPatch, comparer, facade, dialect);
        code.AppendLine("return new " + resultType + "(result, conflicts);");
        code.CloseBlock();
    }

    internal static void EmitKeyedRebasePerKey(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string elementType,
        string keyType,
        bool hasPatch,
        string comparer,
        string facade,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var conflictType = dialect.ConflictType;
        var conflictKindType = dialect.ConflictKindType;
        var hasTempPatchRebase = SparseKeyedCollectionEmitter.HasTemporaryKey(member);
        if (hasTempPatchRebase)
        {
            SparseKeyedSequenceTempRebaseEmitter.AppendTempStateMaps(code, member, elementType, member.Id);
        }
        // Build maps.
        code.AppendLine(
            "var __baseCapacity = baseState.GetValueOrDefault() is global::System.Collections.Generic.ICollection<"
                + elementType
                + "> __baseCollection ? __baseCollection.Count : (baseState.GetValueOrDefault() is global::System.Collections.Generic.IReadOnlyCollection<"
                + elementType
                + "> __baseReadOnly ? __baseReadOnly.Count : 0);"
        );
        code.AppendLine(
            "var baseMap = new global::System.Collections.Generic.Dictionary<" + keyType + ", " + elementType + ">(__baseCapacity, " + comparer + ");"
        );
        string baseUnassignedFragment;
        if (hasTempPatchRebase)
        {
            baseUnassignedFragment =
                "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                + ") { var __btt = "
                + SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member)
                + "(item); if (!__btt.HasValue || __btt.Value == global::System.Guid.Empty) throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed collection operation.\"); continue; } ";
        }
        else if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            baseUnassignedFragment =
                "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                + ") throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed collection operation.\"); ";
        }
        else
        {
            baseUnassignedFragment = string.Empty;
        }
        code.AppendLine(
            "if (baseState.IsPresent && (object?)baseState.Value is not null) foreach (var item in baseState.Value!) { var k = "
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                + "(item); "
                + baseUnassignedFragment
                + SparseKeyedCollectionEmitter.AddUniqueEntry("baseMap", "k", "item")
                + " }"
        );
        code.AppendLine(
            "var __currentCapacity = currentState.GetValueOrDefault() is global::System.Collections.Generic.ICollection<"
                + elementType
                + "> __currentCollection ? __currentCollection.Count : (currentState.GetValueOrDefault() is global::System.Collections.Generic.IReadOnlyCollection<"
                + elementType
                + "> __currentReadOnly ? __currentReadOnly.Count : 0);"
        );
        code.AppendLine(
            "var currentMap = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">(__currentCapacity, "
                + comparer
                + ");"
        );
        string currentUnassignedFragment;
        if (hasTempPatchRebase)
        {
            currentUnassignedFragment =
                "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                + ") { var __ctt = "
                + SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member)
                + "(item); if (!__ctt.HasValue || __ctt.Value == global::System.Guid.Empty) throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed collection operation.\"); continue; } ";
        }
        else if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            currentUnassignedFragment =
                "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                + ") throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed collection operation.\"); ";
        }
        else
        {
            currentUnassignedFragment = string.Empty;
        }
        code.AppendLine(
            "if (currentState.IsPresent && (object?)currentState.Value is not null) foreach (var item in currentState.Value!) { var k = "
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                + "(item); "
                + currentUnassignedFragment
                + SparseKeyedCollectionEmitter.AddUniqueEntry("currentMap", "k", "item")
                + " }"
        );
        code.AppendLine(
            "var __desiredCapacity = desired.GetValueOrDefault() is global::System.Collections.Generic.ICollection<"
                + elementType
                + "> __desiredCollection ? __desiredCollection.Count : (desired.GetValueOrDefault() is global::System.Collections.Generic.IReadOnlyCollection<"
                + elementType
                + "> __desiredReadOnly ? __desiredReadOnly.Count : 0);"
        );
        code.AppendLine(
            "var desiredMap = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">(__desiredCapacity, "
                + comparer
                + ");"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member) || hasTempPatchRebase)
        {
            code.AppendLine("var __desiredUnassigned = new global::System.Collections.Generic.List<" + elementType + ">();");
        }

        string desiredUnassignedFragment;
        if (hasTempPatchRebase)
        {
            desiredUnassignedFragment =
                "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                + ") { var __dtt = "
                + SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member)
                + "(item); if (!__dtt.HasValue || __dtt.Value == global::System.Guid.Empty) { __desiredUnassigned.Add(item); } continue; } ";
        }
        else if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            desiredUnassignedFragment =
                "if (" + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k") + ") { __desiredUnassigned.Add(item); continue; } ";
        }
        else
        {
            desiredUnassignedFragment = string.Empty;
        }
        code.AppendLine(
            "if (desired.IsPresent && (object?)desired.Value is not null) foreach (var item in desired.Value!) { var k = "
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                + "(item); "
                + desiredUnassignedFragment
                + SparseKeyedCollectionEmitter.AddUniqueEntry("desiredMap", "k", "item")
                + " }"
        );
        // Index touched keys once for comparer-aware constant-time lookups in
        // the union-key loop.
        code.AppendLine("var __touchedAdded = new global::System.Collections.Generic.HashSet<" + keyType + ">(" + comparer + ");");
        code.AppendLine(
            "var __addedByKey = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">((local.__added is null) ? 0 : local.__added.Count, "
                + comparer
                + ");"
        );
        // Locally added unassigned entries merge positionally below; only
        // assigned touches join the keyed three-way.
        var touchedUnassignedFragment =
            hasTempPatchRebase || SparseKeyedCollectionEmitter.HasUnassignedKey(member)
                ? "if (" + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__touchedKey") + ") continue; "
                : string.Empty;
        code.AppendLine(
            "if (local.__added is not null) foreach (var __touchedItem in local.__added) { var __touchedKey = "
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                + "(__touchedItem); "
                + touchedUnassignedFragment
                + "__touchedAdded.Add(__touchedKey); __addedByKey[__touchedKey] = __touchedItem; }"
        );
        code.AppendLine(
            "var __touchedRemoved = (local.__removed is null) ? new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ") : new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(local.__removed, "
                + comparer
                + ");"
        );
        // Seed from edited keys for an exact capacity hint, then merge other
        // local touches.
        code.AppendLine(
            "var __pendingKeys = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(local.__edited is null ? (global::System.Collections.Generic.IEnumerable<"
                + keyType
                + ">)__touchedAdded : local.__edited.Keys, "
                + comparer
                + ");"
        );
        code.AppendLine("if (local.__edited is not null) __pendingKeys.UnionWith(__touchedAdded);");
        code.AppendLine("__pendingKeys.UnionWith(__touchedRemoved);");
        if (hasPatch)
        {
            var elementPatch = SparseKeyedCollectionEmitter.ElementPatchType(member);
            var elementFragment = SparseKeyedCollectionEmitter.ElementFragmentType(member);
            var prefix = SparseKeyedCollectionEmitter.ElementPatchPrefix(member);
            SparseKeyedCollectionEmitter.AppendPendingKeyLoopStart(code);
            code.AppendLine(
                "var inBase = baseMap.TryGetValue(k, out var b); var inCurrent = currentMap.TryGetValue(k, out var c); var inDesired = desiredMap.TryGetValue(k, out var d);"
            );
            code.AppendLine("bool baseEqualsCurrent = inBase == inCurrent && (!inBase || " + facade + ".AreEqual((object?)b, (object?)c));");
            code.AppendLine("bool desiredEqualsCurrent = inDesired == inCurrent && (!inDesired || " + facade + ".AreEqual((object?)d, (object?)c));");
            code.AppendLine("if (baseEqualsCurrent)");
            code.OpenBlock();
            code.AppendLine(
                "if (__touchedRemoved.Contains(k)) { (result.__removed ??= new global::System.Collections.Generic.List<" + keyType + ">()).Add(k); }"
            );
            code.AppendLine(
                "else if (__addedByKey.TryGetValue(k, out var __rebasedAdded)) (result.__added ??= new global::System.Collections.Generic.List<"
                    + elementType
                    + ">()).Add(__rebasedAdded);"
            );
            code.AppendLine(
                "if (local.__edited is not null && local.__edited.TryGetValue(k, out var edit) && !edit.__SparseIsEmpty()) { (result.__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementPatch
                    + ">("
                    + comparer
                    + "))[k] = edit; }"
            );
            code.CloseBlock();
            code.AppendLine("else if (desiredEqualsCurrent) { }");
            code.AppendLine(
                "else if (inBase && inCurrent && inDesired && local.__edited is not null && local.__edited.TryGetValue(k, out var localEdit))"
            );
            code.OpenBlock();
            code.AppendLine(
                "var nested = "
                    + elementPatch
                    + "."
                    + prefix
                    + "Rebase("
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(b!)), localEdit, "
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(c!)));"
            );
            code.AppendLine(
                "if (!nested.HasConflicts && !nested.Rebased.__SparseIsEmpty()) (result.__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementPatch
                    + ">("
                    + comparer
                    + "))[k] = nested.Rebased;"
            );
            code.AppendLine("else foreach (var nc in nested.Conflicts) conflicts.Add(nc.WithKeyPrefix(k));");
            code.CloseBlock();
            code.AppendLine("else");
            code.OpenBlock();
            code.AppendLine(
                "conflicts.Add(new "
                    + conflictType
                    + "(__SparseRootPath.Key(k), "
                    + conflictKindType
                    + ".Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)b), "
                    + runtime
                    + "Optional<object?>.Present((object?)d), "
                    + runtime
                    + "Optional<object?>.Present((object?)c), \"The keyed element conflicts with a concurrent change.\"));"
            );
            code.CloseBlock();
            SparseKeyedCollectionEmitter.AppendPendingKeyLoopEnd(code);
            if (hasTempPatchRebase)
            {
                SparseKeyedSequenceTempRebaseEmitter.AppendTempThreeWay(code, member, elementType, hasPatch, facade, dialect, member.Id);
            }
            // Order: keep local order only when current order unchanged and no
            // order conflict.
            code.AppendLine("if (local.__order is not null && local.__order.Count > 0)");
            code.OpenBlock();
            code.AppendLine(
                "var baseOrder = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(); if (baseState.IsPresent && (object?)baseState.Value is not null) foreach (var item in baseState.Value!) baseOrder.Add("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item));"
            );
            code.AppendLine(
                "var currentOrder = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(); if (currentState.IsPresent && (object?)currentState.Value is not null) foreach (var item in currentState.Value!) currentOrder.Add("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item));"
            );
            code.AppendLine(
                "var desiredOrder = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(); if (desired.IsPresent && (object?)desired.Value is not null) foreach (var item in desired.Value!) desiredOrder.Add("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item));"
            );
            if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
            {
                code.AppendLine(
                    "var __ordinaryDesiredOrder = new global::System.Collections.Generic.List<"
                        + keyType
                        + ">(); foreach (var __orderKey in desiredOrder) if (!"
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__orderKey")
                        + ") __ordinaryDesiredOrder.Add(__orderKey); bool __unassignedOnlyAdds = __desiredUnassigned.Count > 0 && "
                        + facade
                        + ".KeyOrderEquals<"
                        + keyType
                        + ">(baseOrder, __ordinaryDesiredOrder);"
                );
            }
            code.AppendLine(
                "if ("
                    + (SparseKeyedCollectionEmitter.HasUnassignedKey(member) ? "__unassignedOnlyAdds) { } else if (" : "")
                    + facade
                    + ".KeyOrderEquals<"
                    + keyType
                    + ">(baseOrder, currentOrder)) result.__order = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(local.__order);"
            );
            if (hasTempPatchRebase)
            {
                code.AppendLine(
                    "if (local.__orderTemp is not null && result.__order is not null) result.__orderTemp = new global::System.Collections.Generic.List<global::System.Guid?>(local.__orderTemp);"
                );
            }
            code.AppendLine(
                "else if (!"
                    + facade
                    + ".KeyOrderEquals<"
                    + keyType
                    + ">(desiredOrder, currentOrder)) conflicts.Add(new "
                    + conflictType
                    + "(__SparseRootPath, "
                    + conflictKindType
                    + ".Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)baseOrder), "
                    + runtime
                    + "Optional<object?>.Present((object?)desiredOrder), "
                    + runtime
                    + "Optional<object?>.Present((object?)currentOrder), \"The collection order conflicts with a concurrent change.\"));"
            );
            code.CloseBlock();
        }
        else
        {
            SparseKeyedCollectionEmitter.AppendPendingKeyLoopStart(code);
            code.AppendLine(
                "var inBase = baseMap.TryGetValue(k, out var b); var inCurrent = currentMap.TryGetValue(k, out var c); var inDesired = desiredMap.TryGetValue(k, out var d);"
            );
            code.AppendLine("bool baseEqualsCurrent = inBase == inCurrent && (!inBase || " + facade + ".AreEqual((object?)b, (object?)c));");
            code.AppendLine("bool desiredEqualsCurrent = inDesired == inCurrent && (!inDesired || " + facade + ".AreEqual((object?)d, (object?)c));");
            code.AppendLine("if (baseEqualsCurrent)");
            code.OpenBlock();
            code.AppendLine(
                "if (__touchedRemoved.Contains(k)) { (result.__removed ??= new global::System.Collections.Generic.List<" + keyType + ">()).Add(k); }"
            );
            code.AppendLine(
                "else if (__addedByKey.TryGetValue(k, out var __rebasedAdded)) (result.__added ??= new global::System.Collections.Generic.List<"
                    + elementType
                    + ">()).Add(__rebasedAdded);"
            );
            code.AppendLine(
                "else if (local.__edited is not null && local.__edited.TryGetValue(k, out var upd)) { (result.__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementType
                    + ">("
                    + comparer
                    + "))[k] = upd; }"
            );
            code.CloseBlock();
            code.AppendLine("else if (!desiredEqualsCurrent)");
            code.OpenBlock();
            code.AppendLine(
                "conflicts.Add(new "
                    + conflictType
                    + "(__SparseRootPath.Key(k), "
                    + conflictKindType
                    + ".Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)b), "
                    + runtime
                    + "Optional<object?>.Present((object?)d), "
                    + runtime
                    + "Optional<object?>.Present((object?)c), \"The keyed element conflicts with a concurrent change.\"));"
            );
            code.CloseBlock();
            SparseKeyedCollectionEmitter.AppendPendingKeyLoopEnd(code);
            code.AppendLine("if (local.__order is not null && local.__order.Count > 0)");
            code.OpenBlock();
            code.AppendLine(
                "var baseOrder = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(); if (baseState.IsPresent && (object?)baseState.Value is not null) foreach (var item in baseState.Value!) baseOrder.Add("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item));"
            );
            code.AppendLine(
                "var currentOrder = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(); if (currentState.IsPresent && (object?)currentState.Value is not null) foreach (var item in currentState.Value!) currentOrder.Add("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item));"
            );
            code.AppendLine(
                "var desiredOrder = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(); if (desired.IsPresent && (object?)desired.Value is not null) foreach (var item in desired.Value!) desiredOrder.Add("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item));"
            );
            if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
            {
                code.AppendLine(
                    "var __ordinaryDesiredOrder = new global::System.Collections.Generic.List<"
                        + keyType
                        + ">(); foreach (var __orderKey in desiredOrder) if (!"
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__orderKey")
                        + ") __ordinaryDesiredOrder.Add(__orderKey); bool __unassignedOnlyAdds = __desiredUnassigned.Count > 0 && "
                        + facade
                        + ".KeyOrderEquals<"
                        + keyType
                        + ">(baseOrder, __ordinaryDesiredOrder);"
                );
            }
            code.AppendLine(
                "if ("
                    + (SparseKeyedCollectionEmitter.HasUnassignedKey(member) ? "__unassignedOnlyAdds) { } else if (" : "")
                    + facade
                    + ".KeyOrderEquals<"
                    + keyType
                    + ">(baseOrder, currentOrder)) result.__order = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(local.__order);"
            );
            if (hasTempPatchRebase)
            {
                code.AppendLine(
                    "if (local.__orderTemp is not null && result.__order is not null) result.__orderTemp = new global::System.Collections.Generic.List<global::System.Guid?>(local.__orderTemp);"
                );
            }
            code.AppendLine(
                "else if (!"
                    + facade
                    + ".KeyOrderEquals<"
                    + keyType
                    + ">(desiredOrder, currentOrder)) conflicts.Add(new "
                    + conflictType
                    + "(__SparseRootPath, "
                    + conflictKindType
                    + ".Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)baseOrder), "
                    + runtime
                    + "Optional<object?>.Present((object?)desiredOrder), "
                    + runtime
                    + "Optional<object?>.Present((object?)currentOrder), \"The collection order conflicts with a concurrent change.\"));"
            );
            code.CloseBlock();
        }
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine(
                "if (__desiredUnassigned.Count > 0) { (result.__added ??= new global::System.Collections.Generic.List<"
                    + elementType
                    + ">()).AddRange(__desiredUnassigned); }"
            );
        }
    }
}
