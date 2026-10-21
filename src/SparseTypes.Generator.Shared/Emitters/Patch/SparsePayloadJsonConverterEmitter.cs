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
        code.AppendLine(
            "/// <summary>Trim-safe JSON converter for this model's change payload.</summary>"
        );
        code.AppendLine(
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        code.AppendLine(
            "public sealed class "
                + context.ConverterName
                + " : global::System.Text.Json.Serialization.JsonConverter<ChangePayload>"
        );
        code.OpenBlock();
        AppendPayload(code, context);
        AppendCore(code, context);
        AppendRoot(code, context);
        // Public-first order: the internal leaf-codec converter follows the
        // public readers and precedes the private member codecs below.
        if (SparsePayloadJsonPocoEmitter.HasPocos(context))
        {
            SparsePayloadJsonPocoEmitter.AppendPocos(code, context);
        }

        AppendDispatch(code, context, members);
        foreach (var member in members)
        {
            SparsePayloadJsonMemberEmitter.AppendMember(code, context, member);
        }

        SparsePayloadJsonSharedEmitter.AppendShared(code, context);
        code.CloseBlock();
        code.AppendLine();
    }

    private static void AppendPayload(
        SharedIndentedBuilder code,
        PayloadJsonEmissionContext context
    )
    {
        var change = context.Dto("Change");
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine(
            "public override ChangePayload? Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return null;"
        );
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A payload must be an object.\");"
        );
        code.AppendLine("string? version = null;");
        code.AppendLine(
            "var changes = new global::System.Collections.Generic.List<" + change + ">();"
        );
        code.AppendLine("var hasChanges = false;");
        code.AppendLine("while (reader.Read())");
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed payload.\");"
        );
        code.AppendLine("if (reader.ValueTextEquals(\"version\"))");
        code.AppendLine(
            "{ if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload.\"); version = reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? null : reader.GetString(); }"
        );
        code.AppendLine("else if (reader.ValueTextEquals(\"changes\"))");
        code.OpenBlock();
        code.AppendLine(
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload.\");"
        );
        code.AppendLine("hasChanges = true;");
        code.AppendLine("if (reader.TokenType != global::System.Text.Json.JsonTokenType.Null)");
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"Payload changes must be an array.\");"
        );
        code.AppendLine("while (reader.Read())");
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;"
        );
        code.AppendLine("changes.Add(ReadChange(ref reader, options));");
        code.CloseBlock();
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine(
            "else { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload.\"); reader.Skip(); }"
        );
        code.CloseBlock();
        code.AppendLine("var payload = new ChangePayload { Version = version };");
        code.AppendLine("if (hasChanges) payload.Changes = changes;");
        code.AppendLine("return payload;");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine(
            "public override void Write(global::System.Text.Json.Utf8JsonWriter writer, ChangePayload? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLine("writer.WriteStartObject();");
        code.AppendLine("writer.WriteString(\"version\", value.Version);");
        code.AppendLine("writer.WritePropertyName(\"changes\");");
        code.AppendLine("if (value.Changes is null) writer.WriteNullValue();");
        code.AppendLine(
            "else { writer.WriteStartArray(); foreach (var item in value.Changes) WriteChange(writer, item, options); writer.WriteEndArray(); }"
        );
        code.AppendLine("writer.WriteEndObject();");
        code.CloseBlock();
        code.AppendLine();
    }

    private static void AppendCore(SharedIndentedBuilder code, PayloadJsonEmissionContext context)
    {
        var core = context.Dto("Core");
        var change = context.Dto("Change");
        code.AppendLine("/// <summary>Reads a nested change core.</summary>");
        code.AppendLine(
            "public static "
                + core
                + " ReadCore(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A core must be an object.\");"
        );
        code.AppendLine("var core = new " + core + "();");
        code.AppendLine("while (reader.Read())");
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed core.\");"
        );
        code.AppendLine("if (reader.ValueTextEquals(\"changes\"))");
        code.OpenBlock();
        code.AppendLine(
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed core.\");"
        );
        code.AppendLine(
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) core.Changes = null;"
        );
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"Payload changes must be an array.\");"
        );
        code.AppendLine(
            "var list = new global::System.Collections.Generic.List<" + change + ">();"
        );
        code.AppendLine("while (reader.Read())");
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;"
        );
        code.AppendLine("list.Add(ReadChange(ref reader, options));");
        code.CloseBlock();
        code.AppendLine("core.Changes = list;");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine(
            "else { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed core.\"); reader.Skip(); }"
        );
        code.CloseBlock();
        code.AppendLine("return core;");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine("/// <summary>Writes a nested change core.</summary>");
        code.AppendLine(
            "public static void WriteCore(global::System.Text.Json.Utf8JsonWriter writer, "
                + core
                + "? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLine("writer.WriteStartObject();");
        code.AppendLine("writer.WritePropertyName(\"changes\");");
        code.AppendLine("if (value.Changes is null) writer.WriteNullValue();");
        code.AppendLine(
            "else { writer.WriteStartArray(); foreach (var item in value.Changes) WriteChange(writer, item, options); writer.WriteEndArray(); }"
        );
        code.AppendLine("writer.WriteEndObject();");
        code.CloseBlock();
        code.AppendLine();
    }

    private static void AppendRoot(SharedIndentedBuilder code, PayloadJsonEmissionContext context)
    {
        var root = context.Dto("Root");
        code.AppendLine("/// <summary>Reads a snapshot root.</summary>");
        code.AppendLine(
            "public static "
                + root
                + " ReadRoot(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A root must be an object.\");"
        );
        code.AppendLine("var root = new " + root + "();");
        code.AppendLine("while (reader.Read())");
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed root.\");"
        );
        code.AppendLine("if (reader.ValueTextEquals(\"members\"))");
        code.OpenBlock();
        code.AppendLine(
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed root.\");"
        );
        code.AppendLine(
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) root.Members = new();"
        );
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"Root members must be an array.\");"
        );
        code.AppendLine("root.Members.Clear();");
        code.AppendLine("while (reader.Read())");
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;"
        );
        code.AppendLine("root.Members.Add(ReadChange(ref reader, options));");
        code.CloseBlock();
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine(
            "else { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed root.\"); reader.Skip(); }"
        );
        code.CloseBlock();
        code.AppendLine("return root;");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine("/// <summary>Writes a snapshot root.</summary>");
        code.AppendLine(
            "public static void WriteRoot(global::System.Text.Json.Utf8JsonWriter writer, "
                + root
                + "? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLine("writer.WriteStartObject();");
        code.AppendLine("writer.WritePropertyName(\"members\");");
        code.AppendLine("writer.WriteStartArray();");
        code.AppendLine("foreach (var item in value.Members) WriteChange(writer, item, options);");
        code.AppendLine("writer.WriteEndArray();");
        code.AppendLine("writer.WriteEndObject();");
        code.CloseBlock();
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
        code.AppendLine(
            "private static "
                + change
                + " ReadChange(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A change must be an object.\");"
        );
        code.AppendLine("var lookahead = reader;");
        code.AppendLine("string? member = null;");
        code.AppendLine("var hasMember = false;");
        code.AppendLine("var hasEndObject = false;");
        code.AppendLine("while (lookahead.Read())");
        code.OpenBlock();
        code.AppendLine(
            "if (lookahead.TokenType == global::System.Text.Json.JsonTokenType.EndObject) { hasEndObject = true; break; }"
        );
        code.AppendLine(
            "if (lookahead.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) { lookahead.Skip(); continue; }"
        );
        code.AppendLine("var isMember = lookahead.ValueTextEquals(\"member\");");
        code.AppendLine("if (!lookahead.Read()) break;");
        code.AppendLine("if (isMember)");
        code.OpenBlock();
        code.AppendLine("hasMember = true;");
        code.AppendLine(
            "member = lookahead.TokenType == global::System.Text.Json.JsonTokenType.String ? lookahead.GetString() : null;"
        );
        code.CloseBlock();
        code.AppendLine("lookahead.Skip();");
        code.CloseBlock();
        code.AppendLine(
            "if (hasEndObject && hasMember) return ReadDispatchedChange(ref reader, options, member);"
        );
        code.AppendLine(
            "using var document = global::System.Text.Json.JsonDocument.ParseValue(ref reader);"
        );
        code.AppendLine("var element = document.RootElement;");
        code.AppendLine(
            "if (element.TryGetProperty(\"member\", out var discriminator) && discriminator.ValueKind == global::System.Text.Json.JsonValueKind.String) member = discriminator.GetString();"
        );
        code.AppendLine("var bytes = System.Text.Encoding.UTF8.GetBytes(element.GetRawText());");
        code.AppendLine("var nested = new global::System.Text.Json.Utf8JsonReader(bytes);");
        code.AppendLine("nested.Read();");
        code.AppendLine("return ReadDispatchedChange(ref nested, options, member);");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static "
                + change
                + " ReadDispatchedChange(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options, string? member)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (member == "
                + Format(context.RootDiscriminator)
                + ") return ReadRootChange(ref reader, options);"
        );
        foreach (var member in members)
        {
            code.AppendLine(
                "if (member == "
                    + Format(member.Property.Name)
                    + ") return ReadMember"
                    + member.Id
                    + "(ref reader, options);"
            );
        }

        code.AppendLine(
            "throw new global::System.Text.Json.JsonException(\"Unknown payload member.\");"
        );
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static void WriteChange(global::System.Text.Json.Utf8JsonWriter writer, "
                + change
                + " item, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("writer.WriteStartObject();");
        code.AppendLine("var written = false;");
        foreach (var member in members)
        {
            var memberChange = context.MemberDto("Change", member.Id);
            code.AppendLine(
                "if (!written && item is " + memberChange + " member" + member.Id + ")"
            );
            code.OpenBlock();
            code.AppendLine(
                "writer.WriteString(\"member\", " + Format(member.Property.Name) + ");"
            );
            code.AppendLine(
                "WriteMember" + member.Id + "Body(writer, member" + member.Id + ", options);"
            );
            code.AppendLine("written = true;");
            code.CloseBlock();
        }

        code.AppendLine("if (!written && item is " + rootChange + " rootChange)");
        code.OpenBlock();
        code.AppendLine(
            "writer.WriteString(\"member\", " + Format(context.RootDiscriminator) + ");"
        );
        code.AppendLine("WriteRootChangeBody(writer, rootChange, options);");
        code.AppendLine("written = true;");
        code.CloseBlock();
        code.AppendLine(
            "if (!written) throw new global::System.Text.Json.JsonException(\"Unknown payload change.\");"
        );
        code.AppendLine("writer.WriteEndObject();");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static "
                + rootChange
                + " ReadRootChange(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A change must be an object.\");"
        );
        code.AppendLine("var change = new " + rootChange + "();");
        code.AppendLine("while (reader.Read())");
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed change.\");"
        );
        code.AppendLine("var name = reader.GetString() ?? string.Empty;");
        code.AppendLine(
            "if (name == \"member\") { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed change.\"); reader.Skip(); continue; }"
        );
        code.AppendLine(
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed change.\");"
        );
        code.AppendLine(
            "if (name == \"before\") { if (!TryReadNull(ref reader)) change.Before = ReadEndpoint<"
                + context.Dto("Root")
                + ">(ref reader, options, ReadRoot, null); }"
        );
        code.AppendLine(
            "else if (name == \"after\") { if (!TryReadNull(ref reader)) change.After = ReadEndpoint<"
                + context.Dto("Root")
                + ">(ref reader, options, ReadRoot, null); }"
        );
        code.AppendLine("else reader.Skip();");
        code.CloseBlock();
        code.AppendLine("return change;");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static void WriteRootChangeBody(global::System.Text.Json.Utf8JsonWriter writer, "
                + rootChange
                + " change, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (change.Before is not null) { writer.WritePropertyName(\"before\"); WriteEndpoint(writer, change.Before, options, WriteRoot); }"
        );
        code.AppendLine(
            "if (change.After is not null) { writer.WritePropertyName(\"after\"); WriteEndpoint(writer, change.After, options, WriteRoot); }"
        );
        code.CloseBlock();
        code.AppendLine();
    }

    private static string Format(string value) =>
        global::Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, true);
}
