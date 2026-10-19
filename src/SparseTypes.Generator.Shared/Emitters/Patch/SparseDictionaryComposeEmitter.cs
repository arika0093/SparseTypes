namespace SparseTypes.Generator.Shared;

internal static class SparseDictionaryComposeEmitter
{
    internal static void EmitDictionaryCompose(
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
        SparseMemberOperationSplit? split = null
    )
    {
        if (split is not null)
        {
            split.Shell.AppendLineAt(
                3,
                "/// <summary>Composes this dictionary patch with a following patch.</summary>"
            );
            split.Shell.AppendLineAt(
                3,
                "public "
                    + patchName
                    + " Compose("
                    + patchName
                    + " next) => "
                    + split.OperationsType
                    + ".Compose(this, next);"
            );
            split.Shell.AppendLineAt(3, "/// <summary>Composes two dictionary patches.</summary>");
            split.Shell.AppendLineAt(
                3,
                "public static "
                    + patchName
                    + " Compose("
                    + patchName
                    + " first, "
                    + patchName
                    + " second) => "
                    + split.OperationsType
                    + ".Compose(first, second);"
            );
            code.AppendLineAt(
                3,
                "/// <summary>Composes a dictionary patch with a following patch.</summary>"
            );
            code.AppendLineAt(
                3,
                "internal static "
                    + patchName
                    + " Compose("
                    + patchName
                    + " self, "
                    + patchName
                    + " next)"
            );
            code.AppendLineAt(3, "{");
            SparseDictionaryPatchEmitter.AppendDictionaryFieldAliases(code);
        }
        else
        {
            // Compose.
            code.AppendLineAt(3, "public " + patchName + " Compose(" + patchName + " next)");
            code.AppendLineAt(3, "{");
        }
        code.AppendLineAt(
            4,
            "if (next is null) throw new global::System.ArgumentNullException(nameof(next));"
        );
        code.AppendLineAt(4, "var result = new " + patchName + "();");
        code.AppendLineAt(
            4,
            "if (next.__whole.Kind != "
                + kind
                + ".Keep) { result.__whole = next.__whole; return result; }"
        );
        code.AppendLineAt(4, "if (__whole.Kind != " + kind + ".Keep)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__whole.Kind == "
                + kind
                + ".Remove) throw new global::System.InvalidOperationException(\"Cannot compose granular operations after a whole Remove.\");"
        );
        code.AppendLineAt(
            4,
            "var applied = next.Apply("
                + runtime
                + "Optional<"
                + dictType
                + ">.Present(__whole.Value));"
        );
        code.AppendLineAt(4, "result.__whole = " + operation + ".Set(applied.Value!);");
        code.AppendLineAt(4, "return result;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "if (" + (split is null ? "this" : "self") + ".IsEmpty && next.IsEmpty) return result;"
        );
        SparseDictionaryComposeFastPathEmitter.AppendSetThenRemoveFastPath(code, keyType, comparer);
        SparseDictionaryComposeFastPathEmitter.AppendRemoveThenSetFastPath(
            code,
            keyType,
            valueType,
            comparer
        );
        code.AppendLineAt(
            4,
            "global::System.Collections.Generic.HashSet<" + keyType + ">? removed = null;"
        );
        code.AppendLineAt(
            4,
            "if (__removed is not null && __removed.Count > 0) removed = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(__removed, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "if (next.__removed is not null && next.__removed.Count > 0) { removed ??= new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + "); foreach (var k in next.__removed) removed.Add(k); }"
        );
        code.AppendLineAt(
            4,
            "global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">? set = null;"
        );
        code.AppendLineAt(
            4,
            "if (__set is not null && __set.Count > 0) set = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(__set, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "if (next.__set is not null && next.__set.Count > 0) { set ??= new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">("
                + comparer
                + "); foreach (var kv in next.__set) { set[kv.Key] = kv.Value; removed?.Remove(kv.Key); } }"
        );
        // Drop keys added by this patch when the next patch removes them.
        code.AppendLineAt(
            4,
            "if (next.__removed is not null && set is not null) foreach (var k in next.__removed) set.Remove(k);"
        );
        if (hasPatch)
        {
            var valuePatch = SparseKeyedCollectionEmitter.ValuePatchType(member);
            // Pre-index next removals only when first-patch nested edits need
            // filtering: a nullable local with a guarded Contains check keeps
            // comparer-correct O(1) lookups without allocating for scalar-only
            // callers or when either side is empty.
            code.AppendLineAt(
                4,
                "global::System.Collections.Generic.HashSet<"
                    + keyType
                    + ">? __nextRemoved = (__edited is not null && __edited.Count > 0 && next.__removed is not null && next.__removed.Count > 0) ? new global::System.Collections.Generic.HashSet<"
                    + keyType
                    + ">(next.__removed, "
                    + comparer
                    + ") : null;"
            );
            code.AppendLineAt(
                4,
                "global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valuePatch
                    + ">? edited = null;"
            );
            code.AppendLineAt(
                4,
                "if (__edited is not null) foreach (var kv in __edited) { if (kv.Value.__SparseIsEmpty()) continue; if (__nextRemoved is not null && __nextRemoved.Contains(kv.Key)) continue; edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valuePatch
                    + ">("
                    + comparer
                    + "); edited[kv.Key] = kv.Value; }"
            );
            code.AppendLineAt(
                4,
                "if (next.__edited is not null) foreach (var kv in next.__edited) { if (kv.Value.__SparseIsEmpty()) continue; if (set is not null && set.TryGetValue(kv.Key, out var setBase)) { var __folded = kv.Value.Apply("
                    + runtime
                    + "Optional<"
                    + SparseKeyedCollectionEmitter.ValueFragmentType(member)
                    + "?>.Present("
                    + SparseKeyedCollectionEmitter.ValueFragmentType(member)
                    + ".From(setBase))); if (__folded.IsPresent && __folded.Value is not null) set[kv.Key] = __folded.Value!.ToModel(); continue; } if (edited is not null && edited.TryGetValue(kv.Key, out var first)) { var __composed = first.Compose(kv.Value); if (__composed.__SparseIsEmpty()) edited.Remove(kv.Key); else edited[kv.Key] = __composed; } else (edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valuePatch
                    + ">("
                    + comparer
                    + "))[kv.Key] = kv.Value; }"
            );
            code.AppendLineAt(
                4,
                "if (edited is not null && edited.Count > 0) result.__edited = edited;"
            );
        }
        else
        {
            code.AppendLineAt(
                4,
                "global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">? edited = null;"
            );
            code.AppendLineAt(
                4,
                "if (__edited is not null && __edited.Count > 0) edited = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">(__edited, "
                    + comparer
                    + ");"
            );
            code.AppendLineAt(
                4,
                "if (next.__edited is not null && next.__edited.Count > 0) { edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + "); foreach (var kv in next.__edited) edited[kv.Key] = kv.Value; }"
            );
            code.AppendLineAt(
                4,
                "if (removed is not null) foreach (var k in removed) { edited?.Remove(k); set?.Remove(k); }"
            );
            code.AppendLineAt(
                4,
                "if (set is not null && edited is not null) foreach (var k in set.Keys) edited.Remove(k);"
            );
            code.AppendLineAt(
                4,
                "if (edited is not null && edited.Count > 0) result.__edited = edited;"
            );
        }

        code.AppendLineAt(
            4,
            "if (removed is not null && removed.Count > 0) result.__removed = new global::System.Collections.Generic.List<"
                + keyType
                + ">(removed);"
        );
        code.AppendLineAt(4, "if (set is not null && set.Count > 0) result.__set = set;");
        code.AppendLineAt(4, "return result;");
        code.AppendLineAt(3, "}");
        if (split is null)
        {
            code.AppendLineAt(
                3,
                "public static "
                    + patchName
                    + " Compose("
                    + patchName
                    + " first, "
                    + patchName
                    + " second) { if (first is null) throw new global::System.ArgumentNullException(nameof(first)); return first.Compose(second); }"
            );
        }
    }
}
