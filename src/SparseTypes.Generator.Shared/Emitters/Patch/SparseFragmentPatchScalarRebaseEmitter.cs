using Microsoft.CodeAnalysis.CSharp;

namespace SparseTypes.Generator.Shared;

/// <summary>Scalar and custom-strategy member rebase for generated patches.</summary>
internal static class SparseFragmentPatchScalarRebaseEmitter
{
    internal static void AppendCustomStrategyMemberRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string field,
        string kind,
        string baseMember,
        string desiredMember,
        string currentMember,
        string conflict,
        string conflictKind,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var operationType =
            SparseFragmentPatchEmitter.Operation(dialect)
            + "<"
            + SparseFragmentPatchEmitter.GetMemberValueType(dialect, member)
            + ">";
        // A member-level rebase policy wins over the merge strategy's
        // TryRebase; the strategy still owns Merge.
        var reconcilerField =
            "Fragment." + SparseFragmentPatchEmitter.GetMergeStrategyField(dialect, member);
        var reconcilerFallback = "The custom merge strategy could not rebase the member.";
        if (member.RebasePolicyType is not null)
        {
            reconcilerField =
                "Fragment." + SparseFragmentPatchEmitter.GetRebasePolicyField(dialect, member);
            reconcilerFallback = "The custom rebase policy could not rebase the member.";
        }
        var reconcilerPathLit = SymbolDisplay.FormatLiteral(member.Property.Name, true);
        code.AppendLine(
            "if (local."
                + field
                + ".Kind != "
                + kind
                + ".Keep && __SparseIsRedacted(options, "
                + reconcilerPathLit
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
            "__SparseRootPath.Member(" + reconcilerPathLit + ")",
            "conflicts"
        );
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine("result." + field + " = local." + field + ";");
        code.CloseBlock();
        code.CloseBlock();
        // Custom strategies observe every member edit (Set and Remove).
        code.AppendLine(
            "if (local."
                + field
                + ".Kind != "
                + kind
                + ".Keep && !__SparseIsRedacted(options, "
                + reconcilerPathLit
                + "))"
        );
        code.OpenBlock();
        code.AppendLine("var " + baseMember + " = baseFragment." + name + ";");
        code.AppendLine("var " + currentMember + " = currentFragment." + name + ";");
        code.AppendLine(
            "var " + desiredMember + " = local." + field + ".Apply(" + baseMember + ");"
        );
        var strategyField = reconcilerField;
        var strategyFallback = reconcilerFallback;
        code.AppendLine(
            "if ("
                + strategyField
                + ".TryRebase("
                + baseMember
                + ", "
                + desiredMember
                + ", "
                + currentMember
                + ", out var rebasedValue, out var reason))"
        );
        code.OpenBlock();
        code.AppendLine(
            "if (!((!rebasedValue.IsPresent && !"
                + currentMember
                + ".IsPresent) || (rebasedValue.IsPresent && "
                + currentMember
                + ".IsPresent && "
                + strategyField
                + ".AreEqual("
                + currentMember
                + ".Value, rebasedValue.Value))))"
        );
        code.OpenBlock();
        code.AppendLine("if (rebasedValue.IsPresent)");
        code.OpenBlock();
        code.AppendLine("result." + field + " = " + operationType + ".Set(rebasedValue.Value);");
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine("result." + field + " = " + operationType + ".Remove;");
        code.CloseBlock();
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        code.AppendLine(
            "conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                + "), "
                + conflictKind
                + ".CustomStrategy, __SparseMember("
                + baseMember
                + "), __SparseMember("
                + desiredMember
                + "), __SparseMember("
                + currentMember
                + "), reason ?? \""
                + strategyFallback
                + "\"));"
        );
        code.CloseBlock();
        code.CloseBlock();
    }

    internal static void EmitScalarRebase(
        SharedIndentedBuilder code,
        string field,
        string memberName,
        string baseMember,
        string desiredMember,
        string currentMember,
        string equality,
        string conflictKind,
        string conflict
    )
    {
        code.AppendLine("if (" + equality + "(" + baseMember + ", " + currentMember + "))");
        code.OpenBlock();
        code.AppendLine("result." + field + " = local." + field + ";");
        code.CloseBlock();
        code.AppendLine(
            "else if (!"
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
                + SymbolDisplay.FormatLiteral(memberName, true)
                + "), "
                + conflictKind
                + ", __SparseMember("
                + baseMember
                + "), __SparseMember("
                + desiredMember
                + "), __SparseMember("
                + currentMember
                + "), \"The member conflicts with a concurrent change.\"));"
        );
        code.CloseBlock();
    }
}
