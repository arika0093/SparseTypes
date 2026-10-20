using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits trim-safe JSON codecs for ordinary custom leaf types.</summary>
/// <remarks>Generates closed readers and writers from the analyzed member graph so supported POCOs round-trip without an application JsonSerializerContext.</remarks>
internal static class SparsePayloadJsonPocoEmitter
{
    /// <summary>Whether the context carries custom leaf codecs.</summary>
    internal static bool HasPocos(PayloadJsonEmissionContext context) =>
        !context.Pocos.IsDefaultOrEmpty;

    /// <summary>Emits leaf readers, writers, and the typed dispatch inside the converter.</summary>
    internal static void AppendPocos(SharedIndentedBuilder code, PayloadJsonEmissionContext context)
    {
        if (!HasPocos(context))
            return;
        var pocos = context.Pocos;
        // Public-first order: the internal delegating converters precede the
        // private codec methods below.
        SparsePayloadJsonPocoHelpers.AppendValueConverter(code);
        if (pocos.Any(static poco => poco.IsValueType))
            SparsePayloadJsonPocoHelpers.AppendNullableConverter(code);
        var suffixByName = pocos.ToDictionary(
            static poco => poco.RuntimeName,
            poco => CodecSuffix(poco, code),
            StringComparer.Ordinal
        );
        foreach (var poco in pocos)
        {
            AppendReader(code, poco, suffixByName);
            AppendWriter(code, context, poco, suffixByName);
        }

        AppendCollectionCodecs(code, context);
        SparsePayloadJsonPocoHelpers.AppendHelpers(code, context);
    }

    /// <summary>Emits resolver branches covering the generated leaf types.</summary>
    internal static void AppendResolverBranches(
        SharedIndentedBuilder code,
        PayloadJsonEmissionContext context
    )
    {
        if (!HasPocos(context))
            return;
        for (var pocoIndex = 0; pocoIndex < context.Pocos.Length; pocoIndex++)
        {
            var poco = context.Pocos[pocoIndex];
            code.CancellationToken.ThrowIfCancellationRequested();
            code.AppendLineAt(3, "if (type == typeof(" + poco.RuntimeName + "))");
            code.AppendLineAt(
                4,
                "return global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateValueInfo<"
                    + poco.RuntimeName
                    + ">(options, "
                    + context.ConverterName
                    + ".PocoValueConverter<"
                    + poco.RuntimeName
                    + ">.Default);"
            );
        }

        for (var pocoIndex = 0; pocoIndex < context.Pocos.Length; pocoIndex++)
        {
            var poco = context.Pocos[pocoIndex];
            if (!poco.IsValueType)
                continue;
            code.CancellationToken.ThrowIfCancellationRequested();
            code.AppendLineAt(3, "if (type == typeof(" + poco.RuntimeName + "?))");
            code.AppendLineAt(
                4,
                "return global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateValueInfo<"
                    + poco.RuntimeName
                    + "?>(options, "
                    + context.ConverterName
                    + ".PocoNullableConverter<"
                    + poco.RuntimeName
                    + ">.Default);"
            );
        }
    }

    /// <summary>Suffix for one leaf codec, stable across compilations.</summary>
    internal static string CodecSuffix(
        SparsePayloadJsonPocoModel poco,
        SharedIndentedBuilder code
    ) => SparseNaming.GetStableTypeHash(poco.RuntimeName, code.CancellationToken);

    private static void AppendReader(
        SharedIndentedBuilder code,
        SparsePayloadJsonPocoModel poco,
        Dictionary<string, string> suffixByName
    )
    {
        var suffix = CodecSuffix(poco, code);
        code.AppendLineAt(
            2,
            "private static "
                + poco.RuntimeName
                + " ReadPayloadPoco_"
                + suffix
                + "(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return default!;"
        );
        code.AppendLineAt(
            3,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A leaf value must be an object.\");"
        );
        var members = poco.Members.Where(static member => !member.Property.IsJsonIgnored).ToArray();
        for (var index = 0; index < members.Length; index++)
        {
            var member = members[index];
            var local = LocalName(suffix, index);
            var init = InitializerFor(poco, member);
            code.AppendLineAt(3, member.Property.Type.Name + " " + local + " = " + init + ";");
            if (member.Property.IsRequired)
                code.AppendLineAt(3, "var " + HasName(suffix, index) + " = false;");
        }

        code.AppendLineAt(3, "while (reader.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            4,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed leaf value.\");"
        );
        for (var index = 0; index < members.Length; index++)
        {
            var member = members[index];
            var keyword = index == 0 ? "if" : "else if";
            code.AppendLineAt(
                4,
                keyword
                    + " (MatchesPocoProperty(ref reader, options, "
                    + Format(member.Property.JsonPropertyName ?? member.Property.Name)
                    + ", "
                    + Format(member.Property.Name)
                    + ", "
                    + (member.Property.HasExplicitJsonPropertyName ? "true" : "false")
                    + ")) { if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed leaf value.\"); "
                    + LocalName(suffix, index)
                    + " = "
                    + ReadExpression(suffix, index, member, suffixByName)
                    + ";"
                    + (
                        member.Property.IsRequired
                            ? " " + HasName(suffix, index) + " = true;"
                            : string.Empty
                    )
                    + " }"
            );
        }

        if (members.Length > 0)
            code.AppendLineAt(
                4,
                "else { if (options.UnmappedMemberHandling == global::System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow) throw new global::System.Text.Json.JsonException(\"Unknown leaf property.\"); if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed leaf value.\"); reader.Skip(); }"
            );
        else
            code.AppendLineAt(
                4,
                "if (options.UnmappedMemberHandling == global::System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow) throw new global::System.Text.Json.JsonException(\"Unknown leaf property.\"); if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed leaf value.\"); reader.Skip();"
            );
        code.AppendLineAt(3, "}");
        foreach (
            var required in members
                .Select((member, index) => (member, index))
                .Where(static item => item.member.Property.IsRequired)
        )
        {
            code.CancellationToken.ThrowIfCancellationRequested();
            code.AppendLineAt(
                3,
                "if (!"
                    + HasName(suffix, required.index)
                    + ") throw new global::System.Text.Json.JsonException("
                    + Format(
                        "A required leaf property "
                            + (
                                required.member.Property.JsonPropertyName
                                ?? required.member.Property.Name
                            )
                            + " is missing."
                    )
                    + ");"
            );
        }
        code.AppendLineAt(3, "return " + ConstructionFor(poco, members, suffix) + ";");
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    private static void AppendWriter(
        SharedIndentedBuilder code,
        PayloadJsonEmissionContext context,
        SparsePayloadJsonPocoModel poco,
        Dictionary<string, string> suffixByName
    )
    {
        var suffix = CodecSuffix(poco, code);
        code.AppendLineAt(
            2,
            "private static void WritePayloadPoco_"
                + suffix
                + "(global::System.Text.Json.Utf8JsonWriter writer, "
                + poco.RuntimeName
                + "? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (value is null) { writer.WriteNullValue(); return; }");
        var target = "sparse_poco_target_" + suffix;
        code.AppendLineAt(
            3,
            poco.RuntimeName
                + " "
                + target
                + " = "
                + (poco.IsValueType ? "value.Value;" : "value!;")
        );
        if (poco.HasPublicInstanceFields)
            code.AppendLineAt(
                3,
                "if (options.IncludeFields) throw new global::System.Text.Json.JsonException("
                    + Format(
                        "Leaf type '"
                            + poco.RuntimeName
                            + "' has public fields and requires external JSON metadata when IncludeFields is enabled. Combine "
                            + context.ResolverReference
                            + " with a source-generated context for this type."
                    )
                    + ");"
            );
        code.AppendLineAt(3, "writer.WriteStartObject();");
        var members = poco.Members.Where(static member => !member.Property.IsJsonIgnored).ToArray();
        for (var index = 0; index < members.Length; index++)
        {
            var member = members[index];
            var access = target + "." + SparseNaming.EscapeIdentifier(member.Property.Name);
            code.AppendLineAt(
                3,
                "if (!ShouldOmitPocoValue<"
                    + member.Property.Type.Name
                    + ">("
                    + access
                    + ", options, "
                    + member.Property.JsonIgnoreCondition
                    + ")) { writer.WritePropertyName("
                    + PropertyNameExpression(member)
                    + "); "
                    + WriteExpression(suffix, index, member, access, suffixByName)
                    + "; }"
            );
        }

        for (var index = 0; index < poco.ReadOnly.Length; index++)
        {
            var property = poco.ReadOnly[index];
            if (property.IsJsonIgnored)
                continue;
            var local = "sparse_poco_ro_" + suffix + "_" + index;
            var access = target + "." + SparseNaming.EscapeIdentifier(property.Name);
            code.AppendLineAt(3, "if (!options.IgnoreReadOnlyProperties)");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, property.Type.Name + " " + local + " = " + access + ";");
            code.AppendLineAt(
                4,
                "if (!ShouldOmitPocoValue<"
                    + property.Type.Name
                    + ">("
                    + local
                    + ", options, "
                    + property.JsonIgnoreCondition
                    + ")) { writer.WritePropertyName("
                    + PropertyNameExpression(property)
                    + "); "
                    + ScalarWrite(property.Type.NonNullableName, local)
                    + "; }"
            );
            code.AppendLineAt(3, "}");
        }

        code.AppendLineAt(3, "writer.WriteEndObject();");
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    private static string PropertyNameExpression(SparseMemberModel member) =>
        PropertyNameExpression(member.Property);

    private static string PropertyNameExpression(SparsePropertyModel property)
    {
        var wire = Format(property.JsonPropertyName ?? property.Name);
        if (property.HasExplicitJsonPropertyName)
            return wire;
        return "(options.PropertyNamingPolicy is null ? "
            + wire
            + " : options.PropertyNamingPolicy.ConvertName("
            + Format(property.Name)
            + "))";
    }

    private static void AppendCollectionCodecs(
        SharedIndentedBuilder code,
        PayloadJsonEmissionContext context
    )
    {
        for (var pocoIndex = 0; pocoIndex < context.Pocos.Length; pocoIndex++)
        {
            var poco = context.Pocos[pocoIndex];
            var suffix = CodecSuffix(poco, code);
            var members = poco
                .Members.Where(static member => !member.Property.IsJsonIgnored)
                .ToArray();
            for (var index = 0; index < members.Length; index++)
            {
                var member = members[index];
                if (IsArrayMember(member))
                {
                    AppendArrayCodec(
                        code,
                        member,
                        "ReadPocoArray_" + suffix + "_" + index,
                        "WritePocoArray_" + suffix + "_" + index
                    );
                    continue;
                }

                if (
                    SparseJsonCollectionShapes.TryResolve(
                        member,
                        member.Property.Type.Name,
                        out var collection
                    ) && collection is not null
                )
                    SparseJsonCollectionShapes.AppendCollectionCodec(
                        code,
                        member.Property.Type.Name,
                        collection,
                        "ReadPocoCollection_" + suffix + "_" + index,
                        "WritePocoCollection_" + suffix + "_" + index,
                        "GetValueInfo"
                    );
            }
        }
    }

    private static bool IsArrayMember(SparseMemberModel member) =>
        member.Property.Type.Name.EndsWith("[]", System.StringComparison.Ordinal)
        && member.Collection.CloneKind == SparseCloneCollectionKind.Array
        && member.Property.Type.NonNullableName != "global::System.Byte[]";

    private static void AppendArrayCodec(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string readName,
        string writeName
    )
    {
        var declared = member.Property.Type.Name;
        var element = member.Collection.ElementType;
        var elementName = element.NonNullableName;
        var elementNullable = SparseJsonPrimitiveNames.IsNullableValue(element, element.Name);
        code.AppendLineAt(
            2,
            "private static "
                + declared
                + " "
                + readName
                + "(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return default!;"
        );
        code.AppendLineAt(
            3,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"A leaf collection must be an array.\");"
        );
        code.AppendLineAt(
            3,
            "var list = new global::System.Collections.Generic.List<" + elementName + ">();"
        );
        code.AppendLineAt(3, "while (reader.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;"
        );
        code.AppendLineAt(
            4,
            "list.Add(" + ScalarRead(element, elementName, elementNullable) + ");"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return list.ToArray();");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static void "
                + writeName
                + "(global::System.Text.Json.Utf8JsonWriter writer, "
                + declared
                + (declared.EndsWith("?", System.StringComparison.Ordinal) ? "" : "?")
                + " value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLineAt(3, "writer.WriteStartArray();");
        code.AppendLineAt(3, "foreach (var element in value)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, ScalarWrite(elementName, "element") + ";");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "writer.WriteEndArray();");
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    private static string ScalarRead(SparseTypeModel element, string elementName, bool nullable)
    {
        var direct = "ReadPocoScalar<" + elementName + ">(ref reader, options)";
        if (nullable)
            return "(reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? default! : "
                + direct
                + ")";
        return element.IsReferenceType
            ? direct
            : "(reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? throw new global::System.Text.Json.JsonException(\"Malformed leaf value.\") : "
                + direct
                + ")";
    }

    private static string ScalarWrite(string elementName, string source) =>
        "WritePocoScalar<" + elementName + ">(writer, " + source + ", options)";

    private static string InitializerFor(SparsePayloadJsonPocoModel poco, SparseMemberModel member)
    {
        if (
            poco.Constructor?.Parameters.FirstOrDefault(parameter =>
                parameter.PropertyName == member.Property.Name
            )
                is { } bound
            && bound.HasExplicitDefaultValue
        )
            return bound.DefaultExpression;
        return "default!";
    }

    private static string ReadExpression(
        string suffix,
        int index,
        SparseMemberModel member,
        Dictionary<string, string> suffixByName
    )
    {
        var declared = member.Property.Type.Name;
        var nonNull = member.Property.Type.NonNullableName;
        if (suffixByName.TryGetValue(nonNull, out var nestedSuffix) && !IsCollectionMember(member))
        {
            var call = "ReadPayloadPoco_" + nestedSuffix + "(ref reader, options)";
            if (IsNullableMember(member))
                return "(reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? default! : "
                    + call
                    + ")";
            return member.Property.Type.IsReferenceType
                ? call
                : "(reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? throw new global::System.Text.Json.JsonException(\"Malformed leaf value.\") : "
                    + call
                    + ")";
        }

        if (IsArrayMember(member))
            return "ReadPocoArray_" + suffix + "_" + index + "(ref reader, options)";
        if (
            SparseJsonCollectionShapes.TryResolve(member, declared, out var collection)
            && collection is not null
        )
            return "ReadPocoCollection_" + suffix + "_" + index + "(ref reader, options)";
        return ScalarRead(member.Property.Type, nonNull, IsNullableMember(member));
    }

    private static string WriteExpression(
        string suffix,
        int index,
        SparseMemberModel member,
        string access,
        Dictionary<string, string> suffixByName
    )
    {
        var declared = member.Property.Type.Name;
        var nonNull = member.Property.Type.NonNullableName;
        if (suffixByName.TryGetValue(nonNull, out var writeSuffix) && !IsCollectionMember(member))
        {
            return "WritePayloadPoco_" + writeSuffix + "(writer, " + access + ", options)";
        }

        if (IsArrayMember(member))
            return "WritePocoArray_" + suffix + "_" + index + "(writer, " + access + ", options)";
        if (
            SparseJsonCollectionShapes.TryResolve(member, declared, out var collection)
            && collection is not null
        )
            return "WritePocoCollection_"
                + suffix
                + "_"
                + index
                + "(writer, "
                + access
                + ", options)";
        return ScalarWrite(nonNull, access);
    }

    private static bool IsCollectionMember(SparseMemberModel member) =>
        IsArrayMember(member)
        || (
            SparseJsonCollectionShapes.TryResolve(
                member,
                member.Property.Type.Name,
                out var collection
            ) && collection is not null
        );

    private static bool IsNullableMember(SparseMemberModel member) =>
        member.Property.Type.Name.EndsWith("?", System.StringComparison.Ordinal)
        || member.Property.Type.IsReferenceType;

    private static string LocalName(string suffix, int index) =>
        "sparse_poco_" + suffix + "_" + index;

    private static string HasName(string suffix, int index) =>
        "sparse_poco_" + suffix + "_has_" + index;

    private static string ConstructionFor(
        SparsePayloadJsonPocoModel poco,
        SparseMemberModel[] members,
        string suffix
    )
    {
        var parameters =
            poco.Constructor?.Parameters ?? default(ImmutableArray<ConstructorParameterBinding>);
        var bound = new HashSet<string>(
            parameters.Select(static parameter => parameter.PropertyName),
            StringComparer.Ordinal
        );
        var arguments = string.Join(
            ", ",
            parameters.Select(parameter => ArgumentFor(members, suffix, parameter.PropertyName))
        );
        var assignments = members
            .Select((member, index) => (member, index))
            .Where(item => !bound.Contains(item.member.Property.Name))
            .Select(item =>
                SparseNaming.EscapeIdentifier(item.member.Property.Name)
                + " = "
                + LocalName(suffix, item.index)
            );
        var initializer = string.Join(", ", assignments);
        var result = "new " + poco.RuntimeName + "(" + arguments + ")";
        return initializer.Length == 0 ? result : result + " { " + initializer + " }";
    }

    private static string ArgumentFor(
        SparseMemberModel[] members,
        string suffix,
        string propertyName
    )
    {
        var index = Array.FindIndex(members, candidate => candidate.Property.Name == propertyName);
        return index < 0 ? "default!" : LocalName(suffix, index);
    }

    private static string Format(string value) => SymbolDisplay.FormatLiteral(value, true);
}
