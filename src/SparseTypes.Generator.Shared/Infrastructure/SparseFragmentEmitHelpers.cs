namespace SparseTypes.Generator.Shared;

/// <summary>Small shared primitives for fragment-stage emitters.</summary>
internal static class SparseFragmentEmitHelpers
{
    internal static string FragmentValueType(SparseMemberModel member) =>
        member.ChildModel is null ? member.Property.Type.Name : member.ChildFragmentType + "?";

    internal static string MergeStrategyField(string prefix, SparseMemberModel member) => prefix + member.Id;

    internal static string ComparisonComparerField(SparseMemberModel member) => "__sparse_comparison_" + member.Id;

    internal static void AppendCloneContext(SharedIndentedBuilder code, string cloneContext, string referenceComparer) =>
        code.AppendLine("var " + cloneContext + " = " + referenceComparer + ".CreateCloneContext();");

    internal static void AppendNullGuard(SharedIndentedBuilder code, string variable)
    {
        code.AppendLine("if (" + variable + " is null)");
        code.OpenBlock();
        code.AppendLine("throw new global::System.ArgumentNullException(nameof(" + variable + "));");
        code.CloseBlock();
    }
}
