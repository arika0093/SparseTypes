namespace SparseTypes.Generator.Shared;

/// <summary>Authoritative order and membership staging for keyed reconcile.</summary>
/// <remarks>
/// Builds the persisted-order target list and rebases post-submit
/// insertions, deletions and reorderings by stable identity.
/// </remarks>
internal static class SparseTemporaryKeyOrderEmitter
{
    internal static void EmitBuildKeyLookup(
        SharedIndentedBuilder code,
        string list,
        string lookup,
        string hasNullKey,
        string nullKeyValue,
        string elementType,
        string keyType,
        string keyProp,
        bool keyIsReferenceType,
        bool elementRef,
        bool elementNullableStruct
    )
    {
        var nullableValueKey = keyType.EndsWith("?", StringComparison.Ordinal);
        var nullableKey = nullableValueKey || keyIsReferenceType;
        var lookupKeyType = nullableValueKey ? keyType.Substring(0, keyType.Length - 1) : keyType;
        code.AppendLine(
            "var "
                + lookup
                + " = new global::System.Collections.Generic.Dictionary<"
                + lookupKeyType
                + ", "
                + elementType
                + ">(global::System.Collections.Generic.EqualityComparer<"
                + lookupKeyType
                + ">.Default);"
        );
        if (nullableKey)
        {
            code.AppendLine("var " + hasNullKey + " = false;");
            code.AppendLine(
                elementType + " " + nullKeyValue + " = default" + (elementRef ? "!" : "") + ";"
            );
        }
        code.AppendLine("if (" + list + " is not null)");
        code.OpenBlock();
        code.AppendLine("foreach (var __idxRaw in " + list + ")");
        code.OpenBlock();
        var candidate = "__idxRaw";
        if (elementRef)
        {
            code.AppendLine("if (__idxRaw is null) continue;");
        }
        else if (elementNullableStruct)
        {
            code.AppendLine("if (!__idxRaw.HasValue) continue;");
            code.AppendLine("var __idx = __idxRaw.Value;");
            candidate = "__idx";
        }
        code.AppendLine("var __idxKey = " + candidate + "." + keyProp + ";");
        if (nullableValueKey)
        {
            code.AppendLine("if (!__idxKey.HasValue)");
            code.OpenBlock();
            code.AppendLine(
                "if (!"
                    + hasNullKey
                    + ") { "
                    + hasNullKey
                    + " = true; "
                    + nullKeyValue
                    + " = "
                    + candidate
                    + "; }"
            );
            code.CloseBlock();
            code.AppendLine(
                "else if (!"
                    + lookup
                    + ".ContainsKey(__idxKey.Value)) "
                    + lookup
                    + ".Add(__idxKey.Value, "
                    + candidate
                    + ");"
            );
        }
        else if (keyIsReferenceType)
        {
            code.AppendLine("if (__idxKey is null)");
            code.OpenBlock();
            code.AppendLine(
                "if (!"
                    + hasNullKey
                    + ") { "
                    + hasNullKey
                    + " = true; "
                    + nullKeyValue
                    + " = "
                    + candidate
                    + "; }"
            );
            code.CloseBlock();
            code.AppendLine(
                "else if (!"
                    + lookup
                    + ".ContainsKey(__idxKey)) "
                    + lookup
                    + ".Add(__idxKey, "
                    + candidate
                    + ");"
            );
        }
        else
        {
            code.AppendLine(
                "if (!"
                    + lookup
                    + ".ContainsKey(__idxKey)) "
                    + lookup
                    + ".Add(__idxKey, "
                    + candidate
                    + ");"
            );
        }
        code.CloseBlock();
        code.CloseBlock();
    }

    internal static void EmitCollectIdentities(
        SharedIndentedBuilder code,
        string list,
        string keys,
        string temps,
        string elementType,
        string keyType,
        string keyProp,
        string tempProp,
        string unassigned,
        bool elementRef
    )
    {
        _ = elementType;
        _ = keyType;
        var loopVar = "__c";
        code.AppendLine("foreach (var " + loopVar + "Raw in " + list + ")");
        code.OpenBlock();
        if (elementRef)
        {
            code.AppendLine("var " + loopVar + " = " + loopVar + "Raw;");
            code.AppendLine("if (" + loopVar + " is null) continue;");
        }
        else
        {
            code.AppendLine("var " + loopVar + " = " + loopVar + "Raw;");
        }
        code.AppendLine("var __ck = " + loopVar + "." + keyProp + ";");
        code.AppendLine(
            "if (global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default.Equals(__ck, "
                + unassigned
                + ")) { var __ct = "
                + loopVar
                + "."
                + tempProp
                + "; if (__ct.HasValue && __ct.Value != global::System.Guid.Empty) "
                + temps
                + ".Add(__ct.Value); }"
        );
        code.AppendLine("else " + keys + ".Add(__ck);");
        code.CloseBlock();
    }

    internal static void EmitSurvivingOrder(
        SharedIndentedBuilder code,
        string list,
        string subKeys,
        string subTemps,
        string output,
        string elementType,
        string keyType,
        string keyProp,
        string tempProp,
        string unassigned,
        bool elementRef
    )
    {
        var loopVar = "__v";
        code.AppendLine("foreach (var " + loopVar + "Raw in " + list + ")");
        code.OpenBlock();
        if (elementRef)
        {
            code.AppendLine("var " + loopVar + " = " + loopVar + "Raw;");
            code.AppendLine("if (" + loopVar + " is null) continue;");
        }
        else
        {
            code.AppendLine("var " + loopVar + " = " + loopVar + "Raw;");
        }
        code.AppendLine("var __vk = " + loopVar + "." + keyProp + ";");
        code.AppendLine(
            "if (global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default.Equals(__vk, "
                + unassigned
                + "))"
        );
        code.OpenBlock();
        code.AppendLine(
            "var __vt = "
                + loopVar
                + "."
                + tempProp
                + "; if (!__vt.HasValue || __vt.Value == global::System.Guid.Empty) continue; if (!"
                + subTemps
                + ".Contains(__vt.Value)) continue;"
        );
        code.AppendLine(
            "if (map.TryGetValue((__collectionScope, __vt.Value), out var __vpObj) && __vpObj is "
                + elementType
                + " __vp) "
                + output
                + ".Add(__vp."
                + keyProp
                + ");"
        );
        code.CloseBlock();
        code.AppendLine("else if (" + subKeys + ".Contains(__vk)) " + output + ".Add(__vk);");
        code.CloseBlock();
    }

    internal static void EmitSubmittedSurvivingOrder(
        SharedIndentedBuilder code,
        string list,
        string liveKeys,
        string liveTemps,
        string output,
        string elementType,
        string keyType,
        string keyProp,
        string tempProp,
        string unassigned,
        bool elementRef
    )
    {
        var loopVar = "__s";
        code.AppendLine("foreach (var " + loopVar + "Raw in " + list + ")");
        code.OpenBlock();
        if (elementRef)
        {
            code.AppendLine("var " + loopVar + " = " + loopVar + "Raw;");
            code.AppendLine("if (" + loopVar + " is null) continue;");
        }
        else
        {
            code.AppendLine("var " + loopVar + " = " + loopVar + "Raw;");
        }
        code.AppendLine("var __sk2 = " + loopVar + "." + keyProp + ";");
        code.AppendLine(
            "if (global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default.Equals(__sk2, "
                + unassigned
                + "))"
        );
        code.OpenBlock();
        code.AppendLine(
            "var __st2 = "
                + loopVar
                + "."
                + tempProp
                + "; if (!__st2.HasValue || __st2.Value == global::System.Guid.Empty) continue; if (!"
                + liveTemps
                + ".Contains(__st2.Value)) continue;"
        );
        code.AppendLine(
            "if (map.TryGetValue((__collectionScope, __st2.Value), out var __spObj) && __spObj is "
                + elementType
                + " __sp) "
                + output
                + ".Add(__sp."
                + keyProp
                + ");"
        );
        code.CloseBlock();
        code.AppendLine("else if (" + liveKeys + ".Contains(__sk2)) " + output + ".Add(__sk2);");
        code.CloseBlock();
    }

    internal static void EmitPersistedOrderFill(
        SharedIndentedBuilder code,
        string perList,
        string workingLookup,
        string workingHasNullKey,
        string workingNullKeyValue,
        string ordered,
        string subKeys,
        string subTemps,
        string elementType,
        string keyType,
        string keyProp,
        string tempProp,
        bool keyIsReferenceType,
        bool elementRef
    )
    {
        var loopVar = "__q";
        code.AppendLine("foreach (var " + loopVar + "Raw in " + perList + ")");
        code.OpenBlock();
        if (elementRef)
        {
            code.AppendLine("var " + loopVar + " = " + loopVar + "Raw;");
            code.AppendLine("if (" + loopVar + " is null) continue;");
        }
        else
        {
            code.AppendLine("var " + loopVar + " = " + loopVar + "Raw;");
        }
        code.AppendLine("var __qk = " + loopVar + "." + keyProp + ";");
        // Mapped temp persisted rows join by their assigned key when the
        // submitted temp survives.
        code.AppendLine("var __qt = " + loopVar + "." + tempProp + ";");
        code.AppendLine(
            "bool __isMappedTemp = __qt.HasValue && __qt.Value != global::System.Guid.Empty && "
                + subTemps
                + ".Contains(__qt.Value);"
        );
        code.AppendLine("bool __isSubmittedKey = " + subKeys + ".Contains(__qk);");
        code.AppendLine("if (__isMappedTemp || __isSubmittedKey)");
        code.OpenBlock();
        if (keyType.EndsWith("?", StringComparison.Ordinal))
        {
            code.AppendLine("if (!__qk.HasValue)");
            code.OpenBlock();
            code.AppendLine(
                "if (" + workingHasNullKey + ") " + ordered + ".Add(" + workingNullKeyValue + ");"
            );
            code.CloseBlock();
            code.AppendLine("else if (");
            code.AppendLine(
                workingLookup
                    + ".TryGetValue(__qk.Value, out var __hit)) "
                    + ordered
                    + ".Add(__hit!);"
            );
        }
        else if (keyIsReferenceType)
        {
            code.AppendLine("if (__qk is null)");
            code.OpenBlock();
            code.AppendLine(
                "if (" + workingHasNullKey + ") " + ordered + ".Add(" + workingNullKeyValue + ");"
            );
            code.CloseBlock();
            code.AppendLine("else if (");
            code.AppendLine(
                workingLookup + ".TryGetValue(__qk, out var __hit)) " + ordered + ".Add(__hit!);"
            );
        }
        else
        {
            code.AppendLine(
                "if ("
                    + workingLookup
                    + ".TryGetValue(__qk, out var __hit)) "
                    + ordered
                    + ".Add(__hit!);"
            );
        }
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine(
            ordered
                + ".Add("
                + elementType
                + ".Fragment.From("
                + loopVar
                + ").ToModel()); __dirty = true;"
        );
        code.CloseBlock();
        code.CloseBlock();
    }

    internal static void EmitPostAddInsert(
        SharedIndentedBuilder code,
        string liveList,
        string ordered,
        string subKeys,
        string subTemps,
        string elementType,
        string keyType,
        string keyProp,
        string tempProp,
        string unassigned,
        bool keyIsReferenceType,
        bool elementRef
    )
    {
        _ = elementType;
        var nullableValueKey = keyType.EndsWith("?", StringComparison.Ordinal);
        var nullableKey = nullableValueKey || keyIsReferenceType;
        var lookupKeyType = nullableValueKey ? keyType.Substring(0, keyType.Length - 1) : keyType;
        code.AppendLine(
            "var __orderedIndex = new global::System.Collections.Generic.Dictionary<"
                + lookupKeyType
                + ", int>(global::System.Collections.Generic.EqualityComparer<"
                + lookupKeyType
                + ">.Default);"
        );
        if (nullableKey)
        {
            code.AppendLine("var __orderedNullIndex = -1;");
        }
        code.AppendLine("for (var __oi = 0; __oi < " + ordered + ".Count; __oi++)");
        code.OpenBlock();
        if (elementRef)
        {
            code.AppendLine("var __or = " + ordered + "[__oi];");
            code.AppendLine("if (__or is null) continue;");
        }
        else
        {
            code.AppendLine("var __or = " + ordered + "[__oi];");
        }
        code.AppendLine("var __ok = __or." + keyProp + ";");
        if (nullableValueKey)
        {
            code.AppendLine(
                "if (__ok.HasValue) { if (!__orderedIndex.ContainsKey(__ok.Value)) __orderedIndex.Add(__ok.Value, __oi); } else if (__orderedNullIndex < 0) __orderedNullIndex = __oi;"
            );
        }
        else if (keyIsReferenceType)
        {
            code.AppendLine(
                "if (__ok is null) { if (__orderedNullIndex < 0) __orderedNullIndex = __oi; } else if (!__orderedIndex.ContainsKey(__ok)) __orderedIndex.Add(__ok, __oi);"
            );
        }
        else
        {
            code.AppendLine(
                "if (!__orderedIndex.ContainsKey(__ok)) __orderedIndex.Add(__ok, __oi);"
            );
        }
        code.CloseBlock();
        code.AppendLine(
            "var __after = new global::System.Collections.Generic.List<"
                + elementType
                + ">?["
                + ordered
                + ".Count];"
        );
        code.AppendLine(
            "var __head = new global::System.Collections.Generic.List<" + elementType + ">();"
        );
        code.AppendLine(
            "var __postAddKeys = new global::System.Collections.Generic.HashSet<"
                + lookupKeyType
                + ">(global::System.Collections.Generic.EqualityComparer<"
                + lookupKeyType
                + ">.Default);"
        );
        code.AppendLine("var __predecessor = -1;");
        code.AppendLine("foreach (var __nRaw in " + liveList + ")");
        code.OpenBlock();
        if (elementRef)
        {
            code.AppendLine("var __n = __nRaw;");
            code.AppendLine("if (__n is null) continue;");
        }
        else
        {
            code.AppendLine("var __n = __nRaw;");
        }
        code.AppendLine("var __nk = __n." + keyProp + ";");
        code.AppendLine(
            "var __isUnassigned = global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default.Equals(__nk, "
                + unassigned
                + ");"
        );
        code.AppendLine("if (__isUnassigned)");
        code.OpenBlock();
        code.AppendLine("var __nt = __n." + tempProp + ";");
        code.AppendLine(
            "if (!__nt.HasValue || __nt.Value == global::System.Guid.Empty) return __Fail(\"The live model contains an unassigned element without a temporary identity.\");"
        );
        code.AppendLine("if (" + subTemps + ".Contains(__nt.Value))");
        code.OpenBlock();
        if (nullableValueKey)
        {
            code.AppendLine(
                "if (map.TryGetValue((__collectionScope, __nt.Value), out var __mapped) && __mapped is "
                    + elementType
                    + " __mappedElement) { var __mappedKey = __mappedElement."
                    + keyProp
                    + "; if (__mappedKey.HasValue) { if (__orderedIndex.TryGetValue(__mappedKey.Value, out var __mappedIndex)) __predecessor = __mappedIndex; } else if (__orderedNullIndex >= 0) __predecessor = __orderedNullIndex; }"
            );
        }
        else if (keyIsReferenceType)
        {
            code.AppendLine(
                "if (map.TryGetValue((__collectionScope, __nt.Value), out var __mapped) && __mapped is "
                    + elementType
                    + " __mappedElement) { var __mappedKey = __mappedElement."
                    + keyProp
                    + "; if (__mappedKey is null) { if (__orderedNullIndex >= 0) __predecessor = __orderedNullIndex; } else if (__orderedIndex.TryGetValue(__mappedKey, out var __mappedIndex)) __predecessor = __mappedIndex; }"
            );
        }
        else
        {
            code.AppendLine(
                "if (map.TryGetValue((__collectionScope, __nt.Value), out var __mapped) && __mapped is "
                    + elementType
                    + " __mappedElement && __orderedIndex.TryGetValue(__mappedElement."
                    + keyProp
                    + ", out var __mappedIndex)) __predecessor = __mappedIndex;"
            );
        }
        code.AppendLine("continue;");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("else if (" + subKeys + ".Contains(__nk))");
        code.OpenBlock();
        if (nullableValueKey)
        {
            code.AppendLine(
                "if (__nk.HasValue) { if (__orderedIndex.TryGetValue(__nk.Value, out var __submittedIndex)) __predecessor = __submittedIndex; } else if (__orderedNullIndex >= 0) __predecessor = __orderedNullIndex;"
            );
        }
        else if (keyIsReferenceType)
        {
            code.AppendLine(
                "if (__nk is null) { if (__orderedNullIndex >= 0) __predecessor = __orderedNullIndex; } else if (__orderedIndex.TryGetValue(__nk, out var __submittedIndex)) __predecessor = __submittedIndex;"
            );
        }
        else
        {
            code.AppendLine(
                "if (__orderedIndex.TryGetValue(__nk, out var __submittedIndex)) __predecessor = __submittedIndex;"
            );
        }
        code.AppendLine("continue;");
        code.CloseBlock();
        if (nullableValueKey)
        {
            code.AppendLine(
                "if (!__isUnassigned && ((!__nk.HasValue && __orderedNullIndex >= 0) || (__nk.HasValue && (__orderedIndex.ContainsKey(__nk.Value) || !__postAddKeys.Add(__nk.Value))))) return __Fail(\"Reconciling produced a duplicate key.\");"
            );
        }
        else if (keyIsReferenceType)
        {
            code.AppendLine(
                "if (!__isUnassigned && ((__nk is null && __orderedNullIndex >= 0) || (__nk is not null && (__orderedIndex.ContainsKey(__nk) || !__postAddKeys.Add(__nk))))) return __Fail(\"Reconciling produced a duplicate key.\");"
            );
        }
        else
        {
            code.AppendLine(
                "if (!__isUnassigned && (__orderedIndex.ContainsKey(__nk) || !__postAddKeys.Add(__nk))) return __Fail(\"Reconciling produced a duplicate key.\");"
            );
        }
        code.AppendLine("if (__predecessor == -1) __head.Add(__n);");
        code.AppendLine(
            "else { var __bucket = __after[__predecessor]; if (__bucket is null) __after[__predecessor] = __bucket = new global::System.Collections.Generic.List<"
                + elementType
                + ">(); __bucket.Add(__n); }"
        );
        code.AppendLine("__dirty = true;");
        code.CloseBlock();
        code.AppendLine(
            "var __flattened = new global::System.Collections.Generic.List<"
                + elementType
                + ">("
                + ordered
                + ".Count + __head.Count);"
        );
        code.AppendLine(
            "for (var __hi = __head.Count - 1; __hi >= 0; __hi--) __flattened.Add(__head[__hi]);"
        );
        code.AppendLine(
            "for (var __oi = 0; __oi < "
                + ordered
                + ".Count; __oi++) { __flattened.Add("
                + ordered
                + "[__oi]); var __bucket = __after[__oi]; if (__bucket is not null) for (var __bi = __bucket.Count - 1; __bi >= 0; __bi--) __flattened.Add(__bucket[__bi]); }"
        );
        code.AppendLine(ordered + ".Clear(); " + ordered + ".AddRange(__flattened);");
    }

    internal static void EmitServerAddedAppend(
        SharedIndentedBuilder code,
        string perList,
        string working,
        string subKeys,
        string subTemps,
        string elementType,
        string keyType,
        string keyProp,
        string tempProp,
        string unassigned,
        bool elementRef
    )
    {
        code.AppendLine(
            "var __existingKeys = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default);"
        );
        code.AppendLine(
            "var __existingTemps = new global::System.Collections.Generic.HashSet<global::System.Guid>();"
        );
        code.AppendLine("foreach (var __knownRaw in " + working + ")");
        code.OpenBlock();
        if (elementRef)
        {
            code.AppendLine("if (__knownRaw is null) continue;");
        }
        code.AppendLine("var __knownKey = __knownRaw." + keyProp + ";");
        code.AppendLine(
            "if (global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default.Equals(__knownKey, "
                + unassigned
                + ")) { var __knownTemp = __knownRaw."
                + tempProp
                + "; if (__knownTemp.HasValue && __knownTemp.Value != global::System.Guid.Empty) __existingTemps.Add(__knownTemp.Value); } else __existingKeys.Add(__knownKey);"
        );
        code.CloseBlock();
        var loopVar = "__w";
        code.AppendLine("foreach (var " + loopVar + "Raw in " + perList + ")");
        code.OpenBlock();
        if (elementRef)
        {
            code.AppendLine("var " + loopVar + " = " + loopVar + "Raw;");
            code.AppendLine("if (" + loopVar + " is null) continue;");
        }
        else
        {
            code.AppendLine("var " + loopVar + " = " + loopVar + "Raw;");
        }
        code.AppendLine("var __wk = " + loopVar + "." + keyProp + ";");
        code.AppendLine("var __wt = " + loopVar + "." + tempProp + ";");
        code.AppendLine(
            "if ((__wt.HasValue && __wt.Value != global::System.Guid.Empty && "
                + subTemps
                + ".Contains(__wt.Value)) || "
                + subKeys
                + ".Contains(__wk)) continue;"
        );
        code.AppendLine(
            "if (global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default.Equals(__wk, "
                + unassigned
                + ")) { if (__wt.HasValue && __wt.Value != global::System.Guid.Empty && !__existingTemps.Add(__wt.Value)) return __Fail(\"Reconciling produced a duplicate key.\"); } else if (!__existingKeys.Add(__wk)) continue;"
        );
        code.AppendLine(
            working
                + ".Add("
                + elementType
                + ".Fragment.From("
                + loopVar
                + ").ToModel()); __dirty = true;"
        );
        code.CloseBlock();
    }

    internal static void EmitDuplicateGuard(
        SharedIndentedBuilder code,
        string working,
        string seenKeys,
        string seenTemps,
        string elementType,
        string keyType,
        string keyProp,
        string tempProp,
        string unassigned,
        bool elementRef
    )
    {
        _ = elementType;
        _ = keyType;
        code.AppendLine("foreach (var __gRaw in " + working + ")");
        code.OpenBlock();
        if (elementRef)
        {
            code.AppendLine("var __g = __gRaw;");
            code.AppendLine("if (__g is null) continue;");
        }
        else
        {
            code.AppendLine("var __g = __gRaw;");
        }
        code.AppendLine("var __gk = __g." + keyProp + ";");
        code.AppendLine(
            "if (global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default.Equals(__gk, "
                + unassigned
                + ")) { var __gt = __g."
                + tempProp
                + "; if (__gt.HasValue && __gt.Value != global::System.Guid.Empty) { if (!"
                + seenTemps
                + ".Add(__gt.Value)) return __Fail(\"Reconciling produced a duplicate key.\"); } }"
        );
        code.AppendLine(
            "else if (!"
                + seenKeys
                + ".Add(__gk)) return __Fail(\"Reconciling produced a duplicate key.\");"
        );
        code.CloseBlock();
    }
}
