namespace SparseTypes.Generator.Shared;

internal static class SparseKeyedRemovalIndexEmitter
{
    internal static void Emit(
        SharedIndentedBuilder code,
        string keyType,
        string comparer,
        string? implementationNamespace = null
    )
    {
        // Generated-Once: the keyed patch drops its IndexedRemovals subclass
        // and shares the plain list-plus-slots storage with dictionary patches.
        if (!string.IsNullOrEmpty(implementationNamespace))
        {
            // Relocated member operations reach the lookup through the facade
            // instance, so the field is internal rather than private.
            code.AppendLine("internal int[]? __removedLookup;");
            SparseDictionaryRemovalIndexEmitter.Emit(
                code,
                keyType,
                comparer,
                implementationNamespace
            );
            return;
        }

        EmitLegacy(code, keyType, comparer);
    }

    private static void EmitLegacy(SharedIndentedBuilder code, string keyType, string comparer)
    {
        code.AppendLine(
            "private sealed class __SparseIndexedRemovals : global::System.Collections.Generic.List<"
                + keyType
                + ">"
        );
        code.OpenBlock();
        code.AppendLine("internal int[]? Slots;");
        code.AppendLine(
            "internal __SparseIndexedRemovals(global::System.Collections.Generic.List<"
                + keyType
                + "> source, int[] slots) : base(source.Capacity)"
        );
        code.OpenBlock();
        code.AppendLine("AddRange(source);");
        code.AppendLine("Slots = slots;");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("private int[]? __removedLookup");
        code.OpenBlock();
        code.AppendLine("get => (__removed as __SparseIndexedRemovals)?.Slots;");
        code.AppendLine("set");
        code.OpenBlock();
        code.AppendLine("if (__removed is __SparseIndexedRemovals indexed)");
        code.OpenBlock();
        code.AppendLine("indexed.Slots = value;");
        code.CloseBlock();
        code.AppendLine("else if (value is not null)");
        code.OpenBlock();
        code.AppendLine("__removed = new __SparseIndexedRemovals(__removed!, value);");
        code.CloseBlock();
        code.CloseBlock();
        code.CloseBlock();
        SparseDictionaryRemovalIndexEmitter.Emit(code, keyType, comparer);
    }
}
