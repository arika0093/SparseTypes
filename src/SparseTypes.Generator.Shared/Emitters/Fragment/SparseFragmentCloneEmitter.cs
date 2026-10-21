using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits deep-clone support: model <c>DeepClone</c>, POCO helpers and fragment cloning.</summary>
internal sealed class SparseFragmentCloneEmitter
{
    private string Optional { get; }
    private string CloneContext { get; }
    private string ReferenceComparer { get; }
    private SparseFragmentExpressions Expressions { get; }
    private SparseFragmentOriginEmitter? Origins { get; }

    public SparseFragmentCloneEmitter(
        string optional,
        string cloneContext,
        string referenceComparer,
        SparseFragmentExpressions expressions,
        SparseFragmentOriginEmitter? originEmitter = null
    )
    {
        Optional = optional;
        CloneContext = cloneContext;
        ReferenceComparer = referenceComparer;
        Expressions = expressions;
        Origins = originEmitter;
    }

    public void AppendDeepClone(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool usesPocoCloning,
        ModelConstructorBinding? constructor = null,
        bool modelIsReferenceType = true,
        string? operationsType = null,
        string receiver = "this."
    )
    {
        // Stage 4: facades delegate both DeepClone overloads; bodies move with
        // an explicit receiver (surface construction is `this`).
        if (operationsType is not null)
        {
            AppendDeepClonePublicFacade(code, modelType, operationsType);
            return;
        }
        code.CancellationToken.ThrowIfCancellationRequested();
        var isOperationsBody = !string.Equals(receiver, "this.", StringComparison.Ordinal);
        if (isOperationsBody)
        {
            code.AppendLine("/// <summary>Creates a deep copy of the specified model value.</summary>");
            code.AppendLine("/// <param name=\"value\">The model value to copy.</param>");
            code.AppendLine("/// <returns>The deep copy.</returns>");
            code.AppendIndent().Append("public static ").Append(modelType).Append(" DeepClone(").Append(modelType).AppendLine(" value)");
        }
        else
        {
            code.AppendLine("/// <summary>Creates a deep copy of this model value.</summary>");
            code.AppendLine("/// <returns>The deep copy.</returns>");
            code.AppendIndent().Append("public ").Append(modelType).AppendLine(" DeepClone()");
        }
        code.OpenBlock();
        if (RequiresCloneContext(members))
        {
            Expressions.AppendCloneContext(code, ReferenceComparer);
            code.AppendLine(isOperationsBody ? "return DeepClone(value, " + CloneContext + ");" : "return DeepClone(" + CloneContext + ");");
        }
        else
        {
            AppendModelCloneBody(code, modelType, members, constructor, modelIsReferenceType, registerClone: false, receiver: receiver);
        }
        code.CloseBlock();
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        if (isOperationsBody)
        {
            code.AppendIndent()
                .Append("internal static ")
                .Append(modelType)
                .Append(" DeepClone(")
                .Append(modelType)
                .Append(" value, global::System.Collections.Generic.Dictionary<object, object> ")
                .Append(CloneContext)
                .AppendLine(")");
        }
        else
        {
            code.AppendIndent()
                .Append("internal ")
                .Append(modelType)
                .Append(" DeepClone(global::System.Collections.Generic.Dictionary<object, object> ")
                .Append(CloneContext)
                .AppendLine(")");
        }
        code.OpenBlock();
        if (modelIsReferenceType)
        {
            code.AppendLine(
                "if ("
                    + CloneContext
                    + ".TryGetValue("
                    + (isOperationsBody ? "value" : "this")
                    + ", out var existing)) return ("
                    + modelType
                    + ")existing;"
            );
        }

        AppendModelCloneBody(code, modelType, members, constructor, modelIsReferenceType, registerClone: modelIsReferenceType, receiver: receiver);
        code.CloseBlock();
    }

    private void AppendModelCloneBody(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        ModelConstructorBinding? constructor,
        bool modelIsReferenceType,
        bool registerClone,
        string receiver = "this."
    )
    {
        // Operations bodies address the source through the receiver; the
        // cycle registry likewise tracks the receiver, not `this`.
        var self = receiver.TrimEnd('.');
        if (
            modelIsReferenceType
            && (constructor is null || constructor.Parameters.IsEmpty)
            && ModelConstructionPlan.ForMembers(members).CanOverlayAfterConstruction
        )
        {
            code.AppendLine("var clone = new " + modelType + "();");
            if (registerClone)
            {
                code.AppendLine(CloneContext + ".Add(" + self + ", clone);");
            }
            foreach (var member in members.Where(static member => !member.Property.IsReadOnly))
            {
                var name = SparseNaming.EscapeIdentifier(member.Property.Name);
                code.AppendLine("clone." + name + " = " + Expressions.CloneModelExpression(member, receiver + name) + ";");
            }
            code.AppendLine("return clone;");
            return;
        }

        var boundClones = new Dictionary<string, string>(StringComparer.Ordinal);
        if (constructor is not null)
        {
            foreach (var propertyName in constructor.Parameters.Select(static parameter => parameter.PropertyName))
            {
                if (boundClones.ContainsKey(propertyName))
                {
                    continue;
                }

                var member = members.Single(candidate => candidate.Property.Name == propertyName);
                var local = "__constructor_clone_" + boundClones.Count;
                boundClones.Add(propertyName, local);
                code.AppendLine(
                    "var " + local + " = " + Expressions.CloneModelExpression(member, receiver + SparseNaming.EscapeIdentifier(propertyName)) + ";"
                );
            }
        }
        var arguments = constructor is null
            ? string.Empty
            : string.Join(", ", constructor.Parameters.Select(parameter => boundClones[parameter.PropertyName]));
        // Register the clone before setting mutable members so reference cycles
        // resolve to it.
        var initializerMembers = members
            .Where(static member => !member.Property.IsReadOnly && (member.Property.IsInitOnly || member.Property.IsRequired))
            .ToArray();
        var deferredMembers = members
            .Where(static member => !member.Property.IsReadOnly && !member.Property.IsInitOnly && !member.Property.IsRequired)
            .ToArray();
        code.AppendIndent().Append("var clone = new ").Append(modelType).Append("(").Append(arguments).AppendLine(")");
        if (initializerMembers.Length > 0)
        {
            code.OpenBlock();
            foreach (var member in initializerMembers)
            {
                code.AppendIndent()
                    .Append(SparseNaming.EscapeIdentifier(member.Property.Name))
                    .Append(" = ")
                    .Append(
                        boundClones.TryGetValue(member.Property.Name, out var cloned)
                            ? cloned
                            : Expressions.CloneModelExpression(member, receiver + SparseNaming.EscapeIdentifier(member.Property.Name))
                    )
                    .AppendLine(",");
            }

            code.Unindent().AppendLine("};");
        }
        else
        {
            code.AppendLine(";");
        }
        if (registerClone)
        {
            code.AppendLine(CloneContext + ".Add(" + self + ", clone);");
        }
        foreach (var member in deferredMembers)
        {
            code.AppendIndent()
                .Append("clone.")
                .Append(SparseNaming.EscapeIdentifier(member.Property.Name))
                .Append(" = ")
                .Append(
                    boundClones.TryGetValue(member.Property.Name, out var cloned)
                        ? cloned
                        : Expressions.CloneModelExpression(member, receiver + SparseNaming.EscapeIdentifier(member.Property.Name))
                )
                .AppendLine(";");
        }
        code.AppendLine("return clone;");
    }

    private static bool RequiresCloneContext(ImmutableArray<SparseMemberModel> members) =>
        members.Any(static member =>
            member.ChildModel is not null
            || member.Property.Type.PocoCloneHelperName is not null
            || member.Collection.CloneKind != SparseCloneCollectionKind.Unsupported
        );

    /// <summary>Emits the public model DeepClone facade preceding nested types.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="modelType">Model type name.</param>
    /// <param name="operationsType">Operations class qualifying the delegate.</param>
    public static void AppendDeepClonePublicFacade(SharedIndentedBuilder code, string modelType, string operationsType)
    {
        code.AppendLine("/// <summary>Creates a deep copy of this model value.</summary>");
        code.AppendLine("/// <returns>The deep copy.</returns>");
        code.AppendIndent().Append("public ").Append(modelType).AppendLine(" DeepClone() => " + operationsType + ".DeepClone(this);");
    }

    /// <summary>Emits the internal model DeepClone facade trailing public nested types.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="modelType">Model type name.</param>
    /// <param name="operationsType">Operations class qualifying the delegate.</param>
    public static void AppendDeepCloneInternalFacade(SharedIndentedBuilder code, string modelType, string operationsType)
    {
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendIndent()
            .Append("internal ")
            .Append(modelType)
            .AppendLine(
                " DeepClone(global::System.Collections.Generic.Dictionary<object, object> __sparse_clone_context) => "
                    + operationsType
                    + ".DeepClone(this, __sparse_clone_context);"
            );
    }

    /// <summary>Emits the public operations DeepClone preceding internal helpers.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="modelType">Model type name.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="constructor">Construction binding.</param>
    /// <param name="modelIsReferenceType">Whether the model is a reference type.</param>
    public void AppendDeepCloneOperationsPublic(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        ModelConstructorBinding? constructor = null,
        bool modelIsReferenceType = true
    )
    {
        code.AppendLine("/// <summary>Creates a deep copy of the specified model value.</summary>");
        code.AppendLine("/// <param name=\"value\">The model value to copy.</param>");
        code.AppendLine("/// <returns>The deep copy.</returns>");
        code.AppendIndent().Append("public static ").Append(modelType).Append(" DeepClone(").Append(modelType).AppendLine(" value)");
        code.OpenBlock();
        if (RequiresCloneContext(members))
        {
            Expressions.AppendCloneContext(code, ReferenceComparer);
            code.AppendLine("return DeepClone(value, " + CloneContext + ");");
        }
        else
        {
            AppendModelCloneBody(code, modelType, members, constructor, modelIsReferenceType, registerClone: false, receiver: "value.");
        }
        code.CloseBlock();
    }

    /// <summary>Emits the internal operations DeepClone trailing public operations.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="modelType">Model type name.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="constructor">Construction binding.</param>
    /// <param name="modelIsReferenceType">Whether the model is a reference type.</param>
    public void AppendDeepCloneOperationsInternal(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        ModelConstructorBinding? constructor = null,
        bool modelIsReferenceType = true
    )
    {
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendIndent()
            .Append("internal static ")
            .Append(modelType)
            .Append(" DeepClone(")
            .Append(modelType)
            .Append(" value, global::System.Collections.Generic.Dictionary<object, object> ")
            .Append(CloneContext)
            .AppendLine(")");
        code.OpenBlock();
        if (modelIsReferenceType)
        {
            code.AppendLine("if (" + CloneContext + ".TryGetValue(value, out var existing)) return (" + modelType + ")existing;");
        }

        AppendModelCloneBody(code, modelType, members, constructor, modelIsReferenceType, registerClone: modelIsReferenceType, receiver: "value.");
        code.CloseBlock();
    }

    public void AppendPocoCloneHelper(
        SharedIndentedBuilder code,
        string typeName,
        string cloneHelperName,
        ImmutableArray<SparseMemberModel> members,
        ModelConstructorBinding? constructor = null,
        string? operationsType = null,
        string helperAccessibility = "private"
    )
    {
        // Stage 4: POCO helpers move to the operations class with no surface
        // counterpart.
        if (operationsType is not null)
        {
            return;
        }

        code.CancellationToken.ThrowIfCancellationRequested();
        code.AppendIndent()
            .Append(helperAccessibility + " static ")
            .Append(typeName)
            .Append(' ')
            .Append(cloneHelperName)
            .Append('(')
            .Append(typeName)
            .AppendLine(" value, global::System.Collections.Generic.Dictionary<object, object> " + CloneContext + ")");
        code.OpenBlock();
        code.AppendLine("if (" + CloneContext + ".TryGetValue(value, out var existing)) { return (" + typeName + ")existing; }");
        var boundClones = new Dictionary<string, string>(StringComparer.Ordinal);
        if (constructor is not null)
        {
            foreach (var propertyName in constructor.Parameters.Select(static parameter => parameter.PropertyName))
            {
                if (boundClones.ContainsKey(propertyName))
                {
                    continue;
                }

                var member = members.Single(candidate => candidate.Property.Name == propertyName);
                var local = "__constructor_clone_" + boundClones.Count;
                boundClones.Add(propertyName, local);
                code.AppendLine(
                    "var " + local + " = " + Expressions.CloneModelExpression(member, "value." + SparseNaming.EscapeIdentifier(propertyName)) + ";"
                );
            }
        }
        var arguments = constructor is null
            ? string.Empty
            : string.Join(", ", constructor.Parameters.Select(parameter => boundClones[parameter.PropertyName]));
        code.AppendIndent().Append("var clone = new ").Append(typeName).Append("(").Append(arguments).AppendLine(");");
        code.AppendLine(CloneContext + ".Add(value, clone);");
        foreach (var member in members.Where(static member => !member.Property.IsReadOnly && !member.Property.IsInitOnly))
        {
            var memberName = SparseNaming.EscapeIdentifier(member.Property.Name);
            code.AppendIndent()
                .Append("clone.")
                .Append(memberName)
                .Append(" = ")
                .Append(
                    boundClones.TryGetValue(member.Property.Name, out var cloned)
                        ? cloned
                        : Expressions.CloneModelExpression(member, "value." + memberName)
                )
                .AppendLine(";");
        }

        code.AppendLine("return clone;");
        code.CloseBlock();
    }

    public void AppendFragmentClone(SharedIndentedBuilder code, ImmutableArray<SparseMemberModel> members, bool usesPocoCloning)
    {
        AppendFragmentClonePublic(code, members, usesPocoCloning);
        AppendFragmentCloneInternal(code, members);
    }

    /// <summary>Emits the public fragment DeepClone preceding internal helpers.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="usesPocoCloning">Whether POCO cloning applies.</param>
    public void AppendFragmentClonePublic(SharedIndentedBuilder code, ImmutableArray<SparseMemberModel> members, bool usesPocoCloning)
    {
        _ = usesPocoCloning;
        code.AppendLine("/// <summary>Copies the fragment and its generated nested values.</summary>");
        code.AppendLine("/// <returns>The copied fragment.</returns>");
        code.AppendLine("public Fragment DeepClone()");
        code.OpenBlock();
        if (RequiresCloneContext(members))
        {
            Expressions.AppendCloneContext(code, ReferenceComparer);
            code.AppendLine("return DeepClone(" + CloneContext + ");");
        }
        else
        {
            code.AppendLine(Origins is null ? "return new Fragment" : "return new Fragment(this.Origin)");
            code.OpenBlock();
            foreach (var member in members)
            {
                var name = SparseNaming.EscapeIdentifier(member.Property.Name);
                code.AppendLine(name + " = this." + name + ",");
            }

            if (Origins is not null)
            {
                code.AppendLine("__SparseMemberOrigins = this.__SparseMemberOrigins,");
                code.AppendLine("__SparseElementOrigins = this.__SparseElementOrigins,");
            }

            code.Unindent().AppendLine("};");
        }
        code.CloseBlock();
        code.AppendLine();
    }

    /// <summary>Emits internal and private fragment clone helpers trailing the public surface.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="members">Analyzed members.</param>
    public void AppendFragmentCloneInternal(SharedIndentedBuilder code, ImmutableArray<SparseMemberModel> members)
    {
        AppendFragmentCloneInternalMethod(code);
        AppendFragmentClonePrivateCtor(code, members);
    }

    /// <summary>Emits the internal fragment DeepClone trailing public methods.</summary>
    /// <param name="code">Target builder.</param>
    public void AppendFragmentCloneInternalMethod(SharedIndentedBuilder code)
    {
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendIndent()
            .Append("internal Fragment DeepClone(global::System.Collections.Generic.Dictionary<object, object> ")
            .Append(CloneContext)
            .AppendLine(")");
        code.OpenBlock();
        code.AppendLine("if (" + CloneContext + ".TryGetValue(this, out var existing)) return (Fragment)existing;");
        code.AppendLine("return new Fragment(this, " + CloneContext + ");");
        code.CloseBlock();
        code.AppendLine();
    }

    /// <summary>Emits the private projection constructor trailing internal helpers.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="members">Analyzed members.</param>
    public void AppendFragmentClonePrivateCtor(SharedIndentedBuilder code, ImmutableArray<SparseMemberModel> members)
    {
        code.AppendIndent()
            .Append("private Fragment(Fragment source, global::System.Collections.Generic.Dictionary<object, object> ")
            .Append(CloneContext)
            .AppendLine(")");
        code.OpenBlock();
        code.AppendLine(CloneContext + ".Add(source, this);");
        if (Origins is not null)
        {
            code.AppendLine("this.Origin = source.Origin;");
            code.AppendLine("this.__SparseMemberOrigins = source.__SparseMemberOrigins;");
            code.AppendLine("this.__SparseElementOrigins = source.__SparseElementOrigins;");
        }

        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            var type = SparseFragmentEmitHelpers.FragmentValueType(member);
            var expression = Expressions.CloneFragmentExpression(member, "source." + name + ".Value");
            code.AppendIndent()
                .Append("this.")
                .Append(name)
                .Append(" = source.")
                .Append(name)
                .Append(".IsPresent ? ")
                .Append(Optional)
                .Append("<")
                .Append(type)
                .Append(">.Present(")
                .Append(expression)
                .AppendLine(") : default;");
        }

        code.CloseBlock();
        code.AppendLine();
    }
}
