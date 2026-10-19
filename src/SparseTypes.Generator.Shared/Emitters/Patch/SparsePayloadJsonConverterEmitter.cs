namespace SparseTypes.Generator.Shared;

/// <summary>Emits the per-model trim-safe payload JSON converter envelope.</summary>
internal static class SparsePayloadJsonConverterEmitter
{
    /// <summary>Emits the converter nested in the model.</summary>
    internal static void AppendConverter(
        SharedIndentedBuilder code,
        PayloadJsonEmissionContext context
    )
    {
        var members = context.Members.Where(static m => !m.Property.IsJsonIgnored).ToArray();
        code.AppendLineAt(
            1,
            "/// <summary>Trim-safe JSON converter for this model's change payload.</summary>"
        );
        code.AppendLineAt(
            1,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        code.AppendLineAt(
            1,
            "public sealed class "
                + context.ConverterName
                + " : global::System.Text.Json.Serialization.JsonConverter<ChangePayload>"
        );
        code.AppendLineAt(1, "{");
        AppendPayload(code, context);
        AppendCore(code, context);
        AppendRoot(code, context);
        // Public-first order: the internal leaf-codec converter follows the
        // public readers and precedes the private member codecs below.
        if (SparsePayloadJsonPocoEmitter.HasPocos(context))
            SparsePayloadJsonPocoEmitter.AppendPocos(code, context);
        AppendDispatch(code, context, members);
        foreach (var member in members)
            SparsePayloadJsonMemberEmitter.AppendMember(code, context, member);
        SparsePayloadJsonSharedEmitter.AppendShared(code, context);
        code.AppendLineAt(1, "}");
        code.AppendLine();
    }

    private static void AppendPayload(
        SharedIndentedBuilder code,
        PayloadJsonEmissionContext context
    )
    {
        var change = context.Dto("Change");
        code.AppendLineAt(2, "/// <inheritdoc />");
        code.AppendLineAt(
            2,
            "public override ChangePayload? Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return null;"
        );
        code.AppendLineAt(
            3,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A payload must be an object.\");"
        );
        code.AppendLineAt(3, "string? version = null;");
        code.AppendLineAt(
            3,
            "var changes = new global::System.Collections.Generic.List<" + change + ">();"
        );
        code.AppendLineAt(3, "var hasChanges = false;");
        code.AppendLineAt(3, "while (reader.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            4,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed payload.\");"
        );
        code.AppendLineAt(4, "if (reader.ValueTextEquals(\"version\"))");
        code.AppendLineAt(
            5,
            "{ if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload.\"); version = reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? null : reader.GetString(); }"
        );
        code.AppendLineAt(4, "else if (reader.ValueTextEquals(\"changes\"))");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload.\");"
        );
        code.AppendLineAt(5, "hasChanges = true;");
        code.AppendLineAt(
            5,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.Null)"
        );
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"Payload changes must be an array.\");"
        );
        code.AppendLineAt(6, "while (reader.Read())");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;"
        );
        code.AppendLineAt(7, "changes.Add(ReadChange(ref reader, options));");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "else { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload.\"); reader.Skip(); }"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "var payload = new ChangePayload { Version = version };");
        code.AppendLineAt(3, "if (hasChanges) payload.Changes = changes;");
        code.AppendLineAt(3, "return payload;");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(2, "/// <inheritdoc />");
        code.AppendLineAt(
            2,
            "public override void Write(global::System.Text.Json.Utf8JsonWriter writer, ChangePayload? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLineAt(3, "writer.WriteStartObject();");
        code.AppendLineAt(3, "writer.WriteString(\"version\", value.Version);");
        code.AppendLineAt(3, "writer.WritePropertyName(\"changes\");");
        code.AppendLineAt(3, "if (value.Changes is null) writer.WriteNullValue();");
        code.AppendLineAt(
            3,
            "else { writer.WriteStartArray(); foreach (var item in value.Changes) WriteChange(writer, item, options); writer.WriteEndArray(); }"
        );
        code.AppendLineAt(3, "writer.WriteEndObject();");
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    private static void AppendCore(SharedIndentedBuilder code, PayloadJsonEmissionContext context)
    {
        var core = context.Dto("Core");
        var change = context.Dto("Change");
        code.AppendLineAt(2, "/// <summary>Reads a nested change core.</summary>");
        code.AppendLineAt(
            2,
            "public static "
                + core
                + " ReadCore(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A core must be an object.\");"
        );
        code.AppendLineAt(3, "var core = new " + core + "();");
        code.AppendLineAt(3, "while (reader.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            4,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed core.\");"
        );
        code.AppendLineAt(4, "if (reader.ValueTextEquals(\"changes\"))");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed core.\");"
        );
        code.AppendLineAt(
            5,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) core.Changes = null;"
        );
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"Payload changes must be an array.\");"
        );
        code.AppendLineAt(
            6,
            "var list = new global::System.Collections.Generic.List<" + change + ">();"
        );
        code.AppendLineAt(6, "while (reader.Read())");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;"
        );
        code.AppendLineAt(7, "list.Add(ReadChange(ref reader, options));");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "core.Changes = list;");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "else { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed core.\"); reader.Skip(); }"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return core;");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(2, "/// <summary>Writes a nested change core.</summary>");
        code.AppendLineAt(
            2,
            "public static void WriteCore(global::System.Text.Json.Utf8JsonWriter writer, "
                + core
                + "? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLineAt(3, "writer.WriteStartObject();");
        code.AppendLineAt(3, "writer.WritePropertyName(\"changes\");");
        code.AppendLineAt(3, "if (value.Changes is null) writer.WriteNullValue();");
        code.AppendLineAt(
            3,
            "else { writer.WriteStartArray(); foreach (var item in value.Changes) WriteChange(writer, item, options); writer.WriteEndArray(); }"
        );
        code.AppendLineAt(3, "writer.WriteEndObject();");
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    private static void AppendRoot(SharedIndentedBuilder code, PayloadJsonEmissionContext context)
    {
        var root = context.Dto("Root");
        code.AppendLineAt(2, "/// <summary>Reads a snapshot root.</summary>");
        code.AppendLineAt(
            2,
            "public static "
                + root
                + " ReadRoot(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A root must be an object.\");"
        );
        code.AppendLineAt(3, "var root = new " + root + "();");
        code.AppendLineAt(3, "while (reader.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            4,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed root.\");"
        );
        code.AppendLineAt(4, "if (reader.ValueTextEquals(\"members\"))");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed root.\");"
        );
        code.AppendLineAt(
            5,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) root.Members = new();"
        );
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"Root members must be an array.\");"
        );
        code.AppendLineAt(6, "root.Members.Clear();");
        code.AppendLineAt(6, "while (reader.Read())");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;"
        );
        code.AppendLineAt(7, "root.Members.Add(ReadChange(ref reader, options));");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "else { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed root.\"); reader.Skip(); }"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return root;");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(2, "/// <summary>Writes a snapshot root.</summary>");
        code.AppendLineAt(
            2,
            "public static void WriteRoot(global::System.Text.Json.Utf8JsonWriter writer, "
                + root
                + "? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLineAt(3, "writer.WriteStartObject();");
        code.AppendLineAt(3, "writer.WritePropertyName(\"members\");");
        code.AppendLineAt(3, "writer.WriteStartArray();");
        code.AppendLineAt(
            3,
            "foreach (var item in value.Members) WriteChange(writer, item, options);"
        );
        code.AppendLineAt(3, "writer.WriteEndArray();");
        code.AppendLineAt(3, "writer.WriteEndObject();");
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    private static void AppendDispatch(
        SharedIndentedBuilder code,
        PayloadJsonEmissionContext context,
        SparseMemberModel[] members
    )
    {
        var change = context.Dto("Change");
        var rootChange = context.Dto("RootChange");
        code.AppendLineAt(
            2,
            "private static "
                + change
                + " ReadChange(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A change must be an object.\");"
        );
        code.AppendLineAt(3, "var lookahead = reader;");
        code.AppendLineAt(3, "string? member = null;");
        code.AppendLineAt(3, "var hasMember = false;");
        code.AppendLineAt(3, "var hasEndObject = false;");
        code.AppendLineAt(3, "while (lookahead.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (lookahead.TokenType == global::System.Text.Json.JsonTokenType.EndObject) { hasEndObject = true; break; }"
        );
        code.AppendLineAt(
            4,
            "if (lookahead.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) { lookahead.Skip(); continue; }"
        );
        code.AppendLineAt(4, "var isMember = lookahead.ValueTextEquals(\"member\");");
        code.AppendLineAt(4, "if (!lookahead.Read()) break;");
        code.AppendLineAt(4, "if (isMember)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "hasMember = true;");
        code.AppendLineAt(
            5,
            "member = lookahead.TokenType == global::System.Text.Json.JsonTokenType.String ? lookahead.GetString() : null;"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "lookahead.Skip();");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "if (hasEndObject && hasMember) return ReadDispatchedChange(ref reader, options, member);"
        );
        code.AppendLineAt(
            3,
            "using var document = global::System.Text.Json.JsonDocument.ParseValue(ref reader);"
        );
        code.AppendLineAt(3, "var element = document.RootElement;");
        code.AppendLineAt(
            3,
            "if (element.TryGetProperty(\"member\", out var discriminator) && discriminator.ValueKind == global::System.Text.Json.JsonValueKind.String) member = discriminator.GetString();"
        );
        code.AppendLineAt(
            3,
            "var bytes = System.Text.Encoding.UTF8.GetBytes(element.GetRawText());"
        );
        code.AppendLineAt(3, "var nested = new global::System.Text.Json.Utf8JsonReader(bytes);");
        code.AppendLineAt(3, "nested.Read();");
        code.AppendLineAt(3, "return ReadDispatchedChange(ref nested, options, member);");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static "
                + change
                + " ReadDispatchedChange(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options, string? member)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (member == "
                + Format(context.RootDiscriminator)
                + ") return ReadRootChange(ref reader, options);"
        );
        foreach (var member in members)
            code.AppendLineAt(
                3,
                "if (member == "
                    + Format(member.Property.Name)
                    + ") return ReadMember"
                    + member.Id
                    + "(ref reader, options);"
            );
        code.AppendLineAt(
            3,
            "throw new global::System.Text.Json.JsonException(\"Unknown payload member.\");"
        );
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static void WriteChange(global::System.Text.Json.Utf8JsonWriter writer, "
                + change
                + " item, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "writer.WriteStartObject();");
        code.AppendLineAt(3, "var written = false;");
        foreach (var member in members)
        {
            var memberChange = context.MemberDto("Change", member.Id);
            code.AppendLineAt(
                3,
                "if (!written && item is " + memberChange + " member" + member.Id + ")"
            );
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "writer.WriteString(\"member\", " + Format(member.Property.Name) + ");"
            );
            code.AppendLineAt(
                4,
                "WriteMember" + member.Id + "Body(writer, member" + member.Id + ", options);"
            );
            code.AppendLineAt(4, "written = true;");
            code.AppendLineAt(3, "}");
        }

        code.AppendLineAt(3, "if (!written && item is " + rootChange + " rootChange)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "writer.WriteString(\"member\", " + Format(context.RootDiscriminator) + ");"
        );
        code.AppendLineAt(4, "WriteRootChangeBody(writer, rootChange, options);");
        code.AppendLineAt(4, "written = true;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "if (!written) throw new global::System.Text.Json.JsonException(\"Unknown payload change.\");"
        );
        code.AppendLineAt(3, "writer.WriteEndObject();");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static "
                + rootChange
                + " ReadRootChange(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A change must be an object.\");"
        );
        code.AppendLineAt(3, "var change = new " + rootChange + "();");
        code.AppendLineAt(3, "while (reader.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            4,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed change.\");"
        );
        code.AppendLineAt(4, "var name = reader.GetString() ?? string.Empty;");
        code.AppendLineAt(
            4,
            "if (name == \"member\") { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed change.\"); reader.Skip(); continue; }"
        );
        code.AppendLineAt(
            4,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed change.\");"
        );
        code.AppendLineAt(
            4,
            "if (name == \"before\") { if (!TryReadNull(ref reader)) change.Before = ReadEndpoint<"
                + context.Dto("Root")
                + ">(ref reader, options, ReadRoot, null); }"
        );
        code.AppendLineAt(
            4,
            "else if (name == \"after\") { if (!TryReadNull(ref reader)) change.After = ReadEndpoint<"
                + context.Dto("Root")
                + ">(ref reader, options, ReadRoot, null); }"
        );
        code.AppendLineAt(4, "else reader.Skip();");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return change;");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static void WriteRootChangeBody(global::System.Text.Json.Utf8JsonWriter writer, "
                + rootChange
                + " change, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (change.Before is not null) { writer.WritePropertyName(\"before\"); WriteEndpoint(writer, change.Before, options, WriteRoot); }"
        );
        code.AppendLineAt(
            3,
            "if (change.After is not null) { writer.WritePropertyName(\"after\"); WriteEndpoint(writer, change.After, options, WriteRoot); }"
        );
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    private static string Format(string value) =>
        global::Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, true);
}
