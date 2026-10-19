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
            : SparseGeneratedOnceNames.QualifiedChangePayloadEndpointJsonConverter(
                implementationNamespace
            );

    internal static string ItemKindConverterType(string? implementationNamespace) =>
        implementationNamespace is null
            ? ItemKindConverterName
            : SparseGeneratedOnceNames.QualifiedChangePayloadItemKindJsonConverter(
                implementationNamespace
            );

    /// <summary>Emits bridge converter types into the current generated container.</summary>
    /// <param name="code">Container target builder (indented one level inside the container).</param>
    /// <param name="dialect">Owning patch dialect for runtime type names.</param>
    internal static void AppendBridgeConverters(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var wire = runtime + "ChangePayloadWire";
        var endpoint = runtime + "ChangePayloadEndpoint";
        var state = runtime + "ChangePayloadState";
        var kind = runtime + "ChangePayloadItemKind";

        code.AppendLineAt(
            1,
            "/// <summary>Preserves the v1 endpoint wire shape for one value type.</summary>"
        );
        code.AppendLineAt(
            1,
            "/// <remarks>State tokens and names come from the runtime wire constants so the contract stays single-sourced.</remarks>"
        );
        code.AppendLineAt(
            1,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        code.AppendLineAt(
            1,
            "public sealed class "
                + EndpointConverterName
                + "<TValue> : global::System.Text.Json.Serialization.JsonConverter<"
                + endpoint
                + "<TValue>>"
        );
        code.AppendLineAt(1, "{");
        code.AppendLineAt(2, "/// <inheritdoc />");
        code.AppendLineAt(
            2,
            "public override "
                + endpoint
                + "<TValue>? Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return null;"
        );
        code.AppendLineAt(
            3,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"An endpoint must be an object.\");"
        );
        code.AppendLineAt(3, "var state = default(" + state + ");");
        code.AppendLineAt(3, "var hasState = false;");
        code.AppendLineAt(3, "TValue? value = default;");
        code.AppendLineAt(3, "while (reader.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            4,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\");"
        );
        // Byte-level envelope dispatch while the reader is still on the
        // property name: ValueTextEquals decodes escaped names exactly like
        // the previously materialized string did, without allocating it.
        code.AppendLineAt(4, "if (reader.ValueTextEquals(" + wire + ".StatePropertyUtf8()))");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\");"
        );
        AppendStateTokenComparison(code, 5, dialect, "state");
        code.AppendLineAt(5, "hasState = true;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else if (reader.ValueTextEquals(" + wire + ".ValuePropertyUtf8()))");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\");"
        );
        // Typed declared-type metadata keeps scalar values off the object
        // serializer boundary; reader comparison tokens stay wired into the
        // runtime vocabulary.
        code.AppendLineAt(
            5,
            "value = global::System.Text.Json.JsonSerializer.Deserialize<TValue>(ref reader, GetValueTypeTypeInfo(options));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        // Unknown supplemental properties are additive extensions: skip
        // them without changing the interpreted endpoints. Unknown
        // operations (member discriminators, kinds, states) still fail.
        code.AppendLineAt(
            5,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\");"
        );
        code.AppendLineAt(5, "reader.Skip();");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        // State is required: an omitted state is not an explicit missing
        // endpoint. Unknown tokens already failed above.
        code.AppendLineAt(
            3,
            "if (!hasState) throw new global::System.Text.Json.JsonException(\"An endpoint must contain a state.\");"
        );
        code.AppendLineAt(
            3,
            "return new " + endpoint + "<TValue> { State = state, Value = value };"
        );
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "/// <inheritdoc />");
        code.AppendLineAt(
            2,
            "public override void Write(global::System.Text.Json.Utf8JsonWriter writer, "
                + endpoint
                + "<TValue>? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLineAt(3, "writer.WriteStartObject();");
        code.AppendLineAt(
            3,
            "writer.WriteString(" + wire + ".StateProperty, " + wire + ".ToToken(value.State));"
        );
        code.AppendLineAt(3, "writer.WritePropertyName(" + wire + ".ValueProperty);");
        // Explicit null keeps value-free envelopes byte-identical; otherwise
        // the declared-type metadata serializes exactly like before.
        code.AppendLineAt(3, "if (value.Value is null) writer.WriteNullValue();");
        code.AppendLineAt(
            3,
            "else global::System.Text.Json.JsonSerializer.Serialize(writer, value.Value!, GetValueTypeTypeInfo(options));"
        );
        code.AppendLineAt(3, "writer.WriteEndObject();");
        code.AppendLineAt(2, "}");
        AppendValueTypeTypeInfo(code);
        code.AppendLineAt(1, "}");
        code.AppendLine();

        code.AppendLineAt(
            1,
            "/// <summary>Preserves the lower-case v1 item-kind tokens.</summary>"
        );
        code.AppendLineAt(
            1,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        code.AppendLineAt(
            1,
            "public sealed class "
                + ItemKindConverterName
                + " : global::System.Text.Json.Serialization.JsonConverter<"
                + kind
                + ">"
        );
        code.AppendLineAt(1, "{");
        code.AppendLineAt(2, "/// <inheritdoc />");
        code.AppendLineAt(
            2,
            "public override "
                + kind
                + " Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(2, "var kind = default(" + kind + ");");
        AppendKindTokenComparison(code, 2, dialect, "kind");
        code.AppendLineAt(2, "return kind;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "/// <inheritdoc />");
        code.AppendLineAt(
            2,
            "public override void Write(global::System.Text.Json.Utf8JsonWriter writer, "
                + kind
                + " value, global::System.Text.Json.JsonSerializerOptions options) => writer.WriteStringValue("
                + wire
                + ".ToToken(value));"
        );
        code.AppendLineAt(1, "}");
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
    /// <param name="indent">Body indent.</param>
    /// <param name="dialect">Owning patch dialect for runtime type names.</param>
    /// <param name="stateVariable">The state local compared through the wire token table.</param>
    private static void AppendStateTokenComparison(
        SharedIndentedBuilder code,
        int indent,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string stateVariable
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var wire = runtime + "ChangePayloadWire";
        var state = runtime + "ChangePayloadState";
        code.AppendLineAt(
            indent,
            "var tokenMatched = reader.TokenType == global::System.Text.Json.JsonTokenType.String;"
        );
        code.AppendLineAt(indent, "if (tokenMatched)");
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(indent + 1, stateVariable + " = " + state + ".Missing;");
        AppendTokenComparison(code, indent + 1, wire, "StateTokenUtf8", stateVariable);
        AppendTokenFallback(code, indent + 1, stateVariable + " = " + state + ".Null;");
        AppendTokenComparison(code, indent + 1, wire, "StateTokenUtf8", stateVariable);
        AppendTokenFallback(code, indent + 1, stateVariable + " = " + state + ".Value;");
        AppendTokenComparison(code, indent + 1, wire, "StateTokenUtf8", stateVariable);
        AppendTokenFallback(code, indent + 1, stateVariable + " = " + state + ".Redacted;");
        AppendTokenComparison(code, indent + 1, wire, "StateTokenUtf8", stateVariable);
        code.AppendLineAt(indent, "}");
        AppendUnknownTokenFailure(code, indent, "Unknown endpoint state.");
    }

    /// <summary>Emits the non-allocating comparison of one item-kind token.</summary>
    /// <remarks>The candidate order mirrors <c>ChangePayloadWire.TryParseKind</c>.</remarks>
    /// <param name="code">Converter body builder.</param>
    /// <param name="indent">Body indent.</param>
    /// <param name="dialect">Owning patch dialect for runtime type names.</param>
    /// <param name="kindVariable">The kind local compared through the wire token table.</param>
    private static void AppendKindTokenComparison(
        SharedIndentedBuilder code,
        int indent,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string kindVariable
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var wire = runtime + "ChangePayloadWire";
        var kind = runtime + "ChangePayloadItemKind";
        code.AppendLineAt(
            indent,
            "var tokenMatched = reader.TokenType == global::System.Text.Json.JsonTokenType.String;"
        );
        code.AppendLineAt(indent, "if (tokenMatched)");
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(indent + 1, kindVariable + " = " + kind + ".Add;");
        AppendTokenComparison(code, indent + 1, wire, "KindTokenUtf8", kindVariable);
        foreach (var kindMember in (string[])["Remove", "Edit", "Reorder"])
        {
            AppendTokenFallback(
                code,
                indent + 1,
                kindVariable + " = " + kind + "." + kindMember + ";"
            );
            AppendTokenComparison(code, indent + 1, wire, "KindTokenUtf8", kindVariable);
        }

        code.AppendLineAt(indent, "}");
        AppendUnknownTokenFailure(code, indent, "Unknown item kind.");
    }

    /// <summary>Emits one candidate token comparison against the wire table.</summary>
    /// <param name="code">Converter body builder.</param>
    /// <param name="indent">Body indent.</param>
    /// <param name="wire">The runtime wire type name.</param>
    /// <param name="tokenAccessor">The wire token accessor name (state or kind table).</param>
    /// <param name="tokenVariable">The enum local holding the current candidate.</param>
    private static void AppendTokenComparison(
        SharedIndentedBuilder code,
        int indent,
        string wire,
        string tokenAccessor,
        string tokenVariable
    ) =>
        code.AppendLineAt(
            indent,
            "tokenMatched = reader.ValueTextEquals("
                + wire
                + "."
                + tokenAccessor
                + "("
                + tokenVariable
                + "));"
        );

    /// <summary>Emits the fallback assignment of the next candidate; polarity is already false.</summary>
    /// <param name="code">Converter body builder.</param>
    /// <param name="indent">Body indent.</param>
    /// <param name="candidateAssignment">The complete candidate enum assignment.</param>
    private static void AppendTokenFallback(
        SharedIndentedBuilder code,
        int indent,
        string candidateAssignment
    ) => code.AppendLineAt(indent, "if (!tokenMatched) " + candidateAssignment);

    /// <summary>Emits the failure path for a token outside the wire vocabulary.</summary>
    /// <remarks>
    /// The previous string path threw <see cref="InvalidOperationException"/>
    /// for non-string tokens through <c>GetString</c> and treated explicit
    /// null like any unknown token; both behaviors are preserved without
    /// materializing recognized token names.
    /// </remarks>
    /// <param name="code">Converter body builder.</param>
    /// <param name="indent">Body indent.</param>
    /// <param name="message">The unchanged JsonException message.</param>
    private static void AppendUnknownTokenFailure(
        SharedIndentedBuilder code,
        int indent,
        string message
    )
    {
        code.AppendLineAt(indent, "if (!tokenMatched)");
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(
            indent + 1,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.Null) _ = reader.GetString();"
        );
        code.AppendLineAt(
            indent + 1,
            "throw new global::System.Text.Json.JsonException(\"" + message + "\");"
        );
        code.AppendLineAt(indent, "}");
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
        code.AppendLineAt(
            2,
            "private static global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue> GetValueTypeTypeInfo(global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "try");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "return (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue>)options.GetTypeInfo(typeof(TValue));"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "catch (global::System.NotSupportedException exception)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "throw new global::System.InvalidOperationException(\"The generated ChangePayload endpoint converter requires JsonTypeInfo metadata for value type '\" + typeof(TValue) + \"'. Add the model/member types to a source-generated JsonSerializerContext and set it as JsonSerializerOptions.TypeInfoResolver.\", exception);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
    }

    /// <summary>Emits a bare exclude-from-JSON marker when JSON is enabled.</summary>
    /// <remarks>Computed transition projections must not serialize.</remarks>
    /// <param name="code">Target builder.</param>
    /// <param name="indent">Member indent.</param>
    /// <param name="emitJson">Whether the v1 JSON surface is enabled.</param>
    internal static void AppendBareJsonIgnore(SharedIndentedBuilder code, int indent, bool emitJson)
    {
        if (!emitJson)
            return;
        code.AppendLineAt(indent, "[global::System.Text.Json.Serialization.JsonIgnore]");
    }

    /// <summary>Emits the endpoint converter attribute for one endpoint property.</summary>
    /// <param name="code">DTO target builder.</param>
    /// <param name="indent">Member indent.</param>
    /// <param name="valueType">The endpoint value type name.</param>
    internal static void AppendEndpointConverterAttribute(
        SharedIndentedBuilder code,
        int indent,
        string valueType,
        string? implementationNamespace = null
    ) =>
        code.AppendLineAt(
            indent,
            "[global::System.Text.Json.Serialization.JsonConverter(typeof("
                + EndpointConverterType(implementationNamespace)
                + "<"
                + valueType
                + ">))]"
        );

    /// <summary>Emits the item-kind converter attribute for one kind property.</summary>
    /// <param name="code">DTO target builder.</param>
    /// <param name="indent">Member indent.</param>
    internal static void AppendItemKindConverterAttribute(
        SharedIndentedBuilder code,
        int indent,
        string? implementationNamespace = null
    ) =>
        code.AppendLineAt(
            indent,
            "[global::System.Text.Json.Serialization.JsonConverter(typeof("
                + ItemKindConverterType(implementationNamespace)
                + "))]"
        );
}
