using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SparseTypes.Generator.Shared;

internal sealed class SparseModelIdentityEqualityAnalyzer
{
    private readonly SparseGeneratorConfig _config;
    private readonly CancellationToken _cancellationToken;
    private readonly Dictionary<INamedTypeSymbol, bool> _modelCloneEquality = new(SymbolEqualityComparer.Default);

    internal SparseModelIdentityEqualityAnalyzer(SparseGeneratorConfig config, CancellationToken cancellationToken)
    {
        _config = config;
        _cancellationToken = cancellationToken;
    }

    internal bool ModelClonePreservesEquality(INamedTypeSymbol model) =>
        ModelClonePreservesEquality(model, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default));

    private bool ClonePreservesEquality(ITypeSymbol type, HashSet<INamedTypeSymbol> activeModels)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var collection = SparseCollectionAnalyzer.GetCollectionInfo(type);
        if (collection.CloneKind == SparseCloneCollectionKind.Dictionary)
        {
            return collection.ValueType is not null
                && DictionaryKeysSurviveClone(collection.ElementType)
                && ClonePreservesEquality(collection.ValueType, activeModels);
        }

        if (collection.CloneKind == SparseCloneCollectionKind.Set)
        {
            return SetElementsSurviveClone(collection.ElementType);
        }

        if (collection.CloneKind is SparseCloneCollectionKind.Array or SparseCloneCollectionKind.List)
        {
            return ClonePreservesEquality(collection.ElementType, activeModels);
        }

        if (IsGeneratedModel(type, out var model))
        {
            return ModelClonePreservesEquality(model, activeModels);
        }

        // Structural POCOs are deep-cloned, and their comparer may observe the
        // resulting identities even when both source members share one object.
        if (
            type is INamedTypeSymbol
            && SparseModelDiscovery.ClassifyStructuralType(type, _config, _cancellationToken)
                == SparseModelDiscovery.StructuralTypeKind.StructuralObject
        )
        {
            return false;
        }

        return type.IsReferenceType || SparseModelDiscovery.UsesDefaultScalarEquality(type);
    }

    private bool ModelClonePreservesEquality(INamedTypeSymbol model, HashSet<INamedTypeSymbol> activeModels)
    {
        if (!IsGeneratedModel(model, out _))
        {
            return false;
        }

        if (_modelCloneEquality.TryGetValue(model, out var cached))
        {
            return cached;
        }

        if (!activeModels.Add(model))
        {
            // Cyclic object graphs need graph-aware equality, which the
            // generated member comparer does not promise.
            return false;
        }

        try
        {
            if (!HasSafeCloneConstructors(model))
            {
                _modelCloneEquality[model] = false;
                return false;
            }

            foreach (var member in SparseModelDiscovery.GetMembers(model, _config, _cancellationToken))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (
                    member.Property.SetMethod is not { DeclaredAccessibility: Accessibility.Public, IsInitOnly: false }
                    || !IsAutoProperty(member.Property, _cancellationToken)
                    || !HasDefaultComparison(member)
                    || !ClonePreservesEquality(member.Property.Type, activeModels)
                )
                {
                    _modelCloneEquality[model] = false;
                    return false;
                }
            }

            _modelCloneEquality[model] = true;
            return true;
        }
        finally
        {
            activeModels.Remove(model);
        }
    }

    private static bool HasSafeCloneConstructors(INamedTypeSymbol model)
    {
        for (var current = model; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            if (current.InstanceConstructors.Any(static constructor => !constructor.IsImplicitlyDeclared))
            {
                return false;
            }
        }

        return true;
    }

    private bool DictionaryKeysSurviveClone(ITypeSymbol keyType)
    {
        if (IsGeneratedModel(keyType, out _))
        {
            return false;
        }

        if (
            keyType is INamedTypeSymbol
            && SparseModelDiscovery.ClassifyStructuralType(keyType, _config, _cancellationToken)
                == SparseModelDiscovery.StructuralTypeKind.StructuralObject
        )
        {
            return false;
        }

        return keyType.IsReferenceType || SparseModelDiscovery.UsesDefaultScalarEquality(keyType);
    }

    private bool SetElementsSurviveClone(ITypeSymbol elementType)
    {
        if (IsGeneratedModel(elementType, out _))
        {
            return false;
        }

        if (
            elementType is INamedTypeSymbol
            && SparseModelDiscovery.ClassifyStructuralType(elementType, _config, _cancellationToken)
                == SparseModelDiscovery.StructuralTypeKind.StructuralObject
        )
        {
            return false;
        }

        return elementType.IsReferenceType || SparseModelDiscovery.UsesDefaultScalarEquality(elementType);
    }

    private bool IsGeneratedModel(ITypeSymbol type, out INamedTypeSymbol model)
    {
        if (
            type is INamedTypeSymbol named
            && (
                SparseModelDiscovery.IsFragmentModel(named, _config, _cancellationToken)
                || SparsePromotedDiscovery.IsPromotablePartial(named, _config, _cancellationToken)
            )
        )
        {
            model = named;
            return true;
        }

        model = null!;
        return false;
    }

    private static bool HasDefaultComparison(SparseSymbolMemberModel member) =>
        member.MergeStrategyType is null && member.ComparisonComparerType is null && member.RebasePolicyType is null;

    private static bool IsAutoProperty(IPropertySymbol property, CancellationToken cancellationToken)
    {
        var getter = property.GetMethod;
        var setter = property.SetMethod;
        if (
            property.IsStatic
            || getter is null
            || setter is null
            || getter.IsVirtual
            || getter.IsOverride
            || getter.IsAbstract
            || getter.IsExtern
            || setter.IsVirtual
            || setter.IsOverride
            || setter.IsAbstract
            || setter.IsExtern
        )
        {
            return false;
        }

        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            if (
                reference.GetSyntax(cancellationToken) is PropertyDeclarationSyntax declaration
                && declaration.ExpressionBody is null
                && declaration.AccessorList?.Accessors.Any(static accessor =>
                    accessor.Keyword.ValueText == "get" && accessor.Body is null && accessor.ExpressionBody is null
                ) == true
                && declaration.AccessorList.Accessors.Any(static accessor =>
                    accessor.Keyword.ValueText == "set" && accessor.Body is null && accessor.ExpressionBody is null
                )
            )
            {
                return true;
            }
        }

        return false;
    }
}
