using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseTypes.Generator.Shared;

internal readonly record struct ConstructorParameterBinding(
    string PropertyName,
    string DefaultExpression,
    bool HasExplicitDefaultValue
);

/// <summary>Constructor parameters bound by property name and exact type.</summary>
internal sealed record ModelConstructorBinding(
    ImmutableArray<ConstructorParameterBinding> Parameters,
    bool SetsRequiredMembers = false,
    bool IsImplicitParameterlessClassConstructor = false
)
{
    public static ModelConstructorBinding Parameterless { get; } =
        new(ImmutableArray<ConstructorParameterBinding>.Empty);

    public bool Equals(ModelConstructorBinding? other) =>
        other is not null
        && SetsRequiredMembers == other.SetsRequiredMembers
        && IsImplicitParameterlessClassConstructor == other.IsImplicitParameterlessClassConstructor
        && Parameters.SequenceEqual(other.Parameters);

    public override int GetHashCode()
    {
        var hash =
            (SetsRequiredMembers ? 1 : 0) + (IsImplicitParameterlessClassConstructor ? 2 : 0);
        foreach (var parameter in Parameters)
        {
            hash = unchecked(hash * 31 + parameter.GetHashCode());
        }

        return hash;
    }

    public static ModelConstructorBinding? AnalyzeRoot(
        INamedTypeSymbol model,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    ) => Analyze(model, config, allowNonPublicConstructors: true, cancellationToken);

    public static ModelConstructorBinding? AnalyzeRoot(
        INamedTypeSymbol model,
        CancellationToken cancellationToken
    ) => Analyze(model, null, allowNonPublicConstructors: true, cancellationToken);

    public static ModelConstructorBinding? AnalyzeStructural(
        INamedTypeSymbol model,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    ) => Analyze(model, config, allowNonPublicConstructors: false, cancellationToken);

    public static ModelConstructorBinding? AnalyzeStructural(
        INamedTypeSymbol model,
        CancellationToken cancellationToken
    ) => Analyze(model, null, allowNonPublicConstructors: false, cancellationToken);

    private static ModelConstructorBinding? Analyze(
        INamedTypeSymbol model,
        SparseGeneratorConfig? config,
        bool allowNonPublicConstructors,
        CancellationToken cancellationToken
    )
    {
        var properties = SparseModelDiscovery
            .GetReadableProperties(model, config, cancellationToken, includeSparseIgnored: true)
            .Where(static property =>
                property.SetMethod is null
                || property.SetMethod.DeclaredAccessibility == Accessibility.Public
            )
            .ToArray();
        // Indexed lazily so the parameterless fast path pays nothing.
        Dictionary<string, List<IPropertySymbol>>? propertyIndex = null;
        foreach (
            var constructor in model
                .InstanceConstructors.Where(constructor =>
                    allowNonPublicConstructors
                    || constructor.DeclaredAccessibility == Accessibility.Public
                )
                .OrderBy(static constructor => constructor.Parameters.Length)
                .ThenBy(static constructor => constructor.ToDisplayString(), StringComparer.Ordinal)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var constructorParameters = constructor.Parameters;
            var setsRequiredMembers = constructor
                .GetAttributes()
                .Any(static attribute =>
                    attribute.AttributeClass?.ToDisplayString()
                    == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute"
                );
            if (constructorParameters.IsEmpty)
            {
                if (model.TypeKind == TypeKind.Class && constructor.IsImplicitlyDeclared)
                {
                    return new(
                        ImmutableArray<ConstructorParameterBinding>.Empty,
                        setsRequiredMembers,
                        true
                    );
                }

                return setsRequiredMembers
                    ? new(ImmutableArray<ConstructorParameterBinding>.Empty, true)
                    : Parameterless;
            }
            var parameters = ImmutableArray.CreateBuilder<ConstructorParameterBinding>();
            foreach (var parameter in constructorParameters)
            {
                propertyIndex ??= BuildPropertyIndex(properties);
                var match = FindUniqueMatch(propertyIndex, parameter);
                if (parameter.RefKind != RefKind.None || match is null)
                {
                    break;
                }

                parameters.Add(
                    new ConstructorParameterBinding(
                        match.Name,
                        DefaultExpression(parameter),
                        parameter.HasExplicitDefaultValue
                    )
                );
            }
            if (parameters.Count == constructorParameters.Length)
            {
                return new(parameters.ToImmutable(), setsRequiredMembers);
            }
        }
        return model.IsValueType ? Parameterless : null;
    }

    // Buckets share a case-insensitive name; the parameter type then selects
    // within the bucket, so same-name/different-type candidates still bind
    // while same-name/same-type candidates stay ambiguous.
    private static Dictionary<string, List<IPropertySymbol>> BuildPropertyIndex(
        IPropertySymbol[] properties
    )
    {
        var index = new Dictionary<string, List<IPropertySymbol>>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in properties)
        {
            if (!index.TryGetValue(property.Name, out var bucket))
            {
                index[property.Name] = bucket = new List<IPropertySymbol>(1);
            }

            bucket.Add(property);
        }
        return index;
    }

    private static IPropertySymbol? FindUniqueMatch(
        Dictionary<string, List<IPropertySymbol>> index,
        IParameterSymbol parameter
    )
    {
        if (!index.TryGetValue(parameter.Name, out var bucket))
        {
            return null;
        }

        IPropertySymbol? match = null;
        foreach (var candidate in bucket)
        {
            if (!SymbolEqualityComparer.Default.Equals(parameter.Type, candidate.Type))
            {
                continue;
            }

            if (match is not null)
            {
                return null;
            }

            match = candidate;
        }
        return match;
    }

    private static string DefaultExpression(IParameterSymbol parameter)
    {
        var typeName = SparseNaming.TypeName(parameter.Type);
        if (!parameter.HasExplicitDefaultValue || parameter.ExplicitDefaultValue is null)
        {
            return "default(" + typeName + ")!";
        }

        var value = parameter.ExplicitDefaultValue;
        if (value is string text)
        {
            return SymbolDisplay.FormatLiteral(text, true);
        }

        if (value is char character)
        {
            return SymbolDisplay.FormatLiteral(character, true);
        }

        if (value is bool boolean)
        {
            return boolean ? "true" : "false";
        }

        var literal = Convert.ToString(value, CultureInfo.InvariantCulture)!;
        if (
            value is float specialSingle
            && (float.IsNaN(specialSingle) || float.IsInfinity(specialSingle))
        )
        {
            var field = specialSingle > 0 ? "PositiveInfinity" : "NegativeInfinity";
            if (float.IsNaN(specialSingle))
            {
                field = "NaN";
            }

            return "global::System.Single." + field;
        }
        if (
            value is double specialDouble
            && (double.IsNaN(specialDouble) || double.IsInfinity(specialDouble))
        )
        {
            var field = specialDouble > 0 ? "PositiveInfinity" : "NegativeInfinity";
            if (double.IsNaN(specialDouble))
            {
                field = "NaN";
            }

            return "global::System.Double." + field;
        }
        if (value is float single)
        {
            literal = single.ToString("R", CultureInfo.InvariantCulture) + "F";
        }

        if (value is double number)
        {
            literal = number.ToString("R", CultureInfo.InvariantCulture) + "D";
        }

        if (value is decimal)
        {
            literal += "M";
        }

        return "(" + typeName + ")(" + literal + ")";
    }
}
