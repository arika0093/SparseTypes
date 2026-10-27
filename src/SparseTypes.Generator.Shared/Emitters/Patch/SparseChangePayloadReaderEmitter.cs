using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits the ChangePayload read path: validated conversion to a complete ChangeSet.</summary>
/// <remarks>Incomplete or redacted payloads cannot become a complete ChangeSet.</remarks>
internal static class SparseChangePayloadReaderEmitter
{
    internal static void AppendFromPayload(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        SparseOperationTarget? target = null
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var endpoint = runtime + "ChangePayloadEndpoint";
        var payloadRoot = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "Root");
        var versionLiteral = SymbolDisplay.FormatLiteral(dialect.ChangePayloadVersion, true);
        if (target is not null)
        {
            code.AppendLine("/// <summary>Converts a validated envelope to a complete change set.</summary>");
            code.AppendLine(
                "/// <remarks>Redacted or otherwise incomplete histories are rejected; project them with <see cref=\"ChangePayload.ToPatch\"/> instead.</remarks>"
            );
            code.AppendLine("/// <param name=\"payload\">The validated envelope.</param>");
            code.AppendLine("/// <returns>The complete change set.</returns>");
            code.AppendLine(
                "public static ChangeSet FromPayload(ChangePayload payload) => " + target.ChangeSetOperationsType + ".FromPayload(payload);"
            );
            code = target.ChangeSetOperations;
        }
        code.AppendLine("/// <summary>Converts a validated envelope to a complete change set.</summary>");
        if (target is null)
        {
            code.AppendLine(
                "/// <remarks>Redacted or otherwise incomplete histories are rejected; project them with <see cref=\"ChangePayload.ToPatch\"/> instead.</remarks>"
            );
            code.AppendLine("/// <param name=\"payload\">The validated envelope.</param>");
            code.AppendLine("/// <returns>The complete change set.</returns>");
        }
        code.AppendLine(
            (
                target is null
                    ? "public static ChangeSet FromPayload(ChangePayload payload)"
                    : "internal static ChangeSet FromPayload(ChangePayload payload)"
            )
        );
        code.OpenBlock();
        code.AppendLine("if (payload is null) throw new global::System.ArgumentNullException(nameof(payload));");
        code.AppendLine(
            "if (!global::System.String.Equals(payload.Version, "
                + versionLiteral
                + ", global::System.StringComparison.Ordinal)) throw new global::System.ArgumentException(\"Unsupported ChangePayload version.\", nameof(payload));"
        );
        code.AppendLine(
            "if (!ChangePayload.IsValidSchemaIdentity(payload.Schema)) throw new global::System.ArgumentException(\"Malformed ChangePayload schema identity.\", nameof(payload));"
        );
        code.AppendLine("return FromPayloadCore(payload).WithRecordedAt(payload.RecordedAt);");
        code.CloseBlock();
        // The strict conversion partitions the envelope first and rejects blind
        // paths, so there is exactly one routing seam for mixed requests.
        foreach (
            var member in members.Where(static member =>
                !member.Property.IsTransportIgnored && (SparseChangeSetBasicsEmitter.IsKeyed(member) || SparseChangeSetBasicsEmitter.IsDict(member))
            )
        )
        {
            SparseChangeSetPayloadItemEmitter.AppendPayloadItemHelper(
                code,
                member,
                members,
                runtime,
                SparseChangeSetPayloadEmitter.RequirePayloadContainerName(dialect, modelType),
                modelType
            );
        }
        code.AppendLine();
        code.AppendLine("private static " + runtime + "Optional<Fragment?> __SparsePayloadFragment(" + endpoint + "<" + payloadRoot + "> endpoint)");
        code.OpenBlock();
        code.AppendLine("if (endpoint is null) throw new global::System.ArgumentException(\"A root endpoint is required.\", nameof(endpoint));");
        code.AppendLine("var root = endpoint.ToOptional();");
        code.AppendLine(
            "return !root.IsPresent ? "
                + runtime
                + "Optional<Fragment?>.Missing : "
                + runtime
                + "Optional<Fragment?>.Present(root.Value?.ToFragment());"
        );
        code.CloseBlock();
    }
}
