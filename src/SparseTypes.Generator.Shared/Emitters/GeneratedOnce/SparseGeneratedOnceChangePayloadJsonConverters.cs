using System.Threading;

namespace SparseTypes.Generator.Shared;

/// <summary>Builds compilation-scoped ChangePayload JSON converter source.</summary>
internal static class SparseGeneratedOnceChangePayloadJsonConverters
{
    /// <summary>Builds model-independent wire converters in the configured namespace.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <param name="dialect">Product runtime type vocabulary.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Compilation-scoped source text.</returns>
    public static string BuildSource(
        string implementationNamespace,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        bool emitConverters,
        CancellationToken cancellationToken
    )
    {
        using var code = new SharedIndentedBuilder(cancellationToken);
        SparseGeneratedOnceNames.AppendGeneratedHeader(code, implementationNamespace, cancellationToken);
        if (emitConverters)
        {
            SparseChangePayloadJsonBridgeEmitter.AppendBridgeConverters(code, dialect, SparseGeneratedOnceNames.JsonCodecs);
        }

        code.CloseBlock();
        return code.ToString();
    }
}
