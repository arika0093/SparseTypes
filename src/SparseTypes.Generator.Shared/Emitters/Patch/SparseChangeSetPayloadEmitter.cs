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
        ImmutableArray<SparsePayloadJsonPocoModel> payloadJsonPocos = default
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var endpoint = runtime + "ChangePayloadEndpoint";
        var payloadCore = PayloadName(modelType, "Core");
        var payloadRoot = PayloadName(modelType, "Root");
        var payloadChange = PayloadName(modelType, "Change");
        var variants = members.Where(static member => !member.Property.IsJsonIgnored).ToArray();
        // Write-only members never appear in the read projection; their
        // transport variants below still carry the command after-state.
        var readable = variants
            .Where(member =>
                dialect.GetTransport(member.Property.Name) != SparseMemberTransport.WriteOnly
            )
            .ToArray();

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
            AppendChangePayloadFacadeBlock(code, members, dialect, modelType);
            var payloadJson = PayloadJsonEmissionContext.TryCreate(
                members,
                dialect,
                modelType,
                implementationNamespace,
                payloadJsonPocos
            );
            if (payloadJson is not null)
            {
                SparsePayloadJsonConverterEmitter.AppendConverter(code, payloadJson);
                SparsePayloadJsonResolverEmitter.AppendResolver(code, payloadJson);
            }
        }

        if (emitContainers)
        {
            AppendPayloadVariantContainers(
                code,
                dialect,
                modelType,
                runtime,
                endpoint,
                payloadRoot,
                payloadChange,
                variants,
                readable,
                implementationNamespace,
                containerAccessibility
            );
        }
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
        code.AppendLine(
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        code.AppendLine(
            containerAccessibility
                + " static partial class "
                + RequirePayloadContainerName(dialect, modelType)
        );
        code.OpenBlock();
        code.Indent();
        // Payload DTOs stay permissive toward additive extensions: unknown
        // JSON properties are ignored, while unknown operations (member
        // discriminators, item kinds, endpoint states) still fail.
        code.AppendLine(
            "/// <summary>Transport core carrying the validated member changes.</summary>"
        );
        code.AppendLine(
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        code.AppendLine("public class " + payloadCore);
        code.OpenBlock();
        // XML docs precede serialization attributes so the compiler associates
        // them with the member (CS1591).
        code.AppendLine(
            "/// <summary>Gets or sets the member changes carried by this core.</summary>"
        );
        AppendJsonProperty(code, "Changes", 1, dialect.EmitJson);
        code.AppendLine(
            "public global::System.Collections.Generic.List<"
                + payloadChange
                + ">? Changes { get; set; }"
        );
        // Public DTO surface precedes the internal conversion seams so the
        // emitted container reads public -> internal -> private.
        SparseChangeSetMixedEmitter.AppendMixedPayloadSurface(
            code,
            members,
            dialect,
            modelType,
            ignoredSettablePropertyNames
        );
        SparseChangeSetPayloadValidateEmitter.AppendCoreValidate(code, members, dialect, modelType);
        if (modelType is not null)
        {
            code.AppendLine(
                "internal "
                    + modelType
                    + ".ChangeSet ToChangeSetCore() => "
                    + modelType
                    + ".ChangeSet.FromPayloadCore(this);"
            );
        }

        if (modelType is not null && !SparseDownstreamPolicy.HasAnyNonFullPolicy(dialect))
        {
            code.AppendLine(
                "internal "
                    + modelType
                    + ".Patch ToPatchCore() => "
                    + modelType
                    + ".ChangeSet.PatchFromPayloadCore(this);"
            );
        }

        if (modelType is not null)
        {
            code.AppendLine(
                "internal static "
                    + payloadCore
                    + " FromPatchCore("
                    + modelType
                    + ".Patch patch) => patch.ToChangePayloadCore();"
            );
        }

        if (modelType is not null && SparseDownstreamPolicy.HasAnyNonFullPolicy(dialect))
        {
            SparseChangeSetPayloadProjectionEmitter.AppendCoreToPatch(
                code,
                members,
                dialect,
                modelType
            );
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
        string? modelType
    )
    {
        code.AppendLine(
            "/// <summary>Serializable envelope carrying the validated transition for transport.</summary>"
        );
        code.AppendLine(
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Advanced)]"
        );
        code.AppendLine(
            "public sealed class ChangePayload : "
                + PayloadTypeName(dialect, modelType, "Core")
                + (
                    dialect.PayloadSchemaProviderType is null
                        ? string.Empty
                        : ", " + dialect.PayloadSchemaProviderType
                )
        );
        code.OpenBlock();
        if (
            dialect.PayloadSchemaProviderType is not null
            && dialect.PayloadSchemaDescriptorType is not null
            && modelType is not null
        )
        {
            code.AppendLine("/// <inheritdoc />");
            code.AppendLine(
                "global::System.Collections.Generic.IReadOnlyList<"
                    + dialect.PayloadSchemaDescriptorType
                    + "> "
                    + dialect.PayloadSchemaProviderType
                    + ".GetSparsePayloadSchemas() => new "
                    + dialect.PayloadSchemaDescriptorType
                    + "[]"
            );
            code.OpenBlock();
            code.AppendLine(
                "new(typeof(ChangePayload), "
                    + SymbolDisplay.FormatLiteral(
                        SparseSchemaBaseName(modelType) + "_ChangePayload",
                        true
                    )
                    + ", "
                    + SymbolDisplay.FormatLiteral(PayloadSchema(modelType), true)
                    + "),"
            );
            code.AppendLine(
                "new(typeof("
                    + PayloadTypeName(dialect, modelType, "Change")
                    + "), "
                    + SymbolDisplay.FormatLiteral(SparseSchemaBaseName(modelType) + "_Change", true)
                    + ", "
                    + SymbolDisplay.FormatLiteral(ChangeUnionSchema(members, modelType), true)
                    + "),"
            );
            code.AppendLine(
                "new(typeof("
                    + PayloadTypeName(dialect, modelType, "RootChange")
                    + "), "
                    + SymbolDisplay.FormatLiteral(
                        SparseSchemaBaseName(modelType) + "_RootChange",
                        true
                    )
                    + ", \"{}\"),"
            );
            code.AppendLine(
                "new(typeof("
                    + PayloadTypeName(dialect, modelType, "Core")
                    + "), "
                    + SymbolDisplay.FormatLiteral(SparseSchemaBaseName(modelType) + "_Core", true)
                    + ", \"{}\"),"
            );
            code.AppendLine(
                "new(typeof("
                    + PayloadTypeName(dialect, modelType, "Root")
                    + "), "
                    + SymbolDisplay.FormatLiteral(SparseSchemaBaseName(modelType) + "_Root", true)
                    + ", \"{}\"),"
            );
            foreach (var member in members.Where(static member => !member.Property.IsJsonIgnored))
            {
                code.AppendLine(
                    "new(typeof("
                        + PayloadMemberTypeName(dialect, modelType, "Change", member.Id)
                        + "), "
                        + SymbolDisplay.FormatLiteral(
                            SparseSchemaMemberName(modelType, member.Property.Name),
                            true
                        )
                        + ", \"{}\"),"
                );
            }

            foreach (
                var member in members.Where(static member =>
                    !member.Property.IsJsonIgnored
                    && (
                        SparseChangeSetBasicsEmitter.IsKeyed(member)
                        || SparseChangeSetBasicsEmitter.IsDict(member)
                    )
                )
            )
            {
                code.AppendLine(
                    "new(typeof("
                        + PayloadMemberTypeName(dialect, modelType, "Item", member.Id)
                        + "), "
                        + SymbolDisplay.FormatLiteral(
                            SparseSchemaBaseName(modelType)
                                + "_Item_"
                                + SchemaNamePart(member.Property.Name),
                            true
                        )
                        + ", \"{}\"),"
                );
            }

            code.Unindent().AppendLine("};");
        }
        code.AppendLine("/// <summary>Gets or sets the provisional wire version token.</summary>");
        AppendJsonProperty(code, "Version", 0, dialect.EmitJson);
        code.AppendLine("public string? Version { get; set; }");
        if (modelType is not null)
        {
            code.AppendLine(
                "/// <summary>Converts this validated envelope to a complete change set.</summary>"
            );
            code.AppendLine(
                "/// <remarks>Redacted or otherwise incomplete histories are rejected; project them with <see cref=\"ToPatch\"/> instead.</remarks>"
            );
            code.AppendLine("/// <returns>The validated change set.</returns>");
            code.AppendLine(
                "public "
                    + modelType
                    + ".ChangeSet ToChangeSet() => "
                    + modelType
                    + ".ChangeSet.FromPayload(this);"
            );
            // Keep baseline-discarding conversion discoverable on the validated
            // envelope.
            if (!SparseDownstreamPolicy.HasOwnNonFullPolicy(members, dialect))
            {
                code.AppendLine(
                    "/// <summary>Discards baseline information and returns the equivalent desired-operation patch.</summary>"
                );
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
            code.AppendLine(
                "/// <summary>Builds a baseline-free command envelope from a patch.</summary>"
            );
            code.AppendLine(
                "/// <remarks>Before-states are redacted by construction; the result only supports <see cref=\"ToPatch\"/>.</remarks>"
            );
            code.AppendLine("/// <param name=\"patch\">The patch to convert.</param>");
            code.AppendLine("/// <returns>The baseline-free command envelope.</returns>");
            code.AppendLine("public static ChangePayload FromPatch(" + modelType + ".Patch patch)");
            code.OpenBlock();
            code.AppendLine(
                "if (patch is null) throw new global::System.ArgumentNullException(nameof(patch));"
            );
            code.AppendLine(
                "return new ChangePayload { Version = "
                    + SymbolDisplay.FormatLiteral(dialect.ChangePayloadVersion, true)
                    + ", Changes = "
                    + PayloadTypeName(dialect, modelType, "Core")
                    + ".FromPatchCore(patch).Changes };"
            );
            code.CloseBlock();
            SparseChangeSetPayloadProjectionEmitter.AppendRootToPatch(
                code,
                members,
                dialect,
                modelType
            );
        }
        code.CloseBlock();
        code.AppendLine();
    }

    private static string SparseSchemaBaseName(string modelType)
    {
        var identity = modelType.Replace("global::", string.Empty);
        var genericStart = identity.IndexOf('<');
        var genericArguments = string.Empty;
        if (genericStart >= 0)
        {
            genericArguments = "_" + SchemaNamePart(identity[(genericStart + 1)..^1]);
            identity = identity[..genericStart];
        }
        return identity.Replace('.', '_').Replace('+', '_') + genericArguments;
    }

    private static string SparseSchemaMemberName(string modelType, string memberName) =>
        SparseSchemaBaseName(modelType) + "_Change_" + SchemaNamePart(memberName);

    private static string SchemaNamePart(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var character in name)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (character == '_')
            {
                builder.Append("__");
            }
            else
            {
                builder.Append("_x").Append(((int)character).ToString("X4")).Append('_');
            }
        }
        return builder.ToString();
    }

    private static string PayloadSchema(string modelType) =>
        "{\"type\":\"object\",\"properties\":{\"version\":{\"type\":\"string\"},\"changes\":{\"type\":\"array\",\"items\":{\"$ref\":\"#/components/schemas/"
        + SparseSchemaBaseName(modelType)
        + "_Change\"}}}}";

    private static string ChangeUnionSchema(
        ImmutableArray<SparseMemberModel> members,
        string modelType
    )
    {
        var baseName = SparseSchemaBaseName(modelType);
        var rootDiscriminator = new System.Text.StringBuilder("$root");
        while (
            members.Any(member =>
                !member.Property.IsJsonIgnored
                && member.Property.Name == rootDiscriminator.ToString()
            )
        )
        {
            rootDiscriminator.Append('$');
        }

        var result = new System.Text.StringBuilder("{\"oneOf\":[{\"$ref\":\"#/components/schemas/")
            .Append(baseName)
            .Append("_RootChange\"}");
        foreach (var member in members.Where(static member => !member.Property.IsJsonIgnored))
        {
            result
                .Append(",{\"$ref\":\"#/components/schemas/")
                .Append(baseName)
                .Append("_Change_")
                .Append(SchemaNamePart(member.Property.Name))
                .Append("\"}");
        }

        result
            .Append("],\"discriminator\":{\"propertyName\":\"member\",\"mapping\":{\"")
            .Append(rootDiscriminator)
            .Append("\":\"#/components/schemas/")
            .Append(baseName)
            .Append("_RootChange\"");
        foreach (
            var property in members
                .Where(static member => !member.Property.IsJsonIgnored)
                .Select(static member => member.Property.Name)
        )
        {
            result
                .Append(",\"")
                .Append(JsonStringContent(property))
                .Append("\":\"#/components/schemas/")
                .Append(baseName)
                .Append("_Change_")
                .Append(SchemaNamePart(property))
                .Append("\"");
        }

        return result.Append("}}}").ToString();
    }

    private static string JsonStringContent(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (character < 0x20)
                    {
                        builder.Append("\\u").Append(((int)character).ToString("x4"));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }
        return builder.ToString();
    }

    private static void AppendPayloadVariantContainers(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        string runtime,
        string endpoint,
        string payloadRoot,
        string payloadChange,
        SparseMemberModel[] variants,
        SparseMemberModel[] readable,
        string? implementationNamespace,
        string containerAccessibility
    )
    {
        code.AppendLine(
            containerAccessibility
                + " static partial class "
                + RequirePayloadContainerName(dialect, modelType)
        );
        code.OpenBlock();
        code.Indent();
        code.AppendLine(
            "/// <summary>Snapshot of the present member values for whole-root transport.</summary>"
        );
        code.AppendLine(
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        code.AppendLine("public sealed class " + payloadRoot);
        code.OpenBlock();
        code.AppendLine("/// <summary>Gets or sets the snapshot member values.</summary>");
        AppendJsonProperty(code, "Members", 0, dialect.EmitJson);
        code.AppendLine(
            "public global::System.Collections.Generic.List<"
                + payloadChange
                + "> Members { get; set; } = new();"
        );
        code.AppendLine("/// <summary>Builds a snapshot from a fragment.</summary>");
        code.AppendLine("/// <param name=\"value\">The fragment to snapshot.</param>");
        code.AppendLine("/// <returns>The snapshot root.</returns>");
        code.AppendLine("public static " + payloadRoot + " FromFragment(Fragment value)");
        code.OpenBlock();
        code.AppendLine("var result = new " + payloadRoot + "();");
        SparseChangeSetPayloadSnapshotEmitter.AppendFromFragmentMembers(
            code,
            System.Collections.Immutable.ImmutableArray.CreateRange(readable),
            endpoint,
            runtime,
            dialect,
            modelType,
            false,
            implementationNamespace
        );
        code.AppendLine("return result;");
        code.CloseBlock();
        code.AppendLine("/// <summary>Converts this snapshot to a fragment.</summary>");
        code.AppendLine("/// <returns>The converted fragment.</returns>");
        code.AppendLine("public Fragment ToFragment()");
        // The redacting overload stays internal and follows the public
        // surface so the emitted container reads public -> internal.
        code.OpenBlock();
        code.AppendLine(
            "if (Members is null) throw new global::System.ArgumentException(\"Root members must not be null.\", nameof(Members));"
        );
        foreach (var member in readable)
        {
            var valueType = SparseChangeSetBasicsEmitter.IsNested(member)
                ? member.ChildFragmentType + "?"
                : SparseChangeSetBasicsEmitter.FragmentValueType(member);
            code.AppendLine(
                runtime
                    + "Optional<"
                    + valueType
                    + "> rootMember"
                    + member.Id
                    + " = "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Missing;"
            );
        }
        foreach (var member in readable)
        {
            code.AppendLine("bool seen" + member.Id + " = false;");
        }

        code.AppendLine("foreach (var member in Members)");
        code.OpenBlock();
        code.AppendLine("switch (member)");
        code.OpenBlock();
        foreach (var member in readable)
        {
            code.AppendLine("case " + PayloadMemberName(modelType, "Change", member.Id) + " item:");
            code.AppendLine(
                "if (seen"
                    + member.Id
                    + ") throw new global::System.ArgumentException(\"Root contains duplicate member values.\", nameof(Members));"
            );
            code.AppendLine("seen" + member.Id + " = true;");
            code.AppendLine(
                "if (item.Value is null) throw new global::System.ArgumentException(\"Root member value is required.\", nameof(Members));"
            );
            code.AppendLine(
                "if (item.Value.State == "
                    + runtime
                    + "ChangePayloadState.Redacted) throw new global::System.ArgumentException(\"A redacted root member cannot convert to a fragment.\", nameof(Members));"
            );
            if (SparseChangeSetBasicsEmitter.IsNested(member))
            {
                var childFragment = member.ChildFragmentType + "?";
                code.AppendLine("var member" + member.Id + " = item.Value.ToOptional();");
                code.AppendLine(
                    "rootMember"
                        + member.Id
                        + " = member"
                        + member.Id
                        + ".IsPresent ? "
                        + runtime
                        + "Optional<"
                        + childFragment
                        + ">.Present(member"
                        + member.Id
                        + ".Value is null ? null : member"
                        + member.Id
                        + ".Value.ToFragment()) : "
                        + runtime
                        + "Optional<"
                        + childFragment
                        + ">.Missing;"
                );
            }
            else
            {
                code.AppendLine("rootMember" + member.Id + " = item.Value.ToOptional();");
            }
            code.AppendLine("break;");
        }
        code.AppendLine(
            "default: throw new global::System.ArgumentException(\"Root contains an unknown member value.\", nameof(Members));"
        );
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("return new Fragment");
        code.OpenBlock();
        foreach (var member in readable)
        {
            code.AppendLine(
                SparseNaming.EscapeIdentifier(member.Property.Name)
                    + " = rootMember"
                    + member.Id
                    + ","
            );
        }

        code.Unindent().AppendLine("};");
        code.CloseBlock();
        code.AppendLine(
            "internal static " + payloadRoot + " FromFragment(Fragment value, bool redactBefores)"
        );
        code.OpenBlock();
        code.AppendLine("var result = new " + payloadRoot + "();");
        SparseChangeSetPayloadSnapshotEmitter.AppendFromFragmentMembers(
            code,
            System.Collections.Immutable.ImmutableArray.CreateRange(readable),
            endpoint,
            runtime,
            dialect,
            modelType,
            true,
            implementationNamespace
        );
        code.AppendLine("return result;");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine();

        // XML docs precede serialization attributes so the compiler associates
        // them with the member (CS1591).
        code.AppendLine("/// <summary>Base type for the per-member change variants.</summary>");
        if (dialect.EmitJson)
        {
            code.AppendLine(
                "[global::System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = \"member\")]"
            );
            var rootDiscriminatorBuilder = new System.Text.StringBuilder("$root");
            while (
                variants.Any(member => member.Property.Name == rootDiscriminatorBuilder.ToString())
            )
            {
                rootDiscriminatorBuilder.Append('$');
            }

            var rootDiscriminator = rootDiscriminatorBuilder.ToString();
            code.AppendLine(
                "[global::System.Text.Json.Serialization.JsonDerivedType(typeof("
                    + PayloadName(modelType, "RootChange")
                    + "), "
                    + SymbolDisplay.FormatLiteral(rootDiscriminator, true)
                    + ")]"
            );
            foreach (var member in variants)
            {
                code.AppendLine(
                    "[global::System.Text.Json.Serialization.JsonDerivedType(typeof("
                        + PayloadMemberName(modelType, "Change", member.Id)
                        + "), "
                        + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                        + ")]"
                );
            }
        }
        code.AppendLine(
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        code.AppendLine("public abstract class " + payloadChange + " { }");
        code.AppendLine();

        code.AppendLine(
            "/// <summary>Whole-root change carrying the before and after snapshots.</summary>"
        );
        code.AppendLine(
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        code.AppendLine(
            "public sealed class " + PayloadName(modelType, "RootChange") + " : " + payloadChange
        );
        code.OpenBlock();
        code.AppendLine("/// <summary>Gets or sets the before snapshot.</summary>");
        AppendIgnoreNull(code, dialect.EmitJson);
        AppendJsonProperty(code, "Before", 0, dialect.EmitJson);
        if (dialect.EmitJson)
        {
            SparseChangePayloadJsonBridgeEmitter.AppendEndpointConverterAttribute(
                code,
                payloadRoot,
                implementationNamespace
            );
        }

        code.AppendLine("public " + endpoint + "<" + payloadRoot + ">? Before { get; set; }");
        code.AppendLine("/// <summary>Gets or sets the after snapshot.</summary>");
        AppendIgnoreNull(code, dialect.EmitJson);
        AppendJsonProperty(code, "After", 1, dialect.EmitJson);
        if (dialect.EmitJson)
        {
            SparseChangePayloadJsonBridgeEmitter.AppendEndpointConverterAttribute(
                code,
                payloadRoot,
                implementationNamespace
            );
        }

        code.AppendLine("public " + endpoint + "<" + payloadRoot + ">? After { get; set; }");
        code.CloseBlock();
        code.AppendLine();

        foreach (var member in variants)
        {
            SparseChangeSetPayloadItemEmitter.AppendMemberPayload(
                code,
                member,
                endpoint,
                dialect,
                modelType,
                implementationNamespace
            );
            code.AppendLine();
        }
        // The endpoint/kind bridge preserves the v1 wire shape once the runtime
        // stopped carrying serializer attributes.
        if (dialect.EmitJson && implementationNamespace is null)
        {
            SparseChangePayloadJsonBridgeEmitter.AppendBridgeConverters(code, dialect);
        }

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
        SparseChangeSetPayloadTransferEmitter.AppendToPayload(
            code,
            members,
            dialect,
            modelType,
            target
        );

    internal static string PayloadName(string? modelType, string suffix)
    {
        var modelIdentity = modelType ?? "SparseGeneratedModel";
        var shortModelId = SparseNaming.GetStableTypeHash(modelIdentity, CancellationToken.None);
        return suffix + "_" + shortModelId;
    }

    internal static string PayloadMemberName(string? modelType, string suffix, int id)
    {
        var modelIdentity = modelType ?? "SparseGeneratedModel";
        var shortModelId = SparseNaming.GetStableTypeHash(modelIdentity, CancellationToken.None);
        return suffix + id + "_" + shortModelId;
    }

    internal static string PayloadTypeName(
        string payloadContainerName,
        string? modelType,
        string suffix
    ) => payloadContainerName + "." + PayloadName(modelType, suffix);

    internal static string PayloadTypeName(
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        string suffix
    ) => PayloadTypeName(RequirePayloadContainerName(dialect, modelType), modelType, suffix);

    internal static string PayloadMemberTypeName(
        string payloadContainerName,
        string? modelType,
        string suffix,
        int id
    ) => payloadContainerName + "." + PayloadMemberName(modelType, suffix, id);

    internal static string PayloadMemberTypeName(
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        string suffix,
        int id
    ) =>
        PayloadMemberTypeName(
            RequirePayloadContainerName(dialect, modelType),
            modelType,
            suffix,
            id
        );

    internal static string RequirePayloadContainerName(
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    ) =>
        !string.IsNullOrWhiteSpace(dialect.PayloadImplementationContainerPrefix)
            ? dialect.PayloadImplementationContainerPrefix
                + "_"
                + SparseNaming.GetStableTypeHash(
                    modelType ?? "SparseGeneratedModel",
                    CancellationToken.None
                )
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
        return SparseGeneratedPlacement.QualifyPayloadContainer(simple, implementationNamespace)
            + "."
            + PayloadName(childModelType, suffix);
    }

    internal static void AppendIgnoreNull(SharedIndentedBuilder code, bool emitJson = true)
    {
        if (!emitJson)
        {
            return;
        }

        code.AppendLine(
            "[global::System.Text.Json.Serialization.JsonIgnore(Condition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]"
        );
    }

    internal static void AppendJsonProperty(
        SharedIndentedBuilder code,
        string name,
        int order,
        bool emitJson = true
    )
    {
        if (!emitJson)
        {
            return;
        }

        var jsonName = char.ToLowerInvariant(name[0]) + name.Substring(1);
        code.AppendLine(
            "[global::System.Text.Json.Serialization.JsonPropertyName(\"" + jsonName + "\")]"
        );
        code.AppendLine(
            "[global::System.Text.Json.Serialization.JsonPropertyOrder(" + order + ")]"
        );
    }

    internal static void AppendIgnoreDefault(SharedIndentedBuilder code, bool emitJson = true)
    {
        if (!emitJson)
        {
            return;
        }

        code.AppendLine(
            "[global::System.Text.Json.Serialization.JsonIgnore(Condition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]"
        );
    }
}
