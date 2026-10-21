namespace SparseTypes.Generator.Shared;

internal static class SparseDictionaryRebaseEmitter
{
    internal static void EmitDictionaryRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string patchName,
        string keyType,
        string valueType,
        string runtime,
        string kind,
        string optionalDict,
        string facade,
        string comparer,
        bool hasPatch,
        string editedValueType,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseMemberOperationSplit? split = null
    )
    {
        // Rebase (simplified key-wise).
        var resultType = dialect.RebaseResult(patchName);
        var conflictType = dialect.ConflictType;
        var conflictKindType = dialect.ConflictKindType;
        if (split is not null)
        {
            split.Shell.AppendLine(
                "/// <summary>Rebases this dictionary patch onto a newer member value.</summary>"
            );
            split.Shell.AppendLine(
                "public static "
                    + resultType
                    + " Rebase("
                    + optionalDict
                    + " baseState, "
                    + patchName
                    + " local, "
                    + optionalDict
                    + " currentState) => "
                    + split.OperationsType
                    + ".Rebase(baseState, local, currentState);"
            );
            code.AppendLine(
                "/// <summary>Rebases a dictionary patch onto a newer member value.</summary>"
            );
        }
        code.AppendLine(
            (split is null ? "public static " : "internal static ")
                + resultType
                + " Rebase("
                + optionalDict
                + " baseState, "
                + patchName
                + " local, "
                + optionalDict
                + " currentState)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (local is null) throw new global::System.ArgumentNullException(nameof(local));"
        );
        code.AppendLine(
            "if (local.__SparseIsEmpty()) return "
                + resultType
                + ".Success(new "
                + patchName
                + "());"
        );
        code.AppendLine("var result = new " + patchName + "();");
        code.AppendLine(
            "var conflicts = new global::System.Collections.Generic.List<" + conflictType + ">();"
        );
        code.AppendLine(optionalDict + " desired;");
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
        code.AppendLine("if (local.__whole.Kind != " + kind + ".Keep)");
        code.OpenBlock();
        code.AppendLine(
            "if ("
                + facade
                + ".AreDictionaryEqual<"
                + keyType
                + ", "
                + valueType
                + ">(baseState.Value, currentState.Value)) { result.__whole = local.__whole; return new "
                + resultType
                + "(result, conflicts); }"
        );
        code.AppendLine(
            "if (desired.IsPresent == currentState.IsPresent && "
                + facade
                + ".AreDictionaryEqual<"
                + keyType
                + ", "
                + valueType
                + ">(desired.Value, currentState.Value)) return "
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
                + "Optional<object?>.Missing, \"The whole dictionary conflicts with a concurrent change.\"));"
        );
        code.AppendLine("return new " + resultType + "(result, conflicts);");
        code.CloseBlock();
        code.AppendLine(
            "if ("
                + facade
                + ".AreDictionaryEqual<"
                + keyType
                + ", "
                + valueType
                + ">(baseState.Value, currentState.Value)) { result.__set = local.__set is null ? null : new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(local.__set, "
                + comparer
                + "); result.__removed = local.__removed is null ? null : new global::System.Collections.Generic.List<"
                + keyType
                + ">(local.__removed); result.__edited = local.__edited is null ? null : new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + editedValueType
                + ">(local.__edited, "
                + comparer
                + "); return new "
                + resultType
                + "(result, conflicts); }"
        );
        code.AppendLine(
            "if (desired.IsPresent == currentState.IsPresent && "
                + facade
                + ".AreDictionaryEqual<"
                + keyType
                + ", "
                + valueType
                + ">(desired.Value, currentState.Value)) return "
                + resultType
                + ".Success(new "
                + patchName
                + "());"
        );
        // Per-key three-way (without nested rebase composition for brevity:
        // nested edits rebase recursively when both edited).
        EmitRebaseDictionarySnapshot(code, "baseState", "baseDict", keyType, valueType, comparer);
        EmitRebaseDictionarySnapshot(
            code,
            "currentState",
            "currentDict",
            keyType,
            valueType,
            comparer
        );
        EmitRebaseDictionarySnapshot(code, "desired", "desiredDict", keyType, valueType, comparer);
        // Pre-index locally removed keys once (O(K)) so per-key touch checks
        // Use comparer-aware maps instead of repeated linear scans.
        code.AppendLine(
            "var __dictRemoved = (local.__removed is null) ? new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ") : new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(local.__removed, "
                + comparer
                + ");"
        );
        code.AppendLine(
            "var __pendingSeen = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLine(
            "var __pendingKeys = new global::System.Collections.Generic.List<"
                + keyType
                + ">((local.__set is null ? 0 : local.__set.Count) + (local.__edited is null ? 0 : local.__edited.Count) + __dictRemoved.Count);"
        );
        code.AppendLine(
            "if (local.__set is not null) foreach (var k in local.__set.Keys) if (__pendingSeen.Add(k)) __pendingKeys.Add(k);"
        );
        code.AppendLine(
            "if (local.__edited is not null) foreach (var k in local.__edited.Keys) if (__pendingSeen.Add(k)) __pendingKeys.Add(k);"
        );
        code.AppendLine(
            "if (local.__removed is not null) foreach (var k in local.__removed) if (__pendingSeen.Add(k)) __pendingKeys.Add(k);"
        );
        if (hasPatch)
        {
            var valuePatch = SparseKeyedCollectionEmitter.ValuePatchType(member);
            var valueFragment = SparseKeyedCollectionEmitter.ValueFragmentType(member);
            var prefix = SparseKeyedCollectionEmitter.ValuePatchPrefix(member);
            // Drive the per-key phase from the ordered pending operations and
            // probe each map directly: a single pending key never scans full
            // maps. Conflicts follow operation order (Set, Edit, Remove) with
            // the local key representative; keys absent from every map keep
            // the previous skip behavior.
            code.AppendLine("foreach (var k in __pendingKeys)");
            code.OpenBlock();
            code.AppendLine(
                "var inBase = baseDict.TryGetValue(k, out var b); var inCurrent = currentDict.TryGetValue(k, out var c); var inDesired = desiredDict.TryGetValue(k, out var d);"
            );
            code.AppendLine("if (!inBase && !inCurrent && !inDesired) continue;");
            code.AppendLine(
                "var touchesSet = local.__set is not null && local.__set.ContainsKey(k); var touchesRemoved = __dictRemoved.Contains(k); var touchesEdited = local.__edited is not null && local.__edited.ContainsKey(k);"
            );
            code.AppendLine(
                "bool baseEqCurrent = inBase == inCurrent && (!inBase || "
                    + facade
                    + ".AreEqual<"
                    + valueType
                    + ">(b, c));"
            );
            code.AppendLine(
                "bool desiredEqCurrent = inDesired == inCurrent && (!inDesired || "
                    + facade
                    + ".AreEqual<"
                    + valueType
                    + ">(d, c));"
            );
            code.AppendLine(
                "if (baseEqCurrent) { if (touchesSet) (result.__set ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + "))[k] = local.__set![k]; else if (touchesRemoved) (result.__removed ??= new global::System.Collections.Generic.List<"
                    + keyType
                    + ">()).Add(k); else (result.__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valuePatch
                    + ">("
                    + comparer
                    + "))[k] = local.__edited![k]; continue; }"
            );
            code.AppendLine("if (desiredEqCurrent) continue;");
            code.AppendLine("if (touchesEdited && inBase && inCurrent && inDesired)");
            code.OpenBlock();
            code.AppendLine(
                "var nested = "
                    + valuePatch
                    + "."
                    + prefix
                    + "Rebase("
                    + runtime
                    + "Optional<"
                    + valueFragment
                    + "?>.Present("
                    + valueFragment
                    + ".From(b!)), local.__edited![k], "
                    + runtime
                    + "Optional<"
                    + valueFragment
                    + "?>.Present("
                    + valueFragment
                    + ".From(c!)));"
            );
            code.AppendLine(
                "if (!nested.HasConflicts && !nested.Rebased.__SparseIsEmpty()) (result.__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valuePatch
                    + ">("
                    + comparer
                    + "))[k] = nested.Rebased; else foreach (var nc in nested.Conflicts) conflicts.Add(nc.WithKeyPrefix(k));"
            );
            code.CloseBlock();
            code.AppendLine(
                "else conflicts.Add(new "
                    + conflictType
                    + "(__SparseRootPath.Key(k), "
                    + conflictKindType
                    + ".Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)b), "
                    + runtime
                    + "Optional<object?>.Present((object?)d), "
                    + runtime
                    + "Optional<object?>.Present((object?)c), \"The dictionary entry conflicts with a concurrent change.\"));"
            );
            code.CloseBlock();
        }
        else
        {
            // Same operation-driven traversal as the nested-edit branch above.
            code.AppendLine("foreach (var k in __pendingKeys)");
            code.OpenBlock();
            code.AppendLine(
                "var inBase = baseDict.TryGetValue(k, out var b); var inCurrent = currentDict.TryGetValue(k, out var c); var inDesired = desiredDict.TryGetValue(k, out var d);"
            );
            code.AppendLine("if (!inBase && !inCurrent && !inDesired) continue;");
            code.AppendLine(
                "bool baseEqCurrent = inBase == inCurrent && (!inBase || "
                    + facade
                    + ".AreEqual<"
                    + valueType
                    + ">(b, c));"
            );
            code.AppendLine(
                "bool desiredEqCurrent = inDesired == inCurrent && (!inDesired || "
                    + facade
                    + ".AreEqual<"
                    + valueType
                    + ">(d, c));"
            );
            code.AppendLine(
                "if (baseEqCurrent) { if (local.__set is not null && local.__set.TryGetValue(k, out var sv)) (result.__set ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + "))[k] = sv; else if (__dictRemoved.Contains(k)) (result.__removed ??= new global::System.Collections.Generic.List<"
                    + keyType
                    + ">()).Add(k); else if (local.__edited is not null && local.__edited.TryGetValue(k, out var ev)) (result.__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + "))[k] = ev; continue; }"
            );
            code.AppendLine(
                "if (!desiredEqCurrent) conflicts.Add(new "
                    + conflictType
                    + "(__SparseRootPath.Key(k), "
                    + conflictKindType
                    + ".Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)b), "
                    + runtime
                    + "Optional<object?>.Present((object?)d), "
                    + runtime
                    + "Optional<object?>.Present((object?)c), \"The dictionary entry conflicts with a concurrent change.\"));"
            );
            code.CloseBlock();
        }

        code.AppendLine("return new " + resultType + "(result, conflicts);");
        code.CloseBlock();
    }

    internal static void EmitRebaseDictionarySnapshot(
        SharedIndentedBuilder code,
        string state,
        string variable,
        string keyType,
        string valueType,
        string comparer
    )
    {
        var dictionaryType =
            $"global::System.Collections.Generic.Dictionary<{keyType}, {valueType}>";
        var interfaceType =
            $"global::System.Collections.Generic.IDictionary<{keyType}, {valueType}>";
        var direct = "__" + variable + "Direct";
        // These indexes are read-only. Keep fallback assignment semantics for
        // custom comparers.
        code.AppendLine(
            $"var {variable} = {state}.IsPresent && {state}.Value is {dictionaryType} {direct} && global::System.Object.Equals({direct}.Comparer, {comparer}) ? {direct} : null;"
        );
        code.AppendLine($"if ({variable} is null)");
        code.OpenBlock();
        var source = "__" + variable + "Source";
        code.AppendLine(
            $"var {source} = {state}.IsPresent ? ({interfaceType}?){state}.Value : null;"
        );
        code.AppendLine($"{variable} = new {dictionaryType}({source}?.Count ?? 0, {comparer});");
        code.AppendLine(
            $"if ({source} is not null) foreach (var kv in {source}) {variable}[kv.Key] = kv.Value;"
        );
        code.CloseBlock();
    }
}
