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
    /// <param name="jsonSchema">A JSON Schema object encoded as JSON.</param>
    public SparsePayloadSchema(Type type, string name, string jsonSchema)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("A schema name is required.", nameof(name))
            : name;
        JsonSchema = string.IsNullOrWhiteSpace(jsonSchema)
            ? throw new ArgumentException("A JSON schema is required.", nameof(jsonSchema))
            : jsonSchema;
    }

    /// <summary>Gets the represented CLR type.</summary>
    public Type Type { get; }

    /// <summary>Gets the stable preferred component name.</summary>
    public string Name { get; }

    /// <summary>Gets the JSON Schema document for the represented type.</summary>
    public string JsonSchema { get; }
}

/// <summary>Provides generated JSON schemas for a ChangePayload and its nested payload DTOs.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Advanced)]
public interface ISparsePayloadSchemaProvider
{
    /// <summary>Gets the schema descriptors generated for this payload.</summary>
    IReadOnlyList<SparsePayloadSchema> GetSparsePayloadSchemas();
}

/// <summary>Creates deterministic OpenAPI component names for CLR types.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Advanced)]
public static class SparsePayloadSchemaNames
{
    /// <summary>Gets a stable component name for a CLR type.</summary>
    /// <param name="type">The type to name.</param>
    /// <returns>A deterministic name derived from the type and its containing types.</returns>
    public static string GetName(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var names = new Stack<string>();
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            var name = RemoveGeneratedHash(current.Name);
            if (!name.StartsWith("__Internal", StringComparison.Ordinal))
                names.Push(name);
        }

        if (!string.IsNullOrEmpty(type.Namespace))
            names.Push(type.Namespace);
        var result = string.Join("_", names).Replace('.', '_');
        if (!type.IsGenericType)
            return result;

        var arguments = type.GetGenericArguments();
        if (type.IsGenericTypeDefinition)
            arguments = Array.Empty<Type>();
        if (arguments.Length == 0)
            return result;

        var builder = new System.Text.StringBuilder(result);
        foreach (var argument in arguments)
            builder.Append('_').Append(GetName(argument));
        return builder.ToString();
    }

    private static string RemoveGeneratedHash(string name)
    {
        var separator = name.LastIndexOf('_');
        var arity = name.IndexOf('`');
        if (arity >= 0)
            name = name[..arity];
        separator = name.LastIndexOf('_');
        if (separator < 0 || name.Length - separator != 9)
            return name;
        for (var index = separator + 1; index < name.Length; index++)
        {
            if (!Uri.IsHexDigit(name[index]))
                return name;
        }
        return name[..separator];
    }
}
