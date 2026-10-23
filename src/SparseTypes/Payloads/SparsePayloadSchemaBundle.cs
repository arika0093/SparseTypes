using System;
using System.Collections.Generic;
using System.Linq;

namespace SparseTypes;

/// <summary>Contains generated payload schemas for OpenAPI 3.0 and 3.1.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Advanced)]
public sealed class SparsePayloadSchemaBundle
{
    private readonly IReadOnlyList<Func<SparsePayloadSchemaBundle>> _dependencies;
    private readonly IReadOnlyList<SparsePayloadSchema> _directOpenApi30Schemas;
    private readonly IReadOnlyList<SparsePayloadSchema> _directOpenApi31Schemas;
    private readonly IReadOnlyList<Type> _directRequiredExternalSchemaTypes;
    private IReadOnlyList<SparsePayloadSchema>? _openApi30Schemas;
    private IReadOnlyList<SparsePayloadSchema>? _openApi31Schemas;
    private IReadOnlyList<Type>? _requiredExternalSchemaTypes;

    /// <summary>Creates an immutable schema bundle for one generated payload root.</summary>
    /// <param name="rootType">The generated ChangePayload CLR type.</param>
    /// <param name="openApi30Schemas">The complete OpenAPI 3.0 schema set, including the root.</param>
    /// <param name="openApi31Schemas">The complete OpenAPI 3.1 schema set, including the root.</param>
    /// <param name="requiredExternalSchemaTypes">Types whose schemas must be registered separately.</param>
    /// <param name="dependencies">Generated child-model schema bundles included when the schemas are read.</param>
    public SparsePayloadSchemaBundle(
        Type rootType,
        IReadOnlyList<SparsePayloadSchema> openApi30Schemas,
        IReadOnlyList<SparsePayloadSchema> openApi31Schemas,
        IReadOnlyList<Type> requiredExternalSchemaTypes,
        IReadOnlyList<Func<SparsePayloadSchemaBundle>>? dependencies = null
    )
    {
        RootType = rootType ?? throw new ArgumentNullException(nameof(rootType));
        _directOpenApi30Schemas = CopySchemas(openApi30Schemas, RootType, "OpenAPI 3.0", out var rootSchemaName);
        _directOpenApi31Schemas = CopySchemas(openApi31Schemas, RootType, "OpenAPI 3.1", out var openApi31RootSchemaName);
        if (!string.Equals(rootSchemaName, openApi31RootSchemaName, StringComparison.Ordinal))
        {
            throw new ArgumentException("The OpenAPI 3.0 and 3.1 root component names must match.", nameof(openApi31Schemas));
        }

        ValidateComponentNames(_directOpenApi30Schemas, _directOpenApi31Schemas);
        RootSchemaName = rootSchemaName;
        _directRequiredExternalSchemaTypes = CopyExternalTypes(requiredExternalSchemaTypes, _directOpenApi30Schemas, _directOpenApi31Schemas);
        _dependencies = CopyDependencies(dependencies);
    }

    /// <summary>Gets the generated ChangePayload CLR type represented by this bundle.</summary>
    public Type RootType { get; }

    /// <summary>Gets the stable component name of the payload root.</summary>
    public string RootSchemaName { get; }

    /// <summary>Gets the complete OpenAPI 3.0 schema set.</summary>
    public IReadOnlyList<SparsePayloadSchema> OpenApi30Schemas => _openApi30Schemas ??= FlattenSchemas(openApi31: false);

    /// <summary>Gets the complete OpenAPI 3.1 schema set.</summary>
    public IReadOnlyList<SparsePayloadSchema> OpenApi31Schemas => _openApi31Schemas ??= FlattenSchemas(openApi31: true);

    /// <summary>Gets leaf types that require an application-supplied schema.</summary>
    public IReadOnlyList<Type> RequiredExternalSchemaTypes => _requiredExternalSchemaTypes ??= FlattenExternalTypes();

    private static IReadOnlyList<Func<SparsePayloadSchemaBundle>> CopyDependencies(IReadOnlyList<Func<SparsePayloadSchemaBundle>>? dependencies)
    {
        if (dependencies is null || dependencies.Count == 0)
        {
            return Array.Empty<Func<SparsePayloadSchemaBundle>>();
        }

        var items = new Func<SparsePayloadSchemaBundle>[dependencies.Count];
        for (var index = 0; index < dependencies.Count; index++)
        {
            items[index] =
                dependencies[index] ?? throw new ArgumentException("Schema dependencies must not contain null entries.", nameof(dependencies));
        }

        return Array.AsReadOnly(items);
    }

    private IReadOnlyList<SparsePayloadSchema> FlattenSchemas(bool openApi31)
    {
        var schemas = new List<SparsePayloadSchema>();
        var byType = new Dictionary<Type, SparsePayloadSchema>();
        var byName = new Dictionary<string, Type>(StringComparer.Ordinal);
        var visited = new HashSet<SparsePayloadSchemaBundle>();
        Collect(this);
        return Array.AsReadOnly(schemas.ToArray());

        void Collect(SparsePayloadSchemaBundle bundle)
        {
            if (!visited.Add(bundle))
            {
                return;
            }

            var directSchemas = openApi31 ? bundle._directOpenApi31Schemas : bundle._directOpenApi30Schemas;
            foreach (var schema in directSchemas)
            {
                if (byType.TryGetValue(schema.Type, out var existing))
                {
                    if (
                        !string.Equals(existing.Name, schema.Name, StringComparison.Ordinal)
                        || !string.Equals(existing.OpenApi30JsonSchema, schema.OpenApi30JsonSchema, StringComparison.Ordinal)
                        || !string.Equals(existing.OpenApi31JsonSchema, schema.OpenApi31JsonSchema, StringComparison.Ordinal)
                    )
                    {
                        throw new ArgumentException($"Generated payload schema type '{schema.Type}' has conflicting definitions.");
                    }

                    continue;
                }

                if (byName.TryGetValue(schema.Name, out var existingType) && existingType != schema.Type)
                {
                    throw new ArgumentException($"Generated payload schema name '{schema.Name}' is used by multiple types.");
                }

                byType.Add(schema.Type, schema);
                byName.Add(schema.Name, schema.Type);
                schemas.Add(schema);
            }

            foreach (var dependency in bundle._dependencies)
            {
                Collect(dependency() ?? throw new InvalidOperationException("A generated schema dependency returned null."));
            }
        }
    }

    private IReadOnlyList<Type> FlattenExternalTypes()
    {
        var externalTypes = new List<Type>();
        var seenExternalTypes = new HashSet<Type>();
        var generatedTypes = new HashSet<Type>();
        var visited = new HashSet<SparsePayloadSchemaBundle>();
        Collect(this);
        var conflict = externalTypes.FirstOrDefault(generatedTypes.Contains);
        if (conflict is not null)
        {
            throw new ArgumentException($"Type '{conflict}' cannot be both generated and external.");
        }

        return Array.AsReadOnly(externalTypes.ToArray());

        void Collect(SparsePayloadSchemaBundle bundle)
        {
            if (!visited.Add(bundle))
            {
                return;
            }

            foreach (var schema in bundle._directOpenApi30Schemas)
            {
                generatedTypes.Add(schema.Type);
            }

            externalTypes.AddRange(bundle._directRequiredExternalSchemaTypes.Where(seenExternalTypes.Add));

            foreach (var dependency in bundle._dependencies)
            {
                Collect(dependency() ?? throw new InvalidOperationException("A generated schema dependency returned null."));
            }
        }
    }

    private static IReadOnlyList<SparsePayloadSchema> CopySchemas(
        IReadOnlyList<SparsePayloadSchema> schemas,
        Type rootType,
        string dialect,
        out string rootSchemaName
    )
    {
        if (schemas is null)
        {
            throw new ArgumentNullException(nameof(schemas));
        }

        if (schemas.Count == 0)
        {
            throw new ArgumentException($"{dialect} schemas must include the payload root.", nameof(schemas));
        }

        var items = new SparsePayloadSchema[schemas.Count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        var types = new HashSet<Type>();
        SparsePayloadSchema? root = null;
        for (var index = 0; index < schemas.Count; index++)
        {
            var schema = schemas[index] ?? throw new ArgumentException($"{dialect} schemas must not contain null entries.", nameof(schemas));
            if (!names.Add(schema.Name))
            {
                throw new ArgumentException($"{dialect} schema name '{schema.Name}' is duplicated.", nameof(schemas));
            }

            if (!types.Add(schema.Type))
            {
                throw new ArgumentException($"{dialect} schema type '{schema.Type}' is duplicated.", nameof(schemas));
            }

            if (schema.Type == rootType)
            {
                root = schema;
            }

            items[index] = schema;
        }

        rootSchemaName = root?.Name ?? throw new ArgumentException($"{dialect} schemas do not contain the payload root type.", nameof(schemas));
        return Array.AsReadOnly(items);
    }

    private static IReadOnlyList<Type> CopyExternalTypes(
        IReadOnlyList<Type> types,
        IReadOnlyList<SparsePayloadSchema> openApi30Schemas,
        IReadOnlyList<SparsePayloadSchema> openApi31Schemas
    )
    {
        if (types is null)
        {
            throw new ArgumentNullException(nameof(types));
        }

        var items = new Type[types.Count];
        var seen = new HashSet<Type>();
        for (var index = 0; index < types.Count; index++)
        {
            var type = types[index] ?? throw new ArgumentException("Required external schema types must not contain null entries.", nameof(types));
            if (!seen.Add(type))
            {
                throw new ArgumentException($"External schema type '{type}' is duplicated.", nameof(types));
            }

            if (ContainsType(openApi30Schemas, type) || ContainsType(openApi31Schemas, type))
            {
                throw new ArgumentException($"Type '{type}' cannot be both generated and external.", nameof(types));
            }

            items[index] = type;
        }

        return Array.AsReadOnly(items);
    }

    private static bool ContainsType(IReadOnlyList<SparsePayloadSchema> schemas, Type type)
    {
        for (var index = 0; index < schemas.Count; index++)
        {
            if (schemas[index].Type == type)
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateComponentNames(
        IReadOnlyList<SparsePayloadSchema> openApi30Schemas,
        IReadOnlyList<SparsePayloadSchema> openApi31Schemas
    )
    {
        if (openApi30Schemas.Count != openApi31Schemas.Count)
        {
            throw new ArgumentException("OpenAPI 3.0 and 3.1 schema sets must contain the same component types.", nameof(openApi31Schemas));
        }

        var openApi31Names = new Dictionary<Type, string>(openApi31Schemas.Count);
        for (var index = 0; index < openApi31Schemas.Count; index++)
        {
            openApi31Names.Add(openApi31Schemas[index].Type, openApi31Schemas[index].Name);
        }

        for (var index = 0; index < openApi30Schemas.Count; index++)
        {
            var schema = openApi30Schemas[index];
            if (!openApi31Names.TryGetValue(schema.Type, out var name) || !string.Equals(name, schema.Name, StringComparison.Ordinal))
            {
                throw new ArgumentException($"OpenAPI 3.0 and 3.1 component identities differ for type '{schema.Type}'.", nameof(openApi31Schemas));
            }
        }
    }
}
