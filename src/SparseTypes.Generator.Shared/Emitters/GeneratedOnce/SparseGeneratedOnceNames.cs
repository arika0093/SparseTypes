using System.Threading;

namespace SparseTypes.Generator.Shared;

/// <summary>Stable names for compilation-scoped Generated-Once helpers.</summary>
/// <remarks>Centralizes names for helpers emitted once per compilation.</remarks>
internal static class SparseGeneratedOnceNames
{
    /// <summary>Simple name of the shared read-only list adapter.</summary>
    public const string ReadOnlyListAdapter = "SparseReadOnlyListAdapter";

    /// <summary>Simple name of the shared read-only dictionary adapter.</summary>
    public const string ReadOnlyDictionaryAdapter = "SparseReadOnlyDictionaryAdapter";

    /// <summary>Simple name of the shared read-only dictionary-entries adapter.</summary>
    public const string ReadOnlyDictionaryEntriesAdapter = "SparseReadOnlyDictionaryEntriesAdapter";

    /// <summary>Simple name of the shared collection clone kernel container.</summary>
    public const string CloneKernels = "SparseCloneKernels";

    /// <summary>Simple name of the shared ordered removal-index helper.</summary>
    public const string RemovalIndex = "SparseRemovalIndex";

    /// <summary>Simple name of the shared endpoint JSON converter.</summary>
    public const string ChangePayloadEndpointJsonConverter = "ChangePayloadEndpointJsonConverter";

    /// <summary>Simple name of the shared JSON codec container.</summary>
    public const string JsonCodecs = "SparseJsonCodecs";

    /// <summary>Simple name of the shared payload JSON codec container.</summary>
    public const string JsonPayloadCodecs = "SparseJsonPayloadCodecs";

    /// <summary>Qualified name of the shared list adapter.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <returns>Globally qualified type name.</returns>
    public static string QualifiedListAdapter(string implementationNamespace) => "global::" + implementationNamespace + "." + ReadOnlyListAdapter;

    /// <summary>Qualified name of the shared dictionary adapter.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <returns>Globally qualified type name.</returns>
    public static string QualifiedDictionaryAdapter(string implementationNamespace) =>
        "global::" + implementationNamespace + "." + ReadOnlyDictionaryAdapter;

    /// <summary>Qualified name of the shared dictionary-entries adapter.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <returns>Globally qualified type name.</returns>
    public static string QualifiedDictionaryEntriesAdapter(string implementationNamespace) =>
        "global::" + implementationNamespace + "." + ReadOnlyDictionaryEntriesAdapter;

    /// <summary>Qualified name of the shared clone kernel container.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <returns>Globally qualified type name.</returns>
    public static string QualifiedCloneKernels(string implementationNamespace) => "global::" + implementationNamespace + "." + CloneKernels;

    /// <summary>Qualified name of the shared removal-index helper.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <returns>Globally qualified type name.</returns>
    public static string QualifiedRemovalIndex(string implementationNamespace) => "global::" + implementationNamespace + "." + RemovalIndex;

    /// <summary>Qualified name of the shared ChangePayload endpoint converter.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <returns>Globally qualified type name.</returns>
    public static string QualifiedChangePayloadEndpointJsonConverter(string implementationNamespace) =>
        "global::" + implementationNamespace + "." + ChangePayloadEndpointJsonConverter;

    /// <summary>Qualified name of the shared JSON codec container.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <returns>Globally qualified type name.</returns>
    public static string QualifiedJsonCodecs(string implementationNamespace) => "global::" + implementationNamespace + "." + JsonCodecs;

    /// <summary>Qualified name of the shared payload JSON codec container.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <returns>Globally qualified type name.</returns>
    public static string QualifiedJsonPayloadCodecs(string implementationNamespace) => "global::" + implementationNamespace + "." + JsonPayloadCodecs;

    /// <summary>Stable hint name for the shared read-only adapter source.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace (reserved for future product disambiguation).</param>
    /// <returns>Deterministic hint name under the shared Generated prefix.</returns>
    public static string ReadOnlyAdaptersHintName(string implementationNamespace) => "Generated.ReadOnlyAdapters.g.cs";

    /// <summary>Stable hint name for the shared clone kernel source.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace (reserved for future product disambiguation).</param>
    /// <returns>Deterministic hint name under the shared Generated prefix.</returns>
    public static string CloneKernelsHintName(string implementationNamespace) => "Generated.CloneKernels.g.cs";

    /// <summary>Stable hint name for the shared removal-index source.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace (reserved for future product disambiguation).</param>
    /// <returns>Deterministic hint name under the shared Generated prefix.</returns>
    public static string RemovalIndexHintName(string implementationNamespace) => "Generated.RemovalIndex.g.cs";

    /// <summary>Stable hint name for shared JSON codec source.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace (reserved for future product disambiguation).</param>
    /// <returns>Deterministic hint name under the shared Generated prefix.</returns>
    public static string JsonCodecsHintName(string implementationNamespace) => "Generated.JsonCodecs.g.cs";

    /// <summary>Stable hint name for shared payload JSON codec source.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace (reserved for future product disambiguation).</param>
    /// <returns>Deterministic hint name under the shared Generated prefix.</returns>
    public static string JsonPayloadCodecsHintName(string implementationNamespace) => "Generated.JsonPayloadCodecs.g.cs";

    /// <summary>Opens a block-scoped generated namespace compatible with C# 9.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public static void AppendGeneratedHeader(SharedIndentedBuilder code, string implementationNamespace, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        code.AppendLine("// <auto-generated />");
        code.AppendLine("#nullable enable");
        // Block-scoped namespace: file-scoped namespaces need C# 10, while the
        // generated surface targets C# 9 as its minimum language version.
        code.Append("namespace ").Append(implementationNamespace).AppendLine();
        code.OpenBlock();
    }
}
