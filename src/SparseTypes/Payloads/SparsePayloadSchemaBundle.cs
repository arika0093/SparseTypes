using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SparseTypes;

/// <summary>Contains generated payload schemas for OpenAPI 3.0 and 3.1.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Advanced)]
public sealed class SparsePayloadSchemaBundle
{
    private readonly IReadOnlyList<Func<SparsePayloadSchemaBundle>> _dependencies;
    private readonly Lazy<IReadOnlyList<SparsePayloadSchema>> _directOpenApi30Schemas;
    private readonly Lazy<IReadOnlyList<SparsePayloadSchema>> _directOpenApi31Schemas;
    private readonly IReadOnlyList<SparsePayloadSchemaIdentity> _directSchemaIdentities;
    private readonly IReadOnlyList<Type> _directRequiredExternalSchemaTypes;
    private readonly Lazy<IReadOnlyList<SparsePayloadSchema>> _openApi30Schemas;
    private readonly Lazy<IReadOnlyList<SparsePayloadSchema>> _openApi31Schemas;
    private readonly Lazy<IReadOnlyList<SparsePayloadSchemaIdentity>> _schemaIdentities;
    private readonly Lazy<IReadOnlyList<Type>> _requiredExternalSchemaTypes;

    /// <summary>Creates an immutable schema bundle from explicitly named schema options.</summary>
    /// <param name="rootType">The generated ChangePayload CLR type.</param>
    /// <param name="options">The schema sets and dependencies included in the bundle.</param>
    public SparsePayloadSchemaBundle(Type rootType, SparsePayloadSchemaBundleOptions options)
    {
        RootType = rootType ?? throw new ArgumentNullException(nameof(rootType));
        ArgumentNullException.ThrowIfNull(options);
        _directSchemaIdentities = CopyIdentities(options.SchemaIdentities, RootType, out var rootSchemaName);
        RootSchemaName = rootSchemaName;
        _directRequiredExternalSchemaTypes = CopyExternalTypes(options.RequiredExternalSchemaTypes, _directSchemaIdentities);
        _dependencies = CopyDependencies(options.Dependencies);
        _directOpenApi30Schemas = CreateDirectSchemas(options.Schema30, SparsePayloadSchemaDialect.OpenApi30, _directSchemaIdentities);
        _directOpenApi31Schemas = CreateDirectSchemas(options.Schema31, SparsePayloadSchemaDialect.OpenApi31, _directSchemaIdentities);
        _openApi30Schemas = new Lazy<IReadOnlyList<SparsePayloadSchema>>(
            () => FlattenSchemas(SparsePayloadSchemaDialect.OpenApi30),
            LazyThreadSafetyMode.ExecutionAndPublication
        );
        _openApi31Schemas = new Lazy<IReadOnlyList<SparsePayloadSchema>>(
            () => FlattenSchemas(SparsePayloadSchemaDialect.OpenApi31),
            LazyThreadSafetyMode.ExecutionAndPublication
        );
        _schemaIdentities = new Lazy<IReadOnlyList<SparsePayloadSchemaIdentity>>(
            FlattenSchemaIdentities,
            LazyThreadSafetyMode.ExecutionAndPublication
        );
        _requiredExternalSchemaTypes = new Lazy<IReadOnlyList<Type>>(FlattenExternalTypes, LazyThreadSafetyMode.ExecutionAndPublication);
    }

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
        : this(rootType, CreateLegacyOptions(rootType, openApi30Schemas, openApi31Schemas, requiredExternalSchemaTypes, dependencies)) { }

    /// <summary>Gets the generated ChangePayload CLR type represented by this bundle.</summary>
    public Type RootType { get; }

    /// <summary>Gets the stable component name of the payload root.</summary>
    public string RootSchemaName { get; }

    /// <summary>Gets the complete OpenAPI 3.0 schema set.</summary>
    public IReadOnlyList<SparsePayloadSchema> OpenApi30Schemas => _openApi30Schemas.Value;

    /// <summary>Gets the complete OpenAPI 3.1 schema set.</summary>
    public IReadOnlyList<SparsePayloadSchema> OpenApi31Schemas => _openApi31Schemas.Value;

    /// <summary>Gets leaf types that require an application-supplied schema.</summary>
    public IReadOnlyList<Type> RequiredExternalSchemaTypes => _requiredExternalSchemaTypes.Value;

    /// <summary>Gets the complete schema set for one requested OpenAPI dialect.</summary>
    /// <param name="dialect">The OpenAPI dialect to materialize.</param>
    /// <returns>The immutable schema set for the requested dialect.</returns>
    public IReadOnlyList<SparsePayloadSchema> GetSchemas(SparsePayloadSchemaDialect dialect) =>
        dialect switch
        {
            SparsePayloadSchemaDialect.OpenApi30 => _openApi30Schemas.Value,
            SparsePayloadSchemaDialect.OpenApi31 => _openApi31Schemas.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "Unsupported OpenAPI schema dialect."),
        };

    /// <summary>Gets generated component identities without materializing schema JSON.</summary>
    public IReadOnlyList<SparsePayloadSchemaIdentity> SchemaIdentities => _schemaIdentities.Value;

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

    private IReadOnlyList<SparsePayloadSchema> FlattenSchemas(SparsePayloadSchemaDialect dialect)
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

            var directSchemas = bundle.GetDirectSchemas(dialect);
            foreach (var schema in directSchemas)
            {
                if (byType.TryGetValue(schema.Type, out var existing))
                {
                    if (
                        !string.Equals(existing.Name, schema.Name, StringComparison.Ordinal)
                        || !string.Equals(existing.JsonSchema, schema.JsonSchema, StringComparison.Ordinal)
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

    private IReadOnlyList<SparsePayloadSchemaIdentity> FlattenSchemaIdentities()
    {
        var identities = new List<SparsePayloadSchemaIdentity>();
        var byType = new Dictionary<Type, SparsePayloadSchemaIdentity>();
        var byName = new Dictionary<string, Type>(StringComparer.Ordinal);
        var visited = new HashSet<SparsePayloadSchemaBundle>();
        Collect(this);
        return Array.AsReadOnly(identities.ToArray());

        void Collect(SparsePayloadSchemaBundle bundle)
        {
            if (!visited.Add(bundle))
            {
                return;
            }

            foreach (var identity in bundle._directSchemaIdentities)
            {
                if (byType.TryGetValue(identity.Type, out var existing))
                {
                    if (!string.Equals(existing.Name, identity.Name, StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"Generated payload schema type '{identity.Type}' has conflicting component names.");
                    }

                    continue;
                }

                if (byName.TryGetValue(identity.Name, out var existingType) && existingType != identity.Type)
                {
                    throw new ArgumentException($"Generated payload schema name '{identity.Name}' is used by multiple types.");
                }

                byType.Add(identity.Type, identity);
                byName.Add(identity.Name, identity.Type);
                identities.Add(identity);
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

            foreach (var identity in bundle._directSchemaIdentities)
            {
                generatedTypes.Add(identity.Type);
            }

            externalTypes.AddRange(bundle._directRequiredExternalSchemaTypes.Where(seenExternalTypes.Add));

            foreach (var dependency in bundle._dependencies)
            {
                Collect(dependency() ?? throw new InvalidOperationException("A generated schema dependency returned null."));
            }
        }
    }

    private IReadOnlyList<SparsePayloadSchema> GetDirectSchemas(SparsePayloadSchemaDialect dialect) =>
        dialect switch
        {
            SparsePayloadSchemaDialect.OpenApi30 => _directOpenApi30Schemas.Value,
            SparsePayloadSchemaDialect.OpenApi31 => _directOpenApi31Schemas.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "Unsupported OpenAPI schema dialect."),
        };

    private static Lazy<IReadOnlyList<SparsePayloadSchema>> CreateDirectSchemas(
        Func<IReadOnlyList<SparsePayloadSchema>> factory,
        SparsePayloadSchemaDialect dialect,
        IReadOnlyList<SparsePayloadSchemaIdentity> identities
    )
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new Lazy<IReadOnlyList<SparsePayloadSchema>>(
            () => CopySchemas(factory() ?? throw new InvalidOperationException("A schema factory returned null."), dialect, identities),
            LazyThreadSafetyMode.ExecutionAndPublication
        );
    }

    private static IReadOnlyList<SparsePayloadSchema> CopySchemas(
        IReadOnlyList<SparsePayloadSchema> schemas,
        SparsePayloadSchemaDialect dialect,
        IReadOnlyList<SparsePayloadSchemaIdentity> identities
    )
    {
        if (schemas is null)
        {
            throw new ArgumentNullException(nameof(schemas));
        }

        if (schemas.Count != identities.Count)
        {
            throw new ArgumentException("Schema factories must return every declared schema identity.", nameof(schemas));
        }

        var items = new SparsePayloadSchema[schemas.Count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        var types = new HashSet<Type>();
        var identitiesByType = identities.ToDictionary(static identity => identity.Type);
        for (var index = 0; index < schemas.Count; index++)
        {
            var schema = schemas[index] ?? throw new ArgumentException("Schema factories must not return null entries.", nameof(schemas));
            if (!names.Add(schema.Name))
            {
                throw new ArgumentException($"Schema name '{schema.Name}' is duplicated.", nameof(schemas));
            }

            if (!types.Add(schema.Type))
            {
                throw new ArgumentException($"Schema type '{schema.Type}' is duplicated.", nameof(schemas));
            }

            if (!identitiesByType.TryGetValue(schema.Type, out var identity) || !string.Equals(identity.Name, schema.Name, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Schema factory identity differs for type '{schema.Type}'.", nameof(schemas));
            }

            items[index] = schema.ForDialect(dialect);
        }

        return Array.AsReadOnly(items);
    }

    private static IReadOnlyList<SparsePayloadSchemaIdentity> CopyIdentities(
        IReadOnlyList<SparsePayloadSchemaIdentity> identities,
        Type rootType,
        out string rootSchemaName
    )
    {
        ArgumentNullException.ThrowIfNull(identities);
        if (identities.Count == 0)
        {
            throw new ArgumentException("Schema identities must include the payload root.", nameof(identities));
        }

        var items = new SparsePayloadSchemaIdentity[identities.Count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        var types = new HashSet<Type>();
        SparsePayloadSchemaIdentity? root = null;
        for (var index = 0; index < identities.Count; index++)
        {
            var identity = identities[index] ?? throw new ArgumentException("Schema identities must not contain null entries.", nameof(identities));
            if (!names.Add(identity.Name))
            {
                throw new ArgumentException($"Schema identity name '{identity.Name}' is duplicated.", nameof(identities));
            }

            if (!types.Add(identity.Type))
            {
                throw new ArgumentException($"Schema identity type '{identity.Type}' is duplicated.", nameof(identities));
            }

            if (identity.Type == rootType)
            {
                root = identity;
            }

            items[index] = identity;
        }

        rootSchemaName = root?.Name ?? throw new ArgumentException("Schema identities do not contain the payload root type.", nameof(identities));
        return Array.AsReadOnly(items);
    }

    private static SparsePayloadSchemaBundleOptions CreateLegacyOptions(
        Type rootType,
        IReadOnlyList<SparsePayloadSchema> openApi30Schemas,
        IReadOnlyList<SparsePayloadSchema> openApi31Schemas,
        IReadOnlyList<Type> requiredExternalSchemaTypes,
        IReadOnlyList<Func<SparsePayloadSchemaBundle>>? dependencies
    )
    {
        ArgumentNullException.ThrowIfNull(rootType);
        var schemas30 = CopySchemaInput(openApi30Schemas);
        var schemas31 = CopySchemaInput(openApi31Schemas);
        var identities = new SparsePayloadSchemaIdentity[schemas30.Count];
        for (var index = 0; index < schemas30.Count; index++)
        {
            var schema = schemas30[index];
            identities[index] = new SparsePayloadSchemaIdentity(schema.Type, schema.Name);
        }

        return new SparsePayloadSchemaBundleOptions
        {
            SchemaIdentities = identities,
            Schema30 = () => schemas30,
            Schema31 = () => schemas31,
            RequiredExternalSchemaTypes = requiredExternalSchemaTypes,
            Dependencies = dependencies ?? Array.Empty<Func<SparsePayloadSchemaBundle>>(),
        };
    }

    private static IReadOnlyList<SparsePayloadSchema> CopySchemaInput(IReadOnlyList<SparsePayloadSchema> schemas)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        var items = new SparsePayloadSchema[schemas.Count];
        for (var index = 0; index < schemas.Count; index++)
        {
            items[index] = schemas[index] ?? throw new ArgumentException("Schema lists must not contain null entries.", nameof(schemas));
        }

        return Array.AsReadOnly(items);
    }

    private static IReadOnlyList<Type> CopyExternalTypes(IReadOnlyList<Type> types, IReadOnlyList<SparsePayloadSchemaIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(types);
        var items = new Type[types.Count];
        var seen = new HashSet<Type>();
        var generatedTypes = identities.Select(static identity => identity.Type).ToHashSet();
        for (var index = 0; index < types.Count; index++)
        {
            var type = types[index] ?? throw new ArgumentException("Required external schema types must not contain null entries.", nameof(types));
            if (!seen.Add(type))
            {
                throw new ArgumentException($"External schema type '{type}' is duplicated.", nameof(types));
            }

            if (generatedTypes.Contains(type))
            {
                throw new ArgumentException($"Type '{type}' cannot be both generated and external.", nameof(types));
            }

            items[index] = type;
        }

        return Array.AsReadOnly(items);
    }
}

/// <summary>Configures the schema sets and dependencies for a payload schema bundle.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Advanced)]
public sealed class SparsePayloadSchemaBundleOptions
{
    /// <summary>Gets or initializes a factory for the OpenAPI 3.0 schema set.</summary>
    public Func<IReadOnlyList<SparsePayloadSchema>> Schema30 { get; init; } = null!;

    /// <summary>Gets or initializes a factory for the OpenAPI 3.1 schema set.</summary>
    public Func<IReadOnlyList<SparsePayloadSchema>> Schema31 { get; init; } = null!;

    /// <summary>Gets or initializes schema identities used for component registration.</summary>
    public IReadOnlyList<SparsePayloadSchemaIdentity> SchemaIdentities { get; init; } = Array.Empty<SparsePayloadSchemaIdentity>();

    /// <summary>Gets or initializes types whose schemas must be registered separately.</summary>
    public IReadOnlyList<Type> RequiredExternalSchemaTypes { get; init; } = Array.Empty<Type>();

    /// <summary>Gets or initializes generated child-model schema bundles included in the result.</summary>
    public IReadOnlyList<Func<SparsePayloadSchemaBundle>> Dependencies { get; init; } = Array.Empty<Func<SparsePayloadSchemaBundle>>();
}
