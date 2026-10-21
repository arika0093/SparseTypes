namespace SparseTypes.Generator.Shared;

/// <summary>Emits the optional System.Text.Json bridge for ChangePayload DTOs.</summary>
/// <remarks>The base runtime is serializer-agnostic: endpoint and enum types carry no JSON attributes.</remarks>
internal static class SparseChangePayloadJsonBridgeEmitter
{
    /// <summary>Nested generic endpoint converter name within one payload container.</summary>
    internal const string EndpointConverterName = "ChangePayloadEndpointJsonConverter";

    /// <summary>Nested item-kind converter name within one payload container.</summary>
    internal const string ItemKindConverterName = "ChangePayloadItemKindJsonConverter";

    internal static string EndpointConverterType(string? implementationNamespace) =>
        implementationNamespace is null
            ? EndpointConverterName
            : SparseGeneratedOnceNames.QualifiedChangePayloadEndpointJsonConverter(implementationNamespace);

    internal static string ItemKindConverterType(string? implementationNamespace) =>
        implementationNamespace is null
            ? ItemKindConverterName
            : SparseGeneratedOnceNames.QualifiedChangePayloadItemKindJsonConverter(implementationNamespace);

    /// <summary>Emits bridge converter types into the current generated container.</summary>
    /// <param name="code">Container target builder (indented one level inside the container).</param>
    /// <param name="dialect">Owning patch dialect for runtime type names.</param>
    internal static void AppendBridgeConverters(SharedIndentedBuilder code, SparseFragmentPatchEmitter.SparsePatchDialect dialect)
    {
        var runtime = dialect.RuntimeNamespace;
        var wire = runtime + "ChangePayloadWire";
        var endpoint = runtime + "ChangePayloadEndpoint";
        var state = runtime + "ChangePayloadState";
        var kind = runtime + "ChangePayloadItemKind";

        code.AppendLine("/// <summary>Preserves the v1 endpoint wire shape for one value type.</summary>");
        code.AppendLine("/// <remarks>State tokens and names come from the runtime wire constants so the contract stays single-sourced.</remarks>");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine(
            "public sealed class "
                + EndpointConverterName
                + "<TValue> : global::System.Text.Json.Serialization.JsonConverter<"
                + endpoint
                + "<TValue>>"
        );
        code.OpenBlock();
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine(
            "public override "
                + endpoint
                + "<TValue>? Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return null;");
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"An endpoint must be an object.\");"
        );
        code.AppendLine("var state = default(" + state + ");");
        code.AppendLine("var hasState = false;");
        code.AppendLine("TValue? value = default;");
        code.AppendLine("while (reader.Read())");
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;");
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\");"
        );
        // Byte-level envelope dispatch while the reader is still on the
        // property name: ValueTextEquals decodes escaped names exactly like
        // the previously materialized string did, without allocating it.
        code.AppendLine("if (reader.ValueTextEquals(" + wire + ".StatePropertyUtf8()))");
        code.OpenBlock();
        code.AppendLine("if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\");");
        AppendStateTokenComparison(code, dialect, "state");
        code.AppendLine("hasState = true;");
        code.CloseBlock();
        code.AppendLine("else if (reader.ValueTextEquals(" + wire + ".ValuePropertyUtf8()))");
        code.OpenBlock();
        code.AppendLine("if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\");");
        // Typed declared-type metadata keeps scalar values off the object
        // serializer boundary; reader comparison tokens stay wired into the
        // runtime vocabulary.
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) value = default;");
        code.AppendLine("else if (TryReadScalar<TValue>(ref reader, out var scalarValue)) value = scalarValue;");
        code.AppendLine("else value = global::System.Text.Json.JsonSerializer.Deserialize<TValue>(ref reader, GetValueTypeTypeInfo(options));");
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        // Unknown supplemental properties are additive extensions: skip
        // them without changing the interpreted endpoints. Unknown
        // operations (member discriminators, kinds, states) still fail.
        code.AppendLine("if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\");");
        code.AppendLine("reader.Skip();");
        code.CloseBlock();
        code.CloseBlock();
        // State is required: an omitted state is not an explicit missing
        // endpoint. Unknown tokens already failed above.
        code.AppendLine("if (!hasState) throw new global::System.Text.Json.JsonException(\"An endpoint must contain a state.\");");
        code.AppendLine("return new " + endpoint + "<TValue> { State = state, Value = value };");
        code.CloseBlock();
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine(
            "public override void Write(global::System.Text.Json.Utf8JsonWriter writer, "
                + endpoint
                + "<TValue>? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLine("writer.WriteStartObject();");
        code.AppendLine("writer.WriteString(" + wire + ".StateProperty, " + wire + ".ToToken(value.State));");
        code.AppendLine("writer.WritePropertyName(" + wire + ".ValueProperty);");
        // Explicit null keeps value-free envelopes byte-identical; otherwise
        // the declared-type metadata serializes exactly like before.
        code.AppendLine("if (value.Value is null) writer.WriteNullValue();");
        code.AppendLine(
            "else if (!TryWriteScalar(writer, value.Value!)) global::System.Text.Json.JsonSerializer.Serialize(writer, value.Value!, GetValueTypeTypeInfo(options));"
        );
        code.AppendLine("writer.WriteEndObject();");
        code.CloseBlock();
        SparseJsonScalarShapes.AppendScalarHelpers(code, "TScalar");
        AppendValueTypeTypeInfo(code);
        code.CloseBlock();
        code.AppendLine();

        code.AppendLine("/// <summary>Preserves the lower-case v1 item-kind tokens.</summary>");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine("public sealed class " + ItemKindConverterName + " : global::System.Text.Json.Serialization.JsonConverter<" + kind + ">");
        code.OpenBlock();
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine(
            "public override "
                + kind
                + " Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("var kind = default(" + kind + ");");
        AppendKindTokenComparison(code, dialect, "kind");
        code.AppendLine("return kind;");
        code.CloseBlock();
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine(
            "public override void Write(global::System.Text.Json.Utf8JsonWriter writer, "
                + kind
                + " value, global::System.Text.Json.JsonSerializerOptions options) => writer.WriteStringValue("
                + wire
                + ".ToToken(value));"
        );
        code.CloseBlock();
        code.AppendLine();
    }

    /// <summary>Emits the non-allocating comparison of one endpoint state token.</summary>
    /// <remarks>
    /// Recognized tokens never materialize a string and candidate order
    /// mirrors <c>ChangePayloadWire.TryParseState</c>. Unrecognized tokens
    /// keep the previous failure behavior: an explicit null still fails with
    /// the wire message and non-string tokens still surface through
    /// <c>GetString</c>'s InvalidOperationException.
    /// </remarks>
    /// <param name="code">Converter body builder.</param>
    /// <param name="dialect">Owning patch dialect for runtime type names.</param>
    /// <param name="stateVariable">The state local compared through the wire token table.</param>
    private static void AppendStateTokenComparison(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string stateVariable
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var wire = runtime + "ChangePayloadWire";
        var state = runtime + "ChangePayloadState";
        code.AppendLine("var tokenMatched = reader.TokenType == global::System.Text.Json.JsonTokenType.String;");
        code.AppendLine("if (tokenMatched)");
        code.OpenBlock();
        code.AppendLine(stateVariable + " = " + state + ".Missing;");
        AppendTokenComparison(code, wire, "StateTokenUtf8", stateVariable);
        AppendTokenFallback(code, stateVariable + " = " + state + ".Null;");
        AppendTokenComparison(code, wire, "StateTokenUtf8", stateVariable);
        AppendTokenFallback(code, stateVariable + " = " + state + ".Value;");
        AppendTokenComparison(code, wire, "StateTokenUtf8", stateVariable);
        AppendTokenFallback(code, stateVariable + " = " + state + ".Redacted;");
        AppendTokenComparison(code, wire, "StateTokenUtf8", stateVariable);
        code.CloseBlock();
        AppendUnknownTokenFailure(code, "Unknown endpoint state.");
    }

    /// <summary>Emits the non-allocating comparison of one item-kind token.</summary>
    /// <remarks>The candidate order mirrors <c>ChangePayloadWire.TryParseKind</c>.</remarks>
    /// <param name="code">Converter body builder.</param>
    /// <param name="dialect">Owning patch dialect for runtime type names.</param>
    /// <param name="kindVariable">The kind local compared through the wire token table.</param>
    private static void AppendKindTokenComparison(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string kindVariable
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var wire = runtime + "ChangePayloadWire";
        var kind = runtime + "ChangePayloadItemKind";
        code.AppendLine("var tokenMatched = reader.TokenType == global::System.Text.Json.JsonTokenType.String;");
        code.AppendLine("if (tokenMatched)");
        code.OpenBlock();
        code.AppendLine(kindVariable + " = " + kind + ".Add;");
        AppendTokenComparison(code, wire, "KindTokenUtf8", kindVariable);
        foreach (var kindMember in (string[])["Remove", "Edit", "Reorder"])
        {
            AppendTokenFallback(code, kindVariable + " = " + kind + "." + kindMember + ";");
            AppendTokenComparison(code, wire, "KindTokenUtf8", kindVariable);
        }

        code.CloseBlock();
        AppendUnknownTokenFailure(code, "Unknown item kind.");
    }

    /// <summary>Emits one candidate token comparison against the wire table.</summary>
    /// <param name="code">Converter body builder.</param>
    /// <param name="wire">The runtime wire type name.</param>
    /// <param name="tokenAccessor">The wire token accessor name (state or kind table).</param>
    /// <param name="tokenVariable">The enum local holding the current candidate.</param>
    private static void AppendTokenComparison(SharedIndentedBuilder code, string wire, string tokenAccessor, string tokenVariable) =>
        code.AppendLine("tokenMatched = reader.ValueTextEquals(" + wire + "." + tokenAccessor + "(" + tokenVariable + "));");

    /// <summary>Emits the fallback assignment of the next candidate; polarity is already false.</summary>
    /// <param name="code">Converter body builder.</param>
    /// <param name="candidateAssignment">The complete candidate enum assignment.</param>
    private static void AppendTokenFallback(SharedIndentedBuilder code, string candidateAssignment) =>
        code.AppendLine("if (!tokenMatched) " + candidateAssignment);

    /// <summary>Emits the failure path for a token outside the wire vocabulary.</summary>
    /// <remarks>
    /// The previous string path threw <see cref="InvalidOperationException"/>
    /// for non-string tokens through <c>GetString</c> and treated explicit
    /// null like any unknown token; both behaviors are preserved without
    /// materializing recognized token names.
    /// </remarks>
    /// <param name="code">Converter body builder.</param>
    /// <param name="message">The unchanged JsonException message.</param>
    private static void AppendUnknownTokenFailure(SharedIndentedBuilder code, string message)
    {
        code.AppendLine("if (!tokenMatched)");
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType != global::System.Text.Json.JsonTokenType.Null) _ = reader.GetString();");
        code.AppendLine("throw new global::System.Text.Json.JsonException(\"" + message + "\");");
        code.CloseBlock();
    }

    /// <summary>Emits the typed declared-type metadata lookup shared by endpoint reads and writes.</summary>
    /// <remarks>
    /// The runtime cast mirrors the fragment converter helper: options resolve
    /// convertible <c>JsonTypeInfo&lt;TValue&gt;</c> metadata, keeping both the
    /// trim-safe lookup and unsupported-metadata diagnostics identical.
    /// </remarks>
    /// <param name="code">Converter body builder (converter class interior).</param>
    private static void AppendValueTypeTypeInfo(SharedIndentedBuilder code)
    {
        code.AppendLine(
            "private static global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> GetValueTypeTypeInfo(global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("try");
        code.OpenBlock();
        code.AppendLine("return (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue>)options.GetTypeInfo(typeof(TValue));");
        code.CloseBlock();
        code.AppendLine("catch (global::System.NotSupportedException exception)");
        code.OpenBlock();
        code.AppendLine(
            "throw new global::System.InvalidOperationException(\"The generated ChangePayload endpoint converter requires JsonTypeInfo metadata for value type '\" + typeof(TValue) + \"'. Add the model/member types to a source-generated JsonSerializerContext and set it as JsonSerializerOptions.TypeInfoResolver.\", exception);"
        );
        code.CloseBlock();
        code.CloseBlock();
    }

    /// <summary>Emits a bare exclude-from-JSON marker when JSON is enabled.</summary>
    /// <remarks>Computed transition projections must not serialize.</remarks>
    /// <param name="code">Target builder.</param>
    /// <param name="emitJson">Whether the v1 JSON surface is enabled.</param>
    internal static void AppendBareJsonIgnore(SharedIndentedBuilder code, bool emitJson)
    {
        if (!emitJson)
        {
            return;
        }

        code.AppendLine("[global::System.Text.Json.Serialization.JsonIgnore]");
    }

    /// <summary>Emits the endpoint converter attribute for one endpoint property.</summary>
    /// <param name="code">DTO target builder.</param>
    /// <param name="valueType">The endpoint value type name.</param>
    internal static void AppendEndpointConverterAttribute(SharedIndentedBuilder code, string valueType, string? implementationNamespace = null) =>
        code.AppendLine(
            "[global::System.Text.Json.Serialization.JsonConverter(typeof("
                + EndpointConverterType(implementationNamespace)
                + "<"
                + valueType
                + ">))]"
        );

    /// <summary>Emits the item-kind converter attribute for one kind property.</summary>
    /// <param name="code">DTO target builder.</param>
    internal static void AppendItemKindConverterAttribute(SharedIndentedBuilder code, string? implementationNamespace = null) =>
        code.AppendLine("[global::System.Text.Json.Serialization.JsonConverter(typeof(" + ItemKindConverterType(implementationNamespace) + "))]");
}
