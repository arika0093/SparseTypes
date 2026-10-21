namespace SparseTypes.Generator.Shared;

/// <summary>Emits keyed-sequence Apply and Between algebra.</summary>
internal static class SparseKeyedSequenceApplyEmitter
{
    /// <summary>Aliases nested keyed-patch fields by reference for relocated bodies.</summary>
    internal static void AppendKeyedFieldAliases(SharedIndentedBuilder code, SparseMemberModel? member = null)
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

    internal static void EmitKeyedApply(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string elementType,
        string keyType,
        string listType,
        bool hasPatch,
        string comparer,
        string runtime,
        SparseMemberOperationSplit? split = null
    )
    {
        var optionalList = runtime + "Optional<" + listType + ">";
        var kind = runtime + "FragmentOperationKind";
        var patchName = SparseKeyedCollectionEmitter.CollectionPatchName(member);
        if (split is not null)
        {
            split.Shell.AppendLine("/// <summary>Applies this keyed patch to a member value.</summary>");
            split.Shell.AppendLine(
                "public " + optionalList + " Apply(" + optionalList + " current) => " + split.OperationsType + ".Apply(this, current);"
            );
            code.AppendLine("/// <summary>Applies a keyed patch to a member value.</summary>");
            code.AppendLine("internal static " + optionalList + " Apply(" + patchName + " self, " + optionalList + " current)");
        }
        else
        {
            code.AppendLine("public " + optionalList + " Apply(" + optionalList + " current)");
        }
        code.OpenBlock();
        if (split is not null)
        {
            AppendKeyedFieldAliases(code, member);
        }
        var hasTempApply = SparseKeyedCollectionEmitter.HasTemporaryKey(member);
        var keyOfApply = SparseKeyedCollectionEmitter.KeyOfMethod(member);
        if (hasTempApply)
        {
            SparseKeyedSequenceTempBetweenEmitter.AppendApplyBaselineValidation(code, member, keyOfApply);
        }
        else if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine(
                "if (current.IsPresent && (object?)current.Value is not null) foreach (var __baselineItem in current.Value!) if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                        member,
                        SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(__baselineItem)"
                    )
                    + ") throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed collection operation.\");"
            );
        }
        code.AppendLine("if (__whole.Kind != " + kind + ".Keep) return __whole.Apply(current);");
        code.AppendLine("if (" + (split is null ? string.Empty : "self.") + "IsEmpty) return current;");
        code.AppendLine(
            "if (!current.IsPresent || (object?)current.Value is null) throw new global::System.InvalidOperationException(\"Cannot apply granular collection operations to a missing collection.\");"
        );
        code.AppendLine("var source = current.Value!;");
        code.AppendLine("var map = new global::System.Collections.Generic.Dictionary<" + keyType + ", " + elementType + ">(" + comparer + ");");
        code.AppendLine("foreach (var item in source)");
        code.OpenBlock();
        if (hasTempApply)
        {
            SparseKeyedSequenceTempBetweenEmitter.AppendApplyBaselineMap(code, member, keyOfApply);
        }
        else
        {
            code.AppendLine("var k = " + SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(item);");
            if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
            {
                code.AppendLine(
                    "if ("
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                        + ") throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed collection operation.\");"
                );
            }
            code.AppendLine(SparseKeyedCollectionEmitter.AddUniqueEntry("map", "k", "item"));
        }
        code.CloseBlock();
        // Removals.
        if (hasTempApply)
        {
            SparseKeyedSequenceTempBetweenEmitter.AppendApplyAssignedRemovals(code, member);
        }
        else
        {
            code.AppendLine("if (__removed is not null) foreach (var k in __removed) map.Remove(k);");
        }

        if (hasTempApply)
        {
            SparseKeyedSequenceTempBetweenEmitter.AppendApplyTempRemovals(code, member);
        }
        // Edits.
        if (hasPatch)
        {
            var elementFragment = SparseKeyedCollectionEmitter.ElementFragmentType(member);
            code.AppendLine("if (__edited is not null) foreach (var kv in __edited)");
            code.OpenBlock();
            code.AppendLine(
                "if (!map.TryGetValue(kv.Key, out var existing)) throw new global::System.InvalidOperationException(\"Cannot edit a missing element.\");"
            );
            code.AppendLine(
                "var applied = kv.Value.Apply(" + runtime + "Optional<" + elementFragment + "?>.Present(" + elementFragment + ".From(existing)));"
            );
            code.AppendLine(
                "if (!applied.IsPresent || applied.Value is null) throw new global::System.InvalidOperationException(\"Element edit removed the element. Use Remove instead.\");"
            );
            code.AppendLine("var updated = applied.Value!.ToModel();");
            code.AppendLine(
                "if (!"
                    + comparer
                    + ".Equals("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(updated), kv.Key)) throw new global::System.InvalidOperationException(\"Changing an element's identity through an edit is not allowed. Use remove-old + add-new instead.\");"
            );
            code.AppendLine("map[kv.Key] = updated;");
            if (hasTempApply)
            {
                SparseKeyedSequenceTempBetweenEmitter.AppendApplyAssignedEdit(code, member, "updated");
            }

            code.CloseBlock();
        }
        else
        {
            code.AppendLine("if (__edited is not null) foreach (var kv in __edited)");
            code.OpenBlock();
            code.AppendLine(
                "if (!map.TryGetValue(kv.Key, out var existing)) throw new global::System.InvalidOperationException(\"Cannot update a missing element.\");"
            );
            code.AppendLine(
                "if (!"
                    + comparer
                    + ".Equals("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(kv.Value), kv.Key)) throw new global::System.InvalidOperationException(\"Changing an element's identity through an update is not allowed. Use remove-old + add-new instead.\");"
            );
            code.AppendLine("map[kv.Key] = kv.Value;");
            if (hasTempApply)
            {
                SparseKeyedSequenceTempBetweenEmitter.AppendApplyAssignedEdit(code, member, "kv.Value");
            }

            code.CloseBlock();
        }
        if (hasTempApply)
        {
            SparseKeyedSequenceTempBetweenEmitter.AppendApplyTempEdits(code, member, hasPatch, runtime);
        }

        // Adds.
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member) || SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            code.AppendLine("var __unassignedAdded = new global::System.Collections.Generic.List<" + elementType + ">();");
        }

        code.AppendLine("if (__added is not null) foreach (var item in __added)");
        code.OpenBlock();
        code.AppendLine("var k = " + SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(item);");
        if (hasTempApply)
        {
            // Temporary adds validate against the baseline identities;
            // temporary-less unassigned adds keep positional legacy flow.
            code.AppendLine(
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                    + ") { var __addTemp = "
                    + SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member)
                    + "(item); if (__addTemp.HasValue && __addTemp.Value != global::System.Guid.Empty) { if (__baselineTemp"
                    + member.Id
                    + ".ContainsKey(__addTemp.Value)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection.\"); foreach (var __pending in __unassignedAdded) if ("
                    + SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member)
                    + "(__pending) == __addTemp.Value) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection.\"); } __unassignedAdded.Add(item); continue; }"
            );
        }
        else if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine(
                "if (" + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k") + ") { __unassignedAdded.Add(item); continue; }"
            );
        }
        code.AppendLine("if (map.ContainsKey(k)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection.\");");
        code.AppendLine("map[k] = item;");
        code.CloseBlock();
        code.AppendLine("global::System.Collections.Generic.List<" + elementType + "> result;");
        code.AppendLine("if (__order is not null)");
        code.OpenBlock();
        code.AppendLine(
            "if (__order.Count != map.Count"
                + (SparseKeyedCollectionEmitter.HasUnassignedKey(member) ? " + __unassignedAdded.Count" : "")
                + (
                    SparseKeyedCollectionEmitter.HasTemporaryKey(member)
                        ? " + __baselineTemp" + member.Id + ".Count - __baselineTempEcho" + member.Id
                        : ""
                )
                + ") throw new global::System.InvalidOperationException(\"Order must list exactly the final keys.\");"
        );
        code.AppendLine("result = new global::System.Collections.Generic.List<" + elementType + ">(map.Count);");
        if (hasTempApply)
        {
            SparseKeyedSequenceTempBetweenEmitter.AppendApplyOrderConsumption(code, member, elementType);
        }
        else
        {
            if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
            {
                code.AppendLine("var __unassignedIndex = 0;");
            }

            code.AppendLine("foreach (var k in __order)");
            code.OpenBlock();
            if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
            {
                code.AppendLine(
                    "if ("
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                        + ") { if (__unassignedIndex >= __unassignedAdded.Count) throw new global::System.InvalidOperationException(\"Order contains too many unassigned-key entries.\"); result.Add(__unassignedAdded[__unassignedIndex++]); continue; }"
                );
            }
            code.AppendLine(
                "if (!map.TryGetValue(k, out var item)) throw new global::System.InvalidOperationException(\"Order lists an unknown key.\");"
            );
            code.AppendLine("result.Add(item);");
            code.CloseBlock();
        }
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        // map.Count is the exact result capacity. Check added keys with a
        // comparer-aware set to avoid repeated linear scans.
        code.AppendLine("result = new global::System.Collections.Generic.List<" + elementType + ">(map.Count);");
        code.AppendLine("foreach (var item in source)");
        code.OpenBlock();
        code.AppendLine("var k = " + SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(item);");
        if (hasTempApply)
        {
            // Temporary baselines survive through their own map.
            code.AppendLine(
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                    + ") { var __st = "
                    + SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member)
                    + "(item); if (__st.HasValue && __baselineTemp"
                    + member.Id
                    + ".TryGetValue(__st.Value, out var __scurrent)) result.Add(__scurrent); }"
            );
            code.AppendLine(
                "else { var __ste = "
                    + SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member)
                    + "(item); if (__ste.HasValue && __ste.Value != global::System.Guid.Empty && __baselineTemp"
                    + member.Id
                    + ".TryGetValue(__ste.Value, out var __tecurrent)) result.Add(__tecurrent);"
            );
            code.AppendLine("else if (map.TryGetValue(k, out var current2)) result.Add(current2); }");
        }
        else
        {
            code.AppendLine("if (map.TryGetValue(k, out var current2)) result.Add(current2);");
        }

        code.CloseBlock();
        code.AppendLine("if (__added is not null)");
        code.OpenBlock();
        code.AppendLine("var __emitted = new global::System.Collections.Generic.HashSet<" + keyType + ">(" + comparer + ");");
        code.AppendLine("foreach (var __placed in result) __emitted.Add(" + SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(__placed));");
        code.AppendLine(
            "foreach (var item in __added) { var k = "
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                + "(item); "
                + (
                    SparseKeyedCollectionEmitter.HasUnassignedKey(member)
                        ? "if (" + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k") + ") continue; "
                        : ""
                )
                + "if (__emitted.Add(k)) result.Add(map[k]); }"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine("result.AddRange(__unassignedAdded);");
        }

        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("return " + SparseKeyedCollectionEmitter.MaterializeSequence(member, "result", runtime) + ";");
        code.CloseBlock();
    }

    internal static void EmitKeyedBetween(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string patchName,
        string elementType,
        string keyType,
        string listType,
        bool hasPatch,
        string comparer,
        string facade,
        string runtime,
        SparseMemberOperationSplit? split = null
    )
    {
        var optionalList = runtime + "Optional<" + listType + ">";
        var operation = runtime + "FragmentOperation<" + listType + ">";
        if (split is not null)
        {
            split.Shell.AppendLine("/// <summary>Derives a keyed patch between two member values.</summary>");
            split.Shell.AppendLine(
                "public static "
                    + patchName
                    + " Between("
                    + optionalList
                    + " before, "
                    + optionalList
                    + " after) => "
                    + split.OperationsType
                    + ".Between(before, after);"
            );
            code.AppendLine("/// <summary>Derives a keyed patch between two member values.</summary>");
        }
        code.AppendLine(
            (split is null ? "public static " : "internal static ") + patchName + " Between(" + optionalList + " before, " + optionalList + " after)"
        );
        code.OpenBlock();
        code.AppendLine("var patch = new " + patchName + "();");
        var hasTempBetween = SparseKeyedCollectionEmitter.HasTemporaryKey(member);
        if (hasTempBetween)
        {
            SparseKeyedSequenceTempBetweenEmitter.AppendBetweenBaselineValidation(code, member);
        }
        else if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine(
                "if (before.IsPresent && (object?)before.Value is not null) foreach (var __baselineItem in before.Value!) if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                        member,
                        SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(__baselineItem)"
                    )
                    + ") throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed collection operation.\");"
            );
        }
        code.AppendLine("if (before.IsPresent != after.IsPresent)");
        code.OpenBlock();
        code.AppendLine("patch.__whole = after.IsPresent ? " + operation + ".Set(after.Value) : " + operation + ".Remove;");
        code.AppendLine("return patch;");
        code.CloseBlock();
        code.AppendLine("if (!before.IsPresent) return patch;");
        code.AppendLine("if (global::System.Object.ReferenceEquals(before.Value, after.Value)) return patch;");
        code.AppendLine("if ((object?)before.Value is null || (object?)after.Value is null)");
        code.OpenBlock();
        code.AppendLine(
            "if (!" + facade + ".AreEqual((object?)before.Value, (object?)after.Value)) patch.__whole = " + operation + ".Set(after.Value);"
        );
        code.AppendLine("return patch;");
        code.CloseBlock();
        code.AppendLine("if (" + facade + ".AreEqual((object?)before.Value, (object?)after.Value)) return patch;");
        // Build each key map and order list in one pass, reusing afterMap for
        // additions while preserving duplicate and comparer validation.
        code.AppendLine(
            "var __beforeCapacity = before.GetValueOrDefault() is global::System.Collections.Generic.ICollection<"
                + elementType
                + "> __beforeCollection ? __beforeCollection.Count : (before.GetValueOrDefault() is global::System.Collections.Generic.IReadOnlyCollection<"
                + elementType
                + "> __beforeReadOnly ? __beforeReadOnly.Count : 0);"
        );
        code.AppendLine(
            "var beforeMap = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">(__beforeCapacity, "
                + comparer
                + ");"
        );
        code.AppendLine("var beforeOrder = new global::System.Collections.Generic.List<" + keyType + ">(__beforeCapacity);");
        code.AppendLine("foreach (var item in before.Value!)");
        code.OpenBlock();
        code.AppendLine("var k = " + SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(item);");
        if (hasTempBetween)
        {
            // Temporary baselines correlate by Guid; assigned entries keep the
            // key map.
            code.AppendLine("if (" + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k") + ") continue;");
        }
        else if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine(
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                    + ") throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed collection operation.\");"
            );
        }
        code.AppendLine(SparseKeyedCollectionEmitter.AddUniqueEntry("beforeMap", "k", "item"));
        code.AppendLine("beforeOrder.Add(k);");
        code.CloseBlock();
        code.AppendLine(
            "var __afterCapacity = after.GetValueOrDefault() is global::System.Collections.Generic.ICollection<"
                + elementType
                + "> __afterCollection ? __afterCollection.Count : (after.GetValueOrDefault() is global::System.Collections.Generic.IReadOnlyCollection<"
                + elementType
                + "> __afterReadOnly ? __afterReadOnly.Count : 0);"
        );
        code.AppendLine(
            "var afterMap = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">(__afterCapacity, "
                + comparer
                + ");"
        );
        code.AppendLine("var afterOrder = new global::System.Collections.Generic.List<" + keyType + ">(__afterCapacity);");
        code.AppendLine("var unassignedAfter = new global::System.Collections.Generic.List<" + elementType + ">();");
        if (hasTempBetween)
        {
            code.AppendLine(
                "var __afterTemp" + member.Id + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, " + elementType + ">();"
            );
            code.AppendLine("var __orderTemp = new global::System.Collections.Generic.List<global::System.Guid?>();");
        }
        code.AppendLine("foreach (var item in after.Value!)");
        code.OpenBlock();
        code.AppendLine("var k = " + SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(item);");
        if (hasTempBetween)
        {
            SparseKeyedSequenceTempBetweenEmitter.AppendBetweenAfterPartition(code, member);
        }
        else if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine(
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                    + ") { afterOrder.Add(k!); unassignedAfter.Add(item); continue; }"
            );
        }
        code.AppendLine(SparseKeyedCollectionEmitter.AddUniqueEntry("afterMap", "k", "item"));
        code.AppendLine("afterOrder.Add(k);");
        if (hasTempBetween)
        {
            code.AppendLine("__orderTemp.Add(null);");
        }
        code.CloseBlock();
        code.AppendLine("var removed = new global::System.Collections.Generic.List<" + keyType + ">(beforeMap.Count);");
        code.AppendLine("foreach (var k in beforeMap.Keys) if (!afterMap.ContainsKey(k)) removed.Add(k);");
        // Added entries are derived from afterOrder/afterMap (after-order, no
        // key recomputation, no second enumeration of after.Value).
        code.AppendLine(
            "var added = new global::System.Collections.Generic.List<"
                + elementType
                + ">(global::System.Math.Max(0, afterMap.Count - beforeMap.Count + removed.Count));"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine("var unassignedIndex = 0;");
        }

        code.AppendLine("foreach (var k in afterOrder)");
        code.OpenBlock();
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLine(
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                    + ") { added.Add(unassignedAfter[unassignedIndex++]); continue; }"
            );
        }
        code.AppendLine("if (!beforeMap.ContainsKey(k)) added.Add(afterMap[k]);");
        code.CloseBlock();
        if (hasPatch)
        {
            var elementFragment = SparseKeyedCollectionEmitter.ElementFragmentType(member);
            var elementPatch = SparseKeyedCollectionEmitter.ElementPatchType(member);
            var prefix = SparseKeyedCollectionEmitter.ElementPatchPrefix(member);
            code.AppendLine("global::System.Collections.Generic.Dictionary<" + keyType + ", " + elementPatch + ">? edited = null;");
            code.AppendLine("foreach (var k in beforeMap.Keys)");
            code.OpenBlock();
            code.AppendLine("if (!afterMap.TryGetValue(k, out var afterItem)) continue;");
            code.AppendLine("var beforeItem = beforeMap[k];");
            code.AppendLine(
                "var nested = "
                    + elementPatch
                    + "."
                    + prefix
                    + "Between("
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(beforeItem)), "
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(afterItem)));"
            );
            code.AppendLine(
                "if (!nested.__SparseIsEmpty()) (edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementPatch
                    + ">("
                    + comparer
                    + "))[k] = nested;"
            );
            code.CloseBlock();
        }
        else
        {
            code.AppendLine("global::System.Collections.Generic.Dictionary<" + keyType + ", " + elementType + ">? edited = null;");
            code.AppendLine("foreach (var k in beforeMap.Keys)");
            code.OpenBlock();
            code.AppendLine("if (!afterMap.TryGetValue(k, out var afterItem)) continue;");
            code.AppendLine(
                "if (!"
                    + facade
                    + ".AreEqual((object?)beforeMap[k], (object?)afterItem)) (edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementType
                    + ">("
                    + comparer
                    + "))[k] = afterItem;"
            );
            code.CloseBlock();
        }

        if (hasTempBetween)
        {
            SparseKeyedSequenceTempBetweenEmitter.AppendBetweenTempChanges(code, member, elementType, hasPatch, facade, runtime);
        }

        code.AppendLine(
            "if (removed.Count == 0 && added.Count == 0 && (edited is null || edited.Count == 0)"
                + (hasTempBetween ? " && patch.__removedTemp is null && patch.__editedTemp is null" : "")
                + ")"
        );
        code.OpenBlock();
        // Order-only change?
        code.AppendLine("if (" + facade + ".KeyOrderEquals<" + keyType + ">(beforeOrder, afterOrder)) return patch;");
        code.AppendLine("patch.__order = afterOrder;");
        if (hasTempBetween)
        {
            code.AppendLine("patch.__orderTemp = __orderTemp;");
        }
        code.AppendLine("return patch;");
        code.CloseBlock();
        code.AppendLine("if (removed.Count > 0) patch.__removed = removed;");
        code.AppendLine("if (added.Count > 0) patch.__added = added;");
        code.AppendLine("if (edited is not null && edited.Count > 0) patch.__edited = edited;");
        code.AppendLine("if (!" + facade + ".KeyOrderEquals<" + keyType + ">(beforeOrder, afterOrder)) patch.__order = afterOrder;");
        if (hasTempBetween)
        {
            code.AppendLine("if (!" + facade + ".KeyOrderEquals<" + keyType + ">(beforeOrder, afterOrder)) patch.__orderTemp = __orderTemp;");
        }
        code.AppendLine("return patch;");
        code.CloseBlock();
    }
}
