using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SparseTypes.OpenApi.AspNetCore;

/// <summary>Maps ChangePayload wire enums to string schemas.</summary>
/// <remarks>
/// The v1 wire encodes <see cref="SparseTypes.ChangePayloadState"/> and
/// <see cref="SparseTypes.ChangePayloadItemKind"/> as lower-case string tokens
/// (see <see cref="SparseTypes.ChangePayloadWire"/>), but reflection-based schema
/// generation reports them as integers. These helpers rewrite them to string
/// enums so the OpenAPI document matches the actual JSON bytes.
/// </remarks>
internal static class SparsePayloadEnumSchemas
{
    internal static Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
        if (type == typeof(SparseTypes.ChangePayloadState))
        {
            ApplyStateSchema(schema);
        }
        else if (type == typeof(SparseTypes.ChangePayloadItemKind))
        {
            ApplyKindSchema(schema);
        }

        return Task.CompletedTask;
    }

    internal static void ApplyStateSchema(OpenApiSchema schema)
    {
        schema.Type = JsonSchemaType.String;
        schema.Format = null;
        schema.Enum =
        [
            JsonValue.Create(SparseTypes.ChangePayloadWire.MissingState),
            JsonValue.Create(SparseTypes.ChangePayloadWire.NullState),
            JsonValue.Create(SparseTypes.ChangePayloadWire.ValueState),
            JsonValue.Create(SparseTypes.ChangePayloadWire.RedactedState),
        ];
    }

    internal static void ApplyKindSchema(OpenApiSchema schema)
    {
        schema.Type = JsonSchemaType.String;
        schema.Format = null;
        schema.Enum =
        [
            JsonValue.Create(SparseTypes.ChangePayloadWire.AddKind),
            JsonValue.Create(SparseTypes.ChangePayloadWire.RemoveKind),
            JsonValue.Create(SparseTypes.ChangePayloadWire.EditKind),
            JsonValue.Create(SparseTypes.ChangePayloadWire.ReorderKind),
        ];
    }

    /// <summary>Rewrites leftover integer state/kind schemas to wire-accurate string enums.</summary>
    /// <remarks>
    /// The schema transformer covers newly generated schemas. This walk fixes
    /// inline schemas that bypassed it, such as endpoint objects materialized
    /// before the mapping. Wire names are lower-case <c>state</c> and <c>kind</c>;
    /// only those properties and the dedicated <c>ChangePayloadState</c>/
    /// <c>ChangePayloadItemKind</c> components are touched so user domain enums stay untouched.
    /// </remarks>
    internal static void FixDocument(OpenApiDocument document)
    {
        if (document.Components?.Schemas is null)
        {
            return;
        }

        foreach (var entry in document.Components.Schemas)
        {
            if (TryResolveTarget(entry.Value, out var target) && target is OpenApiSchema concrete)
            {
                if (entry.Key.Contains("ChangePayloadState", StringComparison.Ordinal))
                {
                    ApplyStateSchema(concrete);
                }
                else if (entry.Key.Contains("ChangePayloadItemKind", StringComparison.Ordinal))
                {
                    ApplyKindSchema(concrete);
                }
            }
        }
        var visited = new HashSet<IOpenApiSchema>(ReferenceEqualityComparer.Instance);
        foreach (var schema in document.Components.Schemas.Values)
        {
            FixSchema(schema, visited);
        }
    }

    private static void FixSchema(IOpenApiSchema? schema, HashSet<IOpenApiSchema> visited)
    {
        if (schema is null || !visited.Add(schema))
        {
            return;
        }

        if (schema is OpenApiSchemaReference reference)
        {
            FixSchema(reference.Target, visited);
            return;
        }
        if (schema is not OpenApiSchema concrete)
        {
            return;
        }

        if (concrete.Properties is not null)
        {
            foreach (var entry in concrete.Properties)
            {
                if (
                    string.Equals(entry.Key, SparseTypes.ChangePayloadWire.StateProperty, StringComparison.Ordinal)
                    && TryResolveTarget(entry.Value, out var stateTarget)
                    && stateTarget is OpenApiSchema stateSchema
                )
                {
                    ApplyStateSchema(stateSchema);
                }
                else if (
                    string.Equals(entry.Key, "kind", StringComparison.Ordinal)
                    && TryResolveTarget(entry.Value, out var kindTarget)
                    && kindTarget is OpenApiSchema kindSchema
                )
                {
                    ApplyKindSchema(kindSchema);
                }

                FixSchema(entry.Value, visited);
            }
        }
        if (concrete.Items is not null)
        {
            FixSchema(concrete.Items, visited);
        }

        if (concrete.AdditionalProperties is not null)
        {
            FixSchema(concrete.AdditionalProperties, visited);
        }

        if (concrete.OneOf is not null)
        {
            foreach (var variant in concrete.OneOf)
            {
                FixSchema(variant, visited);
            }
        }
        if (concrete.AnyOf is not null)
        {
            foreach (var variant in concrete.AnyOf)
            {
                FixSchema(variant, visited);
            }
        }
        if (concrete.AllOf is not null)
        {
            foreach (var variant in concrete.AllOf)
            {
                FixSchema(variant, visited);
            }
        }
        if (concrete.Not is not null)
        {
            FixSchema(concrete.Not, visited);
        }
    }

    private static bool TryResolveTarget(IOpenApiSchema? schema, out IOpenApiSchema? target)
    {
        target = schema;
        var guard = 0;
        while (target is OpenApiSchemaReference reference && guard++ < 8)
        {
            target = reference.Target;
        }

        return target is not null;
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<IOpenApiSchema>
    {
        internal static readonly ReferenceEqualityComparer Instance = new();

        public bool Equals(IOpenApiSchema? x, IOpenApiSchema? y) => ReferenceEquals(x, y);

        public int GetHashCode(IOpenApiSchema obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
