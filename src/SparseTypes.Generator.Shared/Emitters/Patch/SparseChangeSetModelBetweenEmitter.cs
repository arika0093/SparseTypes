using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Compares model members that need no projection or cloning.</summary>
internal static class SparseChangeSetModelBetweenEmitter
{
    internal static bool CanUseSharedReferenceEqualityFastPath(ImmutableArray<SparseMemberModel> members) =>
        !members.IsDefaultOrEmpty
        && members.All(static member => member.Property.Type.IsReferenceType && member.CanUseSharedReferenceEqualityFastPath);

    internal static bool CanCompareDirectly(ImmutableArray<SparseMemberModel> members) => members.All(CanCompareMemberDirectly);

    private static bool CanCompareMemberDirectly(SparseMemberModel member)
    {
        if (member.Property.Type.PocoCloneHelperName is not null)
        {
            return false;
        }

        if (IsSet(member) || member.Collection.IsScalarSequence)
        {
            return member.ChildModel is null
                && member.Collection.CloneKind != SparseCloneCollectionKind.Unsupported
                && !member.Collection.ElementType.IsFragmentModel
                && member.Collection.ElementType.PocoCloneHelperName is null;
        }

        return IsScalar(member) && !SparseChangeSetTransitionEmitter.NeedsSnapshot(member);
    }

    internal static void AppendBody(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType,
        string runtime,
        string optionalFragment
    )
    {
        if (modelIsReferenceType)
        {
            code.AppendLine("if (global::System.Object.ReferenceEquals(before, null) || global::System.Object.ReferenceEquals(after, null))");
            code.AppendLine("throw new global::System.ArgumentNullException(\"value\");");
            code.AppendLine("if (global::System.Object.ReferenceEquals(before, after)) return __sparse_empty;");
        }
        foreach (var side in new[] { "before", "after" })
        {
            foreach (var member in members)
            {
                code.AppendLine(
                    "var __"
                        + side
                        + member.Id
                        + " = "
                        + runtime
                        + "Optional<"
                        + FragmentValueType(member)
                        + ">.Present("
                        + side
                        + "."
                        + SparseNaming.EscapeIdentifier(member.Property.Name)
                        + ");"
                );
            }
        }
        var conditions = new List<string>();
        var args = new List<string> { "false", "default", "default" };
        foreach (var member in members)
        {
            var id = member.Id;
            code.AppendLine("bool __h" + id + " = !Fragment.__SparseEqual_" + id + "(__before" + id + ", __after" + id + ");");
            conditions.Add("!__h" + id);
            var beforeValue = "__before" + id;
            var afterValue = "__after" + id;
            if (SparseChangeSetTransitionEmitter.NeedsSnapshot(member))
            {
                beforeValue = "__SparseSnapshot_" + id + "(" + beforeValue + ")";
                afterValue = "__SparseSnapshot_" + id + "(" + afterValue + ")";
            }
            args.Add("__h" + id + " ? " + beforeValue + " : default");
            args.Add("__h" + id + " ? " + afterValue + " : default");
            args.Add("__h" + id);
        }
        if (conditions.Count > 0)
        {
            code.AppendLine("if (" + string.Join(" && ", conditions) + ")");
        }

        code.AppendLine("return Between(default(" + optionalFragment + "), default(" + optionalFragment + "));");
        if (conditions.Count > 0)
        {
            code.AppendLine("return new ChangeSet(" + string.Join(", ", args) + ");");
        }
    }

    internal static void AppendProjectedReturn(
        SharedIndentedBuilder code,
        string optionalFragment,
        bool modelIsReferenceType,
        ImmutableArray<SparseMemberModel> members
    )
    {
        if (modelIsReferenceType)
        {
            code.AppendLine(
                "if (global::System.Object.ReferenceEquals(before, null) || global::System.Object.ReferenceEquals(after, null)) throw new global::System.ArgumentNullException(\"value\");"
            );
            code.AppendLine("if (global::System.Object.ReferenceEquals(before, after)) return __sparse_empty;");
            if (CanUseSharedReferenceEqualityFastPath(members))
            {
                var sharedMembers = members.Select(member =>
                    "global::System.Object.ReferenceEquals(before."
                    + SparseNaming.EscapeIdentifier(member.Property.Name)
                    + ", after."
                    + SparseNaming.EscapeIdentifier(member.Property.Name)
                    + ")"
                );
                code.AppendLine("if (" + string.Join(" && ", sharedMembers) + ") return __sparse_empty;");
            }
        }
        code.AppendLine(
            "return Between(" + optionalFragment + ".Present(Fragment.From(before)), " + optionalFragment + ".Present(Fragment.From(after)));"
        );
    }
}
