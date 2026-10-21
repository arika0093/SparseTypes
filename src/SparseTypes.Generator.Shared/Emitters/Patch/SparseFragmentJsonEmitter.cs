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
        code.AppendLine("/// <summary>Gets the JSON converter for this fragment type.</summary>");
        code.AppendLine(
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
    public static void AppendStandaloneFragmentJsonFacade(SharedIndentedBuilder code, string jsonConverterQualifiedName)
    {
        AppendStandaloneFragmentJsonAccessor(code, jsonConverterQualifiedName);
    }

    /// <summary>Emits the public JSON converter accessor preceding internal helpers.</summary>
    /// <param name="code">Surface target builder.</param>
    /// <param name="jsonConverterQualifiedName">Qualified implementation converter type.</param>
    public static void AppendStandaloneFragmentJsonAccessor(SharedIndentedBuilder code, string jsonConverterQualifiedName)
    {
        code.CancellationToken.ThrowIfCancellationRequested();
        code.AppendLine("/// <summary>Gets the JSON converter for this fragment type.</summary>");
        code.AppendLine(
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
        bool emitFastPaths = false,
        string? sharedCodecsReference = null
    )
    {
        code.CancellationToken.ThrowIfCancellationRequested();
        code.AppendLine("/// <summary>Reads and writes sparse fragment properties without materializing absent values.</summary>");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine(
            converterAccessibility
                + (sealedConverter ? " sealed class " : " class ")
                + converterClassName
                + " : global::System.Text.Json.Serialization.JsonConverter<Fragment>"
        );
        code.OpenBlock();
        // Wider converters use a per-options name index for case-insensitive reads.
        var indexedDispatch = members.Length > 3;
        var jsonMemberIndexes = ImmutableArray.CreateBuilder<int>(members.Length);
        var ignoredMemberIndexes = ImmutableArray.CreateBuilder<int>();
        for (var memberIndex = 0; memberIndex < members.Length; memberIndex++)
        {
            if (members[memberIndex].Property.IsJsonIgnored)
            {
                ignoredMemberIndexes.Add(memberIndex);
            }
            else
            {
                jsonMemberIndexes.Add(memberIndex);
            }
        }
        // Public readers first; private buffers and helpers trail the writers
        // below.
        code.AppendLine("/// <summary>Reads a fragment from its JSON object representation.</summary>");
        code.AppendLine("/// <param name=\"reader\">The reader positioned at the fragment object.</param>");
        code.AppendLine("/// <param name=\"typeToConvert\">The type to convert.</param>");
        code.AppendLine("/// <param name=\"options\">The serializer options.</param>");
        code.AppendLine("/// <returns>The deserialized fragment.</returns>");
        code.AppendLine(
            "public override Fragment Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("var jsonPropertyCache = GetJsonPropertyCache(options, true);");
        if (indexedDispatch)
        {
            code.AppendLine("var propertyIndexes = jsonPropertyCache.Indexes!;");
        }

        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) { throw new global::System.Text.Json.JsonException(\"A fragment must be a JSON object.\"); }"
        );
        code.AppendLine("var builder = new FragmentBuilder();");
        code.AppendLine("while (reader.Read())");
        code.OpenBlock();
        code.AppendLine("if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) { return builder.Build(); }");
        code.AppendLine(
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) { throw new global::System.Text.Json.JsonException(\"Expected a fragment property name.\"); }"
        );
        code.AppendLine("var propertyIndex = -1;");
        code.AppendLine("string? propertyName;");
        code.AppendLine("if (options.PropertyNameCaseInsensitive)");
        code.OpenBlock();
        code.AppendLine("propertyName = reader.GetString();");
        if (indexedDispatch)
        {
            code.AppendLine(
                "if (propertyIndexes is not null && propertyIndexes.TryGetValue(propertyName!, out var matchedPropertyIndex)) { propertyIndex = matchedPropertyIndex; }"
            );
            code.AppendLine("else");
            code.OpenBlock();
            foreach (var index in jsonMemberIndexes.Concat(ignoredMemberIndexes))
            {
                code.AppendLine(
                    "if (propertyIndex < 0 && Matches(propertyName, jsonPropertyCache.PropertyNames["
                        + index.ToString()
                        + "], options)) { propertyIndex = "
                        + index.ToString()
                        + "; }"
                );
            }

            code.CloseBlock();
        }
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendIndent().Append("propertyIndex = ");
        foreach (var index in jsonMemberIndexes.Concat(ignoredMemberIndexes))
        {
            code.Append("reader.ValueTextEquals(jsonPropertyCache.Utf8Names[")
                .Append(index.ToString())
                .Append("]) ? ")
                .Append(index.ToString())
                .Append(" : ");
        }
        code.AppendLine("-1;");
        code.AppendLine(
            "propertyName = propertyIndex < 0 && options.UnmappedMemberHandling == global::System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow ? reader.GetString() : null;"
        );
        code.CloseBlock();
        code.AppendLine("if (!reader.Read()) { throw new global::System.Text.Json.JsonException(\"Unexpected end of fragment.\"); }");
        if (jsonMemberIndexes.Count + ignoredMemberIndexes.Count > 0)
        {
            var first = true;
            if (indexedDispatch)
            {
                code.AppendLine("if (propertyIndex >= 0)");
                code.OpenBlock();
                code.AppendLine("switch (propertyIndex)");
                code.OpenBlock();
            }
            foreach (var index in jsonMemberIndexes)
            {
                var member = members[index];
                var property = SparseNaming.EscapeIdentifier(member.Property.Name);
                if (indexedDispatch)
                {
                    code.AppendIndent().Append("case ").Append(index.ToString()).AppendLine(":");
                }
                else
                {
                    code.AppendIndent().Append(first ? "if (" : "else if (");
                    code.Append("propertyIndex == ")
                        .Append(index.ToString())
                        .Append(" || (options.PropertyNameCaseInsensitive && Matches(propertyName, jsonPropertyCache.PropertyNames[")
                        .Append(index.ToString())
                        .AppendLine("], options)))");
                }
                if (member.ChildModel is null)
                {
                    var memberValueType = FragmentValueType(member);
                    var readExpression =
                        "global::System.Text.Json.JsonSerializer.Deserialize(ref reader, GetMemberTypeInfo<" + memberValueType + ">(options))";
                    if (emitFastPaths)
                    {
                        SparseJsonCollectionModel? memberCollection = null;
                        if (SparseJsonCollectionShapes.TryResolve(member, memberValueType, out memberCollection) && memberCollection is not null)
                        {
                            readExpression = "ReadCollection" + member.Id + "(ref reader, options)";
                        }
                        else
                        {
                            readExpression = "ReadMemberValue<" + memberValueType + ">(ref reader, options)";
                        }
                    }

                    code.AppendIndent()
                        .Append("builder.")
                        .Append(property)
                        .Append(" = " + optional + "<")
                        .Append(memberValueType)
                        .AppendLine(">.Present(" + readExpression + ");");
                }
                else
                {
                    var childFragment = member.ChildFragmentType!;
                    code.AppendIndent()
                        .Append("builder.")
                        .Append(property)
                        .Append(" = " + optional + "<")
                        .Append(FragmentValueType(member))
                        .Append(">.Present(reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? null : ")
                        .Append(childFragment)
                        .AppendLine(".JsonConverter.Read(ref reader, typeof(" + childFragment + "), options));");
                }
                if (indexedDispatch)
                {
                    code.AppendLine("break;");
                }

                first = false;
            }

            foreach (var index in ignoredMemberIndexes)
            {
                if (indexedDispatch)
                {
                    code.AppendIndent().Append("case ").Append(index.ToString()).AppendLine(":");
                }
                else
                {
                    code.AppendIndent().Append(first ? "if (" : "else if (");
                    code.Append("propertyIndex == ")
                        .Append(index.ToString())
                        .Append(" || (options.PropertyNameCaseInsensitive && Matches(propertyName, jsonPropertyCache.PropertyNames[")
                        .Append(index.ToString())
                        .AppendLine("], options)))");
                }
                code.AppendLine("{ reader.Skip(); }");
                if (indexedDispatch)
                {
                    code.AppendLine("break;");
                }

                first = false;
            }

            if (indexedDispatch)
            {
                code.AppendLine("default: HandleUnknownFragmentProperty(ref reader, options, propertyName); break;");
                code.CloseBlock();
                code.CloseBlock();
                code.AppendLine("else");
                code.OpenBlock();
                code.AppendLine("HandleUnknownFragmentProperty(ref reader, options, propertyName);");
                code.CloseBlock();
            }
            else
            {
                code.AppendLine("else HandleUnknownFragmentProperty(ref reader, options, propertyName);");
            }
        }
        else
        {
            code.AppendLine("HandleUnknownFragmentProperty(ref reader, options, propertyName);");
        }

        code.CloseBlock();
        code.AppendLine("throw new global::System.Text.Json.JsonException(\"Unexpected end of fragment.\");");
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine("/// <summary>Writes the present members of a fragment as a JSON object.</summary>");
        code.AppendLine("/// <param name=\"writer\">The writer receiving the fragment object.</param>");
        code.AppendLine("/// <param name=\"value\">The fragment to write.</param>");
        code.AppendLine("/// <param name=\"options\">The serializer options.</param>");
        code.AppendLine(
            "public override void Write(global::System.Text.Json.Utf8JsonWriter writer, Fragment value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("var jsonPropertyCache = GetJsonPropertyCache(options, false);");
        code.AppendLine("writer.WriteStartObject();");
        foreach (var index in jsonMemberIndexes)
        {
            var member = members[index];
            var property = SparseNaming.EscapeIdentifier(member.Property.Name);
            code.AppendIndent().Append("if (value.").Append(property).AppendLine(".IsPresent)");
            code.OpenBlock();
            var conditionalIndent = 5;
            if (member.Property.IsJsonIgnoreWhenWritingNull)
            {
                code.AppendIndent().Append("if (value.").Append(property).AppendLine(".Value is not null)");
                code.OpenBlock();
                conditionalIndent = 6;
            }
            else if (member.Property.IsJsonIgnoreWhenWritingDefault)
            {
                code.AppendIndent()
                    .Append("if (!global::System.Collections.Generic.EqualityComparer<" + FragmentValueType(member) + ">.Default.Equals(value.")
                    .Append(property)
                    .AppendLine(".Value!, default))");
                code.OpenBlock();
                conditionalIndent = 6;
            }
            code.AppendIndent().Append("writer.WritePropertyName(jsonPropertyCache.PropertyNames[").Append(index.ToString()).AppendLine("]);");

            if (member.ChildModel is null)
            {
                var writeValueType = FragmentValueType(member);
                SparseJsonCollectionModel? writeCollection = null;
                var isWriteCollection =
                    emitFastPaths
                    && SparseJsonCollectionShapes.TryResolve(member, writeValueType, out writeCollection)
                    && writeCollection is not null;
                if (isWriteCollection)
                {
                    code.AppendIndent()
                        .Append("WriteCollection")
                        .Append(member.Id.ToString())
                        .Append("(writer, value.")
                        .Append(property)
                        .AppendLine(".Value!, options);");
                }
                else if (emitFastPaths)
                {
                    code.AppendIndent()
                        .Append("WriteMemberValue<")
                        .Append(writeValueType)
                        .Append(">(writer, value.")
                        .Append(property)
                        .AppendLine(".Value!, options);");
                }
                else
                {
                    code.AppendIndent()
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
                code.AppendIndent().Append("if (value.").Append(property).AppendLine(".Value is null)");
                code.AppendLine("{ writer.WriteNullValue(); }");
                code.AppendIndent().AppendLine("else");
                code.OpenBlock();
                code.AppendIndent()
                    .Append(childFragment)
                    .Append(".JsonConverter.Write(writer, value.")
                    .Append(property)
                    .AppendLine(".Value, options);");
                code.CloseBlock();
            }
            if (conditionalIndent > 5)
            {
                code.CloseBlock();
            }
            code.CloseBlock();
        }

        code.AppendLine("writer.WriteEndObject();");
        code.CloseBlock();
        code.AppendLine();
        if (emitFastPaths)
        {
            SparseFragmentJsonFastPathEmitter.AppendFastPaths(code, members, sharedCodecsReference);
        }

        code.AppendLine(
            "private static global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TMember> GetMemberTypeInfo<TMember>(global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("var cache = GetJsonOptionsCache(options);");
        code.AppendLine("lock (cache.Gate)");
        code.OpenBlock();
        code.AppendLine(
            "if (cache.TypeInfos.TryGetValue(typeof(TMember), out var cached)) { return (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TMember>)cached; }"
        );
        code.AppendLine("var resolved = ResolveMemberTypeInfo<TMember>(options);");
        code.AppendLine("cache.TypeInfos.Add(typeof(TMember), resolved);");
        code.AppendLine("return resolved;");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine(
            "private static global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TMember> ResolveMemberTypeInfo<TMember>(global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.OpenBlock();
        code.AppendLine("try");
        code.OpenBlock();
        code.AppendLine("return (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TMember>)options.GetTypeInfo(typeof(TMember));");
        code.CloseBlock();
        code.AppendLine("catch (global::System.NotSupportedException exception)");
        code.OpenBlock();
        code.AppendLine(
            "throw new global::System.InvalidOperationException(\"The generated fragment converter requires JsonTypeInfo metadata for member type '\" + typeof(TMember) + \"'. Add the model/member types to a source-generated JsonSerializerContext and set it as JsonSerializerOptions.TypeInfoResolver.\", exception);"
        );
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine();
        if (sharedCodecsReference is null)
        {
            code.AppendLine("private static bool Matches(string? actual, string expected, global::System.Text.Json.JsonSerializerOptions options)");
            code.OpenBlock();
            code.AppendLine("if (actual is null) { return false; }");
            code.AppendLine(
                "return global::System.String.Equals(actual, expected, options.PropertyNameCaseInsensitive ? global::System.StringComparison.OrdinalIgnoreCase : global::System.StringComparison.Ordinal);"
            );
            code.CloseBlock();
        }
        else
        {
            code.AppendLine(
                "private static bool Matches(string? actual, string expected, global::System.Text.Json.JsonSerializerOptions options) => "
                    + sharedCodecsReference
                    + ".Matches(actual, expected, options);"
            );
        }

        code.AppendLine();
        if (sharedCodecsReference is null)
        {
            code.AppendLine(
                "private static void HandleUnknownFragmentProperty(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options, string? propertyName)"
            );
            code.OpenBlock();
            code.AppendLine(
                "if (options.UnmappedMemberHandling == global::System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow) { throw new global::System.Text.Json.JsonException(\"The JSON property '\" + propertyName + \"' could not be mapped to fragment '\" + typeof(Fragment) + \"'.\"); }"
            );
            code.AppendLine("reader.Skip();");
            code.CloseBlock();
        }
        else
        {
            code.AppendLine(
                "private static void HandleUnknownFragmentProperty(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options, string? propertyName) => "
                    + sharedCodecsReference
                    + ".HandleUnknownFragmentProperty(ref reader, options, propertyName, typeof(Fragment));"
            );
        }
        AppendJsonOptionsCache(code, members, jsonMemberIndexes, ignoredMemberIndexes, indexedDispatch);
        code.CloseBlock();
    }

    private static void AppendJsonOptionsCache(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<int>.Builder jsonMemberIndexes,
        ImmutableArray<int>.Builder ignoredMemberIndexes,
        bool indexedDispatch
    )
    {
        code.AppendLine(
            "private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<global::System.Text.Json.JsonSerializerOptions, JsonOptionsCache> __jsonOptionsCaches = new global::System.Runtime.CompilerServices.ConditionalWeakTable<global::System.Text.Json.JsonSerializerOptions, JsonOptionsCache>();"
        );
        code.AppendLine("private sealed class JsonOptionsCache");
        code.OpenBlock();
        code.AppendLine("public readonly object Gate = new object();");
        code.AppendLine("public JsonPropertyIndexCache? Properties;");
        code.AppendLine(
            "public readonly global::System.Collections.Generic.Dictionary<global::System.Type, global::System.Text.Json.Serialization.Metadata.JsonTypeInfo> TypeInfos = new global::System.Collections.Generic.Dictionary<global::System.Type, global::System.Text.Json.Serialization.Metadata.JsonTypeInfo>();"
        );
        code.CloseBlock();
        code.AppendLine("private sealed class JsonPropertyIndexCache");
        code.OpenBlock();
        code.AppendLine(
            "public JsonPropertyIndexCache(string[] propertyNames, byte[][] utf8Names, global::System.Collections.Generic.Dictionary<string, int>? indexes, global::System.Text.Json.JsonNamingPolicy? namingPolicy, bool caseInsensitive, bool namesValidated)"
        );
        code.OpenBlock();
        code.AppendLine("PropertyNames = propertyNames;");
        code.AppendLine("Utf8Names = utf8Names;");
        code.AppendLine("Indexes = indexes;");
        code.AppendLine("NamingPolicy = namingPolicy;");
        code.AppendLine("CaseInsensitive = caseInsensitive;");
        code.AppendLine("NamesValidated = namesValidated;");
        code.CloseBlock();
        code.AppendLine("public readonly string[] PropertyNames;");
        code.AppendLine("public readonly byte[][] Utf8Names;");
        code.AppendLine("public readonly global::System.Collections.Generic.Dictionary<string, int>? Indexes;");
        code.AppendLine("public readonly global::System.Text.Json.JsonNamingPolicy? NamingPolicy;");
        code.AppendLine("public readonly bool CaseInsensitive;");
        code.AppendLine("public readonly bool NamesValidated;");
        code.CloseBlock();
        code.AppendLine(
            "private static JsonOptionsCache GetJsonOptionsCache(global::System.Text.Json.JsonSerializerOptions options) => __jsonOptionsCaches.GetValue(options, _ => new JsonOptionsCache());"
        );
        code.AppendLine(
            "private static JsonPropertyIndexCache GetJsonPropertyCache(global::System.Text.Json.JsonSerializerOptions options, bool validateNames)"
        );
        code.OpenBlock();
        code.AppendLine("var namingPolicy = options.PropertyNamingPolicy;");
        code.AppendLine("var caseInsensitive = options.PropertyNameCaseInsensitive;");
        code.AppendLine("var buildIndexes = validateNames && " + (indexedDispatch ? "caseInsensitive;" : "false;"));
        // Each options instance owns the first evaluated names, including results from custom policies.
        code.AppendLine("var cache = GetJsonOptionsCache(options);");
        code.AppendLine("lock (cache.Gate)");
        code.OpenBlock();
        code.AppendLine("var current = cache.Properties;");
        code.AppendLine(
            "if (current is not null && global::System.Object.ReferenceEquals(current.NamingPolicy, namingPolicy) && current.CaseInsensitive == caseInsensitive && (!validateNames || current.NamesValidated) && (!buildIndexes || current.Indexes is not null)) { return current; }"
        );
        code.AppendLine("var built = BuildJsonPropertyCache(options, validateNames, buildIndexes);");
        code.AppendLine("cache.Properties = built;");
        code.AppendLine("return built;");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine(
            "private static JsonPropertyIndexCache BuildJsonPropertyCache(global::System.Text.Json.JsonSerializerOptions options, bool validateNames, bool buildIndexes)"
        );
        code.OpenBlock();
        code.AppendLine(
            "var comparer = options.PropertyNameCaseInsensitive ? global::System.StringComparer.OrdinalIgnoreCase : global::System.StringComparer.Ordinal;"
        );
        code.AppendLine("var visibleNames = validateNames ? new global::System.Collections.Generic.HashSet<string>(comparer) : null;");
        code.AppendLine(
            "global::System.Collections.Generic.Dictionary<string, int>? indexes = buildIndexes ? new global::System.Collections.Generic.Dictionary<string, int>(comparer) : null;"
        );
        code.AppendLine("var propertyNames = new string[" + members.Length.ToString() + "];");
        code.AppendLine("var utf8Names = new byte[" + members.Length.ToString() + "][];");
        foreach (var index in jsonMemberIndexes)
        {
            var property = members[index].Property;
            var literal = SymbolDisplay.FormatLiteral(property.JsonPropertyName ?? property.Name, true);
            var expression = property.HasExplicitJsonPropertyName
                ? literal
                : "options.PropertyNamingPolicy?.ConvertName(" + literal + ") ?? " + literal;
            code.AppendLine("var __jsonName_" + index + " = " + expression + ";");
            code.AppendLine("propertyNames[" + index + "] = __jsonName_" + index + ";");
            code.AppendLine("utf8Names[" + index + "] = global::System.Text.Encoding.UTF8.GetBytes(__jsonName_" + index + ");");
            code.AppendLine(
                "if (visibleNames is not null && !visibleNames.Add(__jsonName_"
                    + index
                    + ")) { throw new global::System.Text.Json.JsonException(\"Multiple fragment members map to the same JSON property name.\"); }"
            );
            if (indexedDispatch)
            {
                code.AppendLine("if (indexes is not null) { indexes.Add(__jsonName_" + index + ", " + index + "); }");
            }
        }
        foreach (var index in ignoredMemberIndexes)
        {
            var property = members[index].Property;
            var literal = SymbolDisplay.FormatLiteral(property.JsonPropertyName ?? property.Name, true);
            var expression = property.HasExplicitJsonPropertyName
                ? literal
                : "options.PropertyNamingPolicy?.ConvertName(" + literal + ") ?? " + literal;
            code.AppendLine("var __jsonName_" + index + " = " + expression + ";");
            code.AppendLine("propertyNames[" + index + "] = __jsonName_" + index + ";");
            code.AppendLine("utf8Names[" + index + "] = global::System.Text.Encoding.UTF8.GetBytes(__jsonName_" + index + ");");
            if (indexedDispatch)
            {
                code.AppendLine(
                    "if (indexes is not null && !indexes.ContainsKey(__jsonName_"
                        + index
                        + ")) { indexes.Add(__jsonName_"
                        + index
                        + ", "
                        + index
                        + "); }"
                );
            }
        }
        code.AppendLine(
            "return new JsonPropertyIndexCache(propertyNames, utf8Names, indexes, options.PropertyNamingPolicy, options.PropertyNameCaseInsensitive, validateNames);"
        );
        code.CloseBlock();
    }
}
