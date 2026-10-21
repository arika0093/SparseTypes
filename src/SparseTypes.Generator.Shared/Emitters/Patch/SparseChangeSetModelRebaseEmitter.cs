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
            shell.AppendLine("/// <summary>Rebases this change onto an ordinary model.</summary>");
            shell.AppendLine(
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
            code.AppendLine("/// <summary>Rebases a change onto an ordinary model.</summary>");
            code.AppendLine(
                "internal static " + rebaseResult + " RebaseOnto(ChangeSet self, " + modelType + " current, " + optionsType + "? options = null)"
            );
            code.OpenBlock();
            code.AppendLine("if (self.IsEmpty)");
            code.OpenBlock();
            AppendNullGuard(code, modelIsReferenceType);
            code.AppendLine("return RebaseOnto(self, default(" + optionalFragment + "), options);");
            code.CloseBlock();
            code.AppendLine("return RebaseOnto(self, " + optionalFragment + ".Present(Fragment.From(current)), options);");
            code.CloseBlock();
            return;
        }
        code.AppendLine("/// <summary>Rebases this change onto an ordinary model.</summary>");
        code.AppendLine("public " + rebaseResult + " RebaseOnto(" + modelType + " current, " + optionsType + "? options = null)");
        code.OpenBlock();
        code.AppendLine("if (this.IsEmpty)");
        code.OpenBlock();
        AppendNullGuard(code, modelIsReferenceType);
        code.AppendLine("return RebaseOnto(default(" + optionalFragment + "), options);");
        code.CloseBlock();
        code.AppendLine("return RebaseOnto(" + optionalFragment + ".Present(Fragment.From(current)), options);");
        code.CloseBlock();
    }

    private static void AppendNullGuard(SharedIndentedBuilder code, bool modelIsReferenceType)
    {
        if (!modelIsReferenceType)
        {
            return;
        }

        code.AppendLine("if (global::System.Object.ReferenceEquals(current, null)) throw new global::System.ArgumentNullException(\"value\");");
    }
}
