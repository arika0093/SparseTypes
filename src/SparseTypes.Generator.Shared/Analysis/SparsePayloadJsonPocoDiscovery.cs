using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace SparseTypes.Generator.Shared;

/// <summary>Discovers ordinary custom leaf types needing generated payload JSON codecs.</summary>
/// <remarks>Walks endpoint value types transitively with cycle detection; unsupported shapes report an advisory diagnostic and fall back to external metadata.</remarks>
internal static class SparsePayloadJsonPocoDiscovery
{
    /// <summary>Whether payload JSON leaf discovery applies to one configuration.</summary>
    /// <remarks>Non-JSON products skip the traversal entirely: no codecs, no diagnostics, byte-identical output.</remarks>
    /// <param name="config">Owning generator configuration.</param>
    /// <returns>True when change-payload JSON resolvers are emitted.</returns>
    public static bool IsEnabledFor(SparseGeneratorConfig config) =>
        config.EffectiveEmissionFeatures.EmitChangePayload
        && config.PatchDialect is { EmitJson: true }
        && config.PatchDialect.Value.EffectiveStjCapabilities.SupportsPayloadJsonResolver;

    private const string JsonConverterAttribute =
        "System.Text.Json.Serialization.JsonConverterAttribute";
    private const string JsonPolymorphicAttribute =
        "System.Text.Json.Serialization.JsonPolymorphicAttribute";
    private const string JsonDerivedTypeAttribute =
        "System.Text.Json.Serialization.JsonDerivedTypeAttribute";
    private const string JsonExtensionDataAttribute =
        "System.Text.Json.Serialization.JsonExtensionDataAttribute";
    private const string JsonIncludeAttribute =
        "System.Text.Json.Serialization.JsonIncludeAttribute";
    private const string JsonRequiredAttribute =
        "System.Text.Json.Serialization.JsonRequiredAttribute";

    /// <summary>Collects JSON codec models reachable from one model's members.</summary>
    /// <param name="members">Analyzed symbol members of the owning model.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Supported codec models with advisory diagnostics for the rest.</returns>
    public static (
        ImmutableArray<SparsePayloadJsonPocoModel> Pocos,
        ImmutableArray<SparseGeneratorDiagnostic> Diagnostics
    ) Collect(
        ImmutableArray<SparseSymbolMemberModel> members,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var pocos = ImmutableArray.CreateBuilder<SparsePayloadJsonPocoModel>();
        var warnings = new SortedDictionary<string, SparseGeneratorDiagnostic>(
            StringComparer.Ordinal
        );
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var valueReached = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<(ITypeSymbol Type, bool IsValue)>();
        foreach (var member in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pending.Push((member.Property.Type, member.ChildModel is null));
        }

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (type, isValue) = pending.Pop();
            var unwrapped = UnwrapNullable(type);
            var collection = SparseCollectionAnalyzer.GetCollectionInfo(unwrapped);
            if (collection.CloneKind != SparseCloneCollectionKind.Unsupported)
            {
                if (collection.CloneKind == SparseCloneCollectionKind.Dictionary)
                {
                    // Dictionary keys travel as strings, never as values.
                    pending.Push((collection.ElementType, false));
                    if (collection.ValueType is not null)
                        pending.Push((collection.ValueType, isValue));
                }
                else
                {
                    pending.Push((collection.ElementType, isValue));
                    if (collection.ValueType is not null)
                        pending.Push((collection.ValueType, isValue));
                }

                continue;
            }

            if (unwrapped is not INamedTypeSymbol named)
                continue;
            var key = StripNullability(named);
            if (isValue)
                valueReached.Add(key);
            if (!seen.Add(key))
                continue;
            if (IsDirectScalar(key))
                continue;
            var isPocoOwner = !IsFragmentOrPromoted(key, config, cancellationToken);
            foreach (var nested in GetNestedTypes(key, isPocoOwner, config, cancellationToken))
                pending.Push(nested);
        }

        // Codecs cover value positions only: nested-only children travel
        // through cores, keeping edits to them from invalidating this model.
        foreach (
            var reached in valueReached.OrderBy(
                static type => type.ToDisplayString(),
                StringComparer.Ordinal
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reached is not INamedTypeSymbol named)
                continue;
            if (IsDirectScalar(named))
                continue;
            if (!TryCreatePoco(named, config, cancellationToken, out var poco, out var reason))
            {
                if (IsUserDefinedLeaf(named) && reason is not null)
                    warnings[reason] = new SparseGeneratorDiagnostic(
                        config.EffectiveDiagnosticIds.UnsupportedPayloadJsonPoco,
                        named.Locations.FirstOrDefault(),
                        reason
                    )
                    {
                        IsWarning = true,
                    };
                continue;
            }

            pocos.Add(poco);
        }

        pocos.Sort(
            static (left, right) => string.CompareOrdinal(left.RuntimeName, right.RuntimeName)
        );
        return (pocos.ToImmutable(), warnings.Values.ToImmutableArray());
    }

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            ? named.TypeArguments[0]
            : type;

    private static INamedTypeSymbol StripNullability(INamedTypeSymbol type) =>
        (INamedTypeSymbol)type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

    private static bool IsFragmentOrPromoted(
        INamedTypeSymbol type,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    ) =>
        SparseModelDiscovery.IsFragmentModel(type, config, cancellationToken)
        || SparsePromotedDiscovery.IsPromotablePartial(type, config, cancellationToken);

    /// <summary>Symbol members for one codec, including fragment-ignored settable extras.</summary>
    /// <remarks>Fragment-ignored members stay out of fragments but travel in whole-model snapshots, matching source-generated model metadata.</remarks>
    private static ImmutableArray<SparseSymbolMemberModel> GetCodecSymbolMembers(
        INamedTypeSymbol type,
        bool includeIgnoredExtras,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var baseMembers = SparseModelDiscovery
            .GetMembers(type, config, cancellationToken)
            .ToArray();
        if (!includeIgnoredExtras)
            return baseMembers.ToImmutableArray();
        var covered = new HashSet<ISymbol>(
            baseMembers.Select(static member => member.Property),
            SymbolEqualityComparer.Default
        );
        var extras = SparseModelDiscovery
            .GetReadableProperties(type, config, cancellationToken, includeSparseIgnored: true)
            .Where(property =>
                !covered.Contains(property)
                && SparseModelDiscovery.IsSparseIgnored(property, config)
                && property.SetMethod?.DeclaredAccessibility == Accessibility.Public
                && !RoslynSymbolCompat.IsRequired(property)
            )
            .Select(
                (property, index) =>
                    new SparseSymbolMemberModel(
                        baseMembers.Length + index,
                        property,
                        null,
                        SparseMergeModes.Default,
                        SparseCollectionAnalyzer.GetCollectionInfo(property.Type),
                        null,
                        false
                    )
            )
            .ToArray();
        return baseMembers.Concat(extras).ToImmutableArray();
    }

    /// <summary>Orders codec members like System.Text.Json: base types first, then declaration order.</summary>
    /// <remarks>Matches the default source-generated wire order instead of the alphabetical fragment order.</remarks>
    private static ImmutableArray<SparseSymbolMemberModel> InDeclarationOrder(
        ImmutableArray<SparseSymbolMemberModel> members,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return members
            .OrderBy(static member => TypeDepth(member.Property.ContainingType))
            .ThenBy(static member => SyntaxPath(member.Property))
            .ThenBy(static member => SyntaxOffset(member.Property))
            .ThenBy(static member => member.Property.Name, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static int TypeDepth(ITypeSymbol? type)
    {
        var depth = 0;
        for (var current = type?.BaseType; current is not null; current = current.BaseType)
            depth++;
        return depth;
    }

    private static string SyntaxPath(IPropertySymbol property) =>
        property.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree.FilePath ?? string.Empty;

    private static int SyntaxOffset(IPropertySymbol property) =>
        property.DeclaringSyntaxReferences.FirstOrDefault()?.Span.Start ?? int.MaxValue;

    private static ImmutableArray<SparsePropertyModel> InPropertyOrder(
        List<(SparsePropertyModel Model, int Depth, string Path, int Offset)> entries,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return entries
            .OrderBy(static entry => entry.Depth)
            .ThenBy(static entry => entry.Path, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Offset)
            .ThenBy(static entry => entry.Model.Name, StringComparer.Ordinal)
            .Select(static entry => entry.Model)
            .ToImmutableArray();
    }

    private static bool IsDirectScalar(INamedTypeSymbol type)
    {
        if (type.SpecialType != SpecialType.None || type.TypeKind == TypeKind.Enum)
            return true;
        return type.ContainingNamespace.ToDisplayString() == "System"
            && type.Name is "Guid" or "DateTime" or "DateTimeOffset" or "TimeSpan" or "Decimal";
    }

    private static bool IsUserDefinedLeaf(INamedTypeSymbol type) =>
        type.SpecialType == SpecialType.None
        && type.TypeKind is TypeKind.Class or TypeKind.Struct
        && !SparseModelDiscovery.IsFrameworkType(type);

    private static IEnumerable<(ITypeSymbol Type, bool IsValue)> GetNestedTypes(
        INamedTypeSymbol poco,
        bool isPocoOwner,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var includeExtras = !isPocoOwner;
        var codecMembers = GetCodecSymbolMembers(poco, includeExtras, config, cancellationToken);
        foreach (var member in codecMembers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return (member.Property.Type, isPocoOwner || member.ChildModel is null);
        }

        var covered = new HashSet<ISymbol>(
            codecMembers.Select(static member => member.Property),
            SymbolEqualityComparer.Default
        );
        foreach (
            var property in SparseModelDiscovery.GetReadableProperties(
                poco,
                config,
                cancellationToken
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!covered.Contains(property))
                yield return (property.Type, true);
        }
    }

    private static bool TryCreatePoco(
        INamedTypeSymbol type,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken,
        out SparsePayloadJsonPocoModel poco,
        out string? reason
    )
    {
        poco = null!;
        reason = null;
        if (
            type.TypeKind is not (TypeKind.Class or TypeKind.Struct)
            || type.IsAbstract
            || type.Arity != 0
            || type.SpecialType != SpecialType.None
            || SparseModelDiscovery.IsFrameworkType(type)
            || !SparseModelDiscovery.IsAccessibleForGeneration(type)
            || IsFileLocal(type)
        )
        {
            reason = DisplayName(type);
            return false;
        }

        if (
            HasAttribute(
                type,
                JsonConverterAttribute,
                JsonPolymorphicAttribute,
                JsonDerivedTypeAttribute
            )
        )
        {
            reason = DisplayName(type);
            return false;
        }

        var constructor = ModelConstructorBinding.AnalyzeStructural(
            type,
            config,
            cancellationToken
        );
        if (constructor is null)
        {
            reason = DisplayName(type);
            return false;
        }

        var includeIgnoredExtras = IsFragmentOrPromoted(type, config, cancellationToken);
        var symbolMembers = InDeclarationOrder(
            GetCodecSymbolMembers(type, includeIgnoredExtras, config, cancellationToken),
            cancellationToken
        );
        var memberModels = SparseModelDiscovery.CreateMemberModels(
            symbolMembers,
            config,
            cancellationToken,
            type
        );
        if (!SparseShapeValidation.FindDuplicateWireNames(memberModels).IsEmpty)
        {
            reason = DisplayName(type);
            return false;
        }

        // Constructor-bound members the codec never reads cannot be constructed.
        var ignored = new HashSet<string>(
            memberModels
                .Where(static member => member.Property.IsJsonIgnored)
                .Select(static member => member.Property.Name),
            StringComparer.Ordinal
        );
        cancellationToken.ThrowIfCancellationRequested();
        if (constructor.Parameters.Any(parameter => ignored.Contains(parameter.PropertyName)))
        {
            reason = DisplayName(type);
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (
            symbolMembers.Any(static member =>
                HasAttribute(
                    member.Property,
                    JsonConverterAttribute,
                    JsonExtensionDataAttribute,
                    JsonRequiredAttribute
                )
            )
        )
        {
            reason = DisplayName(type);
            return false;
        }

        // A required member the codec never writes cannot be constructed.
        if (
            symbolMembers
                .Select(static member => member.Property)
                .Any(property =>
                    RoslynSymbolCompat.IsRequired(property)
                    && SparseJsonNaming.GetJsonIgnoreCondition(property, cancellationToken)
                        == SparseJsonNaming.JsonIgnoreAlways
                )
        )
        {
            reason = DisplayName(type);
            return false;
        }

        if (
            ModelConstructionPlan
                .UnsupportedRequiredMembers(
                    type,
                    symbolMembers.Select(static member => member.Property),
                    config,
                    cancellationToken
                )
                .Any()
        )
        {
            reason = DisplayName(type);
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (
            symbolMembers.Any(static member =>
                member.Property.Type is IArrayTypeSymbol array && array.Rank != 1
            )
        )
        {
            reason = DisplayName(type);
            return false;
        }

        var readable = SparseModelDiscovery
            .GetReadableProperties(type, config, cancellationToken)
            .ToArray();
        var covered = new HashSet<ISymbol>(
            symbolMembers.Select(static member => member.Property),
            SymbolEqualityComparer.Default
        );
        var readOnly = new List<(SparsePropertyModel Model, int Depth, string Path, int Offset)>();
        foreach (var property in readable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (covered.Contains(property))
                continue;
            if (HasAttribute(property, JsonIncludeAttribute, JsonConverterAttribute))
            {
                reason = DisplayName(type);
                return false;
            }

            readOnly.Add(
                (
                    ToReadOnly(property, cancellationToken),
                    TypeDepth(property.ContainingType),
                    SyntaxPath(property),
                    SyntaxOffset(property)
                )
            );
        }

        if (HasIncludedField(type, cancellationToken))
        {
            reason = DisplayName(type);
            return false;
        }

        var memberWires = memberModels
            .Where(static member => !member.Property.IsJsonIgnored)
            .Select(static member => member.Property.JsonPropertyName ?? member.Property.Name);
        var readOnlyWires = readOnly
            .Select(static entry => entry.Model)
            .Where(static property => !property.IsJsonIgnored)
            .Select(static property => property.JsonPropertyName ?? property.Name);
        cancellationToken.ThrowIfCancellationRequested();
        if (
            memberWires
                .Concat(readOnlyWires)
                .GroupBy(static wire => wire, StringComparer.Ordinal)
                .Any(static group => group.Count() > 1)
        )
        {
            reason = DisplayName(type);
            return false;
        }

        poco = new SparsePayloadJsonPocoModel(
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            constructor,
            memberModels,
            InPropertyOrder(readOnly, cancellationToken),
            HasPublicInstanceFields(type, cancellationToken),
            type.IsValueType
        );
        return true;
    }

    private static SparsePropertyModel ToReadOnly(
        IPropertySymbol property,
        CancellationToken cancellationToken
    )
    {
        var wire = SparseJsonNaming.GetJsonPropertyName(
            property,
            cancellationToken,
            out var explicitName
        );
        return new SparsePropertyModel(
            property.Name,
            new SparseTypeModel(
                SparseNaming.TypeName(property.Type),
                SparseNaming.NonNullableTypeName(property.Type),
                property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                property.Type.IsReferenceType,
                false,
                null
            ),
            IsInitOnly: false,
            IsRequired: false,
            IsReadOnly: true,
            JsonPropertyName: wire,
            HasExplicitJsonPropertyName: explicitName,
            JsonIgnoreCondition: SparseJsonNaming.GetJsonIgnoreCondition(
                property,
                cancellationToken
            ),
            IsNullable: SparseModelDiscovery.IsNullableType(property.Type)
        );
    }

    private static bool HasPublicInstanceFields(INamedTypeSymbol type, CancellationToken ct)
    {
        for (
            var current = type;
            current is not null && current.SpecialType != SpecialType.System_Object;
            current = current.BaseType
        )
        {
            ct.ThrowIfCancellationRequested();
            if (
                current
                    .GetMembers()
                    .OfType<IFieldSymbol>()
                    .Any(static field =>
                        !field.IsStatic
                        && !field.IsConst
                        && field.DeclaredAccessibility == Accessibility.Public
                    )
            )
                return true;
        }

        return false;
    }

    private static bool HasIncludedField(INamedTypeSymbol type, CancellationToken ct)
    {
        for (
            var current = type;
            current is not null && current.SpecialType != SpecialType.System_Object;
            current = current.BaseType
        )
        {
            ct.ThrowIfCancellationRequested();
            if (
                current
                    .GetMembers()
                    .OfType<IFieldSymbol>()
                    .Where(static field => !field.IsStatic && !field.IsConst)
                    .SelectMany(static field => field.GetAttributes())
                    .Any(static attribute =>
                        attribute.AttributeClass?.ToDisplayString() == JsonIncludeAttribute
                    )
            )
                return true;
        }

        return false;
    }

    private static bool HasAttribute(ISymbol symbol, params string[] metadataNames) =>
        symbol
            .GetAttributes()
            .Select(static attribute => attribute.AttributeClass?.ToDisplayString())
            .Any(name => name is not null && metadataNames.Contains(name, StringComparer.Ordinal));

    private static bool IsFileLocal(INamedTypeSymbol type)
    {
        foreach (var declaration in type.DeclaringSyntaxReferences)
        {
            if (
                declaration.GetSyntax()
                    is Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax syntax
                && syntax.Modifiers.Any(static modifier =>
                    string.Equals(modifier.Text, "file", System.StringComparison.Ordinal)
                )
            )
                return true;
        }

        return false;
    }

    private static string DisplayName(INamedTypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
