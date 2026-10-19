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
        code.AppendLineAt(
            1,
            "/// <summary>Trim-safe resolver for this model's change payload.</summary>"
        );
        code.AppendLineAt(
            1,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        code.AppendLineAt(
            1,
            "public sealed class "
                + context.ResolverName
                + " : global::System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver"
        );
        code.AppendLineAt(1, "{");
        code.AppendLineAt(2, "/// <summary>Gets the shared resolver instance.</summary>");
        code.AppendLineAt(
            2,
            "public static " + context.ResolverName + " Default { get; } = new();"
        );
        code.AppendLineAt(
            2,
            "/// <summary>Gets metadata for the model's change payload.</summary>"
        );
        code.AppendLineAt(
            2,
            "public global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<ChangePayload> ChangePayload => _payloadInfo ??= global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateValueInfo<ChangePayload>(new global::System.Text.Json.JsonSerializerOptions { TypeInfoResolver = Default }, Converter);"
        );
        code.AppendLineAt(2, "/// <inheritdoc />");
        code.AppendLineAt(
            2,
            "public global::System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(global::System.Type type, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (type == typeof(ChangePayload))");
        code.AppendLineAt(
            4,
            "return global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateValueInfo<ChangePayload>(options, Converter);"
        );
        code.AppendLineAt(3, "return null;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "private static readonly " + context.ConverterName + " Converter = new();"
        );
        code.AppendLineAt(
            2,
            "private global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<ChangePayload>? _payloadInfo;"
        );
        code.AppendLineAt(1, "}");
        code.AppendLine();
    }
}
