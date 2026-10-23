using System.Threading;

namespace SparseTypes.Generator.Shared;

/// <summary>Builds the compilation-scoped shared System.Text.Json codec source.</summary>
/// <remarks>Per-model fragment and payload converters forward to these kernels instead of embedding copies.</remarks>
internal static class SparseGeneratedOnceJsonCodecs
{
    /// <summary>Builds shared JSON codecs in the configured namespace.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <param name="dialect">Owning patch dialect for runtime type names, or null for the BCL-only subset.</param>
    /// <param name="emitCodecs">Whether the consumer has the required serializer API.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Compilation-scoped source text.</returns>
    public static string BuildSource(
        string implementationNamespace,
        SparseFragmentPatchEmitter.SparsePatchDialect? dialect,
        bool emitCodecs,
        CancellationToken cancellationToken
    )
    {
        using var code = new SharedIndentedBuilder(cancellationToken);
        SparseGeneratedOnceNames.AppendGeneratedHeader(code, implementationNamespace, cancellationToken);
        if (!emitCodecs)
        {
            code.CloseBlock();
            return code.ToString();
        }

        code.AppendLine("/// <summary>Shared System.Text.Json codecs for generated fragment and payload converters.</summary>");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine("internal static class " + SparseGeneratedOnceNames.JsonCodecs);
        code.OpenBlock();
        AppendDelegates(code);
        code.AppendLine();
        AppendTryReadNull(code);
        code.AppendLine();
        AppendScalarHelpers(code);
        code.AppendLine();
        AppendMapKeyHelpers(code);
        code.AppendLine();
        AppendFragmentHelpers(code);
        if (dialect.HasValue)
        {
            code.AppendLine();
            AppendRuntimeHelpers(code, dialect.Value);
        }

        code.CloseBlock();
        code.CloseBlock();
        return code.ToString();
    }

    private static void AppendDelegates(SharedIndentedBuilder code)
    {
        code.AppendLine(
            "internal delegate T RootReader<T>(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options);"
        );
        code.AppendLine(
            "internal delegate void RootWriter<T>(global::System.Text.Json.Utf8JsonWriter writer, T? value, global::System.Text.Json.JsonSerializerOptions options);"
        );
    }

    private static void AppendTryReadNull(SharedIndentedBuilder code)
    {
        code.AppendLine("internal static bool TryReadNull(ref global::System.Text.Json.Utf8JsonReader reader)");
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return true;");
        code.AppendLine("return false;");
        code.CloseBlock();
    }

    private static void AppendScalarHelpers(SharedIndentedBuilder code) => SparseJsonScalarShapes.AppendScalarHelpers(code, "TValue", "internal");

    private static void AppendMapKeyHelpers(SharedIndentedBuilder code) => SparseJsonScalarShapes.AppendMapKeyHelpers(code, "internal");

    private static void AppendFragmentHelpers(SharedIndentedBuilder code)
    {
        code.AppendLine("internal static bool Matches(string? actual, string expected, global::System.Text.Json.JsonSerializerOptions options)");
        code.OpenBlock();
        code.AppendLine("if (actual is null) { return false; }");
        code.AppendLine(
            "return global::System.String.Equals(actual, expected, options.PropertyNameCaseInsensitive ? global::System.StringComparison.OrdinalIgnoreCase : global::System.StringComparison.Ordinal);"
        );
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "internal static void HandleUnknownFragmentProperty(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options, string? propertyName, global::System.Type fragmentType)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (options.UnmappedMemberHandling == global::System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow) { throw new global::System.Text.Json.JsonException(\"The JSON property '\" + propertyName + \"' could not be mapped to fragment '\" + fragmentType + \"'.\"); }"
        );
        code.AppendLine("reader.Skip();");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "internal static TMember? ReadMemberValue<TMember>(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options, global::System.Func<global::System.Text.Json.JsonSerializerOptions, global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TMember>> getTypeInfo)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) { if (default(TMember) is null) return default; throw new global::System.Text.Json.JsonException(\"Malformed fragment value.\"); }"
        );
        code.AppendLine("if (TryReadScalar<TMember>(ref reader, out var scalarMember)) return scalarMember;");
        code.AppendLine("return global::System.Text.Json.JsonSerializer.Deserialize<TMember>(ref reader, getTypeInfo(options))!;");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "internal static void WriteMemberValue<TMember>(global::System.Text.Json.Utf8JsonWriter writer, TMember? value, global::System.Text.Json.JsonSerializerOptions options, global::System.Func<global::System.Text.Json.JsonSerializerOptions, global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TMember>> getTypeInfo)"
        );
        code.OpenBlock();
        code.AppendLine("if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLine(
            "if (!TryWriteScalar(writer, value!)) global::System.Text.Json.JsonSerializer.Serialize(writer, value!, getTypeInfo(options));"
        );
        code.CloseBlock();
    }

    private static void AppendRuntimeHelpers(SharedIndentedBuilder code, SparseFragmentPatchEmitter.SparsePatchDialect dialect)
    {
        var runtime = dialect.RuntimeNamespace;
        var wire = runtime + "ChangePayloadWire";
        var endpoint = runtime + "ChangePayloadEndpoint";
        var state = runtime + "ChangePayloadState";
        var kind = runtime + "ChangePayloadItemKind";
        code.AppendLine("internal static " + state + " ParseState(string? token) => token switch");
        code.OpenBlock();
        code.AppendLine("\"missing\" => " + state + ".Missing,");
        code.AppendLine("\"null\" => " + state + ".Null,");
        code.AppendLine("\"value\" => " + state + ".Value,");
        code.AppendLine("\"redacted\" => " + state + ".Redacted,");
        code.AppendLine("_ => throw new global::System.Text.Json.JsonException(\"Unknown endpoint state.\"),");
        code.Unindent().AppendLine("};");
        code.AppendLine();
        code.AppendLine("internal static " + kind + " ParseKind(string? token) => token switch");
        code.OpenBlock();
        code.AppendLine("\"add\" => " + kind + ".Add,");
        code.AppendLine("\"remove\" => " + kind + ".Remove,");
        code.AppendLine("\"update\" => " + kind + ".Edit,");
        code.AppendLine("\"replace\" => " + kind + ".Edit,");
        code.AppendLine("_ => throw new global::System.Text.Json.JsonException(\"Unknown item kind.\"),");
        code.Unindent().AppendLine("};");
        code.AppendLine();
        code.AppendLine("internal static " + state + " ReadEndpointState(ref global::System.Text.Json.Utf8JsonReader reader)");
        code.OpenBlock();
        code.AppendLine("var state = default(" + state + ");");
        code.AppendLine("var tokenMatched = reader.TokenType == global::System.Text.Json.JsonTokenType.String;");
        code.AppendLine("if (tokenMatched)");
        code.OpenBlock();
        code.AppendLine("state = " + state + ".Missing;");
        AppendTokenComparison(code, wire, "StateTokenUtf8", "state");
        code.AppendLine("if (!tokenMatched) state = " + state + ".Null;");
        AppendTokenComparison(code, wire, "StateTokenUtf8", "state");
        code.AppendLine("if (!tokenMatched) state = " + state + ".Value;");
        AppendTokenComparison(code, wire, "StateTokenUtf8", "state");
        code.AppendLine("if (!tokenMatched) state = " + state + ".Redacted;");
        AppendTokenComparison(code, wire, "StateTokenUtf8", "state");
        code.CloseBlock();
        code.AppendLine("if (!tokenMatched)");
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType != global::System.Text.Json.JsonTokenType.Null) _ = reader.GetString();");
        code.AppendLine("throw new global::System.Text.Json.JsonException(\"Unknown endpoint state.\");");
        code.CloseBlock();
        code.AppendLine("return state;");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "internal static "
                + endpoint
                + "<TValue>? ReadEndpoint<TValue>(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options, RootReader<TValue>? rootReader, RootWriter<TValue>? rootWriter, global::System.Func<global::System.Text.Json.JsonSerializerOptions, global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue>> getTypeInfo)"
        );
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return null;");
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"An endpoint must be an object.\");"
        );
        code.AppendLine("var state = default(" + state + ");");
        code.AppendLine("var hasState = false;");
        code.AppendLine("var hasValue = false;");
        code.AppendLine("var hasStateProperty = false;");
        code.AppendLine("var hasValueProperty = false;");
        code.AppendLine("TValue? value = default;");
        code.AppendLine("global::System.Collections.Generic.HashSet<string>? seenExtensions = null;");
        code.AppendLine("while (reader.Read())");
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;");
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\");"
        );
        code.AppendLine("if (reader.ValueTextEquals(" + wire + ".StatePropertyUtf8()))");
        code.OpenBlock();
        code.AppendLine("if (hasStateProperty) throw new global::System.Text.Json.JsonException(\"Duplicate JSON property.\");");
        code.AppendLine("hasStateProperty = true;");
        code.AppendLine("if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\");");
        code.AppendLine("state = ReadEndpointState(ref reader);");
        code.AppendLine("hasState = true;");
        code.CloseBlock();
        code.AppendLine("else if (reader.ValueTextEquals(" + wire + ".ValuePropertyUtf8()))");
        code.OpenBlock();
        code.AppendLine("if (hasValueProperty) throw new global::System.Text.Json.JsonException(\"Duplicate JSON property.\");");
        code.AppendLine("hasValueProperty = true;");
        code.AppendLine("if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\");");
        code.AppendLine("hasValue = true;");
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) value = default;");
        code.AppendLine("else if (rootReader is not null) value = rootReader(ref reader, options);");
        code.AppendLine("else if (TryReadScalar<TValue>(ref reader, out var scalarValue)) value = scalarValue;");
        code.AppendLine("else value = global::System.Text.Json.JsonSerializer.Deserialize<TValue>(ref reader, getTypeInfo(options));");
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine("var extensionName = reader.GetString() ?? string.Empty;");
        code.AppendLine("seenExtensions ??= new global::System.Collections.Generic.HashSet<string>(global::System.StringComparer.Ordinal);");
        code.AppendLine("if (!seenExtensions.Add(extensionName)) throw new global::System.Text.Json.JsonException(\"Duplicate JSON property.\");");
        code.AppendLine("if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\");");
        code.AppendLine("reader.Skip();");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("if (!hasState) throw new global::System.Text.Json.JsonException(\"An endpoint must contain a state.\");");
        code.AppendLine(
            "if (state != "
                + state
                + ".Value && hasValue) throw new global::System.Text.Json.JsonException(\"A value-free endpoint must not contain a value property.\");"
        );
        code.AppendLine(
            "if (state == "
                + state
                + ".Value && (!hasValue || (!typeof(TValue).IsValueType && value is null) || (global::System.Nullable.GetUnderlyingType(typeof(TValue)) is not null && global::System.Collections.Generic.EqualityComparer<TValue>.Default.Equals(value!, default!)))) throw new global::System.Text.Json.JsonException(\"A value endpoint must contain a non-null value.\");"
        );
        code.AppendLine("return new " + endpoint + "<TValue> { State = state, Value = value };");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "internal static void WriteEndpoint<TValue>(global::System.Text.Json.Utf8JsonWriter writer, "
                + endpoint
                + "<TValue>? value, global::System.Text.Json.JsonSerializerOptions options, RootWriter<TValue>? rootWriter, global::System.Func<global::System.Text.Json.JsonSerializerOptions, global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TValue>> getTypeInfo)"
        );
        code.OpenBlock();
        code.AppendLine("if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLine("value.Validate();");
        code.AppendLine(
            "if (value.State == "
                + state
                + ".Value && ((!typeof(TValue).IsValueType && value.Value is null) || (global::System.Nullable.GetUnderlyingType(typeof(TValue)) is not null && global::System.Collections.Generic.EqualityComparer<TValue>.Default.Equals(value.Value!, default!)))) throw new global::System.Text.Json.JsonException(\"A value endpoint must contain a non-null value.\");"
        );
        code.AppendLine(
            "if (value.State != "
                + state
                + ".Missing && value.State != "
                + state
                + ".Null && value.State != "
                + state
                + ".Value && value.State != "
                + state
                + ".Redacted) throw new global::System.Text.Json.JsonException(\"Unknown endpoint state.\");"
        );
        code.AppendLine("writer.WriteStartObject();");
        code.AppendLine("writer.WriteString(" + wire + ".StateProperty, " + wire + ".ToToken(value.State));");
        code.AppendLine("if (value.State == " + state + ".Value)");
        code.OpenBlock();
        code.AppendLine("writer.WritePropertyName(" + wire + ".ValueProperty);");
        code.AppendLine("if (rootWriter is not null) rootWriter(writer, value.Value!, options);");
        code.AppendLine(
            "else if (!TryWriteScalar(writer, value.Value!)) global::System.Text.Json.JsonSerializer.Serialize(writer, value.Value!, getTypeInfo(options));"
        );
        code.CloseBlock();
        code.AppendLine("writer.WriteEndObject();");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "internal static TKey ReadKey<TKey>(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options, global::System.Func<global::System.Text.Json.JsonSerializerOptions, global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TKey>> getTypeInfo)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) throw new global::System.Text.Json.JsonException(\"Malformed key.\");"
        );
        code.AppendLine("if (TryReadScalar<TKey>(ref reader, out var scalarKey)) return scalarKey!;");
        code.AppendLine("return global::System.Text.Json.JsonSerializer.Deserialize<TKey>(ref reader, getTypeInfo(options))!;");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "internal static void WriteKey<TKey>(global::System.Text.Json.Utf8JsonWriter writer, TKey key, global::System.Text.Json.JsonSerializerOptions options, global::System.Func<global::System.Text.Json.JsonSerializerOptions, global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TKey>> getTypeInfo)"
        );
        code.OpenBlock();
        code.AppendLine("if (!TryWriteScalar(writer, key!)) global::System.Text.Json.JsonSerializer.Serialize(writer, key!, getTypeInfo(options));");
        code.CloseBlock();
    }

    private static void AppendTokenComparison(SharedIndentedBuilder code, string wire, string tokenAccessor, string tokenVariable) =>
        code.AppendLine("tokenMatched = reader.ValueTextEquals(" + wire + "." + tokenAccessor + "(" + tokenVariable + "));");
}
