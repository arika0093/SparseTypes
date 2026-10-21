using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetBetweenEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetComposeEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetDictBetweenEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetDictComposeEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetDictRebaseEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetDictTransitionEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedBetweenEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedComposeEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedRebaseEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetKeyedTransitionEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetPatchSyncEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetTransitionEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Rebase core: whole-contribution, nested, keyed, and dictionary dispatch.</summary>
internal static class SparseChangeSetRebaseEmitter
{
    internal static void AppendRebase(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment,
        string rebaseResult,
        string rebase,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        ImmutableArray<string> ignoredSettablePropertyNames = default,
        bool canApplyInPlace = false,
        SparseOperationTarget? target = null,
        bool modelIsReferenceType = true
    )
    {
        var shell = code;
        var conflict = dialect.ConflictType;
        var conflictKind = dialect.ConflictKindType;
        var conflictList = "global::System.Collections.Generic.List<" + dialect.ConflictType + ">";
        var optionsType = SparseRebaseOptionEmitter.OptionsType(dialect);
        var modeType = SparseRebaseOptionEmitter.ModeType(dialect);
        var missingValue = runtime + "Optional<object?>.Missing";
        var optionalBoxType = dialect.OptionalBoxType ?? dialect.TransitionContracts?.OptionalBoxType;
        if (target is not null)
        {
            code.AppendLine("/// <summary>Rebases this change onto a newer state without requiring the original baseline.</summary>");
            code.AppendLine("/// <remarks>Redacted-before members pass through as explicit operations unless the options reject them.</remarks>");
            code.AppendLine("/// <param name=\"current\">The newer state to rebase onto.</param>");
            code.AppendLine("/// <param name=\"options\">Optional rebase behavior.</param>");
            code.AppendLine("/// <returns>The rebased change and conflicts.</returns>");
            code.AppendLine(
                "public "
                    + rebaseResult
                    + " RebaseOnto("
                    + optionalFragment
                    + " current, "
                    + optionsType
                    + "? options = null) => "
                    + target.ChangeSetOperationsType
                    + ".RebaseOnto(this, current, options);"
            );
            code = target.ChangeSetOperations;
        }
        if (optionalBoxType is null)
        {
            code.AppendLine(
                "private static "
                    + runtime
                    + "Optional<object?> __SparseState("
                    + optionalFragment
                    + " state) => state.IsPresent ? "
                    + runtime
                    + "Optional<object?>.Present((object?)state.Value) : "
                    + runtime
                    + "Optional<object?>.Missing;"
            );
        }
        SparseRebaseOptionEmitter.AppendHelpers(code, dialect);
        code.AppendLine("/// <summary>Rebases this change onto a newer state without requiring the original baseline.</summary>");
        code.AppendLine("/// <remarks>Redacted-before members pass through as explicit operations unless the options reject them.</remarks>");
        if (target is not null)
        {
            code.AppendLine("/// <param name=\"self\">The change set to rebase.</param>");
        }

        code.AppendLine("/// <param name=\"current\">The newer state to rebase onto.</param>");
        code.AppendLine("/// <param name=\"options\">Optional rebase behavior.</param>");
        code.AppendLine("/// <returns>The rebased change and conflicts.</returns>");
        code.AppendLine(
            (target is null ? "public " : "internal static ")
                + rebaseResult
                + " RebaseOnto("
                + (target is null ? string.Empty : "ChangeSet self, ")
                + optionalFragment
                + " current, "
                + optionsType
                + "? options = null)"
        );
        code.OpenBlock();
        if (target is not null)
        {
            AppendSelfAliases(code, members);
        }
        var __hasSparseRb = members.Any(static m => IsKeyed(m) || IsDict(m));
        code.AppendLine("if (__sparse_hasWhole)");
        code.OpenBlock();
        code.AppendLine("if (__SparseIsRedacted(options, \"\"))");
        code.OpenBlock();
        code.AppendLine("if (__SparseRejectsRedacted(options))");
        code.OpenBlock();
        code.AppendLine("var __rconf = new " + conflictList + "();");
        code.AppendLine(
            "__rconf.Add(new "
                + conflict
                + "(__SparseRootPath, "
                + conflictKind
                + ".RedactedBefore, "
                + missingValue
                + ", "
                + missingValue
                + ", "
                + missingValue
                + ", \""
                + SparseRebaseOptionEmitter.RedactedReason
                + "\"));"
        );
        code.AppendLine("return new " + rebaseResult + "(Between(current, current), __rconf);");
        code.CloseBlock();
        code.AppendLine("return " + rebaseResult + ".Success(Between(current, __sparse_wholeAfter));");
        code.CloseBlock();
        code.AppendLine(target is null ? "var __local = ToPatch();" : "var __local = ToPatch(self);");
        code.AppendLine("var __rb = " + rebase + "(__sparse_wholeBefore, __local, current, options);");
        code.AppendLine("if (__rb.Conflicts.Count == 0 && __rb.Rebased.__SparseIsEmpty())");
        code.OpenBlock();
        code.AppendLine("return " + rebaseResult + ".Success(Between(current, current));");
        code.CloseBlock();
        code.AppendLine("var __ra = __rb.Rebased.Apply(current);");
        code.AppendLine("return new " + rebaseResult + "(Between(current, __ra), __rb.Conflicts);");
        code.CloseBlock();
        code.AppendLine(
            target is null
                ? "if (IsEmpty) return " + rebaseResult + ".Success(Between(current, current));"
                : "if (self.IsEmpty) return " + rebaseResult + ".Success(Between(current, current));"
        );
        code.AppendLine("if (!current.IsPresent || current.Value is null)");
        code.OpenBlock();
        code.AppendLine("var __conf = new " + conflictList + "();");
        code.AppendLine(
            "__conf.Add(new "
                + conflict
                + "(__SparseRootPath, "
                + conflictKind
                + ".WholeContribution, "
                + (
                    optionalBoxType is null
                        ? "__SparseState(__sparse_hasWhole ? __sparse_wholeBefore : default), __SparseState(__sparse_hasWhole ? __sparse_wholeAfter : default), __SparseState(current)"
                        : optionalBoxType
                            + ".ToObject(__sparse_hasWhole ? __sparse_wholeBefore : default), "
                            + optionalBoxType
                            + ".ToObject(__sparse_hasWhole ? __sparse_wholeAfter : default), "
                            + optionalBoxType
                            + ".ToObject(current)"
                )
                + ", \"The contribution conflicts with a concurrent change.\"));"
        );
        code.AppendLine("return new " + rebaseResult + "(Between(current, current), __conf);");
        code.CloseBlock();
        code.AppendLine("var __cur = current.Value!;");
        code.AppendLine("var __conflicts = new " + conflictList + "();");
        ComputePublicNames(members, out _, out var __transNames);
        foreach (var member in members)
        {
            if (IsNested(member))
            {
                code.AppendLine(ChildChangeSet(member, dialect) + "? __r" + member.Id + " = null;");
            }
            else if (IsKeyed(member) || IsDict(member))
            {
                var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
                var trans = __transNames[member.Id];
                var keyType = KeyTypeOf(member);
                code.AppendLine("bool __rh" + member.Id + " = false;");
                code.AppendLine("bool __rwhole" + member.Id + " = false;");
                code.AppendLine(opt + " __rwb" + member.Id + " = default;");
                code.AppendLine(opt + " __rwa" + member.Id + " = default;");
                code.AppendLine("global::System.Collections.Generic.List<" + trans + ".Item>? __ritems" + member.Id + " = null;");
                if (IsKeyed(member))
                {
                    code.AppendLine("global::System.Collections.Generic.List<" + keyType + ">? __rbO" + member.Id + " = null;");
                    code.AppendLine("global::System.Collections.Generic.List<" + keyType + ">? __raO" + member.Id + " = null;");
                    if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
                    {
                        code.AppendLine("global::System.Collections.Generic.List<global::System.Guid>? __rtbO" + member.Id + " = null;");
                        code.AppendLine("global::System.Collections.Generic.List<global::System.Guid>? __rtaO" + member.Id + " = null;");
                    }
                }
            }
            else
            {
                var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
                code.AppendLine(opt + " __rb" + member.Id + " = default;");
                code.AppendLine(opt + " __ra" + member.Id + " = default;");
                code.AppendLine("bool __rh" + member.Id + " = false;");
            }
        }
        if (__hasSparseRb)
        {
            AppendPragmaDisableNullKey(code);
        }

        if (SparseChangeSetMemberRebaseEmitter.UsesDefaultMode(members))
        {
            code.AppendLine("var __mode = options is null ? " + modeType + ".Default : options.DefaultRebaseMode;");
        }

        foreach (var member in members)
        {
            var prop = member.Property.Name;
            var lit = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(prop, true);
            var esc = SparseNaming.EscapeIdentifier(prop);
            code.OpenBlock();
            if (IsNested(member))
            {
                code.AppendLine("if (" + NestedField(member) + " is not null)");
                code.OpenBlock();
                code.AppendLine("if (__SparseIsRedacted(options, " + lit + "))");
                code.OpenBlock();
                code.AppendLine("if (__SparseRejectsRedacted(options))");
                code.OpenBlock();
                SparseRebaseOptionEmitter.AppendRedactedConflict(
                    code,
                    runtime,
                    conflict,
                    dialect,
                    "__SparseRootPath.Member(" + lit + ")",
                    "__conflicts"
                );
                code.CloseBlock();
                code.AppendLine("else");
                code.OpenBlock();
                code.AppendLine("__r" + member.Id + " = " + NestedField(member) + ";");
                code.CloseBlock();
                code.CloseBlock();
                code.AppendLine("else");
                code.OpenBlock();
                code.AppendLine("var __nr" + member.Id + " = " + NestedField(member) + ".RebaseOnto(__cur." + esc + ", options?.Nest(" + lit + "));");
                code.AppendLine("foreach (var __c in __nr" + member.Id + ".Conflicts)");
                code.OpenBlock();
                code.AppendLine("__conflicts.Add(__c.WithPathPrefix(__SparseRootPath.Member(" + lit + ")));");
                code.CloseBlock();
                code.AppendLine("__r" + member.Id + " = __nr" + member.Id + ".Rebased.IsEmpty ? null : __nr" + member.Id + ".Rebased;");
                code.CloseBlock();
                code.CloseBlock();
            }
            else if (IsKeyed(member))
            {
                AppendKeyedRebaseSparse(code, members, member, esc, lit, runtime, dialect);
            }
            else if (IsDict(member))
            {
                AppendDictRebaseSparse(code, members, member, esc, lit, runtime, dialect);
            }
            else if (member.MergeStrategyType is not null)
            {
                var strat = "Fragment." + SparseFragmentPatchEmitter.GetMergeStrategyField(dialect, member);
                var reconciler = strat;
                var equality = strat + ".AreEqual(__curM" + member.Id + ".Value, __reb" + member.Id + ".Value)";
                var reasonFallback = "The custom merge strategy could not rebase the member.";
                if (member.RebasePolicyType is not null)
                {
                    reconciler = "Fragment." + SparseFragmentPatchEmitter.GetRebasePolicyField(dialect, member);
                    equality = reconciler + ".AreEqual(__curM" + member.Id + ".Value, __reb" + member.Id + ".Value)";
                    reasonFallback = "The custom rebase policy could not rebase the member.";
                }
                SparseChangeSetMemberRebaseEmitter.AppendRedactedGuard(
                    code,
                    member,
                    lit,
                    runtime,
                    conflict,
                    dialect,
                    HasField(member),
                    SparseChangeSetMemberRebaseEmitter.ScalarPassthrough(member, esc)
                );
                code.AppendLine("if (" + HasField(member) + " && !__red" + member.Id + ")");
                code.OpenBlock();
                code.AppendLine("var __base" + member.Id + " = " + BeforeField(member) + ";");
                code.AppendLine("var __des" + member.Id + " = " + AfterField(member) + ";");
                code.AppendLine("var __curM" + member.Id + " = __cur." + esc + ";");
                SparseChangeSetMemberRebaseEmitter.AppendReconcilerAttempt(
                    code,
                    member,
                    lit,
                    reconciler,
                    equality,
                    reasonFallback,
                    conflict,
                    conflictKind,
                    dialect.OptionalBoxType ?? dialect.TransitionContracts?.OptionalBoxType
                );
                code.CloseBlock();
            }
            else if (member.MergeMode is SparseMergeModes.Append or SparseMergeModes.SetUnion)
            {
                SparseChangeSetMemberRebaseEmitter.AppendMergeCollectionRebase(code, member, esc, lit, runtime, dialect);
            }
            else
            {
                SparseChangeSetMemberRebaseEmitter.AppendRedactedGuard(
                    code,
                    member,
                    lit,
                    runtime,
                    conflict,
                    dialect,
                    HasField(member),
                    SparseChangeSetMemberRebaseEmitter.ScalarPassthrough(member, esc)
                );
                code.AppendLine("if (" + HasField(member) + " && !__red" + member.Id + ")");
                code.OpenBlock();
                code.AppendLine("var __base" + member.Id + " = " + BeforeField(member) + ";");
                code.AppendLine("var __des" + member.Id + " = " + AfterField(member) + ";");
                code.AppendLine("var __curM" + member.Id + " = __cur." + esc + ";");
                if (member.RebasePolicyType is not null)
                {
                    var policy = "Fragment." + SparseFragmentPatchEmitter.GetRebasePolicyField(dialect, member);
                    SparseChangeSetMemberRebaseEmitter.AppendReconcilerAttempt(
                        code,
                        member,
                        lit,
                        policy,
                        policy + ".AreEqual(__curM" + member.Id + ".Value, __reb" + member.Id + ".Value)",
                        "The custom rebase policy could not rebase the member.",
                        conflict,
                        conflictKind,
                        dialect.OptionalBoxType ?? dialect.TransitionContracts?.OptionalBoxType
                    );
                }
                else
                {
                    SparseChangeSetMemberRebaseEmitter.AppendScalarModeRebase(
                        code,
                        member,
                        lit,
                        conflictKind,
                        conflict,
                        modeType,
                        dialect.TransitionContracts?.ScalarRebaseType,
                        dialect.OptionalBoxType ?? dialect.TransitionContracts?.OptionalBoxType
                    );
                }
                code.CloseBlock();
            }
            code.CloseBlock();
        }
        var rargs = new List<string> { "false", "default", "default" };
        foreach (var member in members)
        {
            if (IsNested(member))
            {
                rargs.Add("__r" + member.Id);
            }
            else if (IsKeyed(member))
            {
                AddKeyedChangeSetArgs(
                    rargs,
                    member,
                    "__rh" + member.Id,
                    "__rwhole" + member.Id,
                    "__rwb" + member.Id,
                    "__rwa" + member.Id,
                    "__ritems" + member.Id,
                    "__rbO" + member.Id,
                    "__raO" + member.Id,
                    SparseKeyedCollectionEmitter.HasTemporaryKey(member) ? "__rtbO" + member.Id : null,
                    SparseKeyedCollectionEmitter.HasTemporaryKey(member) ? "__rtaO" + member.Id : null
                );
            }
            else if (IsDict(member))
            {
                rargs.AddRange(
                    new[] { "__rh" + member.Id, "__rwhole" + member.Id, "__rwb" + member.Id, "__rwa" + member.Id, "__ritems" + member.Id }
                );
            }
            else
            {
                rargs.AddRange(new[] { "__rb" + member.Id, "__ra" + member.Id, "__rh" + member.Id });
            }
        }
        code.AppendLine("var __rebased = new ChangeSet(" + string.Join(", ", rargs) + ");");
        if (__hasSparseRb)
        {
            AppendPragmaRestoreNullKey(code);
        }

        code.AppendLine("return new " + rebaseResult + "(__rebased, __conflicts);");
        code.CloseBlock();
        if (modelType is not null)
        {
            SparseChangeSetModelRebaseEmitter.AppendModelRebase(
                shell,
                code,
                modelType,
                optionalFragment,
                rebaseResult,
                optionsType,
                target,
                modelIsReferenceType
            );
            AppendModelTryApply(
                shell,
                code,
                modelType,
                optionalFragment,
                dialect.ConflictType,
                dialect.ConflictKindType,
                dialect.InPlaceWriteUnavailableKindMemberName,
                runtime,
                members,
                optionsType,
                ignoredSettablePropertyNames,
                canApplyInPlace,
                target
            );
        }
    }

    private static void AppendModelTryApply(
        SharedIndentedBuilder shell,
        SharedIndentedBuilder code,
        string modelType,
        string optionalFragment,
        string conflictType,
        string conflictKindType,
        string? inPlaceWriteUnavailableKindMemberName,
        string runtime,
        ImmutableArray<SparseMemberModel> members,
        string optionsType,
        ImmutableArray<string> ignoredSettablePropertyNames,
        bool canApplyInPlace,
        SparseOperationTarget? target
    )
    {
        if (target is not null)
        {
            shell.AppendLine("/// <summary>Applies this change to an ordinary model if it can be rebased without conflicts.</summary>");
            shell.AppendLine(
                "public bool TryApplyTo("
                    + modelType
                    + " current, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out "
                    + modelType
                    + "? updated, "
                    + optionsType
                    + "? options = null) => "
                    + target.ChangeSetOperationsType
                    + ".TryApplyTo(this, current, out updated, options);"
            );
            shell.AppendLine("/// <summary>Applies this change to an ordinary model and returns structured conflicts when rebasing fails.</summary>");
            shell.AppendLine(
                "public bool TryApplyTo("
                    + modelType
                    + " current, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out "
                    + modelType
                    + "? updated, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out global::System.Collections.Generic.IReadOnlyList<"
                    + conflictType
                    + ">? conflicts, "
                    + optionsType
                    + "? options = null) => "
                    + target.ChangeSetOperationsType
                    + ".TryApplyTo(this, current, out updated, out conflicts, options);"
            );
            code.AppendLine("/// <summary>Applies a change to an ordinary model if it can be rebased without conflicts.</summary>");
            code.AppendLine(
                "internal static bool TryApplyTo("
                    + "ChangeSet self, "
                    + modelType
                    + " current, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out "
                    + modelType
                    + "? updated, "
                    + optionsType
                    + "? options = null)"
            );
            code.OpenBlock();
            code.AppendLine("return TryApplyTo(self, current, out updated, out _, options);");
            code.CloseBlock();
            code.AppendLine("/// <summary>Applies a change to an ordinary model and returns structured conflicts when rebasing fails.</summary>");
            code.AppendLine(
                "internal static bool TryApplyTo("
                    + "ChangeSet self, "
                    + modelType
                    + " current, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out "
                    + modelType
                    + "? updated, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out global::System.Collections.Generic.IReadOnlyList<"
                    + conflictType
                    + ">? conflicts, "
                    + optionsType
                    + "? options = null)"
            );
            AppendTryApplyBody(code, optionalFragment, ignoredSettablePropertyNames, selfName: "self");
        }
        else
        {
            code.AppendLine("/// <summary>Applies this change to an ordinary model if it can be rebased without conflicts.</summary>");
            code.AppendLine(
                "public bool TryApplyTo("
                    + modelType
                    + " current, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out "
                    + modelType
                    + "? updated, "
                    + optionsType
                    + "? options = null)"
            );
            code.OpenBlock();
            code.AppendLine("return TryApplyTo(current, out updated, out _, options);");
            code.CloseBlock();
            code.AppendLine("/// <summary>Applies this change to an ordinary model and returns structured conflicts when rebasing fails.</summary>");
            code.AppendLine(
                "public bool TryApplyTo("
                    + modelType
                    + " current, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out "
                    + modelType
                    + "? updated, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out global::System.Collections.Generic.IReadOnlyList<"
                    + conflictType
                    + ">? conflicts, "
                    + optionsType
                    + "? options = null)"
            );
            AppendTryApplyBody(code, optionalFragment, ignoredSettablePropertyNames, selfName: null);
        }
        if (
            canApplyInPlace
            && (
                members.All(static member => !member.Property.IsReadOnly && !member.Property.IsInitOnly)
                || inPlaceWriteUnavailableKindMemberName is not null
            )
        )
        {
            SparseChangeSetInPlaceEmitter.AppendModelTryApplyInPlace(
                shell,
                code,
                modelType,
                conflictType,
                conflictKindType,
                inPlaceWriteUnavailableKindMemberName,
                runtime,
                members,
                optionsType,
                target
            );
        }
    }

    private static void AppendTryApplyBody(
        SharedIndentedBuilder code,
        string optionalFragment,
        ImmutableArray<string> ignoredSettablePropertyNames,
        string? selfName
    )
    {
        var self = selfName is null ? string.Empty : selfName + ".";
        code.OpenBlock();
        code.AppendLine("var __state = " + optionalFragment + ".Present(Fragment.From(current));");
        code.AppendLine("ChangeSet __toApply;");
        code.AppendLine("if (options is null && " + self + "__SparseBeforeMatches(__state))");
        code.OpenBlock();
        code.AppendLine("__toApply = " + (selfName ?? "this") + ";");
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine(
            selfName is null ? "var __rebase = RebaseOnto(__state, options);" : "var __rebase = RebaseOnto(" + selfName + ", __state, options);"
        );
        code.AppendLine("if (__rebase.HasConflicts)");
        code.OpenBlock();
        code.AppendLine("updated = null;");
        code.AppendLine("conflicts = __rebase.Conflicts;");
        code.AppendLine("return false;");
        code.CloseBlock();
        code.AppendLine("__toApply = __rebase.Rebased;");
        code.CloseBlock();
        code.AppendLine("var __applied = __toApply.ToPatch().Apply(__state);");
        code.AppendLine(
            "if (!__applied.IsPresent || __applied.Value is null) throw new global::System.InvalidOperationException(\"The rebased change does not produce a non-null model root. Use the presence-aware Fragment/Optional API for root presence transitions.\");"
        );
        code.AppendLine("var __updatedModel = __applied.Value.ToModel();");
        foreach (var ignoredName in ignoredSettablePropertyNames.IsDefault ? ImmutableArray<string>.Empty : ignoredSettablePropertyNames)
        {
            var name = SparseNaming.EscapeIdentifier(ignoredName);
            code.AppendLine("__updatedModel." + name + " = current." + name + ";");
        }
        code.AppendLine("updated = __updatedModel;");
        code.AppendLine("conflicts = null;");
        code.AppendLine("return true;");
        code.CloseBlock();
    }
}
