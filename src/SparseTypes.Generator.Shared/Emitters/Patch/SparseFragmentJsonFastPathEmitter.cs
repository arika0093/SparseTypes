using System.Collections.Immutable;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits trim-safe fast paths for fragment JSON converters.</summary>
internal static class SparseFragmentJsonFastPathEmitter
{
    /// <summary>Emits collection codecs and member fast paths.</summary>
    internal static void AppendFastPaths(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members
    )
    {
        foreach (var codecMember in members)
        {
            if (codecMember.ChildModel is not null)
                continue;
            SparseJsonCollectionModel? codecModel = null;
            if (
                !SparseJsonCollectionShapes.TryResolve(
                    codecMember,
                    SparseFragmentEmitHelpers.FragmentValueType(codecMember),
                    out codecModel
                ) || codecModel is null
            )
                continue;
            SparseJsonCollectionShapes.AppendCollectionCodec(
                code,
                SparseFragmentEmitHelpers.FragmentValueType(codecMember),
                codecModel,
                "ReadCollection" + codecMember.Id,
                "WriteCollection" + codecMember.Id,
                "GetMemberTypeInfo"
            );
        }
        AppendMemberValueHelpers(code);
        SparseJsonScalarShapes.AppendScalarHelpers(code, 3, "TMember");
        SparseJsonScalarShapes.AppendMapKeyHelpers(code, 3);
    }

    /// <summary>Emits fast-path fragment member readers and writers.</summary>
    private static void AppendMemberValueHelpers(SharedIndentedBuilder code)
    {
        code.AppendLineAt(
            3,
            "private static TMember? ReadMemberValue<TMember>(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) { if (default(TMember) is null) return default; throw new global::System.Text.Json.JsonException(\"Malformed fragment value.\"); }"
        );
        code.AppendLineAt(
            4,
            "if (TryReadScalar<TMember>(ref reader, out var scalarMember)) return scalarMember;"
        );
        code.AppendLineAt(
            4,
            "return global::System.Text.Json.JsonSerializer.Deserialize<TMember>(ref reader, GetMemberTypeInfo<TMember>(options))!;"
        );
        code.AppendLineAt(3, "}");
        code.AppendLine();
        code.AppendLineAt(
            3,
            "private static void WriteMemberValue<TMember>(global::System.Text.Json.Utf8JsonWriter writer, TMember? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLineAt(
            4,
            "if (!TryWriteScalar(writer, value!)) global::System.Text.Json.JsonSerializer.Serialize(writer, value!, GetMemberTypeInfo<TMember>(options));"
        );
        code.AppendLineAt(3, "}");
        code.AppendLine();
    }
}
