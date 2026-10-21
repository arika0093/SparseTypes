using System.Collections.Immutable;

namespace SparseTypes.Generator.Shared;

internal static class SparseFragmentOriginPathCacheEmitter
{
    internal static void Append(SharedIndentedBuilder code, ImmutableArray<SparseMemberModel> members, string modelType, string pathType)
    {
        code.AppendLine("private static readonly " + pathType + " __SparseOriginRootPath = " + pathType + ".Root(typeof(" + modelType + "));");
        foreach (var member in members)
        {
            code.AppendLine(
                "private static readonly "
                    + pathType
                    + " __SparseOriginMemberPath"
                    + member.Id
                    + " = __SparseOriginRootPath.Member("
                    + Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(member.Property.Name, true)
                    + ");"
            );
        }
    }
}
