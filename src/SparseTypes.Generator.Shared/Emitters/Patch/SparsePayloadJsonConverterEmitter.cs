namespace SparseTypes.Generator.Shared;

/// <summary>Emits the per-model trim-safe payload JSON converter envelope.</summary>
internal static class SparsePayloadJsonConverterEmitter
{
    /// <summary>Emits the converter inside the payload implementation container.</summary>
    internal static void AppendConverter(SharedIndentedBuilder code, PayloadJsonEmissionContext context, string accessibility)
    {
        var members = context.Contract.JsonMembers.Select(static member => member.Member).ToArray();
        var changePayloadType = context.ModelType + ".ChangePayload";
        code.AppendLine("/// <summary>Trim-safe JSON converter for this model's change payload.</summary>");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine(
            accessibility
                + " sealed class "
                + context.ConverterName
                + " : global::System.Text.Json.Serialization.JsonConverter<"
                + changePayloadType
                + ">"
        );
        code.OpenBlock();
        AppendPayload(code, context);
        SparsePayloadJsonPreflightEmitter.AppendPreflight(code, context);
        AppendCore(code, context);
        AppendRoot(code, context);
        // Public-first order: the internal leaf-codec converter follows the
        // public readers and precedes the private member codecs below.
        if (SparsePayloadJsonPocoEmitter.HasPocos(context))
        {
            SparsePayloadJsonPocoEmitter.AppendPocos(code, context);
        }

        AppendDispatch(code, context);
        foreach (var member in members)
        {
            SparsePayloadJsonMemberEmitter.AppendMember(code, context, member);
        }

        SparsePayloadJsonSharedEmitter.AppendShared(code, context);
        code.CloseBlock();
        code.AppendLine();
    }

    private static void AppendPayload(SharedIndentedBuilder code, PayloadJsonEmissionContext context)
    {
        var changePayloadType = context.ModelType + ".ChangePayload";
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine(
            "public override "
                + changePayloadType
                + "? Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return null;");
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A payload must be an object.\");"
        );
        code.AppendLine("string? version = null;");
        code.AppendLine(context.Dto("ModelChange") + "? change = null;");
        code.AppendLine("bool hasVersion = false;");
        code.AppendLine("bool hasChange = false;");
        code.AppendLine("var seenProperties = new global::System.Collections.Generic.HashSet<string>(global::System.StringComparer.Ordinal);");
        code.AppendLine("while (reader.Read())");
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;");
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed payload.\");"
        );
        code.AppendLine("var name = reader.GetString() ?? string.Empty;");
        code.AppendLine("if (!seenProperties.Add(name)) throw new global::System.Text.Json.JsonException(\"Duplicate JSON property.\");");
        code.AppendLine("if (name == \"version\")");
        code.AppendLine(
            "{ if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload.\"); version = reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? null : reader.GetString(); hasVersion = true; }"
        );
        code.AppendLine("else if (name == \"change\")");
        code.AppendLine(
            "{ if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload.\"); change = ReadModelChange(ref reader, options); hasChange = true; }"
        );
        code.AppendLine("else { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed payload.\"); reader.Skip(); }");
        code.CloseBlock();
        code.AppendLine(
            "if (!hasVersion || !hasChange || change is null) throw new global::System.Text.Json.JsonException(\"A payload requires version and change properties.\");"
        );
        code.AppendLine("return new " + changePayloadType + " { Version = version, Change = change };");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine("/// <inheritdoc />");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine(
            "public override void Write(global::System.Text.Json.Utf8JsonWriter writer, "
                + changePayloadType
                + "? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLine("ValidatePayloadForJson(value);");
        code.AppendLine("var change = value.Change ?? throw new global::System.Text.Json.JsonException(\"A payload change is required.\");");
        code.AppendLine("writer.WriteStartObject();");
        code.AppendLine("writer.WriteString(\"version\", value.Version);");
        code.AppendLine("writer.WritePropertyName(\"change\");");
        code.AppendLine("WriteModelChange(writer, change, options);");
        code.AppendLine("writer.WriteEndObject();");
        code.CloseBlock();
        code.AppendLine();
    }

    private static void AppendCore(SharedIndentedBuilder code, PayloadJsonEmissionContext context)
    {
        var core = context.Dto("Core");
        var modelChange = context.Dto("ModelChange");
        code.AppendLine("/// <summary>Reads a nested model change.</summary>");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine(
            "internal static "
                + core
                + " ReadCore(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine(modelChange + " change = ReadModelChange(ref reader, options);");
        code.AppendLine("return new " + core + " { TypedChange = change };");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "internal static "
                + core
                + " ReadMembersCore(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine(
            "return new "
                + core
                + " { TypedChange = new "
                + modelChange
                + " { Kind = "
                + context.Dto("ModelChangeKind")
                + ".Update, Members = ReadMembersDelta(ref reader, options) } };"
        );
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "internal static void WriteMembersCore(global::System.Text.Json.Utf8JsonWriter writer, "
                + core
                + "? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine(
            "var change = value?.TypedChange ?? throw new global::System.Text.Json.JsonException(\"A model item update requires members.\");"
        );
        code.AppendLine(
            "if (change.Kind != "
                + context.Dto("ModelChangeKind")
                + ".Update || change.Members is null || change.Before is not null || change.After is not null) throw new global::System.Text.Json.JsonException(\"A model item update requires members.\");"
        );
        code.AppendLine("writer.WritePropertyName(\"members\"); WriteMembersDelta(writer, change.Members, options);");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine("/// <summary>Writes a nested model change.</summary>");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine(
            "internal static void WriteCore(global::System.Text.Json.Utf8JsonWriter writer, "
                + core
                + "? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLine("var change = value.TypedChange ?? throw new global::System.Text.Json.JsonException(\"A model change is required.\");");
        code.AppendLine("WriteModelChange(writer, change, options);");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "internal static void WriteCoreBody(global::System.Text.Json.Utf8JsonWriter writer, "
                + core
                + "? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("var change = value?.TypedChange ?? throw new global::System.Text.Json.JsonException(\"A model change is required.\");");
        code.AppendLine("WriteModelChangeBody(writer, change, options);");
        code.CloseBlock();
        code.AppendLine();
    }

    private static void AppendRoot(SharedIndentedBuilder code, PayloadJsonEmissionContext context)
    {
        var snapshot = context.Dto("Root");
        code.AppendLine("/// <summary>Reads a typed model snapshot.</summary>");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine(
            "internal static "
                + snapshot
                + " ReadRoot(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A snapshot must be an object.\");"
        );
        code.AppendLine("var snapshot = new " + snapshot + "();");
        code.AppendLine("var seenProperties = new global::System.Collections.Generic.HashSet<string>(global::System.StringComparer.Ordinal);");
        code.AppendLine("while (reader.Read())");
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;");
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed snapshot.\");"
        );
        code.AppendLine("var name = reader.GetString() ?? string.Empty;");
        code.AppendLine("if (!seenProperties.Add(name)) throw new global::System.Text.Json.JsonException(\"Duplicate JSON property.\");");
        code.AppendLine("if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed snapshot.\");");
        code.AppendLine("switch (name)");
        code.OpenBlock();
        foreach (var contractMember in context.Contract.JsonMembers)
        {
            var member = contractMember.Member;
            code.AppendLine("case " + Format(contractMember.JsonName) + ":");
            if (SparseChangeSetBasicsEmitter.IsNested(member))
            {
                code.AppendLine(
                    "snapshot." + SparseNaming.EscapeIdentifier(member.Property.Name) + " = ReadChildEndpoint" + member.Id + "(ref reader, options);"
                );
            }
            else
            {
                var readCollection =
                    SparseJsonCollectionShapes.TryResolve(member, SparseChangeSetBasicsEmitter.FragmentValueType(member), out var collection)
                    && collection is not null
                        ? ", ReadCollection" + member.Id + ", WriteCollection" + member.Id
                        : ", null, null";
                code.AppendLine(
                    "snapshot."
                        + SparseNaming.EscapeIdentifier(member.Property.Name)
                        + " = ReadEndpoint<"
                        + SparseChangeSetBasicsEmitter.FragmentValueType(member)
                        + ">(ref reader, options"
                        + readCollection
                        + ");"
                );
            }
            code.AppendLine("break;");
        }
        foreach (var contractMember in context.Contract.Members.Where(static member => !member.IsJsonIncluded))
        {
            code.AppendLine(
                "case "
                    + Format(contractMember.JsonName)
                    + ": throw new global::System.Text.Json.JsonException(\"A model member is excluded from the JSON payload contract.\");"
            );
        }
        code.AppendLine("default: reader.Skip(); break;");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("return snapshot;");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine("/// <summary>Writes a typed model snapshot.</summary>");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine(
            "internal static void WriteRoot(global::System.Text.Json.Utf8JsonWriter writer, "
                + snapshot
                + "? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("if (value is null) throw new global::System.Text.Json.JsonException(\"A snapshot is required.\");");
        code.AppendLine("writer.WriteStartObject();");
        foreach (var contractMember in context.Contract.JsonMembers)
        {
            var member = contractMember.Member;
            var prop = SparseNaming.EscapeIdentifier(member.Property.Name);
            code.AppendLine("if (value." + prop + " is not null)");
            code.OpenBlock();
            code.AppendLine("writer.WritePropertyName(" + Format(contractMember.JsonName) + ");");
            if (SparseChangeSetBasicsEmitter.IsNested(member))
            {
                code.AppendLine("WriteChildEndpoint" + member.Id + "(writer, value." + prop + ", options);");
            }
            else
            {
                var writeCollection =
                    SparseJsonCollectionShapes.TryResolve(member, SparseChangeSetBasicsEmitter.FragmentValueType(member), out var collection)
                    && collection is not null
                        ? ", WriteCollection" + member.Id
                        : ", null";
                code.AppendLine("WriteEndpoint(writer, value." + prop + ", options" + writeCollection + ");");
            }
            code.CloseBlock();
        }
        code.AppendLine("writer.WriteEndObject();");
        code.CloseBlock();
        code.AppendLine();
    }

    private static void AppendDispatch(SharedIndentedBuilder code, PayloadJsonEmissionContext context)
    {
        var modelChange = context.Dto("ModelChange");
        var kindType = context.Dto("ModelChangeKind");
        var snapshot = context.Dto("Root");
        var delta = context.Dto("MembersDelta");

        code.AppendLine(
            "private static "
                + modelChange
                + " ReadModelChange(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A model change must be an object.\");"
        );
        code.AppendLine("var change = new " + modelChange + "();");
        code.AppendLine("string? operation = null;");
        code.AppendLine("bool hasKind = false, hasMembers = false, hasBefore = false, hasAfter = false;");
        code.AppendLine("var seenProperties = new global::System.Collections.Generic.HashSet<string>(global::System.StringComparer.Ordinal);");
        code.AppendLine("while (reader.Read())");
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;");
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed model change.\");"
        );
        code.AppendLine("var name = reader.GetString() ?? string.Empty;");
        code.AppendLine("if (!seenProperties.Add(name)) throw new global::System.Text.Json.JsonException(\"Duplicate JSON property.\");");
        code.AppendLine("if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed model change.\");");
        code.AppendLine("if (name == \"kind\")");
        code.OpenBlock();
        code.AppendLine("operation = reader.TokenType == global::System.Text.Json.JsonTokenType.String ? reader.GetString() : null;");
        code.AppendLine(
            "change.Kind = operation switch { \"update\" => "
                + kindType
                + ".Update, \"replace\" => "
                + kindType
                + ".Replace, _ => throw new global::System.Text.Json.JsonException(\"Unknown model change kind.\") };"
        );
        code.AppendLine("hasKind = true;");
        code.CloseBlock();
        code.AppendLine("else if (name == \"members\") { change.Members = ReadMembersDelta(ref reader, options); hasMembers = true; }");
        code.AppendLine(
            "else if (name == \"before\") { change.Before = ReadEndpoint<" + snapshot + ">(ref reader, options, ReadRoot, null); hasBefore = true; }"
        );
        code.AppendLine(
            "else if (name == \"after\") { change.After = ReadEndpoint<" + snapshot + ">(ref reader, options, ReadRoot, null); hasAfter = true; }"
        );
        code.AppendLine("else reader.Skip();");
        code.CloseBlock();
        code.AppendLine("if (!hasKind) throw new global::System.Text.Json.JsonException(\"A model change requires a kind.\");");
        code.AppendLine(
            "if (operation == \"replace\" && (!hasBefore || !hasAfter || change.Before is null || change.After is null || hasMembers)) throw new global::System.Text.Json.JsonException(\"A replacement requires both endpoints and no members object.\");"
        );
        code.AppendLine(
            "if (operation == \"update\" && (!hasMembers || change.Members is null || hasBefore || hasAfter)) throw new global::System.Text.Json.JsonException(\"An update requires members and cannot contain replacement endpoints.\");"
        );
        code.AppendLine("return change;");
        code.CloseBlock();
        code.AppendLine();

        code.AppendLine(
            "private static "
                + delta
                + " ReadMembersDelta(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"Members must be an object.\");"
        );
        code.AppendLine("var members = new " + delta + "();");
        code.AppendLine("var seenProperties = new global::System.Collections.Generic.HashSet<string>(global::System.StringComparer.Ordinal);");
        code.AppendLine("while (reader.Read())");
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;");
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed members object.\");"
        );
        code.AppendLine("var name = reader.GetString() ?? string.Empty;");
        code.AppendLine("if (!seenProperties.Add(name)) throw new global::System.Text.Json.JsonException(\"Duplicate JSON property.\");");
        code.AppendLine("if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed members object.\");");
        code.AppendLine("switch (name)");
        code.OpenBlock();
        foreach (var contractMember in context.Contract.JsonMembers)
        {
            var member = contractMember.Member;
            code.AppendLine(
                "case "
                    + Format(contractMember.JsonName)
                    + ": members."
                    + SparseNaming.EscapeIdentifier(member.Property.Name)
                    + " = ("
                    + context.MemberDto("Change", member.Id)
                    + ")ReadMember"
                    + member.Id
                    + "(ref reader, options); break;"
            );
        }
        foreach (var contractMember in context.Contract.Members.Where(static member => !member.IsJsonIncluded))
        {
            code.AppendLine(
                "case "
                    + Format(contractMember.JsonName)
                    + ": throw new global::System.Text.Json.JsonException(\"A model member is excluded from the JSON payload contract.\");"
            );
        }
        code.AppendLine("default: reader.Skip(); break;");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("return members;");
        code.CloseBlock();
        code.AppendLine();

        code.AppendLine(
            "private static void WriteModelChange(global::System.Text.Json.Utf8JsonWriter writer, "
                + modelChange
                + " change, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("if (change is null) throw new global::System.Text.Json.JsonException(\"A model change is required.\");");
        code.AppendLine("writer.WriteStartObject();");
        code.AppendLine("WriteModelChangeBody(writer, change, options);");
        code.AppendLine("writer.WriteEndObject();");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "private static void WriteModelChangeBody(global::System.Text.Json.Utf8JsonWriter writer, "
                + modelChange
                + " change, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("if (change is null) throw new global::System.Text.Json.JsonException(\"A model change is required.\");");
        code.AppendLine(
            "writer.WriteString(\"kind\", change.Kind switch { "
                + kindType
                + ".Update => \"update\", "
                + kindType
                + ".Replace => \"replace\", _ => throw new global::System.Text.Json.JsonException(\"Unknown model change kind.\") });"
        );
        code.AppendLine("if (change.Kind == " + kindType + ".Replace)");
        code.OpenBlock();
        code.AppendLine(
            "if (change.Members is not null || change.Before is null || change.After is null) throw new global::System.Text.Json.JsonException(\"A replacement requires both endpoints and no members object.\");"
        );
        code.AppendLine("writer.WritePropertyName(\"before\"); WriteEndpoint(writer, change.Before, options, WriteRoot);");
        code.AppendLine("writer.WritePropertyName(\"after\"); WriteEndpoint(writer, change.After, options, WriteRoot);");
        code.CloseBlock();
        code.AppendLine("else if (change.Kind == " + kindType + ".Update)");
        code.OpenBlock();
        code.AppendLine(
            "if (change.Members is null || change.Before is not null || change.After is not null) throw new global::System.Text.Json.JsonException(\"An update requires members and cannot contain replacement endpoints.\");"
        );
        code.AppendLine("writer.WritePropertyName(\"members\"); WriteMembersDelta(writer, change.Members, options);");
        code.CloseBlock();
        code.AppendLine("else throw new global::System.Text.Json.JsonException(\"Unknown model change kind.\");");
        code.CloseBlock();
        code.AppendLine();

        code.AppendLine(
            "private static void WriteMembersDelta(global::System.Text.Json.Utf8JsonWriter writer, "
                + delta
                + " members, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("if (members is null) throw new global::System.Text.Json.JsonException(\"Members are required.\");");
        code.AppendLine("writer.WriteStartObject();");
        foreach (var contractMember in context.Contract.JsonMembers)
        {
            var member = contractMember.Member;
            var prop = SparseNaming.EscapeIdentifier(member.Property.Name);
            code.AppendLine("if (members." + prop + " is not null)");
            code.OpenBlock();
            code.AppendLine("writer.WritePropertyName(" + Format(contractMember.JsonName) + ");");
            code.AppendLine("writer.WriteStartObject();");
            code.AppendLine("WriteMember" + member.Id + "Body(writer, members." + prop + ", options);");
            code.AppendLine("writer.WriteEndObject();");
            code.CloseBlock();
        }
        code.AppendLine("writer.WriteEndObject();");
        code.CloseBlock();
        code.AppendLine();
    }

    private static string Format(string value) => global::Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, true);
}
