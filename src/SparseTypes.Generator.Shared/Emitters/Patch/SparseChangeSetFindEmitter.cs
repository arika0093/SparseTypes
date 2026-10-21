using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseTypes.Generator.Shared;

internal static class SparseChangeSetFindEmitter
{
    internal static void AppendShellStub(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        SparseOperationTarget target,
        string recordType
    )
    {
        var pathType = SparseFragmentPatchEmitter.GetPathType(dialect);
        code.AppendLine(
            "/// <summary>Finds the entry at exactly the given path, or null when absent.</summary>"
        );
        code.AppendLine(
            "/// <remarks>Exact paths only; ancestors and descendants never match.</remarks>"
        );
        code.AppendLine(
            "public "
                + recordType
                + "? Find("
                + pathType
                + " path) => "
                + target.ChangeSetOperationsType
                + ".Find(this, path);"
        );
        if (modelType is not null)
        {
            code.AppendLine(
                "/// <summary>Finds the entry at exactly the given typed path, or null when absent.</summary>"
            );
            code.AppendLine(
                "/// <remarks>Exact paths only; ancestors and descendants never match.</remarks>"
            );
            code.AppendLine(
                "public "
                    + recordType
                    + "? Find<TValue>("
                    + SparseFragmentPatchEmitter.GetTypedPathType(dialect, modelType, "TValue")
                    + " path) => "
                    + target.ChangeSetOperationsType
                    + ".Find(this, path);"
            );
        }
    }

    internal static void Append(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        ImmutableArray<SparseMemberModel> members,
        Dictionary<int, string> propertyNames,
        string recordType,
        SparseOperationTarget? target
    )
    {
        var pathType = SparseFragmentPatchEmitter.GetPathType(dialect);
        var receiver = target is null ? string.Empty : "ChangeSet self, ";
        var enumerate = target is null ? "EnumerateChanges()" : "EnumerateChanges(self)";
        var visibility = target is null ? "public" : "internal static";
        code.AppendLine(
            "/// <summary>Finds the entry at exactly the given path, or null when absent.</summary>"
        );
        code.AppendLine(
            "/// <remarks>Exact paths only; ancestors and descendants never match.</remarks>"
        );
        code.AppendLine(visibility + " " + recordType + "? Find(" + receiver + pathType + " path)");
        code.OpenBlock();
        if (!dialect.PathIsValueType)
        {
            code.AppendLine(
                "if (path is null) throw new global::System.ArgumentNullException(nameof(path));"
            );
        }
        code.AppendLine(
            "if (" + (target is null ? string.Empty : "self.") + "IsEmpty) return null;"
        );
        AppendScalarLookup(code, members, propertyNames, modelType, target, recordType);
        code.AppendLine("foreach (var change in " + enumerate + ")");
        code.OpenBlock();
        code.AppendLine("if (change.Path.Equals(path)) return change;");
        code.CloseBlock();
        code.AppendLine("return null;");
        code.CloseBlock();
        if (modelType is null)
        {
            return;
        }

        code.AppendLine(
            "/// <summary>Finds the entry at exactly the given typed path, or null when absent.</summary>"
        );
        code.AppendLine(
            "/// <remarks>Exact paths only; ancestors and descendants never match. The path root and value types are checked at compile time.</remarks>"
        );
        code.AppendLine(
            visibility
                + " "
                + recordType
                + "? Find<TValue>("
                + receiver
                + SparseFragmentPatchEmitter.GetTypedPathType(dialect, modelType, "TValue")
                + " path)"
        );
        code.OpenBlock();
        if (!dialect.PathIsValueType)
        {
            code.AppendLine(
                "if (path is null) throw new global::System.ArgumentNullException(nameof(path));"
            );
        }
        code.AppendLine(
            target is null ? "return Find(path.Path);" : "return Find(self, path.Path);"
        );
        code.CloseBlock();
    }

    private static void AppendScalarLookup(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        Dictionary<int, string> propertyNames,
        string? modelType,
        SparseOperationTarget? target,
        string recordType
    )
    {
        if (modelType is null || !members.Any(IsDirectLookupEntry))
        {
            return;
        }
        var receiver = target is null ? string.Empty : "self.";
        code.AppendLine(
            "if (path.Depth == 1 && !"
                + receiver
                + "__sparse_hasWhole && path.RootType == typeof("
                + modelType
                + "))"
        );
        code.OpenBlock();
        code.AppendLine("switch (path.Segments[0].Name)");
        code.OpenBlock();
        var onlyDirectEntries = true;
        foreach (var member in members)
        {
            if (!IsDirectLookupEntry(member))
            {
                onlyDirectEntries = false;
                continue;
            }
            var name = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(
                member.Property.Name,
                true
            );
            var property = receiver + SparseNaming.EscapeIdentifier(propertyNames[member.Id]);
            code.AppendLine("case " + name + ":");
            code.OpenBlock();
            if (IsSet(member))
            {
                code.AppendLine("var value = " + property + ";");
                code.AppendLine("if (!value.IsChanged) return null;");
                code.AppendLine(
                    "return !value.Before.IsPresent || !value.After.IsPresent || (object?)value.Before.Value is null || (object?)value.After.Value is null ? __SparseCreateChangeInfo(path, value.Before, value.After) : ("
                        + recordType
                        + "?)null;"
                );
            }
            else
            {
                code.AppendLine("var value = " + property + ";");
                code.AppendLine(
                    "return value.IsChanged ? __SparseCreateChangeInfo(path, value.Before, value.After) : ("
                        + recordType
                        + "?)null;"
                );
            }
            code.CloseBlock();
        }
        if (onlyDirectEntries)
        {
            code.AppendLine("default: return null;");
        }
        code.CloseBlock();
        code.CloseBlock();
    }

    private static bool IsDirectLookupEntry(SparseMemberModel member) =>
        IsSet(member) || IsScalarChangeEntry(member);

    private static bool IsScalarChangeEntry(SparseMemberModel member) =>
        IsScalar(member) && !IsSet(member);
}
