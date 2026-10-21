using System.Collections.Immutable;
using System.Linq;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits model/fragment conversion: <c>From</c>, <c>ToModel</c> and projection constructors.</summary>
internal sealed class SparseFragmentConversionEmitter
{
    private string Optional { get; }
    private string CloneContext { get; }
    private string ReferenceComparer { get; }
    private SparseFragmentExpressions Expressions { get; }

    public SparseFragmentConversionEmitter(
        string optional,
        string cloneContext,
        string referenceComparer,
        SparseFragmentExpressions expressions
    )
    {
        Optional = optional;
        CloneContext = cloneContext;
        ReferenceComparer = referenceComparer;
        Expressions = expressions;
    }

    public void AppendFromModel(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType,
        bool usesPocoCloning,
        string? operationsType = null
    )
    {
        // Stage 4: facades delegate both From overloads; bodies move verbatim
        // (static, explicit receivers, Fragment aliases in scope).
        if (operationsType is not null)
        {
            code.AppendLine(
                "/// <summary>Creates a fragment with every member present from a model value.</summary>"
            );
            code.AppendLine("/// <param name=\"value\">The model value to project.</param>");
            code.AppendLine("/// <returns>The fragment with all members present.</returns>");
            code.AppendIndent()
                .Append("public static Fragment From(")
                .Append(modelType)
                .Append(" value) => ")
                .Append(operationsType)
                .AppendLine(".From(value);");
            return;
        }
        var requiresContext = members.Any(static member =>
            member.ChildModel is not null
            || member.Property.Type.PocoCloneHelperName is not null
            || member.Collection.CloneKind != SparseCloneCollectionKind.Unsupported
        );
        AppendFromModelPublicBody(code, modelType, members, modelIsReferenceType, requiresContext);
        AppendFromModelInternalBody(code, modelType, members, modelIsReferenceType);
    }

    /// <summary>Emits the public From body preceding internal helpers.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="modelType">Model type name.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="modelIsReferenceType">Whether the model is a reference type.</param>
    /// <param name="requiresContext">Whether cycle context is required.</param>
    public void AppendFromModelPublicBody(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType,
        bool requiresContext
    )
    {
        code.AppendLine(
            "/// <summary>Creates a fragment with every member present from a model value.</summary>"
        );
        code.AppendLine("/// <param name=\"value\">The model value to project.</param>");
        code.AppendLine("/// <returns>The fragment with all members present.</returns>");
        code.AppendIndent()
            .Append("public static Fragment From(")
            .Append(modelType)
            .AppendLine(" value)");
        code.OpenBlock();
        if (modelIsReferenceType)
        {
            SparseFragmentEmitHelpers.AppendNullGuard(code, "value");
        }

        if (requiresContext)
        {
            Expressions.AppendCloneContext(code, ReferenceComparer);
            if (members.Any(static member => member.ChildModel is not null))
            {
                code.AppendLine(
                    "var __sparse_from_context = "
                        + ReferenceComparer
                        + ".CreateFromCycleContext();"
                );
                code.AppendLine(
                    "return From(value, " + CloneContext + ", __sparse_from_context, \"\");"
                );
            }
            else
            {
                code.AppendLine("return From(value, " + CloneContext + ");");
            }
        }
        else
        {
            AppendFromModelBody(code, members);
        }
        code.CloseBlock();
        code.AppendLine();
    }

    /// <summary>Emits the internal From body trailing public operations.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="modelType">Model type name.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="modelIsReferenceType">Whether the model is a reference type.</param>
    public void AppendFromModelInternalBody(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType
    )
    {
        var requiresFromCycleContext = members.Any(static member => member.ChildModel is not null);
        code.AppendIndent()
            .Append("internal static Fragment From(")
            .Append(modelType)
            .Append(" value, global::System.Collections.Generic.Dictionary<object, object> ")
            .Append(CloneContext);
        if (requiresFromCycleContext)
        {
            code.Append(
                ", global::System.Collections.Generic.HashSet<object> __sparse_from_context, string __sparse_from_path"
            );
        }
        code.AppendLine(")");
        code.OpenBlock();
        if (modelIsReferenceType)
        {
            SparseFragmentEmitHelpers.AppendNullGuard(code, "value");
        }
        if (modelIsReferenceType && members.Any(static member => member.ChildModel is not null))
        {
            code.AppendLine("if (!__sparse_from_context.Add(value))");
            code.OpenBlock();
            code.AppendLine(
                "throw new global::System.NotSupportedException(\"Cyclic reference detected during From at '\" + __sparse_from_path + \"'. Fragment.From does not support cyclic object graphs; DeepClone preserves cycles.\");"
            );
            code.CloseBlock();
            code.AppendLine("try");
            code.OpenBlock();
            AppendFromModelBody(code, members);
            code.CloseBlock();
            code.AppendLine("finally");
            code.OpenBlock();
            code.AppendLine("__sparse_from_context.Remove(value);");
            code.CloseBlock();
        }
        else
        {
            AppendFromModelBody(code, members);
        }
        code.CloseBlock();
        code.AppendLine();
    }

    /// <summary>Emits the internal From facade trailing the public fragment surface.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="modelType">Model type name.</param>
    /// <param name="operationsType">Operations class qualifying the delegate.</param>
    public static void AppendFromModelInternalFacade(
        SharedIndentedBuilder code,
        string modelType,
        string operationsType,
        bool requiresFromCycleContext
    )
    {
        code.AppendIndent()
            .Append("internal static Fragment From(")
            .Append(modelType)
            .Append(
                " value, global::System.Collections.Generic.Dictionary<object, object> __sparse_clone_context"
            );
        if (requiresFromCycleContext)
        {
            code.Append(
                ", global::System.Collections.Generic.HashSet<object> __sparse_from_context, string __sparse_from_path"
            );
        }
        code.Append(") => ")
            .Append(operationsType)
            .AppendLine(
                requiresFromCycleContext
                    ? ".From(value, __sparse_clone_context, __sparse_from_context, __sparse_from_path);"
                    : ".From(value, __sparse_clone_context);"
            );
    }

    private void AppendFromModelBody(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members
    )
    {
        code.AppendLine("return new Fragment");
        code.OpenBlock();
        foreach (var member in members)
        {
            var access = "value." + SparseNaming.EscapeIdentifier(member.Property.Name);
            string value;
            if (member.ChildModel is null)
            {
                value = Expressions.CloneModelExpression(member, access);
            }
            else if (!member.ChildIsReferenceType)
            {
                value = member.ChildRequiresFromCycleContext
                    ? $"{member.ChildFragmentType}.From({access}, {CloneContext}, __sparse_from_context, {FromMemberPath(member)})"
                    : $"{member.ChildFragmentType}.From({access}, {CloneContext})";
            }
            else
            {
                value = member.ChildRequiresFromCycleContext
                    ? $"({access} is null ? null : {member.ChildFragmentType}.From({access}, {CloneContext}, __sparse_from_context, {FromMemberPath(member)}))"
                    : $"({access} is null ? null : {member.ChildFragmentType}.From({access}, {CloneContext}))";
            }

            code.AppendIndent()
                .Append(SparseNaming.EscapeIdentifier(member.Property.Name))
                .Append(" = ")
                .Append(Optional)
                .Append("<")
                .Append(SparseFragmentEmitHelpers.FragmentValueType(member))
                .Append(">.Present(")
                .Append(value)
                .AppendLine("),");
        }

        code.AppendLine("};");
    }

    private static string FromMemberPath(SparseMemberModel member)
    {
        var escaped = member.Property.Name.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return "(__sparse_from_path.Length == 0 ? \""
            + escaped
            + "\" : __sparse_from_path + \"."
            + escaped
            + "\")";
    }

    public static void AppendRootProjectionConstructor(
        SharedIndentedBuilder code,
        string modelName,
        ImmutableArray<SparseMemberModel> members,
        ModelConstructorBinding? constructor = null,
        string bridgeAccessibility = "private"
    )
    {
        if (
            ModelConstructionPlan.ForMembers(members).CanOverlayAfterConstruction
            && (constructor is null || constructor.Parameters.IsEmpty)
        )
        {
            return;
        }
        // Stage 4: split emission widens the token and constructor to the
        // Use the narrowest visibility that lets the operations class call it.
        code.AppendLine(bridgeAccessibility + " readonly struct __SparseProjectionToken { }");
        if (constructor?.IsImplicitParameterlessClassConstructor == true)
        {
            code.AppendLine("public " + modelName + "() { }");
        }

        if (members.Any(static member => member.Property.IsRequired))
        {
            code.AppendLine("[global::System.Diagnostics.CodeAnalysis.SetsRequiredMembers]");
        }

        var arguments = constructor is null
            ? string.Empty
            : string.Join(
                ", ",
                constructor.Parameters.Select(parameter =>
                {
                    var member = members.FirstOrDefault(candidate =>
                        candidate.Property.Name == parameter.PropertyName
                    );
                    if (member.Property.Name is null)
                    {
                        return parameter.DefaultExpression;
                    }

                    var access =
                        "__sparse_projection."
                        + SparseNaming.EscapeIdentifier(parameter.PropertyName);
                    var value = access + ".Value!";
                    if (member.ChildModel is not null)
                    {
                        value = member.ChildIsReferenceType
                            ? access + ".Value?.ToModel()!"
                            : access + ".Value!.ToModel()";
                    }

                    return access + ".IsPresent ? " + value + " : " + parameter.DefaultExpression;
                })
            );
        code.AppendLine(
            bridgeAccessibility
                + " "
                + modelName
                + "(Fragment __sparse_projection, __SparseProjectionToken _) : this("
                + arguments
                + ")"
        );
        code.OpenBlock();
        foreach (var member in members.Where(static member => !member.Property.IsReadOnly))
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            var access = "__sparse_projection." + name;
            var value = access + ".Value!";
            if (member.ChildModel is not null)
            {
                value = member.ChildIsReferenceType
                    ? access + ".Value?.ToModel()!"
                    : access + ".Value!.ToModel()";
            }

            if (member.Property.IsRequired)
            {
                code.AppendLine(
                    "this."
                        + name
                        + " = "
                        + access
                        + ".IsPresent ? "
                        + value
                        + " : this."
                        + name
                        + "!;"
                );
            }
            else
            {
                code.AppendLine("if (" + access + ".IsPresent) this." + name + " = " + value + ";");
            }
        }
        code.CloseBlock();
    }

    public static void AppendToModel(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool hasRootProjectionConstructor = false,
        ModelConstructorBinding? constructor = null,
        string? operationsType = null,
        string receiver = ""
    )
    {
        // Stage 4: facades delegate both ToModel overloads; bodies move
        // with an explicit receiver (surface instance access is bare).
        if (operationsType is not null)
        {
            code.AppendLine(
                "/// <summary>Materializes a model by overlaying this fragment onto a baseline value.</summary>"
            );
            code.AppendLine(
                "/// <param name=\"baseline\">The baseline value supplying absent members.</param>"
            );
            code.AppendLine("/// <returns>The materialized model value.</returns>");
            code.AppendIndent()
                .Append("public ")
                .Append(modelType)
                .Append(" ToModel(")
                .Append(modelType)
                .Append(" baseline) => ")
                .Append(operationsType)
                .AppendLine(".ToModel(this, baseline);");
            code.AppendLine(
                "/// <summary>Materializes a model from the present members of this fragment.</summary>"
            );
            code.AppendLine("/// <returns>The materialized model value.</returns>");
            code.AppendIndent()
                .Append("public ")
                .Append(modelType)
                .AppendLine(" ToModel() => " + operationsType + ".ToModel(this);");
            return;
        }
        var isOperationsBody = receiver.Length != 0;
        code.AppendLine(
            "/// <summary>Materializes a model by overlaying a fragment onto a baseline value.</summary>"
        );
        if (isOperationsBody)
        {
            code.AppendLine("/// <param name=\"fragment\">The fragment to overlay.</param>");
        }

        code.AppendLine(
            "/// <param name=\"baseline\">The baseline value supplying absent members.</param>"
        );
        code.AppendLine("/// <returns>The materialized model value.</returns>");
        if (isOperationsBody)
        {
            code.AppendIndent()
                .Append("public static ")
                .Append(modelType)
                .Append(" ToModel(Fragment fragment, ")
                .Append(modelType)
                .AppendLine(" baseline)");
        }
        else
        {
            code.AppendIndent()
                .Append("public ")
                .Append(modelType)
                .Append(" ToModel(")
                .Append(modelType)
                .AppendLine(" baseline)");
        }
        code.OpenBlock();
        code.AppendLine(
            isOperationsBody
                ? "return ToModel(Merge(From(baseline), fragment));"
                : "return From(baseline).Merge(this).ToModel();"
        );
        code.CloseBlock();
        code.AppendLine();
        code.AppendLine(
            "/// <summary>Materializes a model from the present members of a fragment.</summary>"
        );
        if (isOperationsBody)
        {
            code.AppendLine("/// <param name=\"fragment\">The fragment to materialize.</param>");
        }

        code.AppendLine("/// <returns>The materialized model value.</returns>");
        if (isOperationsBody)
        {
            code.AppendIndent()
                .Append("public static ")
                .Append(modelType)
                .AppendLine(" ToModel(Fragment fragment)");
        }
        else
        {
            code.AppendIndent().Append("public ").Append(modelType).AppendLine(" ToModel()");
        }

        code.OpenBlock();
        var construction = hasRootProjectionConstructor
            ? ModelConstructionPlan.ForMembers(members)
            : ModelConstructionPlan.ForStructuralMembers(members, constructor);
        if (
            hasRootProjectionConstructor
            && (
                !construction.CanOverlayAfterConstruction
                || (constructor is not null && !constructor.Parameters.IsEmpty)
            )
        )
        {
            // Stage 4: split emission widens the projection bridge so
            // the operations class can invoke it; single-file keeps it private.
            var token =
                receiver.Length == 0
                    ? "__SparseProjectionToken"
                    : modelType + ".__SparseProjectionToken";
            var source = receiver.Length == 0 ? "this" : "fragment";
            code.AppendLine(
                "return new " + modelType + "(" + source + ", default(" + token + "));"
            );
            code.CloseBlock();
            code.AppendLine();
            return;
        }
        if (construction.CanOverlayAfterConstruction)
        {
            var arguments = constructor is null
                ? string.Empty
                : string.Join(
                    ", ",
                    constructor.Parameters.Select(parameter =>
                    {
                        var member = members.FirstOrDefault(candidate =>
                            candidate.Property.Name == parameter.PropertyName
                        );
                        if (member.Property.Name is null)
                        {
                            return parameter.DefaultExpression;
                        }

                        var name = SparseNaming.EscapeIdentifier(parameter.PropertyName);
                        var access = receiver + name;
                        var projected = access + ".Value!";
                        if (member.ChildModel is not null)
                        {
                            projected = member.ChildIsReferenceType
                                ? access + ".Value?.ToModel()!"
                                : access + ".Value!.ToModel()";
                        }

                        return access
                            + ".IsPresent ? "
                            + projected
                            + " : "
                            + parameter.DefaultExpression;
                    })
                );
            code.AppendLine("var value = new " + modelType + "(" + arguments + ");");
            foreach (
                var member in members.Where(static member =>
                    !member.Property.IsReadOnly && !member.Property.IsInitOnly
                )
            )
            {
                var name = SparseNaming.EscapeIdentifier(member.Property.Name);
                var access = receiver + name;
                var projected = access + ".Value!";
                if (member.ChildModel is not null)
                {
                    projected = member.ChildIsReferenceType
                        ? access + ".Value?.ToModel()!"
                        : access + ".Value!.ToModel()";
                }

                code.AppendLine(
                    "if (" + access + ".IsPresent) value." + name + " = " + projected + ";"
                );
            }
            code.AppendLine("return value;");
            code.CloseBlock();
            code.AppendLine();
            return;
        }
        if (!members.IsEmpty)
        {
            code.AppendIndent().Append("if (");
            for (var index = 0; index < members.Length; index++)
            {
                if (index > 0)
                {
                    code.Append(" && ");
                }

                code.Append(receiver)
                    .Append(SparseNaming.EscapeIdentifier(members[index].Property.Name))
                    .Append(".IsPresent");
            }

            code.AppendLine(")");
            code.OpenBlock();
            AppendModelInitializer(code, modelType, members, false, receiver);
            code.CloseBlock();
        }

        code.AppendIndent().Append("var defaults = new ").Append(modelType).AppendLine("();");
        AppendModelInitializer(code, modelType, members, true, receiver);
        code.CloseBlock();
        code.AppendLine();
    }

    private static void AppendModelInitializer(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool useDefaults,
        string receiver = ""
    )
    {
        code.AppendIndent().Append("return new ").Append(modelType).AppendLine();
        code.OpenBlock();
        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            var access = receiver + name;
            string value;
            if (member.ChildModel is null)
            {
                value = access + ".Value!";
            }
            else if (!member.ChildIsReferenceType)
            {
                value = access + ".Value!.ToModel()";
            }
            else
            {
                value = access + ".Value?.ToModel()!";
            }

            code.AppendIndent()
                .Append(name)
                .Append(" = ")
                .Append(
                    useDefaults ? access + ".IsPresent ? " + value + " : defaults." + name : value
                )
                .AppendLine(",");
        }

        code.AppendLine("};");
    }
}
