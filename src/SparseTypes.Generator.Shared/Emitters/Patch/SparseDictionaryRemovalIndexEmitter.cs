namespace SparseTypes.Generator.Shared;

/// <summary>Emits an index into the ordered dictionary removal list.</summary>
internal static class SparseDictionaryRemovalIndexEmitter
{
    internal static void Emit(
        SharedIndentedBuilder code,
        string keyType,
        string comparer,
        string? implementationNamespace = null
    )
    {
        // Generated-Once: with an explicit namespace the kernels live once per
        // compilation.
        if (!string.IsNullOrEmpty(implementationNamespace))
        {
            var prefix =
                SparseGeneratedOnceNames.QualifiedRemovalIndex(implementationNamespace!)
                + "<"
                + keyType
                + ">";
            code.AppendLine(
                "internal void __SparseReserveRemovals(int count) => "
                    + prefix
                    + ".ReserveRemovals(ref __removed, ref __removedLookup, count);"
            );
            return;
        }

        EmitLegacy(code, keyType, comparer);
    }

    internal static void EmitAdd(
        SharedIndentedBuilder code,
        string comparer,
        bool keepReservedIndex,
        string? keyType = null,
        string? implementationNamespace = null
    )
    {
        // Shared kernels take the ordered add inline: dedup, indexed
        // insert and capacity growth live once per compilation.
        if (!string.IsNullOrEmpty(implementationNamespace) && keyType is not null)
        {
            var prefix =
                SparseGeneratedOnceNames.QualifiedRemovalIndex(implementationNamespace!)
                + "<"
                + keyType
                + ">";
            code.AppendLine(
                prefix
                    + ".AddRemoval(__removed!, ref __removedLookup, key, "
                    + (keepReservedIndex ? "true" : "false")
                    + ");"
            );
            return;
        }

        EmitLegacyAdd(code, comparer, keepReservedIndex);
    }

    internal static string SharedPrefix(string keyType, string implementationNamespace) =>
        SparseGeneratedOnceNames.QualifiedRemovalIndex(implementationNamespace)
        + "<"
        + keyType
        + ">";

    private static void EmitLegacy(SharedIndentedBuilder code, string keyType, string comparer)
    {
        code.AppendLine("private void __SparseCancelRemoval(" + keyType + " key)");
        code.OpenBlock();
        // Fast path: a valid index already proves absence without scanning
        // the ordered list. Keep the full scan on an indexed hit (order and
        // duplicate cleanup) and when no index exists.
        code.AppendLine("var slots = __removedLookup;");
        code.AppendLine(
            "if (slots is not null && slots[__SparseRemovedSlot(key, slots)] == 0) return;"
        );
        code.AppendLine("for (int index = __removed!.Count - 1; index >= 0; index--)");
        code.OpenBlock();
        code.AppendLine("if (!" + comparer + ".Equals(__removed[index], key)) continue;");
        code.AppendLine("__removed.RemoveAt(index);");
        code.AppendLine("__removedLookup = null;");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("private int __SparseRemovedSlot(" + keyType + " key, int[] slots)");
        code.OpenBlock();
        code.AppendLine("int mask = slots.Length - 1;");
        code.AppendLine("int slot = " + comparer + ".GetHashCode(key!) & mask;");
        code.AppendLine(
            "while (slots[slot] != 0 && !" + comparer + ".Equals(__removed![slots[slot] - 1], key))"
        );
        code.OpenBlock();
        code.AppendLine("slot = (slot + 1) & mask;");
        code.CloseBlock();
        code.AppendLine("return slot;");
        code.CloseBlock();
        code.AppendLine("private bool __SparseContainsRemoved(" + keyType + " key)");
        code.OpenBlock();
        code.AppendLine("var slots = __removedLookup;");
        code.AppendLine("if (slots is not null)");
        code.OpenBlock();
        code.AppendLine("return slots[__SparseRemovedSlot(key, slots)] != 0;");
        code.CloseBlock();
        code.AppendLine("foreach (var existing in __removed!)");
        code.OpenBlock();
        code.AppendLine("if (" + comparer + ".Equals(existing, key)) return true;");
        code.CloseBlock();
        code.AppendLine("return false;");
        code.CloseBlock();
        code.AppendLine("private void __SparseRebuildRemovedIndex(int capacity)");
        code.OpenBlock();
        code.AppendLine("var slots = new int[capacity];");
        code.AppendLine("for (int index = 0; index < __removed!.Count; index++)");
        code.OpenBlock();
        code.AppendLine("slots[__SparseRemovedSlot(__removed[index], slots)] = index + 1;");
        code.CloseBlock();
        code.AppendLine("__removedLookup = slots;");
        code.CloseBlock();
        code.AppendLine("internal void __SparseReserveRemovals(int count)");
        code.OpenBlock();
        code.AppendLine("if (count <= 0) return;");
        code.AppendLine(
            "__removed = new global::System.Collections.Generic.List<" + keyType + ">(count);"
        );
        code.AppendLine("if (count >= 32)");
        code.OpenBlock();
        code.AppendLine("int capacity = 64;");
        code.AppendLine("while (capacity < checked(count * 2))");
        code.OpenBlock();
        code.AppendLine("capacity = checked(capacity * 2);");
        code.CloseBlock();
        code.AppendLine("__SparseRebuildRemovedIndex(capacity);");
        code.CloseBlock();
        code.CloseBlock();
        EmitIndexedAdd(code, keyType);
    }

    internal static void EmitLegacyAdd(
        SharedIndentedBuilder code,
        string comparer,
        bool keepReservedIndex
    )
    {
        var condition = keepReservedIndex
            ? "__removedLookup is not null || __removed!.Count >= 32"
            : "__removed!.Count >= 32";
        code.AppendLine("if (" + condition + ")");
        code.OpenBlock();
        code.AppendLine("__SparseAddIndexedRemoval(key);");
        code.AppendLine("return;");
        code.CloseBlock();
        code.AppendLine(
            "foreach (var existing in __removed!) if ("
                + comparer
                + ".Equals(existing, key)) return;"
        );
        code.AppendLine("__removed.Add(key);");
        if (!keepReservedIndex)
        {
            code.AppendLine("__removedLookup = null;");
        }
    }

    private static void EmitIndexedAdd(SharedIndentedBuilder code, string keyType)
    {
        code.AppendLine("private void __SparseAddIndexedRemoval(" + keyType + " key)");
        code.OpenBlock();
        code.AppendLine("var slots = __removedLookup;");
        code.AppendLine("if (slots is null)");
        code.OpenBlock();
        code.AppendLine("int capacity = 64;");
        code.AppendLine("while (capacity <= checked(__removed!.Count * 2))");
        code.OpenBlock();
        code.AppendLine("capacity = checked(capacity * 2);");
        code.CloseBlock();
        code.AppendLine("__SparseRebuildRemovedIndex(capacity);");
        code.AppendLine("slots = __removedLookup!;");
        code.CloseBlock();
        code.AppendLine("int __slot = __SparseRemovedSlot(key, slots);");
        code.AppendLine("if (slots[__slot] != 0) return;");
        code.AppendLine("if (__removed!.Count >= slots.Length / 2)");
        code.OpenBlock();
        code.AppendLine("__SparseRebuildRemovedIndex(checked(slots.Length * 2));");
        code.AppendLine("slots = __removedLookup!;");
        code.AppendLine("__slot = __SparseRemovedSlot(key, slots);");
        code.CloseBlock();
        code.AppendLine("__removed!.Add(key);");
        code.AppendLine("slots[__slot] = __removed.Count;");
        code.CloseBlock();
    }
}
