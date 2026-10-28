using System.Reflection;

namespace SparseTypes.EntityFrameworkCore.Mapping;

internal static class ProspectiveEntityFactory
{
    internal static TEntity Create<TEntity>(Func<TEntity> factory, string path, string dtoName)
        where TEntity : class
    {
        try
        {
            return factory()
                ?? throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.NestedNotCreatable,
                    path,
                    $"The nested entity for '{dtoName}' is null and its parameterless constructor returned null."
                );
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not (OperationCanceledException or OutOfMemoryException))
        {
            // Constructor failures are scoped validation errors; their messages
            // may contain model data and do not belong in collected diagnostics.
            throw new SparseEntityMappingException(
                new SparseEntityMappingError(
                    SparseEntityMappingFailure.NestedNotCreatable,
                    path,
                    $"The nested entity for '{dtoName}' could not be constructed during preflight.",
                    typeof(TEntity).FullName
                ),
                exception
            );
        }
    }
}
