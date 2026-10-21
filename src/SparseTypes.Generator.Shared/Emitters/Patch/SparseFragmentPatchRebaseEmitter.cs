using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits the standalone three-way rebase with structured conflicts.</summary>
internal static class SparseFragmentPatchRebaseEmitter
{
    private static string EqualMethod(SparseMemberModel member) =>
        "Fragment.__SparseEqual_" + member.Id;

    public static void AppendPatchRebase(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null
    )
    {
        var into = target?.PatchOperations ?? code;
        var runtime = dialect.RuntimeNamespace;
        var pathType = SparseFragmentPatchEmitter.GetPathType(dialect);
        var prefix = SparseNaming.PatchApiPrefix(
            members.Select(static member => member.Property.Name)
        );
        var optionalFragment = runtime + "Optional<Fragment?>";
        var kind = runtime + "FragmentOperationKind";
        var conflict = dialect.ConflictType;
        var conflictKind = dialect.ConflictKindType;
        var conflictList = "global::System.Collections.Generic.List<" + dialect.ConflictType + ">";
        var rebaseResult = dialect.RebaseResult("Patch");
        var optionsType = SparseRebaseOptionEmitter.OptionsType(dialect);
        var modeType = SparseRebaseOptionEmitter.ModeType(dialect);

        if (target is not null)
        {
            code.AppendLine(
                "/// <summary>Rebases a local patch onto a newer sparse state and reports structured conflicts.</summary>"
            );
            code.AppendLine(
                "public static "
                    + rebaseResult
                    + " "
                    + prefix
                    + "Rebase("
                    + optionalFragment
                    + " baseState, Patch local, "
                    + optionalFragment
                    + " currentState, "
                    + optionsType
                    + "? options = null) => "
                    + target.PatchOperationsType
                    + "."
                    + prefix
                    + "Rebase(baseState, local, currentState, options);"
            );
        }
        AppendRebaseStateHelpers(into, runtime);
        into.AppendLine("/// <summary>Model-rooted path factory for rebase conflicts.</summary>");
        into.AppendLine(
            "internal static readonly "
                + pathType
                + " __SparseRootPath = "
                + pathType
                + ".Root(typeof("
                + (string.IsNullOrEmpty(modelType) ? "global::System.Object" : modelType)
                + "));"
        );
        SparseRebaseOptionEmitter.AppendHelpers(into, dialect);
        AppendRebaseHeader(
            into,
            prefix,
            rebaseResult,
            optionalFragment,
            kind,
            conflict,
            conflictKind,
            conflictList,
            optionsType,
            runtime,
            target is not null
        );

        into.AppendLine("var baseFragment = baseState.Value!;");
        into.AppendLine("var currentFragment = currentState.Value!;");

        foreach (var member in members)
        {
            into.OpenBlock();
            if (member.ChildModel is not null)
            {
                AppendNestedMemberRebase(into, member, conflict, conflictKind, dialect);
            }
            else if (SparseFragmentPatchEmitter.IsCollectionPatch(member))
            {
                AppendCollectionMemberRebase(into, member, conflict, conflictKind, dialect);
            }
            else
            {
                AppendScalarMemberRebase(
                    into,
                    member,
                    kind,
                    conflict,
                    conflictKind,
                    dialect,
                    modeType
                );
            }

            into.CloseBlock();
        }

        into.AppendLine("return new " + rebaseResult + "(result, conflicts);");
        into.CloseBlock();
    }

    private static void AppendRebaseStateHelpers(SharedIndentedBuilder code, string runtime)
    {
        code.AppendIndent()
            .Append(
                "private static "
                    + runtime
                    + "Optional<object?> __SparseState("
                    + runtime
                    + "Optional<Fragment?>"
                    + " state) => state.IsPresent ? "
                    + runtime
                    + "Optional<object?>.Present((object?)state.Value) : "
                    + runtime
                    + "Optional<object?>.Missing;"
            )
            .AppendLine();
        code.AppendIndent()
            .Append(
                "private static "
                    + runtime
                    + "Optional<object?> __SparseMember<T>("
                    + runtime
                    + "Optional<T> value) => value.IsPresent ? "
                    + runtime
                    + "Optional<object?>.Present((object?)value.Value) : "
                    + runtime
                    + "Optional<object?>.Missing;"
            )
            .AppendLine();
    }

    private static void AppendRebaseHeader(
        SharedIndentedBuilder code,
        string prefix,
        string rebaseResult,
        string optionalFragment,
        string kind,
        string conflict,
        string conflictKind,
        string conflictList,
        string optionsType,
        string runtime,
        bool isRelocated = false
    )
    {
        code.AppendLine(
            "/// <summary>Rebases a local patch onto a newer sparse state and reports structured conflicts.</summary>"
        );
        code.AppendLine(
            (isRelocated ? "internal static " : "public static ")
                + rebaseResult
                + " "
                + prefix
                + "Rebase("
                + optionalFragment
                + " baseState, Patch local, "
                + optionalFragment
                + " currentState, "
                + optionsType
                + "? options = null)"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (local is null) throw new global::System.ArgumentNullException(nameof(local));"
        );
        code.AppendLine(
            "if (local.__SparseIsEmpty()) return " + rebaseResult + ".Success(new Patch());"
        );
        code.AppendLine("var result = new Patch();");
        code.AppendLine("var conflicts = new " + conflictList + "();");
        code.AppendLine("if (__SparseIsRedacted(options, \"\"))");
        code.OpenBlock();
        code.AppendLine("if (__SparseRejectsRedacted(options))");
        code.OpenBlock();
        code.AppendLine(
            "conflicts.Add(new "
                + conflict
                + "(__SparseRootPath, "
                + conflictKind
                + ".RedactedBefore, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Missing, \""
                + SparseRebaseOptionEmitter.RedactedReason
                + "\"));"
        );
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine("result = local." + prefix + "Compose(new Patch());");
        code.CloseBlock();
        code.AppendLine("return new " + rebaseResult + "(result, conflicts);");
        code.CloseBlock();
        code.AppendLine("var desiredState = local.Apply(baseState);");
        code.AppendLine("if (local.__sparse_whole.Kind != " + kind + ".Keep)");
        code.OpenBlock();
        code.AppendLine("if (Fragment.__SparseAreEqual(baseState, currentState))");
        code.OpenBlock();
        code.AppendLine("result = local." + prefix + "Compose(new Patch());");
        code.CloseBlock();
        code.AppendLine("else if (!Fragment.__SparseAreEqual(desiredState, currentState))");
        code.OpenBlock();
        code.AppendLine(
            "conflicts.Add(new "
                + conflict
                + "(__SparseRootPath, "
                + conflictKind
                + ".WholeContribution, __SparseState(baseState), __SparseState(desiredState), __SparseState(currentState), \"The whole contribution conflicts with a concurrent change.\"));"
        );
        code.CloseBlock();
        code.AppendLine("return new " + rebaseResult + "(result, conflicts);");
        code.CloseBlock();
        code.AppendLine(
            "if (!baseState.IsPresent || !currentState.IsPresent || baseState.Value is null || currentState.Value is null || !desiredState.IsPresent || desiredState.Value is null)"
        );
        code.OpenBlock();
        code.AppendLine("if (Fragment.__SparseAreEqual(baseState, currentState))");
        code.OpenBlock();
        code.AppendLine("result = local." + prefix + "Compose(new Patch());");
        code.CloseBlock();
        code.AppendLine("else if (!Fragment.__SparseAreEqual(desiredState, currentState))");
        code.OpenBlock();
        code.AppendLine(
            "conflicts.Add(new "
                + conflict
                + "(__SparseRootPath, "
                + conflictKind
                + ".WholeContribution, __SparseState(baseState), __SparseState(desiredState), __SparseState(currentState), \"The contribution conflicts with a concurrent change.\"));"
        );
        code.CloseBlock();
        code.AppendLine("return new " + rebaseResult + "(result, conflicts);");
        code.CloseBlock();
    }

    private static void AppendCollectionMemberRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string conflict,
        string conflictKind,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var field = dialect.MemberField(member);
        const string baseMember = "baseMember";
        const string currentMember = "currentMember";
        const string desiredMember = "desiredMember";
        var equality = EqualMethod(member);

        var pathLit = SymbolDisplay.FormatLiteral(member.Property.Name, true);
        code.AppendLine(
            "if (local."
                + field
                + " is not null && !local."
                + field
                + ".__SparseIsEmpty() && __SparseIsRedacted(options, "
                + pathLit
                + "))"
        );
        code.OpenBlock();
        code.AppendLine("if (__SparseRejectsRedacted(options))");
        code.OpenBlock();
        SparseRebaseOptionEmitter.AppendRedactedConflict(
            code,
            dialect.RuntimeNamespace,
            conflict,
            dialect,
            "__SparseRootPath.Member(" + pathLit + ")",
            "conflicts"
        );
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine("result." + field + " = local." + field + ";");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine(
            "if (local."
                + field
                + " is not null && !local."
                + field
                + ".__SparseIsEmpty() && !__SparseIsRedacted(options, "
                + pathLit
                + "))"
        );
        code.OpenBlock();
        code.AppendLine("var " + baseMember + " = baseFragment." + name + ";");
        code.AppendLine("var " + currentMember + " = currentFragment." + name + ";");
        code.AppendLine("var " + desiredMember + " = " + baseMember + ";");
        code.AppendLine("var __applyFailed" + member.Id + " = false;");
        code.AppendLine("try");
        code.OpenBlock();
        code.AppendLine(desiredMember + " = local." + field + ".Apply(" + baseMember + ");");
        code.CloseBlock();
        code.AppendLine(
            "catch (global::System.InvalidOperationException ex) { conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                + "), "
                + conflictKind
                + ".Nested, __SparseMember("
                + baseMember
                + "), __SparseMember("
                + baseMember
                + "), __SparseMember("
                + currentMember
                + "), ex.Message)); __applyFailed"
                + member.Id
                + " = true; }"
        );
        code.AppendLine("if (!__applyFailed" + member.Id + ")");
        code.OpenBlock();
        code.AppendLine("if (" + equality + "(" + baseMember + ", " + currentMember + "))");
        code.OpenBlock();
        code.AppendLine("result." + field + " = local." + field + ";");
        code.CloseBlock();
        code.AppendLine(
            "else if (" + baseMember + ".IsPresent && " + currentMember + ".IsPresent)"
        );
        code.OpenBlock();
        code.AppendLine(
            "var nested = "
                + SparseFragmentPatchEmitter.GetCollectionPatchName(dialect, member)
                + ".Rebase("
                + baseMember
                + ", local."
                + field
                + ", "
                + currentMember
                + ");"
        );
        code.AppendLine("result." + field + " = nested.Rebased;");
        code.AppendLine("foreach (var nestedConflict in nested.Conflicts)");
        code.OpenBlock();
        code.AppendLine(
            "conflicts.Add(nestedConflict.WithPathPrefix(__SparseRootPath.Member("
                + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                + ")));"
        );
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine(
            "else if (!" + equality + "(" + desiredMember + ", " + currentMember + "))"
        );
        code.OpenBlock();
        code.AppendLine(
            "conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                + "), "
                + conflictKind
                + ".Nested, __SparseMember("
                + baseMember
                + "), __SparseMember("
                + desiredMember
                + "), __SparseMember("
                + currentMember
                + "), \"The collection contribution conflicts with a concurrent change.\"));"
        );
        code.CloseBlock();
        code.CloseBlock();
        code.CloseBlock();
    }

    private static void AppendNestedMemberRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string conflict,
        string conflictKind,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var field = dialect.MemberField(member);
        const string baseMember = "baseMember";
        const string currentMember = "currentMember";
        const string desiredMember = "desiredMember";
        var equality = EqualMethod(member);
        var pathLit = SymbolDisplay.FormatLiteral(member.Property.Name, true);

        code.AppendLine(
            "if (local."
                + field
                + " is not null && !local."
                + field
                + ".__SparseIsEmpty() && __SparseIsRedacted(options, "
                + pathLit
                + "))"
        );
        code.OpenBlock();
        code.AppendLine("if (__SparseRejectsRedacted(options))");
        code.OpenBlock();
        SparseRebaseOptionEmitter.AppendRedactedConflict(
            code,
            dialect.RuntimeNamespace,
            conflict,
            dialect,
            "__SparseRootPath.Member(" + pathLit + ")",
            "conflicts"
        );
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine("result." + field + " = local." + field + ";");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine(
            "if (local."
                + field
                + " is not null && !local."
                + field
                + ".__SparseIsEmpty() && !__SparseIsRedacted(options, "
                + pathLit
                + "))"
        );
        code.OpenBlock();
        code.AppendLine("var " + baseMember + " = baseFragment." + name + ";");
        code.AppendLine("var " + currentMember + " = currentFragment." + name + ";");
        code.AppendLine(
            "var " + desiredMember + " = local." + field + ".Apply(" + baseMember + ");"
        );
        code.AppendLine("if (" + equality + "(" + baseMember + ", " + currentMember + "))");
        code.OpenBlock();
        code.AppendLine("result." + field + " = local." + field + ";");
        code.CloseBlock();
        code.AppendLine(
            "else if (" + baseMember + ".IsPresent && " + currentMember + ".IsPresent)"
        );
        code.OpenBlock();
        code.AppendLine(
            "var nested = "
                + dialect.ChildPatchName(member)
                + "."
                + member.ChildModel!.Value.PatchApiPrefix
                + "Rebase("
                + baseMember
                + ", local."
                + field
                + ", "
                + currentMember
                + ", options?.Nest("
                + pathLit
                + "));"
        );
        code.AppendLine("result." + field + " = nested.Rebased;");
        code.AppendLine("foreach (var nestedConflict in nested.Conflicts)");
        code.OpenBlock();
        code.AppendLine(
            "conflicts.Add(nestedConflict.WithPathPrefix(__SparseRootPath.Member("
                + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                + ")));"
        );
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine(
            "else if (!" + equality + "(" + desiredMember + ", " + currentMember + "))"
        );
        code.OpenBlock();
        code.AppendLine(
            "conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                + "), "
                + conflictKind
                + ".Nested, __SparseMember("
                + baseMember
                + "), __SparseMember("
                + desiredMember
                + "), __SparseMember("
                + currentMember
                + "), \"The nested contribution conflicts with a concurrent change.\"));"
        );
        code.CloseBlock();
        code.CloseBlock();
    }

    private static void AppendScalarMemberRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string kind,
        string conflict,
        string conflictKind,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modeType
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var field = dialect.MemberField(member);
        const string baseMember = "baseMember";
        const string currentMember = "currentMember";
        const string desiredMember = "desiredMember";
        var equality = EqualMethod(member);
        var operation = runtime + "FragmentOperation";
        string scalarKind;
        if (member.MergeMode == SparseMergeModes.Append)
        {
            scalarKind = conflictKind + ".CollectionAppend";
        }
        else if (member.MergeMode == SparseMergeModes.SetUnion)
        {
            scalarKind = conflictKind + ".CollectionSetUnion";
        }
        else
        {
            scalarKind = conflictKind + ".Scalar";
        }
        if (member.MergeStrategyType is not null || member.RebasePolicyType is not null)
        {
            SparseFragmentPatchScalarRebaseEmitter.AppendCustomStrategyMemberRebase(
                code,
                member,
                field,
                kind,
                baseMember,
                desiredMember,
                currentMember,
                conflict,
                conflictKind,
                dialect
            );
        }
        else if (member.MergeMode is SparseMergeModes.Append or SparseMergeModes.SetUnion)
        {
            var mergePathLit = SymbolDisplay.FormatLiteral(member.Property.Name, true);
            code.AppendLine(
                "if (local."
                    + field
                    + ".Kind != "
                    + kind
                    + ".Keep && __SparseIsRedacted(options, "
                    + mergePathLit
                    + "))"
            );
            code.OpenBlock();
            code.AppendLine("if (__SparseRejectsRedacted(options))");
            code.OpenBlock();
            SparseRebaseOptionEmitter.AppendRedactedConflict(
                code,
                dialect.RuntimeNamespace,
                conflict,
                dialect,
                "__SparseRootPath.Member(" + mergePathLit + ")",
                "conflicts"
            );
            code.CloseBlock();
            code.AppendLine("else");
            code.OpenBlock();
            code.AppendLine("result." + field + " = local." + field + ";");
            code.CloseBlock();
            code.CloseBlock();
            code.AppendLine(
                "if (local."
                    + field
                    + ".Kind != "
                    + kind
                    + ".Keep && !__SparseIsRedacted(options, "
                    + mergePathLit
                    + "))"
            );
            code.OpenBlock();
            code.AppendLine("var " + baseMember + " = baseFragment." + name + ";");
            code.AppendLine("var " + currentMember + " = currentFragment." + name + ";");
            code.AppendLine(
                "var " + desiredMember + " = local." + field + ".Apply(" + baseMember + ");"
            );
            code.AppendLine("var handled = false;");
            code.AppendLine(
                "if ("
                    + baseMember
                    + ".IsPresent && "
                    + baseMember
                    + ".Value is not null && "
                    + currentMember
                    + ".IsPresent && "
                    + currentMember
                    + ".Value is not null && "
                    + desiredMember
                    + ".IsPresent && "
                    + desiredMember
                    + ".Value is not null)"
            );
            code.OpenBlock();
            code.AppendLine("handled = true;");
            if (
                member.MergeMode == SparseMergeModes.SetUnion
                && member.Collection.CloneKind == SparseCloneCollectionKind.Set
            )
            {
                SparseFragmentPatchCollectionRebaseEmitter.AppendTypedSetUnionRebase(
                    code,
                    member,
                    field,
                    operation,
                    dialect
                );
            }
            else if (
                member.MergeMode is SparseMergeModes.Append or SparseMergeModes.SetUnion
                && member.Collection.CloneKind
                    is SparseCloneCollectionKind.Array
                        or SparseCloneCollectionKind.List
                && member.Collection.ElementType.UsesDefaultScalarEquality
            )
            {
                SparseFragmentPatchCollectionRebaseEmitter.AppendTypedSequenceRebase(
                    code,
                    member,
                    field,
                    operation,
                    dialect
                );
            }
            else
            {
                SparseFragmentPatchCollectionRebaseEmitter.AppendBoxedCollectionRebase(
                    code,
                    member,
                    field,
                    operation,
                    dialect
                );
            }
            code.CloseBlock();
            code.AppendLine("else");
            code.OpenBlock();
            code.AppendLine(
                "conflicts.Add(new "
                    + conflict
                    + "(__SparseRootPath.Member("
                    + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                    + "), "
                    + scalarKind
                    + ", __SparseMember("
                    + baseMember
                    + "), __SparseMember("
                    + desiredMember
                    + "), __SparseMember("
                    + currentMember
                    + "), reason));"
            );
            code.CloseBlock();
            code.CloseBlock();
            code.AppendLine("if (!handled)");
            code.OpenBlock();
            SparseFragmentPatchScalarRebaseEmitter.EmitScalarRebase(
                code,
                field,
                member.Property.Name,
                baseMember,
                desiredMember,
                currentMember,
                equality,
                scalarKind,
                conflict
            );
            code.CloseBlock();
            code.CloseBlock();
        }
        else
        {
            var plainPathLit = SymbolDisplay.FormatLiteral(member.Property.Name, true);
            code.AppendLine("if (local." + field + ".Kind != " + kind + ".Keep)");
            code.OpenBlock();
            code.AppendLine("var " + baseMember + " = baseFragment." + name + ";");
            code.AppendLine("var " + currentMember + " = currentFragment." + name + ";");
            code.AppendLine(
                "var " + desiredMember + " = local." + field + ".Apply(" + baseMember + ");"
            );
            code.AppendLine(
                "var __mode"
                    + member.Id
                    + " = options is null ? "
                    + modeType
                    + ".Default : options.DefaultRebaseMode;"
            );
            code.AppendLine("if (__SparseIsRedacted(options, " + plainPathLit + "))");
            code.OpenBlock();
            code.AppendLine("if (__SparseRejectsRedacted(options))");
            code.OpenBlock();
            SparseRebaseOptionEmitter.AppendRedactedConflict(
                code,
                dialect.RuntimeNamespace,
                conflict,
                dialect,
                "__SparseRootPath.Member(" + plainPathLit + ")",
                "conflicts"
            );
            code.CloseBlock();
            code.AppendLine("else");
            code.OpenBlock();
            code.AppendLine("result." + field + " = local." + field + ";");
            code.CloseBlock();
            code.CloseBlock();
            var scalarRebaseType = dialect.TransitionContracts?.ScalarRebaseType;
            if (scalarRebaseType is { Length: > 0 })
            {
                // Shared three-way decision; the trailing no-op guard preserves
                // the historical rule that a value-identical local Set reports
                // no conflict.
                code.AppendLine(
                    "else if ("
                        + scalarRebaseType
                        + ".TryResolve("
                        + baseMember
                        + ", "
                        + desiredMember
                        + ", "
                        + currentMember
                        + ", __mode"
                        + member.Id
                        + ", "
                        + modeType
                        + ".PreferIncoming, "
                        + modeType
                        + ".PreferCurrent, "
                        + equality
                        + ", out _, out var __scalarConflict"
                        + member.Id
                        + "))"
                );
                code.OpenBlock();
                code.AppendLine("result." + field + " = local." + field + ";");
                code.CloseBlock();
                code.AppendLine(
                    "else if (__scalarConflict"
                        + member.Id
                        + " && !"
                        + equality
                        + "("
                        + desiredMember
                        + ", "
                        + baseMember
                        + "))"
                );
                code.OpenBlock();
                code.AppendLine(
                    "conflicts.Add(new "
                        + conflict
                        + "(__SparseRootPath.Member("
                        + plainPathLit
                        + "), "
                        + scalarKind
                        + ", __SparseMember("
                        + baseMember
                        + "), __SparseMember("
                        + desiredMember
                        + "), __SparseMember("
                        + currentMember
                        + "), \"The member conflicts with a concurrent change.\"));"
                );
                code.CloseBlock();
                code.CloseBlock();
                return;
            }
            code.AppendLine(
                "else if (__mode"
                    + member.Id
                    + " == "
                    + modeType
                    + ".PreferIncoming && !"
                    + equality
                    + "("
                    + desiredMember
                    + ", "
                    + currentMember
                    + "))"
            );
            code.OpenBlock();
            code.AppendLine("result." + field + " = local." + field + ";");
            code.CloseBlock();
            code.AppendLine(
                "else if (__mode"
                    + member.Id
                    + " != "
                    + modeType
                    + ".PreferCurrent && "
                    + equality
                    + "("
                    + baseMember
                    + ", "
                    + currentMember
                    + "))"
            );
            code.OpenBlock();
            code.AppendLine("result." + field + " = local." + field + ";");
            code.CloseBlock();
            code.AppendLine(
                "else if (__mode"
                    + member.Id
                    + " != "
                    + modeType
                    + ".PreferCurrent && !"
                    + equality
                    + "("
                    + desiredMember
                    + ", "
                    + currentMember
                    + ") && !"
                    + equality
                    + "("
                    + desiredMember
                    + ", "
                    + baseMember
                    + "))"
            );
            code.OpenBlock();
            code.AppendLine(
                "conflicts.Add(new "
                    + conflict
                    + "(__SparseRootPath.Member("
                    + plainPathLit
                    + "), "
                    + scalarKind
                    + ", __SparseMember("
                    + baseMember
                    + "), __SparseMember("
                    + desiredMember
                    + "), __SparseMember("
                    + currentMember
                    + "), \"The member conflicts with a concurrent change.\"));"
            );
            code.CloseBlock();
            code.CloseBlock();
        }
    }
}
