namespace SparseTypes.Generator.Shared;

/// <summary>Emits the top-level per-model JSON helper for change payloads.</summary>
internal static class SparsePayloadJsonHelperEmitter
{
    /// <summary>Gets the top-level helper name for one model.</summary>
    /// <param name="model">Owning model.</param>
    /// <returns>The helper name in the model's namespace.</returns>
    internal static string HelperName(SparseModelInfo model) =>
        SparseNaming.EscapeIdentifier(model.Name + "Json");

    /// <summary>Whether a helper identity can be emitted for one model.</summary>
    /// <param name="model">Owning model.</param>
    /// <param name="context">Payload JSON emission context.</param>
    /// <returns>False for identities that would not form valid C#.</returns>
    internal static bool CanEmit(SparseModelInfo model, PayloadJsonEmissionContext context)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            return false;
        }

        // Root models are top-level and non-generic, but downstream products
        // may forward other identities; never emit angle-bracket or arity
        // qualified names as a top-level helper.
        return context.ModelType.IndexOf('<') < 0 && context.ModelType.IndexOf('`') < 0;
    }

    /// <summary>Emits the helper in the model's namespace.</summary>
    /// <param name="code">Owning surface builder.</param>
    /// <param name="model">Owning model.</param>
    /// <param name="context">Payload JSON emission context.</param>
    internal static void AppendHelper(
        SharedIndentedBuilder code,
        SparseModelInfo model,
        PayloadJsonEmissionContext context
    )
    {
        if (!CanEmit(model, context))
        {
            return;
        }

        var helper = HelperName(model);
        var accessibility = model.IsPublic ? "public" : "internal";
        var payloadType = context.ModelType + ".ChangePayload";
        var resolver = context.ModelType + "." + context.ResolverName + ".Default";
        code.AppendLineAt(
            0,
            "/// <summary>JSON metadata for '" + context.ModelType + "' change payloads.</summary>"
        );
        code.AppendLineAt(0, accessibility + " static class " + helper);
        code.AppendLineAt(0, "{");
        code.AppendLineAt(
            1,
            "/// <summary>Gets the shared payload metadata for '"
                + context.ModelType
                + "' change payloads.</summary>"
        );
        code.AppendLineAt(
            1,
            "public static global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<"
                + payloadType
                + "> Default => "
                + resolver
                + ".ChangePayload;"
        );
        code.AppendLineAt(
            1,
            "/// <summary>Gets the resolver supplying payload metadata with caller-provided options.</summary>"
        );
        code.AppendLineAt(
            1,
            "public static global::System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver Resolver => "
                + resolver
                + ";"
        );
        code.AppendLineAt(0, "}");
        code.AppendLine();
    }
}
