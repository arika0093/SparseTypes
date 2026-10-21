namespace SparseTypes.Generator.Shared;

/// <summary>Typed and boxed collection rebase for standalone patch members.</summary>
internal static class SparseFragmentPatchCollectionRebaseEmitter
{
    internal static void AppendTypedSetUnionRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string field,
        string operation,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        code.AppendLine("var beforeValues = baseMember.Value!;");
        code.AppendLine("var currentValues = currentMember.Value!;");
        code.AppendLine("var desiredValues = desiredMember.Value!;");
        code.AppendLine(
            "if ("
                + dialect.RuntimeFacade
                + ".TryRebaseSetUnion<"
                + member.Collection.ElementType.Name
                + ">(beforeValues, desiredValues, currentValues, out var rebasedValues, out var reason))"
        );
        code.OpenBlock();
        var operationType =
            operation + "<" + SparseFragmentPatchEmitter.GetMemberValueType(dialect, member) + ">";
        code.AppendLine("result." + field + " = " + operationType + ".Set(rebasedValues);");
    }

    internal static void AppendTypedSequenceRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string field,
        string operation,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var elementType = member.Collection.ElementType.Name;
        var valueType = SparseFragmentPatchEmitter.GetMemberValueType(dialect, member);
        var listType = $"global::System.Collections.Generic.List<{elementType}>";
        var readOnlyType = $"global::System.Collections.Generic.IReadOnlyList<{elementType}>";
        var typedMethod =
            member.MergeMode == SparseMergeModes.Append
                ? "TryRebaseSequenceAppend"
                : "TryRebaseSequenceSetUnion";
        if (member.Collection.CloneKind == SparseCloneCollectionKind.Array)
        {
            typedMethod += "Array";
        }
        var boxedMethod =
            member.MergeMode == SparseMergeModes.Append ? "TryRebaseAppend" : "TryRebaseSetUnion";
        string NativeInput(string state, string variable) =>
            $"(object?){state}.Value is {readOnlyType} {variable} && ({variable} is {elementType}[] || {variable}.GetType() == typeof({listType}))";

        code.AppendLine($"{valueType} __rebasedCollection = default!;");
        code.AppendLine("string? reason;");
        code.AppendLine("bool __rebaseSucceeded;");
        code.AppendLine(
            "if ("
                + NativeInput("baseMember", "beforeValues")
                + " && "
                + NativeInput("desiredMember", "desiredValues")
                + " && "
                + NativeInput("currentMember", "currentValues")
                + ")"
        );
        code.OpenBlock();
        code.AppendLine(
            $"__rebaseSucceeded = {dialect.RuntimeFacade}.{typedMethod}<{elementType}>(beforeValues, desiredValues, currentValues, null, out var __typedValues, out reason);"
        );
        code.AppendLine("if (__rebaseSucceeded) __rebasedCollection = __typedValues;");
        code.CloseBlock();
        code.AppendLine("else");
        code.OpenBlock();
        foreach (var state in new[] { "before", "current", "desired" })
        {
            var memberState = state == "before" ? "baseMember" : state + "Member";
            code.AppendLine(
                $"var {state}Boxed = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable){memberState}.Value));"
            );
        }
        code.AppendLine(
            $"__rebaseSucceeded = {dialect.RuntimeFacade}.{boxedMethod}(beforeBoxed, desiredBoxed, currentBoxed, (object? left, object? right) => {dialect.RuntimeFacade}.AreEqual(left, right), out var __boxedValues, out reason);"
        );
        var boxedResult = SparseFragmentExpressions.MaterializeCollection(
            member,
            $"global::System.Linq.Enumerable.Cast<{elementType}>(__boxedValues)"
        );
        code.AppendLine($"if (__rebaseSucceeded) __rebasedCollection = {boxedResult};");
        code.CloseBlock();
        code.AppendLine("if (__rebaseSucceeded)");
        code.OpenBlock();
        code.AppendLine($"result.{field} = {operation}<{valueType}>.Set(__rebasedCollection);");
    }

    internal static void AppendBoxedCollectionRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string field,
        string operation,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        code.AppendLine(
            "var beforeValues = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)baseMember.Value));"
        );
        code.AppendLine(
            "var currentValues = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)currentMember.Value));"
        );
        code.AppendLine(
            "var desiredValues = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)desiredMember.Value));"
        );
        code.AppendLine(
            "if ("
                + dialect.RuntimeFacade
                + "."
                + (
                    member.MergeMode == SparseMergeModes.Append
                        ? "TryRebaseAppend"
                        : "TryRebaseSetUnion"
                )
                + "(beforeValues, desiredValues, currentValues, (object? left, object? right) => "
                + dialect.RuntimeFacade
                + ".AreEqual(left, right), out var rebasedValues, out var reason))"
        );
        code.OpenBlock();
        var operationType =
            operation + "<" + SparseFragmentPatchEmitter.GetMemberValueType(dialect, member) + ">";
        var materialized = SparseFragmentExpressions.MaterializeCollection(
            member,
            "global::System.Linq.Enumerable.Cast<"
                + member.Collection.ElementType.Name
                + ">(rebasedValues)"
        );
        code.AppendLine("result." + field + " = " + operationType + ".Set(" + materialized + ");");
    }
}
