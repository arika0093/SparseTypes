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
            split.Shell.AppendLine(
                "/// <summary>Composes this dictionary patch with a following patch.</summary>"
            );
            split.Shell.AppendLine(
                "public "
                    + patchName
                    + " Compose("
                    + patchName
                    + " next) => "
                    + split.OperationsType
                    + ".Compose(this, next);"
            );
            split.Shell.AppendLine("/// <summary>Composes two dictionary patches.</summary>");
            split.Shell.AppendLine(
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
            code.AppendLine(
                "/// <summary>Composes a dictionary patch with a following patch.</summary>"
            );
            code.AppendLine(
                "internal static "
                    + patchName
                    + " Compose("
                    + patchName
                    + " self, "
                    + patchName
                    + " next)"
            );
            code.OpenBlock();
            SparseDictionaryPatchEmitter.AppendDictionaryFieldAliases(code);
        }
        else
        {
            // Compose.
            code.AppendLine("public " + patchName + " Compose(" + patchName + " next)");
            code.OpenBlock();
        }
        code.AppendLine(
            "if (next is null) throw new global::System.ArgumentNullException(nameof(next));"
        );
        code.AppendLine("var result = new " + patchName + "();");
        code.AppendLine(
            "if (next.__whole.Kind != "
                + kind
                + ".Keep) { result.__whole = next.__whole; return result; }"
        );
        code.AppendLine("if (__whole.Kind != " + kind + ".Keep)");
        code.OpenBlock();
        code.AppendLine(
            "if (__whole.Kind == "
                + kind
                + ".Remove) throw new global::System.InvalidOperationException(\"Cannot compose granular operations after a whole Remove.\");"
        );
        code.AppendLine(
            "var applied = next.Apply("
                + runtime
                + "Optional<"
                + dictType
                + ">.Present(__whole.Value));"
        );
        code.AppendLine("result.__whole = " + operation + ".Set(applied.Value!);");
        code.AppendLine("return result;");
        code.CloseBlock();
        code.AppendLine(
            "if (" + (split is null ? "this" : "self") + ".IsEmpty && next.IsEmpty) return result;"
        );
        SparseDictionaryComposeFastPathEmitter.AppendSetThenRemoveFastPath(code, keyType, comparer);
        SparseDictionaryComposeFastPathEmitter.AppendRemoveThenSetFastPath(
            code,
            keyType,
            valueType,
            comparer
        );
        code.AppendLine(
            "global::System.Collections.Generic.HashSet<" + keyType + ">? removed = null;"
        );
        code.AppendLine(
            "if (__removed is not null && __removed.Count > 0) removed = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(__removed, "
                + comparer
                + ");"
        );
        code.AppendLine(
            "if (next.__removed is not null && next.__removed.Count > 0) { removed ??= new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + "); foreach (var k in next.__removed) removed.Add(k); }"
        );
        code.AppendLine(
            "global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">? set = null;"
        );
        code.AppendLine(
            "if (__set is not null && __set.Count > 0) set = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(__set, "
                + comparer
                + ");"
        );
        code.AppendLine(
            "if (next.__set is not null && next.__set.Count > 0) { set ??= new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">("
                + comparer
                + "); foreach (var kv in next.__set) { set[kv.Key] = kv.Value; removed?.Remove(kv.Key); } }"
        );
        // Drop keys added by this patch when the next patch removes them.
        code.AppendLine(
            "if (next.__removed is not null && set is not null) foreach (var k in next.__removed) set.Remove(k);"
        );
        if (hasPatch)
        {
            var valuePatch = SparseKeyedCollectionEmitter.ValuePatchType(member);
            // Pre-index next removals only when first-patch nested edits need
            // filtering: a nullable local with a guarded Contains check keeps
            // comparer-correct O(1) lookups without allocating for scalar-only
            // callers or when either side is empty.
            code.AppendLine(
                "global::System.Collections.Generic.HashSet<"
                    + keyType
                    + ">? __nextRemoved = (__edited is not null && __edited.Count > 0 && next.__removed is not null && next.__removed.Count > 0) ? new global::System.Collections.Generic.HashSet<"
                    + keyType
                    + ">(next.__removed, "
                    + comparer
                    + ") : null;"
            );
            code.AppendLine(
                "global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valuePatch
                    + ">? edited = null;"
            );
            code.AppendLine(
                "if (__edited is not null) foreach (var kv in __edited) { if (kv.Value.__SparseIsEmpty()) continue; if (__nextRemoved is not null && __nextRemoved.Contains(kv.Key)) continue; edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valuePatch
                    + ">("
                    + comparer
                    + "); edited[kv.Key] = kv.Value; }"
            );
            code.AppendLine(
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
            code.AppendLine(
                "if (edited is not null && edited.Count > 0) result.__edited = edited;"
            );
        }
        else
        {
            code.AppendLine(
                "global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">? edited = null;"
            );
            code.AppendLine(
                "if (__edited is not null && __edited.Count > 0) edited = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">(__edited, "
                    + comparer
                    + ");"
            );
            code.AppendLine(
                "if (next.__edited is not null && next.__edited.Count > 0) { edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + "); foreach (var kv in next.__edited) edited[kv.Key] = kv.Value; }"
            );
            code.AppendLine(
                "if (removed is not null) foreach (var k in removed) { edited?.Remove(k); set?.Remove(k); }"
            );
            code.AppendLine(
                "if (set is not null && edited is not null) foreach (var k in set.Keys) edited.Remove(k);"
            );
            code.AppendLine(
                "if (edited is not null && edited.Count > 0) result.__edited = edited;"
            );
        }

        code.AppendLine(
            "if (removed is not null && removed.Count > 0) result.__removed = new global::System.Collections.Generic.List<"
                + keyType
                + ">(removed);"
        );
        code.AppendLine("if (set is not null && set.Count > 0) result.__set = set;");
        code.AppendLine("return result;");
        code.CloseBlock();
        if (split is null)
        {
            code.AppendLine(
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
