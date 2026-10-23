using System;
using System.Collections.Generic;

namespace SparseTypes;

/// <summary>Describes one generated payload JSON schema.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Advanced)]
public sealed class SparsePayloadSchema
{
    /// <summary>Creates a schema descriptor.</summary>
    /// <param name="type">CLR type represented by the schema.</param>
    /// <param name="name">Stable preferred component name.</param>
    /// <param name="jsonSchema">A JSON Schema object encoded as JSON. Used for both OpenAPI dialects.</param>
    public SparsePayloadSchema(Type type, string name, string jsonSchema)
        : this(type, name, jsonSchema, jsonSchema) { }

    /// <summary>Creates a schema descriptor with separate OpenAPI 3.0 and 3.1 forms.</summary>
    /// <param name="type">CLR type represented by the schema.</param>
    /// <param name="name">Stable preferred component name.</param>
    /// <param name="openApi30JsonSchema">The OpenAPI 3.0 JSON Schema object.</param>
    /// <param name="openApi31JsonSchema">The OpenAPI 3.1 JSON Schema object.</param>
    public SparsePayloadSchema(Type type, string name, string openApi30JsonSchema, string openApi31JsonSchema)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("A schema name is required.", nameof(name)) : name;
        OpenApi30JsonSchema = string.IsNullOrWhiteSpace(openApi30JsonSchema)
            ? throw new ArgumentException("An OpenAPI 3.0 JSON schema is required.", nameof(openApi30JsonSchema))
            : openApi30JsonSchema;
        OpenApi31JsonSchema = string.IsNullOrWhiteSpace(openApi31JsonSchema)
            ? throw new ArgumentException("An OpenAPI 3.1 JSON schema is required.", nameof(openApi31JsonSchema))
            : openApi31JsonSchema;
    }

    /// <summary>Gets the represented CLR type.</summary>
    public Type Type { get; }

    /// <summary>Gets the stable preferred component name.</summary>
    public string Name { get; }

    /// <summary>Gets the JSON Schema document for the represented type.</summary>
    public string JsonSchema => OpenApi31JsonSchema;

    /// <summary>Gets the OpenAPI 3.0 JSON Schema document.</summary>
    public string OpenApi30JsonSchema { get; }

    /// <summary>Gets the OpenAPI 3.1 JSON Schema document.</summary>
    public string OpenApi31JsonSchema { get; }

    /// <summary>Gets the JSON Schema document selected for a bundle dialect.</summary>
    /// <param name="dialect">The OpenAPI dialect requested by the consumer.</param>
    /// <returns>The dialect-specific JSON Schema document.</returns>
    public string GetJsonSchema(SparsePayloadSchemaDialect dialect) =>
        dialect switch
        {
            SparsePayloadSchemaDialect.OpenApi30 => OpenApi30JsonSchema,
            SparsePayloadSchemaDialect.OpenApi31 => OpenApi31JsonSchema,
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "Unsupported OpenAPI schema dialect."),
        };

    internal SparsePayloadSchema ForDialect(SparsePayloadSchemaDialect dialect)
    {
        var jsonSchema = GetJsonSchema(dialect);
        return new SparsePayloadSchema(Type, Name, jsonSchema);
    }
}

/// <summary>Identifies an OpenAPI schema dialect supported by SparseTypes.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Advanced)]
public enum SparsePayloadSchemaDialect
{
    /// <summary>OpenAPI 3.0 schema rules.</summary>
    OpenApi30,

    /// <summary>OpenAPI 3.1 schema rules.</summary>
    OpenApi31,
}

/// <summary>Identifies a generated schema component without constructing its JSON document.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Advanced)]
public sealed class SparsePayloadSchemaIdentity
{
    /// <summary>Creates a schema identity.</summary>
    /// <param name="type">The CLR type represented by the schema.</param>
    /// <param name="name">The stable component name.</param>
    public SparsePayloadSchemaIdentity(Type type, string name)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("A schema name is required.", nameof(name)) : name;
    }

    /// <summary>Gets the CLR type represented by the schema.</summary>
    public Type Type { get; }

    /// <summary>Gets the stable component name.</summary>
    public string Name { get; }
}

/// <summary>Creates deterministic OpenAPI component names for CLR types.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Advanced)]
public static class SparsePayloadSchemaNames
{
    /// <summary>Gets a stable component name for a CLR type.</summary>
    /// <param name="type">The type to name.</param>
    /// <returns>A deterministic name derived from the type and its containing types.</returns>
    public static string GetName(Type type) => GetName(type, resolve: null);

    /// <summary>
    /// Gets a stable component name for a CLR type, preferring caller-supplied names.
    /// </summary>
    /// <param name="type">The type to name.</param>
    /// <param name="resolve">
    /// An optional lookup consulted before the default naming rules. The lookup
    /// receives the exact type (including generic arguments as declared) and
    /// returns the preferred name, or <see langword="null"/> to fall back to
    /// the default rules. The lookup is consulted recursively for generic
    /// arguments so registered element types keep their preferred names.
    /// </param>
    /// <returns>
    /// The resolved name when <paramref name="resolve"/> supplies one;
    /// otherwise a deterministic name derived from the type and its
    /// containing types.
    /// </returns>
    public static string GetName(Type type, Func<Type, string?>? resolve)
    {
        ArgumentNullException.ThrowIfNull(type);
        var resolved = resolve?.Invoke(type);
        if (resolved is { Length: > 0 })
        {
            return resolved;
        }

        var names = new Stack<string>();
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            var name = RemoveGeneratedHash(current.Name);
            if (!name.StartsWith("__Internal", StringComparison.Ordinal))
            {
                names.Push(name);
            }
        }

        if (!string.IsNullOrEmpty(type.Namespace))
        {
            names.Push(type.Namespace);
        }

        var result = string.Join("_", names).Replace('.', '_');
        if (!type.IsGenericType)
        {
            return result;
        }

        var arguments = type.GetGenericArguments();
        if (type.IsGenericTypeDefinition)
        {
            arguments = Array.Empty<Type>();
        }

        if (arguments.Length == 0)
        {
            return result;
        }

        var builder = new System.Text.StringBuilder(result);
        foreach (var argument in arguments)
        {
            builder.Append('_').Append(GetName(argument, resolve));
        }

        return builder.ToString();
    }

    private static string RemoveGeneratedHash(string name)
    {
        var separator = name.LastIndexOf('_');
        var arity = name.IndexOf('`');
        if (arity >= 0)
        {
            name = name[..arity];
        }

        separator = name.LastIndexOf('_');
        if (separator < 0 || name.Length - separator != 9)
        {
            return name;
        }

        for (var index = separator + 1; index < name.Length; index++)
        {
            if (!Uri.IsHexDigit(name[index]))
            {
                return name;
            }
        }
        return name[..separator];
    }
}
