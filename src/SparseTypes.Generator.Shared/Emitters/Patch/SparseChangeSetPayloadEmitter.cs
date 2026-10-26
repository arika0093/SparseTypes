using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp;
using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits the typed, serializer-facing payload DTOs for one model.</summary>
internal static class SparseChangeSetPayloadEmitter
{
    internal static void AppendPayload(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        ImmutableArray<string> ignoredSettablePropertyNames = default,
        bool emitContainers = true,
        bool emitFacade = true,
        string? implementationNamespace = null,
        string containerAccessibility = "public",
        ImmutableArray<SparsePayloadJsonPocoModel> payloadJsonPocos = default,
        SparsePayloadContract? payloadContract = null,
        SharedIndentedBuilder? jsonCodecBuilder = null,
        bool emitTopLevelJsonCodecs = false
    )
    {
        if (modelType is null)
        {
            throw new ArgumentNullException(nameof(modelType), "Payload DTO emission requires an explicit model type.");
        }

        var runtime = dialect.RuntimeNamespace;
        var endpoint = runtime + "ChangePayloadEndpoint";
        var payloadCore = PayloadName(modelType, "Core");
        var payloadChange = PayloadName(modelType, "Change");
        var contract = payloadContract ?? SparsePayloadContract.Create(members);
        var variants = contract.TransportMembers.Select(static member => member.Member).ToArray();
        var payloadJson = PayloadJsonEmissionContext.TryCreate(
            contract,
            dialect,
            modelType,
            implementationNamespace,
            emitTopLevelJsonCodecs,
            payloadJsonPocos
        );

        if (emitContainers)
        {
            AppendPayloadCoreContainer(
                code,
                members,
                dialect,
                modelType,
                ignoredSettablePropertyNames,
                payloadCore,
                payloadChange,
                containerAccessibility
            );
        }

        if (emitFacade)
        {
            AppendChangePayloadFacadeBlock(code, members, dialect, modelType, payloadJson);
        }

        if (emitContainers)
        {
            AppendPayloadVariantContainers(
                code,
                contract.TransportMembers,
                dialect,
                modelType,
                endpoint,
                payloadChange,
                variants,
                implementationNamespace,
                containerAccessibility
            );
            if (payloadJson is { SupportsJson: true })
            {
                AppendPayloadJsonImplementation(
                    jsonCodecBuilder ?? code,
                    dialect,
                    modelType,
                    containerAccessibility,
                    payloadJson,
                    nestInContainer: !emitTopLevelJsonCodecs
                );
            }
        }
    }

    private static void AppendPayloadJsonImplementation(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        string containerAccessibility,
        PayloadJsonEmissionContext context,
        bool nestInContainer
    )
    {
        if (nestInContainer)
        {
            code.AppendLine(containerAccessibility + " static partial class " + RequirePayloadContainerName(dialect, modelType));
            code.OpenBlock();
        }

        SparsePayloadJsonConverterEmitter.AppendConverter(code, context, containerAccessibility);
        SparsePayloadJsonResolverEmitter.AppendResolver(code, context);
        if (nestInContainer)
        {
            code.CloseBlock();
        }

        code.AppendLine();
    }

    private static void AppendPayloadCoreContainer(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        ImmutableArray<string> ignoredSettablePropertyNames,
        string payloadCore,
        string payloadChange,
        string containerAccessibility
    )
    {
        code.AppendLine("/// <summary>Serializer-facing payload DTOs for this model.</summary>");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine(containerAccessibility + " static partial class " + RequirePayloadContainerName(dialect, modelType));
        code.OpenBlock();
        code.Indent();
        // Payload DTOs stay permissive toward additive extensions: unknown
        // JSON properties are ignored, while unknown operations (member
        // discriminators, item kinds, endpoint states) still fail.
        code.AppendLine("/// <summary>Transport core carrying the validated member changes.</summary>");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine("public class " + payloadCore);
        code.OpenBlock();
        // Public DTO surface precedes the internal conversion seams so the
        // emitted container reads public -> internal -> private.
        SparseChangeSetMixedEmitter.AppendMixedPayloadSurface(code, members, dialect, modelType, ignoredSettablePropertyNames);
        SparseChangeSetPayloadValidateEmitter.AppendCoreValidate(code, members, dialect, modelType);
        // XML docs precede serialization attributes so the compiler associates
        // them with the member (CS1591).
        code.AppendLine("/// <summary>Gets or sets the member changes carried by this core.</summary>");
        AppendJsonProperty(code, "Changes", 1, dialect.EmitJson);
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        var modelChange = PayloadTypeName(dialect, modelType, "ModelChange");
        var changeKind = PayloadTypeName(dialect, modelType, "ModelChangeKind");
        code.AppendLine("internal " + modelChange + "? TypedChange { get; set; }");
        code.AppendLine("internal global::System.Collections.Generic.IEnumerable<" + payloadChange + "> EnumerateChanges()");
        code.OpenBlock();
        code.AppendLine("var change = TypedChange;");
        code.AppendLine("if (change is null) yield break;");
        code.AppendLine("if (change.Kind == " + changeKind + ".Replace) yield break;");
        code.AppendLine("if (change.Kind != " + changeKind + ".Update) throw new global::System.ArgumentOutOfRangeException(nameof(TypedChange));");
        code.AppendLine(
            "if (change.Members is null) throw new global::System.ArgumentException(\"An update requires a members object.\", nameof(TypedChange));"
        );
        foreach (var member in members.Where(static member => !member.Property.IsTransportIgnored))
        {
            var property = SparseNaming.EscapeIdentifier(member.Property.Name);
            code.AppendLine("if (change.Members." + property + " is not null) yield return change.Members." + property + ";");
        }
        code.CloseBlock();
        if (modelType is not null)
        {
            code.AppendLine("internal " + modelType + ".ChangeSet ToChangeSetCore() => " + modelType + ".ChangeSet.FromPayloadCore(this);");
        }

        if (modelType is not null && !SparseDownstreamPolicy.HasAnyNonFullPolicy(dialect))
        {
            code.AppendLine("internal " + modelType + ".Patch ToPatchCore() => " + modelType + ".ChangeSet.PatchFromPayloadCore(this);");
        }

        if (modelType is not null)
        {
            code.AppendLine("internal static " + payloadCore + " FromPatchCore(" + modelType + ".Patch patch) => patch.ToChangePayloadCore();");
        }

        if (modelType is not null && SparseDownstreamPolicy.HasAnyNonFullPolicy(dialect))
        {
            SparseChangeSetPayloadProjectionEmitter.AppendCoreToPatch(code, members, dialect, modelType);
        }
        code.CloseBlock();
        code.Unindent();
        code.CloseBlock();
        code.AppendLine();
    }

    private static void AppendChangePayloadFacadeBlock(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        PayloadJsonEmissionContext? payloadJson
    )
    {
        code.AppendLine("/// <summary>Serializable envelope carrying the validated transition for transport.</summary>");
        if (payloadJson is { SupportsJson: true })
        {
            code.AppendLine(
                "[global::System.Text.Json.Serialization.JsonConverter(typeof(" + payloadJson.ConverterReference(payloadJson.ModelType) + "))]"
            );
        }
        code.AppendLine("public sealed class ChangePayload : " + PayloadTypeName(dialect, modelType, "Core"));
        code.OpenBlock();
        code.AppendLine("/// <summary>Gets or sets the provisional wire version token.</summary>");
        AppendJsonProperty(code, "Version", 0, dialect.EmitJson);
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine("public string? Version { get; set; }");
        if (modelType is not null)
        {
            SparseChangeSetPayloadShapeEmitter.AppendChangeProperty(code, PayloadTypeName(dialect, modelType, "ModelChange"));
        }
        if (modelType is not null)
        {
            code.AppendLine("/// <summary>Converts this validated envelope to a complete change set.</summary>");
            code.AppendLine(
                "/// <remarks>Redacted or otherwise incomplete histories are rejected; project them with <see cref=\"ToPatch\"/> instead.</remarks>"
            );
            code.AppendLine("/// <returns>The validated change set.</returns>");
            code.AppendLine("public " + modelType + ".ChangeSet ToChangeSet() => " + modelType + ".ChangeSet.FromPayload(this);");
            // Keep baseline-discarding conversion discoverable on the validated
            // envelope.
            if (!SparseDownstreamPolicy.HasOwnNonFullPolicy(members, dialect))
            {
                code.AppendLine("/// <summary>Discards baseline information and returns the equivalent desired-operation patch.</summary>");
                code.AppendLine(
                    "/// <remarks>Redacted before-states project to their requested after-state without historical comparison; ordinary members project their after-state too. The result is baseline-free and can no longer rebase or report conflicts.</remarks>"
                );
                code.AppendLine("/// <returns>The baseline-free patch.</returns>");
                code.AppendLine("public new " + modelType + ".Patch ToPatch()");
                code.OpenBlock();
                SparseChangeSetMixedEmitter.AppendVersionGuard(code, dialect);
                code.AppendLine("return base.ToPatch();");
                code.CloseBlock();
            }
            // Every public interpretation path validates it before reaching the
            // versionless core seam.
            SparseChangeSetMixedEmitter.AppendEnvelopeVersionOverrides(code, dialect, modelType);
            SparseChangeSetPayloadValidateEmitter.AppendEnvelopeValidate(code, dialect, modelType);
            code.AppendLine("/// <summary>Builds a baseline-free command envelope from a patch.</summary>");
            code.AppendLine("/// <remarks>Before-states are redacted by construction; the result only supports <see cref=\"ToPatch\"/>.</remarks>");
            code.AppendLine("/// <param name=\"patch\">The patch to convert.</param>");
            code.AppendLine("/// <returns>The baseline-free command envelope.</returns>");
            code.AppendLine("public static ChangePayload FromPatch(" + modelType + ".Patch patch)");
            code.OpenBlock();
            code.AppendLine("if (patch is null) throw new global::System.ArgumentNullException(nameof(patch));");
            code.AppendLine(
                "return new ChangePayload { Version = "
                    + SymbolDisplay.FormatLiteral(dialect.ChangePayloadVersion, true)
                    + ", Change = "
                    + PayloadTypeName(dialect, modelType, "Core")
                    + ".FromPatchCore(patch).TypedChange };"
            );
            code.CloseBlock();
            SparseChangeSetPayloadProjectionEmitter.AppendRootToPatch(code, members, dialect, modelType);
        }
        code.CloseBlock();
        code.AppendLine();
    }

    private static void AppendPayloadVariantContainers(
        SharedIndentedBuilder code,
        ImmutableArray<SparsePayloadMemberContract> contractMembers,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        string endpoint,
        string payloadChange,
        SparseMemberModel[] variants,
        string? implementationNamespace,
        string containerAccessibility
    )
    {
        code.AppendLine(containerAccessibility + " static partial class " + RequirePayloadContainerName(dialect, modelType));
        code.OpenBlock();
        code.Indent();
        // XML docs precede serialization attributes so the compiler associates
        // them with the member (CS1591).
        code.AppendLine("/// <summary>Base type for the internal change representation.</summary>");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine("public abstract class " + payloadChange + " { }");
        code.AppendLine();

        foreach (var member in variants)
        {
            SparseChangeSetPayloadItemEmitter.AppendMemberPayload(code, member, endpoint, dialect, modelType, implementationNamespace);
            code.AppendLine();
        }
        SparseChangeSetPayloadShapeEmitter.AppendPublicRoles(code, contractMembers, endpoint, modelType, dialect, implementationNamespace);
        code.Unindent();
        code.CloseBlock();
    }

    internal static void AppendToPayload(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        SparseOperationTarget? target = null
    ) =>
        // Reloc-2 split: transfer bodies live in the dedicated TransferEmitter.
        SparseChangeSetPayloadTransferEmitter.AppendToPayload(code, members, dialect, modelType, target);

    internal static string PayloadName(string? modelType, string suffix) => suffix;

    internal static string PayloadMemberName(string? modelType, string suffix, int id) => suffix + id;

    internal static string PayloadTypeName(string payloadContainerName, string? modelType, string suffix) =>
        payloadContainerName + "." + PayloadName(modelType, suffix);

    internal static string PayloadTypeName(SparseFragmentPatchEmitter.SparsePatchDialect dialect, string? modelType, string suffix) =>
        PayloadTypeName(RequirePayloadContainerName(dialect, modelType), modelType, suffix);

    internal static string PayloadMemberTypeName(string payloadContainerName, string? modelType, string suffix, int id) =>
        payloadContainerName + "." + PayloadMemberName(modelType, suffix, id);

    internal static string PayloadMemberTypeName(SparseFragmentPatchEmitter.SparsePatchDialect dialect, string? modelType, string suffix, int id) =>
        PayloadMemberTypeName(RequirePayloadContainerName(dialect, modelType), modelType, suffix, id);

    internal static string RequirePayloadContainerName(SparseFragmentPatchEmitter.SparsePatchDialect dialect, string? modelType) =>
        !string.IsNullOrWhiteSpace(dialect.PayloadImplementationContainerPrefix)
            ? SparseGeneratedPlacement.GetContainerForQualifiedName(modelType ?? "SparseGeneratedModel", CancellationToken.None)
                + dialect.PayloadImplementationContainerPrefix
            : throw new global::System.InvalidOperationException(
                "PayloadImplementationContainerPrefix must be configured when emitting ChangePayload DTOs."
            );

    /// <summary>Resolves the child payload reference for split emission.</summary>
    /// <remarks>Single-file emission keeps the legacy <c>ChildModel.Container.Payload</c> nesting.</remarks>
    /// <param name="dialect">Owning patch dialect.</param>
    /// <param name="childModelType">Child model type name.</param>
    /// <param name="suffix">Payload suffix.</param>
    /// <param name="implementationNamespace">Explicit namespace, or null for single-file.</param>
    /// <returns>The reference to use from generated code.</returns>
    internal static string QualifiedChildPayloadType(
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string childModelType,
        string suffix,
        string? implementationNamespace
    )
    {
        if (string.IsNullOrEmpty(implementationNamespace))
        {
            return childModelType + "." + PayloadTypeName(dialect, childModelType, suffix);
        }

        var simple = RequirePayloadContainerName(dialect, childModelType);
        return SparseGeneratedPlacement.QualifyPayloadContainer(simple, implementationNamespace) + "." + PayloadName(childModelType, suffix);
    }

    internal static void AppendIgnoreNull(SharedIndentedBuilder code, bool emitJson = true)
    {
        // Payload DTO metadata is intentionally emitted only by the explicit resolver.
    }

    internal static void AppendJsonProperty(SharedIndentedBuilder code, string name, int order, bool emitJson = true)
    {
        // Payload DTO metadata is intentionally emitted only by the explicit resolver.
    }

    internal static void AppendIgnoreDefault(SharedIndentedBuilder code, bool emitJson = true)
    {
        // Payload DTO metadata is intentionally emitted only by the explicit resolver.
    }
}
