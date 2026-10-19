namespace SparseTypes.Generator.Shared;

/// <summary>Model-targeted rebase entry point for generated change sets.</summary>
/// <remarks>The ordinary-model overload delegates to the presence-aware core while the facade keeps the ergonomic signature.</remarks>
internal static class SparseChangeSetModelRebaseEmitter
{
    internal static void AppendModelRebase(
        SharedIndentedBuilder shell,
        SharedIndentedBuilder code,
        string modelType,
        string optionalFragment,
        string rebaseResult,
        string optionsType,
        SparseOperationTarget? target,
        bool modelIsReferenceType
    )
    {
        if (target is not null)
        {
            shell.AppendLineAt(
                2,
                "/// <summary>Rebases this change onto an ordinary model.</summary>"
            );
            shell.AppendLineAt(
                2,
                "public "
                    + rebaseResult
                    + " RebaseOnto("
                    + modelType
                    + " current, "
                    + optionsType
                    + "? options = null) => "
                    + target.ChangeSetOperationsType
                    + ".RebaseOnto(this, current, options);"
            );
            code.AppendLineAt(2, "/// <summary>Rebases a change onto an ordinary model.</summary>");
            code.AppendLineAt(
                2,
                "internal static "
                    + rebaseResult
                    + " RebaseOnto(ChangeSet self, "
                    + modelType
                    + " current, "
                    + optionsType
                    + "? options = null)"
            );
            code.AppendLineAt(2, "{");
            code.AppendLineAt(3, "if (self.IsEmpty)");
            code.AppendLineAt(3, "{");
            AppendNullGuard(code, modelIsReferenceType, 4);
            code.AppendLineAt(
                4,
                "return RebaseOnto(self, default(" + optionalFragment + "), options);"
            );
            code.AppendLineAt(3, "}");
            code.AppendLineAt(
                3,
                "return RebaseOnto(self, "
                    + optionalFragment
                    + ".Present(Fragment.From(current)), options);"
            );
            code.AppendLineAt(2, "}");
            return;
        }
        code.AppendLineAt(2, "/// <summary>Rebases this change onto an ordinary model.</summary>");
        code.AppendLineAt(
            2,
            "public "
                + rebaseResult
                + " RebaseOnto("
                + modelType
                + " current, "
                + optionsType
                + "? options = null)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (this.IsEmpty)");
        code.AppendLineAt(3, "{");
        AppendNullGuard(code, modelIsReferenceType, 4);
        code.AppendLineAt(4, "return RebaseOnto(default(" + optionalFragment + "), options);");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "return RebaseOnto(" + optionalFragment + ".Present(Fragment.From(current)), options);"
        );
        code.AppendLineAt(2, "}");
    }

    private static void AppendNullGuard(
        SharedIndentedBuilder code,
        bool modelIsReferenceType,
        int indent
    )
    {
        if (!modelIsReferenceType)
        {
            return;
        }

        code.AppendLineAt(
            indent,
            "if (global::System.Object.ReferenceEquals(current, null)) throw new global::System.ArgumentNullException(\"value\");"
        );
    }
}
