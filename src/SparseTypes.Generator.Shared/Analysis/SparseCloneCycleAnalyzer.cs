using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace SparseTypes.Generator.Shared;

/// <summary>
/// Analyzes unregisterable clone cycles for one analysis context.
/// Detected segments stay identical to the former recursive path walk;
/// completed nodes remember their result so shared acyclic subgraphs are
/// expanded once instead of once per incoming path.
/// </summary>
internal sealed class SparseCloneCycleAnalyzer
{
    private readonly SparseGeneratorConfig _config;
    private readonly CancellationToken _cancellationToken;

    // Registerability is a property of the cycles themselves, not of the
    // entry path, so completed results are safe to reuse from any context.
    private readonly Dictionary<INamedTypeSymbol, bool> _completed = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, bool> _preRegistrationCycles = new(SymbolEqualityComparer.Default);
    private readonly List<INamedTypeSymbol> _path = new();

    public SparseCloneCycleAnalyzer(SparseGeneratorConfig config, CancellationToken cancellationToken)
    {
        _config = config;
        _cancellationToken = cancellationToken;
    }

    public bool ContainsUnregisterableCloneCycle(ITypeSymbol type)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (type is IArrayTypeSymbol array)
        {
            return ContainsUnregisterableCloneCycle(array.ElementType);
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return ContainsUnregisterableCloneCycle(named.TypeArguments[0]);
        }

        var collection = SparseCollectionAnalyzer.GetCollectionInfo(named);
        if (collection.CloneKind != SparseCloneCollectionKind.Unsupported)
        {
            if (collection.ElementType is not null && ContainsUnregisterableCloneCycle(collection.ElementType))
            {
                return true;
            }

            return collection.ValueType is not null && ContainsUnregisterableCloneCycle(collection.ValueType);
        }
        if (
            !SparseModelDiscovery.IsFragmentModel(named, _config, _cancellationToken)
            && SparseModelDiscovery.ClassifyStructuralType(named, _config, _cancellationToken)
                != SparseModelDiscovery.StructuralTypeKind.StructuralObject
        )
        {
            return false;
        }

        return AnalyzeCloneNode(named);
    }

    private bool AnalyzeCloneNode(INamedTypeSymbol named)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (_completed.TryGetValue(named, out var completed))
        {
            return completed;
        }

        var cycleStart = _path.FindIndex(candidate => SymbolEqualityComparer.Default.Equals(candidate, named));
        if (cycleStart >= 0)
        {
            // A back edge to an active node: evaluate exactly the segment the
            // former path walk would have evaluated, then return without
            // re-expanding children that are already on the stack.
            for (var index = cycleStart; index < _path.Count; index++)
            {
                if (HasPreRegistrationCloneCycle(_path[index]))
                {
                    return true;
                }
            }

            return false;
        }

        _path.Add(named);
        var result = false;
        foreach (var property in SparseModelDiscovery.GetMembers(named, _config, _cancellationToken).Select(static member => member.Property))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (
                property
                    .GetAttributes()
                    .Any(attribute => attribute.AttributeClass?.ToDisplayString() == _config.CloneReferenceSafeAttributeMetadataName)
            )
            {
                continue;
            }

            if (ContainsUnregisterableCloneCycle(property.Type))
            {
                result = true;
                break;
            }
        }
        //pop even on the early-true unwind: this list is shared by every root
        //property of the context, unlike the former per-call fresh list.
        _path.RemoveAt(_path.Count - 1);
        _completed.Add(named, result);
        return result;
    }

    /// <summary>Context-scoped memoized form of the pre-registration check.</summary>
    private bool HasPreRegistrationCloneCycle(INamedTypeSymbol model)
    {
        if (_preRegistrationCycles.TryGetValue(model, out var cached))
        {
            return cached;
        }

        var result = ComputePreRegistrationCloneCycle(model);
        _preRegistrationCycles.Add(model, result);
        return result;
    }

    private bool ComputePreRegistrationCloneCycle(INamedTypeSymbol model)
    {
        var fragmentModel = SparseModelDiscovery.IsFragmentModel(model, _config, _cancellationToken);
        var members = SparseModelDiscovery.GetMembers(model, _config, _cancellationToken).ToImmutableArray();
        if (model.TypeKind == TypeKind.Struct)
        {
            return members.Any(member => CanReachCloneType(member.Property.Type, model));
        }

        var constructor = fragmentModel
            ? ModelConstructorBinding.AnalyzeRoot(model, _config, _cancellationToken)
            : ModelConstructorBinding.AnalyzeStructural(model, _config, _cancellationToken);
        if (constructor is null)
        {
            return false;
        }

        IEnumerable<SparseSymbolMemberModel> preRegistrationMembers;
        if (fragmentModel)
        {
            var generatedMembers = SparseModelDiscovery.CreateMemberModels(members, _config, _cancellationToken, model);
            if (constructor.Parameters.IsEmpty && ModelConstructionPlan.ForMembers(generatedMembers).CanOverlayAfterConstruction)
            {
                return false;
            }

            preRegistrationMembers = members;
        }
        else
        {
            if (constructor.Parameters.IsEmpty)
            {
                return false;
            }

            var boundNames = new HashSet<string>(constructor.Parameters.Select(static parameter => parameter.PropertyName), StringComparer.Ordinal);
            preRegistrationMembers = members.Where(member => boundNames.Contains(member.Property.Name));
        }

        return preRegistrationMembers.Any(member => CanReachCloneType(member.Property.Type, model));
    }

    private bool CanReachCloneType(ITypeSymbol type, INamedTypeSymbol target)
    {
        var pending = new Stack<ITypeSymbol>();
        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        pending.Push(type);
        while (pending.Count > 0)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            if (SymbolEqualityComparer.Default.Equals(current, target))
            {
                return true;
            }

            if (!visited.Add(current))
            {
                continue;
            }

            foreach (var (nested, referenceSafe) in SparseCloneAnalysis.GetCloneChildren(current, _config, _cancellationToken))
            {
                if (!referenceSafe)
                {
                    pending.Push(nested);
                }
            }
        }
        return false;
    }
}
