using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseTypes.Generator.Shared;

/// <summary>Shared fragment JSON converter emission.</summary>
internal static class SparseFragmentJsonEmitter
{
    private static string FragmentValueType(SparseMemberModel member) =>
        member.ChildModel is null ? member.Property.Type.Name : member.ChildFragmentType + "?";

    /// <summary>Emits the standalone fragment JSON converter and JsonConverter accessor.</summary>
    public static void AppendStandaloneFragmentJson(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string optional,
        bool emitFastPaths = false
    )
    {
        code.CancellationToken.ThrowIfCancellationRequested();
        code.AppendLineAt(
            2,
            "/// <summary>Gets the JSON converter for this fragment type.</summary>"
        );
        code.AppendLineAt(
            2,
            "public static global::System.Text.Json.Serialization.JsonConverter<Fragment> JsonConverter { get; } = new FragmentJsonConverter();"
        );
        code.AppendLine();
        AppendConverter(code, members, optional, isStandalone: true, emitFastPaths: emitFastPaths);
        code.AppendLine();
    }

    /// <summary>Emits the thin model-facing converter shell delegating to the implementation converter.</summary>
    /// <remarks>The heavyweight read/write bodies live in the per-model implementation source.</remarks>
    /// <param name="code">Surface target builder.</param>
    /// <param name="jsonConverterQualifiedName">Qualified implementation converter type.</param>
    public static void AppendStandaloneFragmentJsonFacade(
        SharedIndentedBuilder code,
        string jsonConverterQualifiedName
    )
    {
        AppendStandaloneFragmentJsonAccessor(code, jsonConverterQualifiedName);
    }

    /// <summary>Emits the public JSON converter accessor preceding internal helpers.</summary>
    /// <param name="code">Surface target builder.</param>
    /// <param name="jsonConverterQualifiedName">Qualified implementation converter type.</param>
    public static void AppendStandaloneFragmentJsonAccessor(
        SharedIndentedBuilder code,
        string jsonConverterQualifiedName
    )
    {
        code.CancellationToken.ThrowIfCancellationRequested();
        code.AppendLineAt(
            2,
            "/// <summary>Gets the JSON converter for this fragment type.</summary>"
        );
        code.AppendLineAt(
            2,
            "public static global::System.Text.Json.Serialization.JsonConverter<Fragment> JsonConverter { get; } = new "
                + jsonConverterQualifiedName
                + "();"
        );
        code.AppendLine();
    }

    /// <summary>Emits the fragment JSON converter used for standard System.Text.Json serialization.</summary>
    public static void AppendConverter(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string optional,
        bool isStandalone,
        string converterClassName = "FragmentJsonConverter",
        string converterAccessibility = "private",
        bool sealedConverter = true,
        bool emitFastPaths = false
    )
    {
        code.CancellationToken.ThrowIfCancellationRequested();
        code.AppendLineAt(
            2,
            "/// <summary>Reads and writes sparse fragment properties without materializing absent values.</summary>"
        );
        code.AppendLineAt(
            2,
            converterAccessibility
                + (sealedConverter ? " sealed class " : " class ")
                + converterClassName
                + " : global::System.Text.Json.Serialization.JsonConverter<Fragment>"
        );
        code.AppendLineAt(2, "{");
        // Wider converters use a per-options name index for case-insensitive reads.
        var indexedDispatch = members.Length > 3;
        var jsonMemberIndexes = ImmutableArray.CreateBuilder<int>(members.Length);
        var ignoredMemberIndexes = ImmutableArray.CreateBuilder<int>();
        for (var memberIndex = 0; memberIndex < members.Length; memberIndex++)
        {
            if (members[memberIndex].Property.IsJsonIgnored)
                ignoredMemberIndexes.Add(memberIndex);
            else
                jsonMemberIndexes.Add(memberIndex);
        }
        // Public readers first; private buffers and helpers trail the writers
        // below.
        code.AppendLineAt(
            3,
            "/// <summary>Reads a fragment from its JSON object representation.</summary>"
        );
        code.AppendLineAt(
            3,
            "/// <param name=\"reader\">The reader positioned at the fragment object.</param>"
        );
        code.AppendLineAt(3, "/// <param name=\"typeToConvert\">The type to convert.</param>");
        code.AppendLineAt(3, "/// <param name=\"options\">The serializer options.</param>");
        code.AppendLineAt(3, "/// <returns>The deserialized fragment.</returns>");
        code.AppendLineAt(
            3,
            "public override Fragment Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var jsonPropertyCache = GetJsonPropertyCache(options, true);");
        if (indexedDispatch)
            code.AppendLineAt(4, "var propertyIndexes = jsonPropertyCache.Indexes!;");
        code.AppendLineAt(
            4,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) { throw new global::System.Text.Json.JsonException(\"A fragment must be a JSON object.\"); }"
        );
        code.AppendLineAt(4, "var builder = new FragmentBuilder();");
        code.AppendLineAt(4, "while (reader.Read())");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) { return builder.Build(); }"
        );
        code.AppendLineAt(
            5,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) { throw new global::System.Text.Json.JsonException(\"Expected a fragment property name.\"); }"
        );
        code.AppendLineAt(5, "var propertyIndex = -1;");
        code.AppendLineAt(5, "string? propertyName;");
        code.AppendLineAt(5, "if (options.PropertyNameCaseInsensitive)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "propertyName = reader.GetString();");
        if (indexedDispatch)
        {
            code.AppendLineAt(
                6,
                "if (propertyIndexes is not null && propertyIndexes.TryGetValue(propertyName!, out var matchedPropertyIndex)) { propertyIndex = matchedPropertyIndex; }"
            );
            code.AppendLineAt(6, "else");
            code.AppendLineAt(6, "{");
            foreach (var index in jsonMemberIndexes.Concat(ignoredMemberIndexes))
                code.AppendLineAt(
                    7,
                    "if (propertyIndex < 0 && Matches(propertyName, jsonPropertyCache.PropertyNames["
                        + index.ToString()
                        + "], options)) { propertyIndex = "
                        + index.ToString()
                        + "; }"
                );
            code.AppendLineAt(6, "}");
        }
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        code.AppendIndent(6).Append("propertyIndex = ");
        foreach (var index in jsonMemberIndexes.Concat(ignoredMemberIndexes))
        {
            code.Append("reader.ValueTextEquals(jsonPropertyCache.Utf8Names[")
                .Append(index.ToString())
                .Append("]) ? ")
                .Append(index.ToString())
                .Append(" : ");
        }
        code.AppendLine("-1;");
        code.AppendLineAt(
            6,
            "propertyName = propertyIndex < 0 && options.UnmappedMemberHandling == global::System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow ? reader.GetString() : null;"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "if (!reader.Read()) { throw new global::System.Text.Json.JsonException(\"Unexpected end of fragment.\"); }"
        );
        if (jsonMemberIndexes.Count + ignoredMemberIndexes.Count > 0)
        {
            var first = true;
            if (indexedDispatch)
            {
                code.AppendLineAt(5, "if (propertyIndex >= 0)");
                code.AppendLineAt(5, "{");
                code.AppendLineAt(6, "switch (propertyIndex)");
                code.AppendLineAt(6, "{");
            }
            foreach (var index in jsonMemberIndexes)
            {
                var member = members[index];
                var property = SparseNaming.EscapeIdentifier(member.Property.Name);
                if (indexedDispatch)
                    code.AppendIndent(7).Append("case ").Append(index.ToString()).AppendLine(":");
                else
                {
                    code.AppendIndent(5).Append(first ? "if (" : "else if (");
                    code.Append("propertyIndex == ")
                        .Append(index.ToString())
                        .Append(
                            " || (options.PropertyNameCaseInsensitive && Matches(propertyName, jsonPropertyCache.PropertyNames["
                        )
                        .Append(index.ToString())
                        .AppendLine("], options)))");
                }
                if (member.ChildModel is null)
                {
                    var memberValueType = FragmentValueType(member);
                    var readExpression =
                        "global::System.Text.Json.JsonSerializer.Deserialize(ref reader, GetMemberTypeInfo<"
                        + memberValueType
                        + ">(options))";
                    if (emitFastPaths)
                    {
                        SparseJsonCollectionModel? memberCollection = null;
                        if (
                            SparseJsonCollectionShapes.TryResolve(
                                member,
                                memberValueType,
                                out memberCollection
                            ) && memberCollection is not null
                        )
                            readExpression = "ReadCollection" + member.Id + "(ref reader, options)";
                        else
                            readExpression =
                                "ReadMemberValue<" + memberValueType + ">(ref reader, options)";
                    }

                    code.AppendIndent(indexedDispatch ? 8 : 6)
                        .Append("builder.")
                        .Append(property)
                        .Append(" = " + optional + "<")
                        .Append(memberValueType)
                        .AppendLine(">.Present(" + readExpression + ");");
                }
                else
                {
                    var childFragment = member.ChildFragmentType!;
                    code.AppendIndent(indexedDispatch ? 8 : 6)
                        .Append("builder.")
                        .Append(property)
                        .Append(" = " + optional + "<")
                        .Append(FragmentValueType(member))
                        .Append(
                            ">.Present(reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? null : "
                        )
                        .Append(childFragment)
                        .AppendLine(
                            ".JsonConverter.Read(ref reader, typeof("
                                + childFragment
                                + "), options));"
                        );
                }
                if (indexedDispatch)
                    code.AppendLineAt(8, "break;");
                first = false;
            }

            foreach (var index in ignoredMemberIndexes)
            {
                if (indexedDispatch)
                    code.AppendIndent(7).Append("case ").Append(index.ToString()).AppendLine(":");
                else
                {
                    code.AppendIndent(5).Append(first ? "if (" : "else if (");
                    code.Append("propertyIndex == ")
                        .Append(index.ToString())
                        .Append(
                            " || (options.PropertyNameCaseInsensitive && Matches(propertyName, jsonPropertyCache.PropertyNames["
                        )
                        .Append(index.ToString())
                        .AppendLine("], options)))");
                }
                code.AppendLineAt(indexedDispatch ? 8 : 6, "{ reader.Skip(); }");
                if (indexedDispatch)
                    code.AppendLineAt(8, "break;");
                first = false;
            }

            if (indexedDispatch)
            {
                code.AppendLineAt(
                    7,
                    "default: HandleUnknownFragmentProperty(ref reader, options, propertyName); break;"
                );
                code.AppendLineAt(6, "}");
                code.AppendLineAt(5, "}");
                code.AppendLineAt(5, "else");
                code.AppendLineAt(5, "{");
                code.AppendLineAt(
                    6,
                    "HandleUnknownFragmentProperty(ref reader, options, propertyName);"
                );
                code.AppendLineAt(5, "}");
            }
            else
                code.AppendLineAt(
                    5,
                    "else HandleUnknownFragmentProperty(ref reader, options, propertyName);"
                );
        }
        else
        {
            code.AppendLineAt(
                5,
                "HandleUnknownFragmentProperty(ref reader, options, propertyName);"
            );
        }

        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "throw new global::System.Text.Json.JsonException(\"Unexpected end of fragment.\");"
        );
        code.AppendLineAt(3, "}");
        code.AppendLine();
        code.AppendLineAt(
            3,
            "/// <summary>Writes the present members of a fragment as a JSON object.</summary>"
        );
        code.AppendLineAt(
            3,
            "/// <param name=\"writer\">The writer receiving the fragment object.</param>"
        );
        code.AppendLineAt(3, "/// <param name=\"value\">The fragment to write.</param>");
        code.AppendLineAt(3, "/// <param name=\"options\">The serializer options.</param>");
        code.AppendLineAt(
            3,
            "public override void Write(global::System.Text.Json.Utf8JsonWriter writer, Fragment value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var jsonPropertyCache = GetJsonPropertyCache(options, false);");
        code.AppendLineAt(4, "writer.WriteStartObject();");
        foreach (var index in jsonMemberIndexes)
        {
            var member = members[index];
            var property = SparseNaming.EscapeIdentifier(member.Property.Name);
            code.AppendIndent(4).Append("if (value.").Append(property).AppendLine(".IsPresent)");
            code.AppendLineAt(4, "{");
            var conditionalIndent = 5;
            if (member.Property.IsJsonIgnoreWhenWritingNull)
            {
                code.AppendIndent(5)
                    .Append("if (value.")
                    .Append(property)
                    .AppendLine(".Value is not null)");
                code.AppendLineAt(5, "{");
                conditionalIndent = 6;
            }
            else if (member.Property.IsJsonIgnoreWhenWritingDefault)
            {
                code.AppendIndent(5)
                    .Append(
                        "if (!global::System.Collections.Generic.EqualityComparer<"
                            + FragmentValueType(member)
                            + ">.Default.Equals(value."
                    )
                    .Append(property)
                    .AppendLine(".Value!, default))");
                code.AppendLineAt(5, "{");
                conditionalIndent = 6;
            }
            code.AppendIndent(conditionalIndent)
                .Append("writer.WritePropertyName(jsonPropertyCache.PropertyNames[")
                .Append(index.ToString())
                .AppendLine("]);");

            if (member.ChildModel is null)
            {
                var writeValueType = FragmentValueType(member);
                SparseJsonCollectionModel? writeCollection = null;
                var isWriteCollection =
                    emitFastPaths
                    && SparseJsonCollectionShapes.TryResolve(
                        member,
                        writeValueType,
                        out writeCollection
                    )
                    && writeCollection is not null;
                if (isWriteCollection)
                {
                    code.AppendIndent(conditionalIndent)
                        .Append("WriteCollection")
                        .Append(member.Id.ToString())
                        .Append("(writer, value.")
                        .Append(property)
                        .AppendLine(".Value!, options);");
                }
                else if (emitFastPaths)
                {
                    code.AppendIndent(conditionalIndent)
                        .Append("WriteMemberValue<")
                        .Append(writeValueType)
                        .Append(">(writer, value.")
                        .Append(property)
                        .AppendLine(".Value!, options);");
                }
                else
                {
                    code.AppendIndent(conditionalIndent)
                        .Append("global::System.Text.Json.JsonSerializer.Serialize<")
                        .Append(writeValueType)
                        .Append(">(writer, value.")
                        .Append(property)
                        .Append(".Value!, GetMemberTypeInfo<")
                        .Append(writeValueType)
                        .AppendLine(">(options));");
                }
            }
            else
            {
                var childFragment = member.ChildFragmentType!;
                code.AppendIndent(conditionalIndent)
                    .Append("if (value.")
                    .Append(property)
                    .AppendLine(".Value is null)");
                code.AppendLineAt(conditionalIndent, "{ writer.WriteNullValue(); }");
                code.AppendIndent(conditionalIndent).AppendLine("else");
                code.AppendLineAt(conditionalIndent, "{");
                code.AppendIndent(conditionalIndent + 1)
                    .Append(childFragment)
                    .Append(".JsonConverter.Write(writer, value.")
                    .Append(property)
                    .AppendLine(".Value, options);");
                code.AppendLineAt(conditionalIndent, "}");
            }
            if (conditionalIndent > 5)
            {
                code.AppendLineAt(5, "}");
            }
            code.AppendLineAt(4, "}");
        }

        code.AppendLineAt(4, "writer.WriteEndObject();");
        code.AppendLineAt(3, "}");
        code.AppendLine();
        if (emitFastPaths)
            SparseFragmentJsonFastPathEmitter.AppendFastPaths(code, members);
        code.AppendLineAt(
            3,
            "private static global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TMember> GetMemberTypeInfo<TMember>(global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var cache = GetJsonOptionsCache(options);");
        code.AppendLineAt(4, "lock (cache.Gate)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (cache.TypeInfos.TryGetValue(typeof(TMember), out var cached)) { return (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TMember>)cached; }"
        );
        code.AppendLineAt(5, "var resolved = ResolveMemberTypeInfo<TMember>(options);");
        code.AppendLineAt(5, "cache.TypeInfos.Add(typeof(TMember), resolved);");
        code.AppendLineAt(5, "return resolved;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "private static global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TMember> ResolveMemberTypeInfo<TMember>(global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "try");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "return (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TMember>)options.GetTypeInfo(typeof(TMember));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "catch (global::System.NotSupportedException exception)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "throw new global::System.InvalidOperationException(\"The generated fragment converter requires JsonTypeInfo metadata for member type '\" + typeof(TMember) + \"'. Add the model/member types to a source-generated JsonSerializerContext and set it as JsonSerializerOptions.TypeInfoResolver.\", exception);"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLine();
        code.AppendLineAt(
            3,
            "private static bool Matches(string? actual, string expected, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (actual is null) { return false; }");
        code.AppendLineAt(
            4,
            "return global::System.String.Equals(actual, expected, options.PropertyNameCaseInsensitive ? global::System.StringComparison.OrdinalIgnoreCase : global::System.StringComparison.Ordinal);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLine();
        code.AppendLineAt(
            3,
            "private static void HandleUnknownFragmentProperty(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options, string? propertyName)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (options.UnmappedMemberHandling == global::System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow) { throw new global::System.Text.Json.JsonException(\"The JSON property '\" + propertyName + \"' could not be mapped to fragment '\" + typeof(Fragment) + \"'.\"); }"
        );
        code.AppendLineAt(4, "reader.Skip();");
        code.AppendLineAt(3, "}");
        AppendJsonOptionsCache(
            code,
            members,
            jsonMemberIndexes,
            ignoredMemberIndexes,
            indexedDispatch
        );
        code.AppendLineAt(2, "}");
    }

    private static void AppendJsonOptionsCache(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<int>.Builder jsonMemberIndexes,
        ImmutableArray<int>.Builder ignoredMemberIndexes,
        bool indexedDispatch
    )
    {
        code.AppendLineAt(
            3,
            "private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<global::System.Text.Json.JsonSerializerOptions, JsonOptionsCache> __jsonOptionsCaches = new global::System.Runtime.CompilerServices.ConditionalWeakTable<global::System.Text.Json.JsonSerializerOptions, JsonOptionsCache>();"
        );
        code.AppendLineAt(3, "private sealed class JsonOptionsCache");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "public readonly object Gate = new object();");
        code.AppendLineAt(4, "public JsonPropertyIndexCache? Properties;");
        code.AppendLineAt(
            4,
            "public readonly global::System.Collections.Generic.Dictionary<global::System.Type, global::System.Text.Json.Serialization.Metadata.JsonTypeInfo> TypeInfos = new global::System.Collections.Generic.Dictionary<global::System.Type, global::System.Text.Json.Serialization.Metadata.JsonTypeInfo>();"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "private sealed class JsonPropertyIndexCache");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "public JsonPropertyIndexCache(string[] propertyNames, byte[][] utf8Names, global::System.Collections.Generic.Dictionary<string, int>? indexes, global::System.Text.Json.JsonNamingPolicy? namingPolicy, bool caseInsensitive, bool namesValidated)"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "PropertyNames = propertyNames;");
        code.AppendLineAt(5, "Utf8Names = utf8Names;");
        code.AppendLineAt(5, "Indexes = indexes;");
        code.AppendLineAt(5, "NamingPolicy = namingPolicy;");
        code.AppendLineAt(5, "CaseInsensitive = caseInsensitive;");
        code.AppendLineAt(5, "NamesValidated = namesValidated;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "public readonly string[] PropertyNames;");
        code.AppendLineAt(4, "public readonly byte[][] Utf8Names;");
        code.AppendLineAt(
            4,
            "public readonly global::System.Collections.Generic.Dictionary<string, int>? Indexes;"
        );
        code.AppendLineAt(
            4,
            "public readonly global::System.Text.Json.JsonNamingPolicy? NamingPolicy;"
        );
        code.AppendLineAt(4, "public readonly bool CaseInsensitive;");
        code.AppendLineAt(4, "public readonly bool NamesValidated;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "private static JsonOptionsCache GetJsonOptionsCache(global::System.Text.Json.JsonSerializerOptions options) => __jsonOptionsCaches.GetValue(options, _ => new JsonOptionsCache());"
        );
        code.AppendLineAt(
            3,
            "private static JsonPropertyIndexCache GetJsonPropertyCache(global::System.Text.Json.JsonSerializerOptions options, bool validateNames)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var namingPolicy = options.PropertyNamingPolicy;");
        code.AppendLineAt(4, "var caseInsensitive = options.PropertyNameCaseInsensitive;");
        code.AppendLineAt(
            4,
            "var buildIndexes = validateNames && "
                + (indexedDispatch ? "caseInsensitive;" : "false;")
        );
        // Each options instance owns the first evaluated names, including results from custom policies.
        code.AppendLineAt(4, "var cache = GetJsonOptionsCache(options);");
        code.AppendLineAt(4, "lock (cache.Gate)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "var current = cache.Properties;");
        code.AppendLineAt(
            5,
            "if (current is not null && global::System.Object.ReferenceEquals(current.NamingPolicy, namingPolicy) && current.CaseInsensitive == caseInsensitive && (!validateNames || current.NamesValidated) && (!buildIndexes || current.Indexes is not null)) { return current; }"
        );
        code.AppendLineAt(
            5,
            "var built = BuildJsonPropertyCache(options, validateNames, buildIndexes);"
        );
        code.AppendLineAt(5, "cache.Properties = built;");
        code.AppendLineAt(5, "return built;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "private static JsonPropertyIndexCache BuildJsonPropertyCache(global::System.Text.Json.JsonSerializerOptions options, bool validateNames, bool buildIndexes)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "var comparer = options.PropertyNameCaseInsensitive ? global::System.StringComparer.OrdinalIgnoreCase : global::System.StringComparer.Ordinal;"
        );
        code.AppendLineAt(
            4,
            "var visibleNames = validateNames ? new global::System.Collections.Generic.HashSet<string>(comparer) : null;"
        );
        code.AppendLineAt(
            4,
            "global::System.Collections.Generic.Dictionary<string, int>? indexes = buildIndexes ? new global::System.Collections.Generic.Dictionary<string, int>(comparer) : null;"
        );
        code.AppendLineAt(4, "var propertyNames = new string[" + members.Length.ToString() + "];");
        code.AppendLineAt(4, "var utf8Names = new byte[" + members.Length.ToString() + "][];");
        foreach (var index in jsonMemberIndexes)
        {
            var property = members[index].Property;
            var literal = SymbolDisplay.FormatLiteral(
                property.JsonPropertyName ?? property.Name,
                true
            );
            var expression = property.HasExplicitJsonPropertyName
                ? literal
                : "options.PropertyNamingPolicy?.ConvertName(" + literal + ") ?? " + literal;
            code.AppendLineAt(4, "var __jsonName_" + index + " = " + expression + ";");
            code.AppendLineAt(4, "propertyNames[" + index + "] = __jsonName_" + index + ";");
            code.AppendLineAt(
                4,
                "utf8Names["
                    + index
                    + "] = global::System.Text.Encoding.UTF8.GetBytes(__jsonName_"
                    + index
                    + ");"
            );
            code.AppendLineAt(
                4,
                "if (visibleNames is not null && !visibleNames.Add(__jsonName_"
                    + index
                    + ")) { throw new global::System.Text.Json.JsonException(\"Multiple fragment members map to the same JSON property name.\"); }"
            );
            if (indexedDispatch)
                code.AppendLineAt(
                    4,
                    "if (indexes is not null) { indexes.Add(__jsonName_"
                        + index
                        + ", "
                        + index
                        + "); }"
                );
        }
        foreach (var index in ignoredMemberIndexes)
        {
            var property = members[index].Property;
            var literal = SymbolDisplay.FormatLiteral(
                property.JsonPropertyName ?? property.Name,
                true
            );
            var expression = property.HasExplicitJsonPropertyName
                ? literal
                : "options.PropertyNamingPolicy?.ConvertName(" + literal + ") ?? " + literal;
            code.AppendLineAt(4, "var __jsonName_" + index + " = " + expression + ";");
            code.AppendLineAt(4, "propertyNames[" + index + "] = __jsonName_" + index + ";");
            code.AppendLineAt(
                4,
                "utf8Names["
                    + index
                    + "] = global::System.Text.Encoding.UTF8.GetBytes(__jsonName_"
                    + index
                    + ");"
            );
            if (indexedDispatch)
                code.AppendLineAt(
                    4,
                    "if (indexes is not null && !indexes.ContainsKey(__jsonName_"
                        + index
                        + ")) { indexes.Add(__jsonName_"
                        + index
                        + ", "
                        + index
                        + "); }"
                );
        }
        code.AppendLineAt(
            4,
            "return new JsonPropertyIndexCache(propertyNames, utf8Names, indexes, options.PropertyNamingPolicy, options.PropertyNameCaseInsensitive, validateNames);"
        );
        code.AppendLineAt(3, "}");
    }
}
