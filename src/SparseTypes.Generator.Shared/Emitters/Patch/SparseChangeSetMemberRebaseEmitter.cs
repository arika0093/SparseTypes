using System.Collections.Immutable;
using System.Linq;
using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Scalar and merge-collection member rebase for generated change sets.</summary>
internal static class SparseChangeSetMemberRebaseEmitter
{
    internal static bool UsesDefaultMode(ImmutableArray<SparseMemberModel> members) =>
        members.Any(member =>
            !IsNested(member)
            && !IsKeyed(member)
            && !IsDict(member)
            && member.MergeStrategyType is null
            && member.MergeMode is not (SparseMergeModes.Append or SparseMergeModes.SetUnion)
            && member.RebasePolicyType is null
        );

    /// <summary>
    /// Emits a redacted-before guard for one member block.
    /// </summary>
    /// <remarks>The guard evaluates before the member's own condition so granular bodies stay untouched.</remarks>
    internal static void AppendRedactedGuard(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string lit,
        string runtime,
        string conflict,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string hasCondition,
        string[] passthrough
    )
    {
        code.AppendLine(
            "var __red"
                + member.Id
                + " = "
                + hasCondition
                + " && __SparseIsRedacted(options, "
                + lit
                + ");"
        );
        code.AppendLine("if (__red" + member.Id + ")");
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
        foreach (var line in passthrough)
        {
            code.AppendLine(line);
        }
        code.CloseBlock();
        code.CloseBlock();
    }

    /// <summary>Emits scalar-style redacted passthrough (replay desired onto current).</summary>
    internal static string[] ScalarPassthrough(SparseMemberModel member, string esc) =>
        [
            "__rh" + member.Id + " = true;",
            "__rb" + member.Id + " = __cur." + esc + ";",
            "__ra" + member.Id + " = " + AfterField(member) + ";",
        ];

    /// <summary>Emits one presence-aware reconciler attempt with no-op normalization.</summary>
    internal static void AppendReconcilerAttempt(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string lit,
        string reconciler,
        string areEqual,
        string reasonFallback,
        string conflict,
        string conflictKind,
        string? optionalBoxType
    )
    {
        string Box(string expression) =>
            optionalBoxType is null
                ? "__SparseBox(" + expression + ")"
                : optionalBoxType + ".ToObject(" + expression + ")";

        code.AppendLine(
            "    if ("
                + reconciler
                + ".TryRebase(__base"
                + member.Id
                + ", __des"
                + member.Id
                + ", __curM"
                + member.Id
                + ", out var __reb"
                + member.Id
                + ", out var __reason"
                + member.Id
                + "))"
        );
        code.AppendLine("    {");
        code.AppendLine(
            "        if (!((!__reb"
                + member.Id
                + ".IsPresent && !__curM"
                + member.Id
                + ".IsPresent) || (__reb"
                + member.Id
                + ".IsPresent && __curM"
                + member.Id
                + ".IsPresent && "
                + areEqual
                + ")))"
        );
        code.AppendLine("        {");
        code.AppendLine("            __rh" + member.Id + " = true;");
        code.AppendLine("            __rb" + member.Id + " = __curM" + member.Id + ";");
        code.AppendLine("            __ra" + member.Id + " = __reb" + member.Id + ";");
        code.AppendLine("        }");
        code.AppendLine("    }");
        code.AppendLine("    else");
        code.AppendLine("    {");
        code.AppendLine(
            "        __conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + lit
                + "), "
                + conflictKind
                + ".CustomStrategy, "
                + Box("__base" + member.Id)
                + ", "
                + Box("__des" + member.Id)
                + ", "
                + Box("__curM" + member.Id)
                + ", __reason"
                + member.Id
                + " ?? \""
                + reasonFallback
                + "\"));"
        );
        code.AppendLine("    }");
    }

    /// <summary>
    /// Emits plain scalar rebase with the caller-wide fallback mode applied.
    /// </summary>
    /// <remarks>Members with an explicit rebase policy or merge strategy never reach this path.</remarks>
    internal static void AppendScalarModeRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string lit,
        string conflictKind,
        string conflict,
        string modeType,
        string? scalarRebaseType,
        string? optionalBoxType
    )
    {
        var id = member.Id;
        string Box(string expression) =>
            optionalBoxType is null
                ? "__SparseBox(" + expression + ")"
                : optionalBoxType + ".ToObject(" + expression + ")";

        if (scalarRebaseType is { Length: > 0 })
        {
            code.AppendLine(
                "    if ("
                    + scalarRebaseType
                    + ".TryResolve(__base"
                    + id
                    + ", __des"
                    + id
                    + ", __curM"
                    + id
                    + ", __mode, "
                    + modeType
                    + ".PreferIncoming, "
                    + modeType
                    + ".PreferCurrent, Fragment.__SparseEqual_"
                    + id
                    + ", out var __after"
                    + id
                    + ", out var __scalarConflict"
                    + id
                    + "))"
            );
            code.AppendLine("    {");
            code.AppendLine("        __rh" + id + " = true;");
            code.AppendLine("        __rb" + id + " = __curM" + id + ";");
            code.AppendLine("        __ra" + id + " = __after" + id + ";");
            code.AppendLine("    }");
            code.AppendLine("    else if (__scalarConflict" + id + ")");
            code.AppendLine("    {");
            code.AppendLine(
                "        __conflicts.Add(new "
                    + conflict
                    + "(__SparseRootPath.Member("
                    + lit
                    + "), "
                    + conflictKind
                    + ".Scalar, "
                    + Box("__base" + id)
                    + ", "
                    + Box("__des" + id)
                    + ", "
                    + Box("__curM" + id)
                    + ", \"The member conflicts with a concurrent change.\"));"
            );
            code.AppendLine("    }");
            return;
        }

        code.AppendLine(
            "    if (__mode"
                + " == "
                + modeType
                + ".PreferIncoming && !Fragment.__SparseEqual_"
                + id
                + "(__des"
                + id
                + ", __curM"
                + id
                + "))"
        );
        code.AppendLine("    {");
        code.AppendLine("        __rh" + id + " = true;");
        code.AppendLine("        __rb" + id + " = __curM" + id + ";");
        code.AppendLine("        __ra" + id + " = __des" + id + ";");
        code.AppendLine("    }");
        code.AppendLine(
            "    else if (__mode"
                + " != "
                + modeType
                + ".PreferCurrent && Fragment.__SparseEqual_"
                + id
                + "(__base"
                + id
                + ", __curM"
                + id
                + "))"
        );
        code.AppendLine("    {");
        code.AppendLine("        __rh" + id + " = true;");
        code.AppendLine("        __rb" + id + " = __curM" + id + ";");
        code.AppendLine("        __ra" + id + " = __des" + id + ";");
        code.AppendLine("    }");
        code.AppendLine(
            "    else if (__mode"
                + " != "
                + modeType
                + ".PreferCurrent && !Fragment.__SparseEqual_"
                + id
                + "(__des"
                + id
                + ", __curM"
                + id
                + "))"
        );
        code.AppendLine("    {");
        code.AppendLine(
            "        __conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + lit
                + "), "
                + conflictKind
                + ".Scalar, "
                + Box("__base" + id)
                + ", "
                + Box("__des" + id)
                + ", "
                + Box("__curM" + id)
                + ", \"The member conflicts with a concurrent change.\"));"
        );
        code.AppendLine("    }");
    }

    /// <summary>
    /// Emits merge-aware rebase for Append/SetUnion scalar-collection members.
    /// </summary>
    /// <remarks>Unlike plain scalar equality.</remarks>
    internal static void AppendMergeCollectionRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string esc,
        string lit,
        string runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var id = member.Id;
        var valueType = FragmentValueType(member);
        var elementType = member.Collection.ElementType.Name;
        var opt = runtime + "Optional<" + valueType + ">";
        var facade = dialect.RuntimeFacade;
        var comparer = dialect.RuntimeFacade;
        var conflict = dialect.ConflictType;
        var kind =
            member.MergeMode == SparseMergeModes.Append
                ? dialect.ConflictKindType + ".CollectionAppend"
                : dialect.ConflictKindType + ".CollectionSetUnion";
        var isSet =
            member.MergeMode == SparseMergeModes.SetUnion
            && member.Collection.CloneKind == SparseCloneCollectionKind.Set;
        var isTypedSequence =
            !isSet
            && member.Collection.CloneKind
                is SparseCloneCollectionKind.Array
                    or SparseCloneCollectionKind.List
            && member.Collection.ElementType.UsesDefaultScalarEquality;
        AppendRedactedGuard(
            code,
            member,
            lit,
            runtime,
            conflict,
            dialect,
            HasField(member),
            ScalarPassthrough(member, esc)
        );
        code.AppendLine("if (" + HasField(member) + " && !__red" + id + ")");
        code.OpenBlock();
        code.AppendLine("    var __base" + id + " = " + BeforeField(member) + ";");
        code.AppendLine("    var __des" + id + " = " + AfterField(member) + ";");
        code.AppendLine("    var __curM" + id + " = __cur." + esc + ";");
        code.AppendLine(
            "    if (Fragment.__SparseEqual_" + id + "(__base" + id + ", __curM" + id + "))"
        );
        code.AppendLine("    {");
        code.AppendLine("        __rh" + id + " = true;");
        code.AppendLine("        __rb" + id + " = __curM" + id + ";");
        code.AppendLine("        __ra" + id + " = __des" + id + ";");
        code.AppendLine("    }");
        code.AppendLine(
            "    else if (__base"
                + id
                + ".IsPresent && __des"
                + id
                + ".IsPresent && __curM"
                + id
                + ".IsPresent && (object?)__base"
                + id
                + ".Value is not null && (object?)__des"
                + id
                + ".Value is not null && (object?)__curM"
                + id
                + ".Value is not null)"
        );
        code.AppendLine("    {");
        code.AppendLine("        " + valueType + " __rebuilt" + id + " = default!;");
        code.AppendLine("        string? __reason" + id + ";");
        code.AppendLine("        bool __ok" + id + ";");
        if (isSet)
        {
            code.AppendLine(
                "        __ok"
                    + id
                    + " = "
                    + facade
                    + ".TryRebaseSetUnion<"
                    + elementType
                    + ">(__base"
                    + id
                    + ".Value!, __des"
                    + id
                    + ".Value!, __curM"
                    + id
                    + ".Value!, out var __rv"
                    + id
                    + ", out __reason"
                    + id
                    + ");"
            );
            code.AppendLine("        if (__ok" + id + ") __rebuilt" + id + " = __rv" + id + ";");
        }
        else if (isTypedSequence)
        {
            var typedMethod =
                member.MergeMode == SparseMergeModes.Append
                    ? "TryRebaseSequenceAppend"
                    : "TryRebaseSequenceSetUnion";
            if (member.Collection.CloneKind == SparseCloneCollectionKind.Array)
            {
                typedMethod += "Array";
            }

            var boxedMethod =
                member.MergeMode == SparseMergeModes.Append
                    ? "TryRebaseAppend"
                    : "TryRebaseSetUnion";
            var readOnly = "global::System.Collections.Generic.IReadOnlyList<" + elementType + ">";
            var list = "global::System.Collections.Generic.List<" + elementType + ">";
            string NativeInput(string state, string variable) =>
                "(object?)"
                + state
                + ".Value is "
                + readOnly
                + " "
                + variable
                + " && ("
                + variable
                + " is "
                + elementType
                + "[] || "
                + variable
                + ".GetType() == typeof("
                + list
                + "))";
            code.AppendLine(
                "        if ("
                    + NativeInput("__base" + id, "__bv" + id)
                    + " && "
                    + NativeInput("__des" + id, "__dv" + id)
                    + " && "
                    + NativeInput("__curM" + id, "__cv" + id)
                    + ")"
            );
            code.AppendLine("        {");
            code.AppendLine(
                "            __ok"
                    + id
                    + " = "
                    + facade
                    + "."
                    + typedMethod
                    + "<"
                    + elementType
                    + ">(__bv"
                    + id
                    + ", __dv"
                    + id
                    + ", __cv"
                    + id
                    + ", null, out var __tv"
                    + id
                    + ", out __reason"
                    + id
                    + ");"
            );
            code.AppendLine(
                "            if (__ok" + id + ") __rebuilt" + id + " = __tv" + id + ";"
            );
            code.AppendLine("        }");
            code.AppendLine("        else");
            code.AppendLine("        {");
            code.AppendLine(
                "            var __bb"
                    + id
                    + " = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)__base"
                    + id
                    + ".Value));"
            );
            code.AppendLine(
                "            var __cb"
                    + id
                    + " = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)__curM"
                    + id
                    + ".Value));"
            );
            code.AppendLine(
                "            var __db"
                    + id
                    + " = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)__des"
                    + id
                    + ".Value));"
            );
            code.AppendLine(
                "            __ok"
                    + id
                    + " = "
                    + facade
                    + "."
                    + boxedMethod
                    + "(__bb"
                    + id
                    + ", __db"
                    + id
                    + ", __cb"
                    + id
                    + ", (object? __l, object? __r) => "
                    + comparer
                    + ".AreEqual(__l, __r), out var __bx"
                    + id
                    + ", out __reason"
                    + id
                    + ");"
            );
            var boxedResult = SparseFragmentExpressions.MaterializeCollection(
                member,
                "global::System.Linq.Enumerable.Cast<" + elementType + ">(__bx" + id + ")"
            );
            code.AppendLine(
                "            if (__ok" + id + ") __rebuilt" + id + " = " + boxedResult + ";"
            );
            code.AppendLine("        }");
        }
        else
        {
            var boxedMethod =
                member.MergeMode == SparseMergeModes.Append
                    ? "TryRebaseAppend"
                    : "TryRebaseSetUnion";
            code.AppendLine(
                "        var __bb"
                    + id
                    + " = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)__base"
                    + id
                    + ".Value));"
            );
            code.AppendLine(
                "        var __cb"
                    + id
                    + " = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)__curM"
                    + id
                    + ".Value));"
            );
            code.AppendLine(
                "        var __db"
                    + id
                    + " = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)__des"
                    + id
                    + ".Value));"
            );
            code.AppendLine(
                "        __ok"
                    + id
                    + " = "
                    + facade
                    + "."
                    + boxedMethod
                    + "(__bb"
                    + id
                    + ", __db"
                    + id
                    + ", __cb"
                    + id
                    + ", (object? __l, object? __r) => "
                    + comparer
                    + ".AreEqual(__l, __r), out var __bx"
                    + id
                    + ", out __reason"
                    + id
                    + ");"
            );
            var boxedResult = SparseFragmentExpressions.MaterializeCollection(
                member,
                "global::System.Linq.Enumerable.Cast<" + elementType + ">(__bx" + id + ")"
            );
            code.AppendLine(
                "        if (__ok" + id + ") __rebuilt" + id + " = " + boxedResult + ";"
            );
        }
        code.AppendLine("        if (__ok" + id + ")");
        code.AppendLine("        {");
        code.AppendLine(
            "            " + opt + " __rebOpt" + id + " = " + opt + ".Present(__rebuilt" + id + ");"
        );
        code.AppendLine(
            "            if (!Fragment.__SparseEqual_"
                + id
                + "(__curM"
                + id
                + ", __rebOpt"
                + id
                + "))"
        );
        code.AppendLine("            {");
        code.AppendLine("                __rh" + id + " = true;");
        code.AppendLine("                __rb" + id + " = __curM" + id + ";");
        code.AppendLine("                __ra" + id + " = __rebOpt" + id + ";");
        code.AppendLine("            }");
        code.AppendLine("        }");
        code.AppendLine("        else");
        code.AppendLine("        {");
        code.AppendLine(
            "            __conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + lit
                + "), "
                + kind
                + ", "
                + BoxExpression(dialect, "__base" + id)
                + ", "
                + BoxExpression(dialect, "__des" + id)
                + ", "
                + BoxExpression(dialect, "__curM" + id)
                + ", __reason"
                + id
                + " ?? \"The member conflicts with a concurrent change.\"));"
        );
        code.AppendLine("        }");
        code.AppendLine("    }");
        code.AppendLine(
            "    else if (!Fragment.__SparseEqual_" + id + "(__des" + id + ", __curM" + id + "))"
        );
        code.AppendLine("    {");
        code.AppendLine(
            "        __conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + lit
                + "), "
                + kind
                + ", "
                + BoxExpression(dialect, "__base" + id)
                + ", "
                + BoxExpression(dialect, "__des" + id)
                + ", "
                + BoxExpression(dialect, "__curM" + id)
                + ", \"The member conflicts with a concurrent change.\"));"
        );
        code.AppendLine("    }");
        code.CloseBlock();
    }
}
