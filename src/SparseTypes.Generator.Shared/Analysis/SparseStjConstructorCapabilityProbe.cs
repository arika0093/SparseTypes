using Microsoft.CodeAnalysis;

namespace SparseTypes.Generator.Shared;

/// <summary>Checks consumer constructor signatures for optional trailing arguments.</summary>
internal static class SparseStjConstructorCapabilityProbe
{
    /// <summary>Checks whether a constructor accepts the required prefix and optional suffix.</summary>
    /// <param name="type">The type whose constructors are inspected.</param>
    /// <param name="compilation">The consuming compilation.</param>
    /// <param name="requiredParameters">The required leading parameter types.</param>
    /// <returns><see langword="true"/> when an accessible constructor can be called with the prefix.</returns>
    public static bool HasAccessibleConstructorWithOptionalSuffix(
        INamedTypeSymbol type,
        Compilation compilation,
        ITypeSymbol[] requiredParameters
    )
    {
        foreach (var ctor in type.InstanceConstructors)
        {
            if (!IsAccessibleTo(ctor, compilation))
            {
                continue;
            }

            if (ctor.Parameters.Length < requiredParameters.Length)
            {
                continue;
            }

            var matches = true;
            for (var index = 0; index < ctor.Parameters.Length; index++)
            {
                if (index < requiredParameters.Length)
                {
                    var parameter = ctor.Parameters[index];
                    if (
                        parameter.RefKind != RefKind.None
                        || !SymbolEqualityComparer.Default.Equals(
                            parameter.Type,
                            requiredParameters[index]
                        )
                    )
                    {
                        matches = false;
                        break;
                    }
                }
                else if (!ctor.Parameters[index].IsOptional)
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAccessibleTo(ISymbol symbol, Compilation compilation)
    {
        try
        {
            return compilation.IsSymbolAccessibleWithin(symbol, compilation.Assembly);
        }
        catch
        {
            return false;
        }
    }
}
