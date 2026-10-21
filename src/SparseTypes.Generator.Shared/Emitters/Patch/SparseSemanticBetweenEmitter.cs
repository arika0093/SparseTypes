using System;
using System.Collections.Immutable;

namespace SparseTypes.Generator.Shared;

/// <summary>
/// Product-neutral dialect for semantic <c>Between</c> emission.
/// </summary>
/// <remarks>The algorithm (whole-state transitions.</remarks>
internal readonly record struct SparseBetweenDialect(
    string RuntimeNamespace,
    string OptionalFragmentType,
    string WholeOperationType,
    string WholeFieldName,
    string MethodPrefix,
    Func<SparseMemberModel, string> MemberField,
    Func<SparseMemberModel, bool> IsScalarMember,
    Func<SparseMemberModel, string> ScalarValueType,
    Func<SparseMemberModel, string, string, string> ScalarEquality,
    Func<SparseMemberModel, string, string, string> NestedBetween
);

/// <summary>Emits a semantic sparse patch between two presence-aware contribution states.</summary>
/// <remarks>Handles whole-state transitions (missing/present-null/present-value).</remarks>
internal static class SparseSemanticBetweenEmitter
{
    public static void AppendBetweenMethod(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseBetweenDialect dialect,
        SparseOperationTarget? target = null
    )
    {
        code.CancellationToken.ThrowIfCancellationRequested();
        if (target is not null)
        {
            code.AppendLine(
                "/// <summary>Derives a patch between two sparse contribution states, preserving presence exactly.</summary>"
            );
            code.AppendLine(
                "public static Patch "
                    + dialect.MethodPrefix
                    + "Between("
                    + dialect.OptionalFragmentType
                    + " before, "
                    + dialect.OptionalFragmentType
                    + " after) => "
                    + target.PatchOperationsType
                    + "."
                    + dialect.MethodPrefix
                    + "Between(before, after);"
            );
            AppendBetweenBody(target.PatchOperations, members, dialect, isStatic: true);
            return;
        }

        AppendBetweenBody(code, members, dialect, isStatic: false);
    }

    private static void AppendBetweenBody(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseBetweenDialect dialect,
        bool isStatic
    )
    {
        code.AppendLine(
            "/// <summary>Derives a patch between two sparse contribution states, preserving presence exactly.</summary>"
        );
        code.AppendLine(
            (isStatic ? "internal static Patch " : "public static Patch ")
                + dialect.MethodPrefix
                + "Between("
                + dialect.OptionalFragmentType
                + " before, "
                + dialect.OptionalFragmentType
                + " after)"
        );
        code.OpenBlock();
        code.AppendLine("var patch = new Patch();");
        code.AppendLine("if (before.IsPresent != after.IsPresent)");
        code.OpenBlock();
        code.AppendLine(
            "patch."
                + dialect.WholeFieldName
                + " = after.IsPresent ? "
                + dialect.WholeOperationType
                + ".Set(after.Value) : "
                + dialect.WholeOperationType
                + ".Remove;"
        );
        code.AppendLine("return patch;");
        code.CloseBlock();
        code.AppendLine("if (!before.IsPresent) return patch;");
        code.AppendLine(
            "if (global::System.Object.ReferenceEquals(before.Value, after.Value)) return patch;"
        );
        code.AppendLine("if (before.Value is null || after.Value is null)");
        code.OpenBlock();
        code.AppendLine(
            "patch."
                + dialect.WholeFieldName
                + " = "
                + dialect.WholeOperationType
                + ".Set(after.Value);"
        );
        code.AppendLine("return patch;");
        code.CloseBlock();
        code.AppendLine("var beforeFragment = before.Value!;");
        code.AppendLine("var afterFragment = after.Value!;");
        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            var field = dialect.MemberField(member);
            if (dialect.IsScalarMember(member))
            {
                var operation =
                    dialect.RuntimeNamespace
                    + "FragmentOperation<"
                    + dialect.ScalarValueType(member)
                    + ">";
                var beforeValue = "beforeFragment." + name + ".Value";
                var afterValue = "afterFragment." + name + ".Value";
                var equality = dialect.ScalarEquality(member, beforeValue, afterValue);
                code.AppendLine("patch." + field + " = !afterFragment." + name + ".IsPresent");
                code.AppendLine(
                    "? (beforeFragment."
                        + name
                        + ".IsPresent ? "
                        + operation
                        + ".Remove : default("
                        + operation
                        + "))"
                );
                code.AppendLine(
                    ": ((beforeFragment."
                        + name
                        + ".IsPresent && "
                        + equality
                        + ") ? default("
                        + operation
                        + ") : "
                        + operation
                        + ".Set(afterFragment."
                        + name
                        + ".Value));"
                );
            }
            else
            {
                code.AppendLine(
                    "patch."
                        + field
                        + " = "
                        + dialect.NestedBetween(
                            member,
                            "beforeFragment." + name,
                            "afterFragment." + name
                        )
                        + ";"
                );
            }
        }

        code.AppendLine("return patch;");
        code.CloseBlock();
    }
}
