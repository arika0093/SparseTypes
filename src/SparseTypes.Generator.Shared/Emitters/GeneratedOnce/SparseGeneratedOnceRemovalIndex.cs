using System.Threading;

namespace SparseTypes.Generator.Shared;

/// <summary>Builds the compilation-scoped ordered removal-index source.</summary>
/// <remarks>The removal list, optional indexed hash slots, cancellation, de-duplication and index capacity management.</remarks>
internal static class SparseGeneratedOnceRemovalIndex
{
    /// <summary>Builds the shared removal-index source for the explicit namespace.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Compilation-scoped source text.</returns>
    public static string BuildSource(
        string implementationNamespace,
        CancellationToken cancellationToken
    )
    {
        using var code = new SharedIndentedBuilder(cancellationToken);
        SparseGeneratedOnceNames.AppendGeneratedHeader(
            code,
            implementationNamespace,
            cancellationToken
        );
        var comparer = "global::System.Collections.Generic.EqualityComparer<TKey>.Default";
        code.AppendLine(
            "/// <summary>Ordered removal-index kernels shared by keyed patch types.</summary>"
        );
        code.AppendLine(
            "internal static class " + SparseGeneratedOnceNames.RemovalIndex + "<TKey>"
        );
        code.OpenBlock();
        // Internal-first ordering: public-equivalent kernels precede private
        // helpers.
        code.AppendLine(
            "internal static void CancelRemoval(global::System.Collections.Generic.List<TKey> removed, ref int[]? lookup, TKey key)"
        );
        code.OpenBlock();
        // Fast path: a valid index already proves absence without scanning
        // the ordered list. Keep the full scan on an indexed hit (order and
        // duplicate cleanup) and when no index exists.
        code.AppendLine("var slots = lookup;");
        code.AppendLine(
            "if (slots is not null && slots[RemovedSlot(removed, key, slots)] == 0) return;"
        );
        code.AppendLine("for (int index = removed.Count - 1; index >= 0; index--)");
        code.OpenBlock();
        code.AppendLine("if (!" + comparer + ".Equals(removed[index], key)) continue;");
        code.AppendLine("removed.RemoveAt(index);");
        code.AppendLine("lookup = null;");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine(
            "internal static bool ContainsRemoved(global::System.Collections.Generic.List<TKey> removed, int[]? lookup, TKey key)"
        );
        code.OpenBlock();
        code.AppendLine("var slots = lookup;");
        code.AppendLine("if (slots is not null)");
        code.OpenBlock();
        code.AppendLine("return slots[RemovedSlot(removed, key, slots)] != 0;");
        code.CloseBlock();
        code.AppendLine("foreach (var existing in removed)");
        code.OpenBlock();
        code.AppendLine("if (" + comparer + ".Equals(existing, key)) return true;");
        code.CloseBlock();
        code.AppendLine("return false;");
        code.CloseBlock();
        code.AppendLine(
            "internal static void ReserveRemovals(ref global::System.Collections.Generic.List<TKey>? removed, ref int[]? lookup, int count)"
        );
        code.OpenBlock();
        code.AppendLine("if (count <= 0) return;");
        code.AppendLine("removed = new global::System.Collections.Generic.List<TKey>(count);");
        code.AppendLine("if (count >= 32)");
        code.OpenBlock();
        code.AppendLine("int capacity = 64;");
        code.AppendLine("while (capacity < checked(count * 2))");
        code.OpenBlock();
        code.AppendLine("capacity = checked(capacity * 2);");
        code.CloseBlock();
        code.AppendLine("RebuildIndex(removed, ref lookup, capacity);");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine(
            "internal static void AddRemoval(global::System.Collections.Generic.List<TKey> removed, ref int[]? lookup, TKey key, bool keepReservedIndex)"
        );
        code.OpenBlock();
        code.AppendLine("var reserved = keepReservedIndex && lookup is not null;");
        code.AppendLine("if (reserved || removed.Count >= 32)");
        code.OpenBlock();
        code.AppendLine("AddIndexedRemoval(removed, ref lookup, key);");
        code.AppendLine("return;");
        code.CloseBlock();
        code.AppendLine(
            "foreach (var existing in removed) if (" + comparer + ".Equals(existing, key)) return;"
        );
        code.AppendLine("removed.Add(key);");
        code.AppendLine("if (!keepReservedIndex)");
        code.OpenBlock();
        code.AppendLine("lookup = null;");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine(
            "private static int RemovedSlot(global::System.Collections.Generic.List<TKey> removed, TKey key, int[] slots)"
        );
        code.OpenBlock();
        code.AppendLine("int mask = slots.Length - 1;");
        code.AppendLine("int slot = " + comparer + ".GetHashCode(key!) & mask;");
        code.AppendLine(
            "while (slots[slot] != 0 && !" + comparer + ".Equals(removed[slots[slot] - 1], key))"
        );
        code.OpenBlock();
        code.AppendLine("slot = (slot + 1) & mask;");
        code.CloseBlock();
        code.AppendLine("return slot;");
        code.CloseBlock();
        code.AppendLine(
            "private static void RebuildIndex(global::System.Collections.Generic.List<TKey> removed, ref int[]? lookup, int capacity)"
        );
        code.OpenBlock();
        code.AppendLine("var slots = new int[capacity];");
        code.AppendLine("for (int index = 0; index < removed.Count; index++)");
        code.OpenBlock();
        code.AppendLine("slots[RemovedSlot(removed, removed[index], slots)] = index + 1;");
        code.CloseBlock();
        code.AppendLine("lookup = slots;");
        code.CloseBlock();
        code.AppendLine(
            "private static void AddIndexedRemoval(global::System.Collections.Generic.List<TKey> removed, ref int[]? lookup, TKey key)"
        );
        code.OpenBlock();
        code.AppendLine("var slots = lookup;");
        code.AppendLine("if (slots is null)");
        code.OpenBlock();
        code.AppendLine("int capacity = 64;");
        code.AppendLine("while (capacity <= checked(removed.Count * 2))");
        code.OpenBlock();
        code.AppendLine("capacity = checked(capacity * 2);");
        code.CloseBlock();
        code.AppendLine("RebuildIndex(removed, ref lookup, capacity);");
        code.AppendLine("slots = lookup!;");
        code.CloseBlock();
        code.AppendLine("int __slot = RemovedSlot(removed, key, slots);");
        code.AppendLine("if (slots[__slot] != 0) return;");
        code.AppendLine("if (removed.Count >= slots.Length / 2)");
        code.OpenBlock();
        code.AppendLine("RebuildIndex(removed, ref lookup, checked(slots.Length * 2));");
        code.AppendLine("slots = lookup!;");
        code.AppendLine("__slot = RemovedSlot(removed, key, slots);");
        code.CloseBlock();
        code.AppendLine("removed.Add(key);");
        code.AppendLine("slots[__slot] = removed.Count;");
        code.CloseBlock();
        code.CloseBlock();
        code.CloseBlock();
        return code.ToString();
    }
}
