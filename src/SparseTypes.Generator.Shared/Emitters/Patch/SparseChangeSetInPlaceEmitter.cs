using System.Collections.Immutable;

namespace SparseTypes.Generator.Shared;

internal static class SparseChangeSetInPlaceEmitter
{
    internal static void AppendModelTryApplyInPlace(
        SharedIndentedBuilder shell,
        SharedIndentedBuilder code,
        string modelType,
        string conflictType,
        string conflictKindType,
        string? inPlaceWriteUnavailableKindMemberName,
        string runtime,
        ImmutableArray<SparseMemberModel> members,
        string optionsType,
        SparseOperationTarget? target
    )
    {
        if (target is not null)
        {
            shell.AppendLine(
                "/// <summary>Applies this change to an existing model after checking its before-state.</summary>"
            );
            shell.AppendLine(
                "/// <remarks>Returns an in-place write conflict when the change includes an immutable member.</remarks>"
            );
            shell.AppendLine(
                "/// <param name=\"current\">The existing model instance to update.</param>"
            );
            shell.AppendLine(
                "/// <param name=\"conflicts\">Structured conflicts when rebasing fails or an immutable member cannot be written.</param>"
            );
            shell.AppendLine("/// <param name=\"options\">Optional rebase behavior.</param>");
            shell.AppendLine(
                "/// <returns><see langword=\"true\"/> when the update was applied.</returns>"
            );
            shell.AppendLine(
                "public bool TryApplyInPlace("
                    + modelType
                    + " current, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out global::System.Collections.Generic.IReadOnlyList<"
                    + conflictType
                    + ">? conflicts, "
                    + optionsType
                    + "? options = null) => "
                    + target.ChangeSetOperationsType
                    + ".TryApplyInPlace(this, current, out conflicts, options);"
            );
            shell.AppendLine(
                "/// <summary>Applies this change to an existing model or throws when its before-state conflicts or it includes an immutable member.</summary>"
            );
            shell.AppendLine(
                "/// <param name=\"current\">The existing model instance to update.</param>"
            );
            shell.AppendLine("/// <param name=\"options\">Optional rebase behavior.</param>");
            shell.AppendLine(
                "public void ApplyInPlace("
                    + modelType
                    + " current, "
                    + optionsType
                    + "? options = null) => "
                    + target.ChangeSetOperationsType
                    + ".ApplyInPlace(this, current, options);"
            );
        }
        code.AppendLine(
            "/// <summary>Applies this change to an existing model after checking its before-state.</summary>"
        );
        code.AppendLine(
            "/// <remarks>Returns an in-place write conflict when the change includes an immutable member.</remarks>"
        );
        if (target is not null)
        {
            code.AppendLine("/// <param name=\"self\">The change set to apply.</param>");
        }

        code.AppendLine(
            "/// <param name=\"current\">The existing model instance to update.</param>"
        );
        code.AppendLine(
            "/// <param name=\"conflicts\">Structured conflicts when rebasing fails or an immutable member cannot be written.</param>"
        );
        code.AppendLine("/// <param name=\"options\">Optional rebase behavior.</param>");
        code.AppendLine(
            "/// <returns><see langword=\"true\"/> when the update was applied.</returns>"
        );
        code.AppendLine(
            (
                target is null
                    ? "public bool TryApplyInPlace("
                    : "internal static bool TryApplyInPlace(ChangeSet self, "
            )
                + modelType
                + " current, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts, "
                + optionsType
                + "? options = null)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (current is null) throw new global::System.ArgumentNullException(nameof(current));"
        );
        code.AppendLine(
            "var __state = " + runtime + "Optional<Fragment?>.Present(Fragment.From(current));"
        );
        code.AppendLine("ChangeSet __toApply;");
        code.AppendLine(
            target is null
                ? "if (options is null && __SparseBeforeMatches(__state))"
                : "if (options is null && self.__SparseBeforeMatches(__state))"
        );
        code.OpenBlock();
        code.AppendLine(target is null ? "__toApply = this;" : "__toApply = self;");
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine(
            target is null
                ? "var __rebase = RebaseOnto(__state, options);"
                : "var __rebase = RebaseOnto(self, __state, options);"
        );
        code.AppendLine("if (__rebase.HasConflicts)");
        code.OpenBlock();
        code.AppendLine("conflicts = __rebase.Conflicts;");
        code.AppendLine("return false;");
        code.CloseBlock();
        code.AppendLine("__toApply = __rebase.Rebased;");
        code.CloseBlock();
        code.AppendLine("var __patch = __toApply.ToPatch();");
        code.AppendLine("var __applied = __patch.Apply(__state);");
        code.AppendLine(
            "if (!__applied.IsPresent || __applied.Value is null) throw new global::System.InvalidOperationException(\"The rebased change does not produce a non-null model root. Use the presence-aware Fragment/Optional API for root presence transitions.\");"
        );
        code.AppendLine("var __updatedModel = __applied.Value.ToModel();");
        code.AppendLine("var __sparse_inPlaceResult = __patch.ApplyInPlace(current);");
        code.AppendLine("if (!__sparse_inPlaceResult.Succeeded)");
        code.OpenBlock();
        code.AppendLine(
            "var __sparse_inPlaceConflicts = new global::System.Collections.Generic.List<"
                + conflictType
                + ">();"
        );
        foreach (
            var member in members.Where(static member =>
                member.Property.IsReadOnly || member.Property.IsInitOnly
            )
        )
        {
            var propertyName = SparseNaming.EscapeIdentifier(member.Property.Name);
            var property = "nameof(" + modelType + "." + propertyName + ")";
            code.AppendLine(
                "if (global::System.Linq.Enumerable.Contains(__sparse_inPlaceResult.UnsupportedMembers, "
                    + property
                    + "))"
            );
            code.OpenBlock();
            code.AppendLine(
                "__sparse_inPlaceConflicts.Add(new "
                    + conflictType
                    + "(__SparseRootPath.Member("
                    + property
                    + "), "
                    + conflictKindType
                    + "."
                    + inPlaceWriteUnavailableKindMemberName
                    + ", "
                    + runtime
                    + "Optional<object?>.Present((object?)current."
                    + propertyName
                    + "), "
                    + runtime
                    + "Optional<object?>.Present((object?)__updatedModel."
                    + propertyName
                    + "), "
                    + runtime
                    + "Optional<object?>.Present((object?)current."
                    + propertyName
                    + "), \"The member cannot be changed in place because it is immutable.\"));"
            );
            code.CloseBlock();
        }
        code.AppendLine(
            "conflicts = global::System.Array.AsReadOnly(__sparse_inPlaceConflicts.ToArray());"
        );
        code.AppendLine("return false;");
        code.CloseBlock();
        code.AppendLine("conflicts = null;");
        code.AppendLine("return true;");
        code.CloseBlock();
        code.AppendLine(
            "/// <summary>Applies this change to an existing model or throws when its before-state conflicts or it includes an immutable member.</summary>"
        );
        if (target is not null)
        {
            code.AppendLine("/// <param name=\"self\">The change set to apply.</param>");
        }

        code.AppendLine(
            "/// <param name=\"current\">The existing model instance to update.</param>"
        );
        code.AppendLine("/// <param name=\"options\">Optional rebase behavior.</param>");
        code.AppendLine(
            (
                target is null
                    ? "public void ApplyInPlace("
                    : "internal static void ApplyInPlace(ChangeSet self, "
            )
                + modelType
                + " current, "
                + optionsType
                + "? options = null)"
        );
        code.OpenBlock();
        code.AppendLine(
            target is null
                ? "if (!TryApplyInPlace(current, out var conflicts, options)) throw new global::System.InvalidOperationException(\"The change set cannot be applied in place because it conflicts or includes immutable members.\");"
                : "if (!TryApplyInPlace(self, current, out var conflicts, options)) throw new global::System.InvalidOperationException(\"The change set cannot be applied in place because it conflicts or includes immutable members.\");"
        );
        code.CloseBlock();
    }
}
