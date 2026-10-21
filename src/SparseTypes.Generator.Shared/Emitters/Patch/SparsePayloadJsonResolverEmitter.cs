namespace SparseTypes.Generator.Shared;

/// <summary>Emits the per-model trim-safe payload resolver.</summary>
internal static class SparsePayloadJsonResolverEmitter
{
    /// <summary>Emits the resolver nested in the model.</summary>
    internal static void AppendResolver(
        SharedIndentedBuilder code,
        PayloadJsonEmissionContext context
    )
    {
        code.AppendLine(
            "/// <summary>Trim-safe resolver for this model's change payload.</summary>"
        );
        code.AppendLine(
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        code.AppendLine(
            "public sealed class "
                + context.ResolverName
                + " : global::System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver"
        );
        code.OpenBlock();
        code.AppendLine("/// <summary>Gets the shared resolver instance.</summary>");
        code.AppendLine("public static " + context.ResolverName + " Default { get; } = new();");
        code.AppendLine("/// <summary>Gets metadata for the model's change payload.</summary>");
        code.AppendLine(
            "public global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<ChangePayload> ChangePayload => _payloadInfo ??= global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateValueInfo<ChangePayload>(new global::System.Text.Json.JsonSerializerOptions { TypeInfoResolver = Default }, Converter);"
        );
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine(
            "public global::System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(global::System.Type type, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("if (type == typeof(ChangePayload))");
        code.AppendLine(
            "return global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateValueInfo<ChangePayload>(options, Converter);"
        );
        SparsePayloadJsonPocoEmitter.AppendResolverBranches(code, context);
        code.AppendLine("return null;");
        code.CloseBlock();
        code.AppendLine("private static readonly " + context.ConverterName + " Converter = new();");
        code.AppendLine(
            "private global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<ChangePayload>? _payloadInfo;"
        );
        code.CloseBlock();
        code.AppendLine();
    }
}
