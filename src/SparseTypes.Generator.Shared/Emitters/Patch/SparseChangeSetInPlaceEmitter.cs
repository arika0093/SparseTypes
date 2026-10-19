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
            shell.AppendLineAt(
                2,
                "/// <summary>Applies this change to an existing model after checking its before-state.</summary>"
            );
            shell.AppendLineAt(
                2,
                "/// <remarks>Returns an in-place write conflict when the change includes an immutable member.</remarks>"
            );
            shell.AppendLineAt(
                2,
                "/// <param name=\"current\">The existing model instance to update.</param>"
            );
            shell.AppendLineAt(
                2,
                "/// <param name=\"conflicts\">Structured conflicts when rebasing fails or an immutable member cannot be written.</param>"
            );
            shell.AppendLineAt(2, "/// <param name=\"options\">Optional rebase behavior.</param>");
            shell.AppendLineAt(
                2,
                "/// <returns><see langword=\"true\"/> when the update was applied.</returns>"
            );
            shell.AppendLineAt(
                2,
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
            shell.AppendLineAt(
                2,
                "/// <summary>Applies this change to an existing model or throws when its before-state conflicts or it includes an immutable member.</summary>"
            );
            shell.AppendLineAt(
                2,
                "/// <param name=\"current\">The existing model instance to update.</param>"
            );
            shell.AppendLineAt(2, "/// <param name=\"options\">Optional rebase behavior.</param>");
            shell.AppendLineAt(
                2,
                "public void ApplyInPlace("
                    + modelType
                    + " current, "
                    + optionsType
                    + "? options = null) => "
                    + target.ChangeSetOperationsType
                    + ".ApplyInPlace(this, current, options);"
            );
        }
        code.AppendLineAt(
            2,
            "/// <summary>Applies this change to an existing model after checking its before-state.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Returns an in-place write conflict when the change includes an immutable member.</remarks>"
        );
        if (target is not null)
            code.AppendLineAt(2, "/// <param name=\"self\">The change set to apply.</param>");
        code.AppendLineAt(
            2,
            "/// <param name=\"current\">The existing model instance to update.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"conflicts\">Structured conflicts when rebasing fails or an immutable member cannot be written.</param>"
        );
        code.AppendLineAt(2, "/// <param name=\"options\">Optional rebase behavior.</param>");
        code.AppendLineAt(
            2,
            "/// <returns><see langword=\"true\"/> when the update was applied.</returns>"
        );
        code.AppendLineAt(
            2,
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
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (current is null) throw new global::System.ArgumentNullException(nameof(current));"
        );
        code.AppendLineAt(
            3,
            "var __state = " + runtime + "Optional<Fragment?>.Present(Fragment.From(current));"
        );
        code.AppendLineAt(3, "ChangeSet __toApply;");
        code.AppendLineAt(
            3,
            target is null
                ? "if (options is null && __SparseBeforeMatches(__state))"
                : "if (options is null && self.__SparseBeforeMatches(__state))"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, target is null ? "__toApply = this;" : "__toApply = self;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "else");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            target is null
                ? "var __rebase = RebaseOnto(__state, options);"
                : "var __rebase = RebaseOnto(self, __state, options);"
        );
        code.AppendLineAt(4, "if (__rebase.HasConflicts)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "conflicts = __rebase.Conflicts;");
        code.AppendLineAt(5, "return false;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "__toApply = __rebase.Rebased;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "var __patch = __toApply.ToPatch();");
        code.AppendLineAt(3, "var __applied = __patch.Apply(__state);");
        code.AppendLineAt(
            3,
            "if (!__applied.IsPresent || __applied.Value is null) throw new global::System.InvalidOperationException(\"The rebased change does not produce a non-null model root. Use the presence-aware Fragment/Optional API for root presence transitions.\");"
        );
        code.AppendLineAt(3, "var __updatedModel = __applied.Value.ToModel();");
        code.AppendLineAt(3, "var __sparse_inPlaceResult = __patch.ApplyInPlace(current);");
        code.AppendLineAt(3, "if (!__sparse_inPlaceResult.Succeeded)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
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
            code.AppendLineAt(
                4,
                "if (global::System.Linq.Enumerable.Contains(__sparse_inPlaceResult.UnsupportedMembers, "
                    + property
                    + "))"
            );
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
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
            code.AppendLineAt(4, "}");
        }
        code.AppendLineAt(
            4,
            "conflicts = global::System.Array.AsReadOnly(__sparse_inPlaceConflicts.ToArray());"
        );
        code.AppendLineAt(4, "return false;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "conflicts = null;");
        code.AppendLineAt(3, "return true;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Applies this change to an existing model or throws when its before-state conflicts or it includes an immutable member.</summary>"
        );
        if (target is not null)
            code.AppendLineAt(2, "/// <param name=\"self\">The change set to apply.</param>");
        code.AppendLineAt(
            2,
            "/// <param name=\"current\">The existing model instance to update.</param>"
        );
        code.AppendLineAt(2, "/// <param name=\"options\">Optional rebase behavior.</param>");
        code.AppendLineAt(
            2,
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
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            target is null
                ? "if (!TryApplyInPlace(current, out var conflicts, options)) throw new global::System.InvalidOperationException(\"The change set cannot be applied in place because it conflicts or includes immutable members.\");"
                : "if (!TryApplyInPlace(self, current, out var conflicts, options)) throw new global::System.InvalidOperationException(\"The change set cannot be applied in place because it conflicts or includes immutable members.\");"
        );
        code.AppendLineAt(2, "}");
    }
}
