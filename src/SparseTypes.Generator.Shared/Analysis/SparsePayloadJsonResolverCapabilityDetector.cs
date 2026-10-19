using Microsoft.CodeAnalysis;

namespace SparseTypes.Generator.Shared;

/// <summary>Probes APIs used by the per-model trim-safe payload JSON resolver.</summary>
internal static class SparsePayloadJsonResolverCapabilityDetector
{
    /// <summary>Checks the payload resolver surface in one consuming compilation.</summary>
    /// <param name="compilation">The consuming compilation.</param>
    /// <param name="reader">The UTF-8 JSON reader symbol.</param>
    /// <param name="writer">The UTF-8 JSON writer symbol.</param>
    /// <param name="options">The serializer options symbol.</param>
    /// <param name="genericTypeInfo">The generic JSON metadata symbol.</param>
    /// <returns><see langword="true"/> when all emitted APIs are available.</returns>
    public static bool Probe(
        Compilation compilation,
        INamedTypeSymbol? reader,
        INamedTypeSymbol? writer,
        INamedTypeSymbol? options,
        INamedTypeSymbol? genericTypeInfo
    ) => FindMissingApi(compilation, reader, writer, options, genericTypeInfo) is null;

    internal static string? FindMissingApi(Compilation compilation)
    {
        return FindMissingApi(
            compilation,
            SparseStjCapabilityDetector.SafeGet(compilation, "System.Text.Json.Utf8JsonReader"),
            SparseStjCapabilityDetector.SafeGet(compilation, "System.Text.Json.Utf8JsonWriter"),
            SparseStjCapabilityDetector.SafeGet(
                compilation,
                "System.Text.Json.JsonSerializerOptions"
            ),
            SparseStjCapabilityDetector.SafeGet(
                compilation,
                "System.Text.Json.Serialization.Metadata.JsonTypeInfo`1"
            )
        );
    }

    private static string? FindMissingApi(
        Compilation compilation,
        INamedTypeSymbol? reader,
        INamedTypeSymbol? writer,
        INamedTypeSymbol? options,
        INamedTypeSymbol? genericTypeInfo
    )
    {
        var document = SparseStjCapabilityDetector.SafeGet(
            compilation,
            "System.Text.Json.JsonDocument"
        );
        var element = SparseStjCapabilityDetector.SafeGet(
            compilation,
            "System.Text.Json.JsonElement"
        );
        var valueKind = SparseStjCapabilityDetector.SafeGet(
            compilation,
            "System.Text.Json.JsonValueKind"
        );
        var tokenType = SparseStjCapabilityDetector.SafeGet(
            compilation,
            "System.Text.Json.JsonTokenType"
        );
        var resolver = SparseStjCapabilityDetector.SafeGet(
            compilation,
            "System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver"
        );
        var metadataServices = SparseStjCapabilityDetector.SafeGet(
            compilation,
            "System.Text.Json.Serialization.Metadata.JsonMetadataServices"
        );
        var converter = SparseStjCapabilityDetector.SafeGet(
            compilation,
            "System.Text.Json.Serialization.JsonConverter"
        );
        var readOnlySpan = SparseStjCapabilityDetector.SafeGet(
            compilation,
            "System.ReadOnlySpan`1"
        );
        var byteType = compilation.GetSpecialType(SpecialType.System_Byte);
        var stringType = compilation.GetSpecialType(SpecialType.System_String);
        var booleanType = compilation.GetSpecialType(SpecialType.System_Boolean);
        var intType = compilation.GetSpecialType(SpecialType.System_Int32);
        var voidType = compilation.GetSpecialType(SpecialType.System_Void);
        var guidType = SparseStjCapabilityDetector.SafeGet(compilation, "System.Guid");
        var dateTimeType = SparseStjCapabilityDetector.SafeGet(compilation, "System.DateTime");
        var dateTimeOffsetType = SparseStjCapabilityDetector.SafeGet(
            compilation,
            "System.DateTimeOffset"
        );
        var exceptionType = SparseStjCapabilityDetector.SafeGet(
            compilation,
            "System.Text.Json.JsonException"
        );
        var systemExceptionType = SparseStjCapabilityDetector.SafeGet(
            compilation,
            "System.Exception"
        );
        if (
            reader is null
            || writer is null
            || options is null
            || genericTypeInfo is null
            || document is null
            || element is null
            || valueKind is null
            || tokenType is null
            || resolver is null
            || metadataServices is null
            || converter is null
            || readOnlySpan is null
            || guidType is null
            || dateTimeType is null
            || dateTimeOffsetType is null
            || exceptionType is null
            || systemExceptionType is null
            || !document.IsAccessibleTo(compilation)
            || !element.IsAccessibleTo(compilation)
            || !valueKind.IsAccessibleTo(compilation)
            || !resolver.IsAccessibleTo(compilation)
            || !metadataServices.IsAccessibleTo(compilation)
            || resolver.TypeKind != TypeKind.Interface
        )
            return "required resolver types";

        var byteSpan = readOnlySpan.Construct(byteType);
        var hasParseValue = document
            .GetMembers("ParseValue")
            .Any(member =>
                member is IMethodSymbol method
                && SparseStjCapabilityDetector.IsUsableMethod(
                    method,
                    compilation,
                    isStatic: true,
                    genericArity: 0
                )
                && method.Parameters.Length == 1
                && SparseStjCapabilityDetector.ParameterMatches(
                    method.Parameters[0],
                    reader,
                    RefKind.Ref
                )
                && SymbolEqualityComparer.Default.Equals(method.ReturnType, document)
            );
        var hasTryGetProperty = element
            .GetMembers("TryGetProperty")
            .Any(member =>
                member is IMethodSymbol method
                && SparseStjCapabilityDetector.IsUsableMethod(
                    method,
                    compilation,
                    isStatic: false,
                    genericArity: 0
                )
                && method.Parameters.Length == 2
                && SparseStjCapabilityDetector.ParameterMatches(
                    method.Parameters[0],
                    stringType,
                    RefKind.None
                )
                && SparseStjCapabilityDetector.ParameterMatches(
                    method.Parameters[1],
                    element,
                    RefKind.Out
                )
                && SymbolEqualityComparer.Default.Equals(method.ReturnType, booleanType)
            );
        var hasCreateValueInfo = metadataServices
            .GetMembers("CreateValueInfo")
            .Any(member =>
                member is IMethodSymbol method
                && SparseStjCapabilityDetector.IsUsableMethod(
                    method,
                    compilation,
                    isStatic: true,
                    genericArity: 1
                )
                && method.Parameters.Length == 2
                && SparseStjCapabilityDetector.ParameterMatches(
                    method.Parameters[0],
                    options,
                    RefKind.None
                )
                && SparseStjCapabilityDetector.ParameterMatches(
                    method.Parameters[1],
                    converter,
                    RefKind.None
                )
                && SymbolEqualityComparer.Default.Equals(
                    method.ReturnType,
                    genericTypeInfo.Construct(method.TypeParameters[0])
                )
            );

        return Check(
                "JsonSerializerOptions.TypeInfoResolver",
                SparseStjCapabilityDetector.HasWritableInstanceProperty(
                    options,
                    "TypeInfoResolver",
                    resolver,
                    compilation
                )
            )
            ?? Check(
                "JsonDocument.RootElement",
                SparseStjCapabilityDetector.HasInstanceProperty(
                    document,
                    "RootElement",
                    element,
                    compilation
                )
            )
            ?? Check(
                "JsonElement.ValueKind",
                SparseStjCapabilityDetector.HasInstanceProperty(
                    element,
                    "ValueKind",
                    valueKind,
                    compilation
                )
            )
            ?? Check("JsonDocument.ParseValue", hasParseValue)
            ?? Check("JsonElement.TryGetProperty", hasTryGetProperty)
            ?? Check(
                "JsonElement.GetString",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    element,
                    "GetString",
                    stringType,
                    compilation,
                    []
                )
            )
            ?? Check(
                "JsonElement.GetRawText",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    element,
                    "GetRawText",
                    stringType,
                    compilation,
                    []
                )
            )
            ?? Check(
                "Utf8JsonReader.GetInt32",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    reader,
                    "GetInt32",
                    intType,
                    compilation,
                    []
                )
            )
            ?? Check(
                "Utf8JsonReader.GetBoolean",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    reader,
                    "GetBoolean",
                    booleanType,
                    compilation,
                    []
                )
            )
            ?? Check(
                "Utf8JsonReader.GetGuid",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    reader,
                    "GetGuid",
                    guidType,
                    compilation,
                    []
                )
            )
            ?? Check(
                "Utf8JsonReader.GetByte",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    reader,
                    "GetByte",
                    compilation.GetSpecialType(SpecialType.System_Byte),
                    compilation,
                    []
                )
            )
            ?? Check(
                "Utf8JsonReader.GetSByte",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    reader,
                    "GetSByte",
                    compilation.GetSpecialType(SpecialType.System_SByte),
                    compilation,
                    []
                )
            )
            ?? Check(
                "Utf8JsonReader.GetInt16",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    reader,
                    "GetInt16",
                    compilation.GetSpecialType(SpecialType.System_Int16),
                    compilation,
                    []
                )
            )
            ?? Check(
                "Utf8JsonReader.GetUInt16",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    reader,
                    "GetUInt16",
                    compilation.GetSpecialType(SpecialType.System_UInt16),
                    compilation,
                    []
                )
            )
            ?? Check(
                "Utf8JsonReader.GetUInt32",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    reader,
                    "GetUInt32",
                    compilation.GetSpecialType(SpecialType.System_UInt32),
                    compilation,
                    []
                )
            )
            ?? Check(
                "Utf8JsonReader.GetInt64",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    reader,
                    "GetInt64",
                    compilation.GetSpecialType(SpecialType.System_Int64),
                    compilation,
                    []
                )
            )
            ?? Check(
                "Utf8JsonReader.GetUInt64",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    reader,
                    "GetUInt64",
                    compilation.GetSpecialType(SpecialType.System_UInt64),
                    compilation,
                    []
                )
            )
            ?? Check(
                "Utf8JsonReader.GetSingle",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    reader,
                    "GetSingle",
                    compilation.GetSpecialType(SpecialType.System_Single),
                    compilation,
                    []
                )
            )
            ?? Check(
                "Utf8JsonReader.GetDouble",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    reader,
                    "GetDouble",
                    compilation.GetSpecialType(SpecialType.System_Double),
                    compilation,
                    []
                )
            )
            ?? Check(
                "Utf8JsonReader.GetDecimal",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    reader,
                    "GetDecimal",
                    compilation.GetSpecialType(SpecialType.System_Decimal),
                    compilation,
                    []
                )
            )
            ?? Check(
                "Utf8JsonReader.GetDateTime",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    reader,
                    "GetDateTime",
                    dateTimeType,
                    compilation,
                    []
                )
            )
            ?? Check(
                "Utf8JsonReader.GetDateTimeOffset",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    reader,
                    "GetDateTimeOffset",
                    dateTimeOffsetType,
                    compilation,
                    []
                )
            )
            ?? Check(
                "Utf8JsonReader(ReadOnlySpan<byte>)",
                SparseStjConstructorCapabilityProbe.HasAccessibleConstructorWithOptionalSuffix(
                    reader,
                    compilation,
                    [byteSpan]
                )
            )
            ?? Check(
                "JsonTokenType.StartArray",
                SparseStjCapabilityDetector.HasEnumMember(tokenType, "StartArray", compilation)
            )
            ?? Check(
                "JsonTokenType.EndArray",
                SparseStjCapabilityDetector.HasEnumMember(tokenType, "EndArray", compilation)
            )
            ?? Check(
                "Utf8JsonWriter.WriteStartArray",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    writer,
                    "WriteStartArray",
                    voidType,
                    compilation,
                    []
                )
            )
            ?? Check(
                "Utf8JsonWriter.WriteEndArray",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    writer,
                    "WriteEndArray",
                    voidType,
                    compilation,
                    []
                )
            )
            ?? Check(
                "Utf8JsonWriter.WriteNumberValue(int)",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    writer,
                    "WriteNumberValue",
                    voidType,
                    compilation,
                    [intType]
                )
            )
            ?? Check(
                "Utf8JsonWriter.WriteNumberValue(uint)",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    writer,
                    "WriteNumberValue",
                    voidType,
                    compilation,
                    [compilation.GetSpecialType(SpecialType.System_UInt32)]
                )
            )
            ?? Check(
                "Utf8JsonWriter.WriteNumberValue(long)",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    writer,
                    "WriteNumberValue",
                    voidType,
                    compilation,
                    [compilation.GetSpecialType(SpecialType.System_Int64)]
                )
            )
            ?? Check(
                "Utf8JsonWriter.WriteNumberValue(ulong)",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    writer,
                    "WriteNumberValue",
                    voidType,
                    compilation,
                    [compilation.GetSpecialType(SpecialType.System_UInt64)]
                )
            )
            ?? Check(
                "Utf8JsonWriter.WriteNumberValue(float)",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    writer,
                    "WriteNumberValue",
                    voidType,
                    compilation,
                    [compilation.GetSpecialType(SpecialType.System_Single)]
                )
            )
            ?? Check(
                "Utf8JsonWriter.WriteNumberValue(double)",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    writer,
                    "WriteNumberValue",
                    voidType,
                    compilation,
                    [compilation.GetSpecialType(SpecialType.System_Double)]
                )
            )
            ?? Check(
                "Utf8JsonWriter.WriteNumberValue(decimal)",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    writer,
                    "WriteNumberValue",
                    voidType,
                    compilation,
                    [compilation.GetSpecialType(SpecialType.System_Decimal)]
                )
            )
            ?? Check(
                "Utf8JsonWriter.WriteStringValue(DateTime)",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    writer,
                    "WriteStringValue",
                    voidType,
                    compilation,
                    [dateTimeType]
                )
            )
            ?? Check(
                "Utf8JsonWriter.WriteStringValue(DateTimeOffset)",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    writer,
                    "WriteStringValue",
                    voidType,
                    compilation,
                    [dateTimeOffsetType]
                )
            )
            ?? Check(
                "JsonException(string, Exception)",
                SparseStjCapabilityDetector.HasConstructor(
                    exceptionType,
                    compilation,
                    [stringType, systemExceptionType]
                )
            )
            ?? Check(
                "Utf8JsonWriter.WriteBooleanValue(bool)",
                SparseStjCapabilityDetector.HasInstanceMethod(
                    writer,
                    "WriteBooleanValue",
                    voidType,
                    compilation,
                    [booleanType]
                )
            )
            ?? Check("JsonMetadataServices.CreateValueInfo<T>", hasCreateValueInfo);
    }

    private static string? Check(string name, bool condition) => condition ? null : name;
}
