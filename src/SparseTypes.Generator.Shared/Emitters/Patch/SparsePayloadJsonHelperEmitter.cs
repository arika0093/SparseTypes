namespace SparseTypes.Generator.Shared;

/// <summary>Emits the model-nested public JSON helper for change payloads.</summary>
internal static class SparsePayloadJsonHelperEmitter
{
    /// <summary>Emits the nested helper on the model surface.</summary>
    /// <param name="code">Owning surface builder.</param>
    /// <param name="model">Owning model.</param>
    /// <param name="context">Payload JSON emission context.</param>
    internal static void AppendHelper(SharedIndentedBuilder code, SparseModelInfo model, PayloadJsonEmissionContext context)
    {
        var payloadType = context.ModelType + ".ChangePayload";
        var resolver = context.ResolverTypeReference + ".Default";
        code.AppendLine("/// <summary>Metadata for '" + context.ModelType + "' change payloads.</summary>");
        if (!model.IsPublic)
        {
            code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        }
        code.AppendLine((model.IsPublic ? "public" : "internal") + " static class PayloadJson");
        code.OpenBlock();
        if (context.Dialect.PayloadSchemaBundleType is not null && context.Dialect.PayloadSchemaDescriptorType is not null)
        {
            SparsePayloadSchemaEmitter.AppendSchemaProperty(code, context);
        }

        if (context.SupportsJson)
        {
            code.AppendLine("/// <summary>Gets the shared payload metadata for '" + context.ModelType + "' change payloads.</summary>");
            code.AppendLine(
                "public static global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<"
                    + payloadType
                    + "> Default => "
                    + resolver
                    + ".ChangePayload;"
            );
            code.AppendLine("/// <summary>Gets the resolver supplying payload metadata with caller-provided options.</summary>");
            code.AppendLine("public static global::System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver Resolver => " + resolver + ";");
        }
        code.CloseBlock();
        code.AppendLine();
    }
}
