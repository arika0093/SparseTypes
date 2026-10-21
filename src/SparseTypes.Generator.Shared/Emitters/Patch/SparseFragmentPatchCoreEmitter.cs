using System;
using System.Collections.Immutable;
using System.Linq;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits patch storage, whole-operation handling, construction and apply paths.</summary>
internal static class SparseFragmentPatchCoreEmitter
{
    public static void AppendPatchMembers(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        // Public-first order: property surface precedes backing fields, which
        // move to AppendPatchMemberFields trailing the public patch surface.
        var runtime = dialect.RuntimeNamespace;
        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            if (member.ChildModel is null && !SparseFragmentPatchEmitter.IsCollectionPatch(member))
            {
                var type =
                    runtime
                    + "FragmentOperation"
                    + "<"
                    + SparseFragmentPatchEmitter.GetMemberValueType(dialect, member)
                    + ">";
                code.AppendLine(
                    "/// <summary>Gets the operation for member '"
                        + member.Property.Name
                        + "'.</summary>"
                );
                code.AppendLine(
                    "public ref "
                        + type
                        + " "
                        + name
                        + " => ref "
                        + dialect.MemberField(member)
                        + ";"
                );
            }
            else if (SparseFragmentPatchEmitter.IsCollectionPatch(member))
            {
                var type = SparseFragmentPatchEmitter.GetCollectionPatchName(dialect, member);
                code.AppendLine(
                    "/// <summary>Gets the nested patch for member '"
                        + member.Property.Name
                        + "'.</summary>"
                );
                code.AppendLine(
                    "public "
                        + type
                        + " "
                        + name
                        + " { get => "
                        + dialect.MemberField(member)
                        + " ??= new "
                        + type
                        + "(); set => "
                        + dialect.MemberField(member)
                        + " = value; }"
                );
            }
            else
            {
                var type = dialect.ChildPatchName(member);
                code.AppendLine(
                    "/// <summary>Gets the nested patch for member '"
                        + member.Property.Name
                        + "'.</summary>"
                );
                code.AppendLine(
                    "public "
                        + type
                        + " "
                        + name
                        + " { get => "
                        + dialect.MemberField(member)
                        + " ??= new "
                        + type
                        + "(); set => "
                        + dialect.MemberField(member)
                        + " = value; }"
                );
            }
        }
    }

    /// <summary>Emits private patch backing fields trailing the public surface.</summary>
    /// <remarks>State-bound bodies live in the private nested <c>__Core</c>.</remarks>
    /// <param name="code">Target builder.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="dialect">Patch dialect.</param>
    public static void AppendPatchMemberFields(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var runtime = dialect.RuntimeNamespace;
        foreach (var member in members)
        {
            var field = dialect.MemberField(member);
            if (member.ChildModel is null && !SparseFragmentPatchEmitter.IsCollectionPatch(member))
            {
                var type =
                    runtime
                    + "FragmentOperation"
                    + "<"
                    + SparseFragmentPatchEmitter.GetMemberValueType(dialect, member)
                    + ">";
                code.AppendLine("private " + type + " " + field + ";");
            }
            else if (SparseFragmentPatchEmitter.IsCollectionPatch(member))
            {
                code.AppendLine(
                    "private "
                        + SparseFragmentPatchEmitter.GetCollectionPatchName(dialect, member)
                        + "? "
                        + field
                        + ";"
                );
            }
            else
            {
                code.AppendLine("private " + dialect.ChildPatchName(member) + "? " + field + ";");
            }
        }
    }

    /// <summary>Emits whole-operation field, empty helpers, Set/Remove, and implicit conversion.</summary>
    public static void AppendPatchWholeOperations(
        SharedIndentedBuilder code,
        string modelType,
        string contract,
        string emptyContract,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var operation = SparseFragmentPatchEmitter.Operation(dialect);
        var kind = SparseFragmentPatchEmitter.Kind(dialect);
        var wholePrefix = SparseNaming.WholeApiPrefix(
            members.Select(static member => member.Property.Name)
        );
        // Public whole operations first (public -> internal order).
        code.AppendLine("/// <summary>Whether this patch carries no changes.</summary>");
        code.AppendLine(
            "public bool "
                + wholePrefix
                + "IsEmpty => "
                + dialect.WholeFieldName
                + ".Kind == "
                + kind
                + ".Keep && "
                + dialect.MembersEmptyName
                + ";"
        );
        code.AppendLine(
            "/// <summary>Replaces the whole contribution with the given model value.</summary>"
        );
        code.AppendLine("/// <param name=\"value\">Model value to store.</param>");
        code.AppendLine(
            "public void "
                + wholePrefix
                + "Set("
                + modelType
                + " value) => "
                + dialect.WholeFieldName
                + " = "
                + operation
                + "<Fragment?>.Set(Fragment.From(value));"
        );
        code.AppendLine("/// <summary>Sets the whole contribution to null.</summary>");
        code.AppendLine(
            "public void "
                + wholePrefix
                + "SetNull() => "
                + dialect.WholeFieldName
                + " = "
                + operation
                + "<Fragment?>.Set(null);"
        );
        code.AppendLine("/// <summary>Removes the whole contribution.</summary>");
        code.AppendLine(
            "public void "
                + wholePrefix
                + "Remove() => "
                + dialect.WholeFieldName
                + " = "
                + operation
                + "<Fragment?>.Remove;"
        );
        code.AppendLine(
            "/// <summary>Creates a patch from a whole-contribution operation.</summary>"
        );
        code.AppendLine("/// <param name=\"operation\">Whole operation to wrap.</param>");
        code.AppendLine("/// <returns>A patch carrying the operation.</returns>");
        code.AppendLine(
            "public static implicit operator Patch("
                + operation
                + "<Fragment?> operation) => new Patch { "
                + dialect.WholeFieldName
                + " = operation };"
        );
    }

    /// <summary>Emits private whole-operation storage trailing the public surface.</summary>
    /// <remarks>Whole storage is private to the facade; the nested <c>__Core</c> reads it directly.</remarks>
    /// <param name="code">Target builder.</param>
    /// <param name="modelType">Model type name.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="dialect">Patch dialect.</param>
    public static void AppendPatchWholeStorage(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var operation = SparseFragmentPatchEmitter.Operation(dialect);
        var wholePrefix = SparseNaming.WholeApiPrefix(
            members.Select(static member => member.Property.Name)
        );
        // Public-first order: internal bridges precede private storage.
        code.AppendLine("internal bool __SparseIsEmpty() => " + wholePrefix + "IsEmpty;");
        code.AppendLine(
            "internal void __SparseSet(" + modelType + " value) => " + wholePrefix + "Set(value);"
        );
        code.AppendLine("internal void __SparseSetNull() => " + wholePrefix + "SetNull();");
        code.AppendLine("internal void __SparseRemove() => " + wholePrefix + "Remove();");
        code.AppendLine("private " + operation + "<Fragment?> " + dialect.WholeFieldName + ";");
        code.AppendLine(
            "private bool "
                + dialect.MembersEmptyName
                + " => "
                + SparseFragmentPatchEmitter.MembersEmptyExpression(members, dialect)
                + ";"
        );
    }

    /// <summary>Emits Patch() and Patch(Fragment) construction from present members.</summary>
    public static void AppendPatchConstructor(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var operation = SparseFragmentPatchEmitter.Operation(dialect);
        code.AppendLine("/// <summary>Initializes an empty patch.</summary>");
        code.AppendLine("public Patch() { }");
        code.AppendLine(
            "/// <summary>Initializes a patch from present fragment members.</summary>"
        );
        code.AppendLine("/// <param name=\"fragment\">Fragment seeding the patch.</param>");
        code.AppendLine("public Patch(Fragment fragment)");
        code.OpenBlock();
        code.AppendLine(
            "if (fragment is null) throw new global::System.ArgumentNullException(nameof(fragment));"
        );
        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            var field = dialect.MemberField(member);
            if (member.ChildModel is null && !SparseFragmentPatchEmitter.IsCollectionPatch(member))
            {
                code.AppendLine(
                    field
                        + " = fragment."
                        + name
                        + ".IsPresent ? "
                        + operation
                        + "<"
                        + SparseFragmentPatchEmitter.GetMemberValueType(dialect, member)
                        + ">.Set(fragment."
                        + name
                        + ".Value) : default;"
                );
            }
            else if (SparseFragmentPatchEmitter.IsCollectionPatch(member))
            {
                var collectionPatch = SparseFragmentPatchEmitter.GetCollectionPatchName(
                    dialect,
                    member
                );
                code.AppendLine("if (fragment." + name + ".IsPresent)");
                code.OpenBlock();
                code.AppendLine(
                    field
                        + " = new "
                        + collectionPatch
                        + "(); "
                        + field
                        + ".Set(fragment."
                        + name
                        + ".Value!);"
                );
                code.CloseBlock();
            }
            else
            {
                code.AppendLine("if (fragment." + name + ".IsPresent)");
                code.OpenBlock();
                code.AppendLine(
                    field
                        + " = fragment."
                        + name
                        + ".Value is null ? new "
                        + dialect.ChildPatchName(member)
                        + "() : new "
                        + dialect.ChildPatchName(member)
                        + "(fragment."
                        + name
                        + ".Value);"
                );
                code.AppendLine(
                    "if (fragment." + name + ".Value is null) " + field + ".__SparseSetNull();"
                );
                code.CloseBlock();
            }
        }
        code.CloseBlock();
    }

    /// <summary>Emits per-member ApplyMembers used by both Optional apply paths.</summary>
    /// <param name="target">Relocation target, or null for single-file emission.</param>
    public static void AppendPatchApplyMembers(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null
    )
    {
        if (target is not null)
        {
            code.AppendLine(
                "internal Fragment ApplyMembers(Fragment current) => "
                    + target.PatchOperationsType
                    + ".ApplyMembers(this, current);"
            );
            AppendPatchApplyMembersBody(target.PatchOperations, members, dialect, "self.");
            return;
        }

        AppendPatchApplyMembersBody(code, members, dialect, string.Empty);
    }

    private static void AppendPatchApplyMembersBody(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string receiver
    )
    {
        var declaration =
            receiver.Length == 0
                ? "internal Fragment ApplyMembers(Fragment current) => new Fragment"
                : "internal static Fragment ApplyMembers(Patch self, Fragment current) => new Fragment";
        code.AppendLine(declaration);
        code.OpenBlock();
        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            var field = receiver + dialect.MemberField(member);
            string expression;
            if (member.ChildModel is null && !SparseFragmentPatchEmitter.IsCollectionPatch(member))
            {
                expression = field + ".Apply(current." + name + ")";
            }
            else if (dialect.CastNestedApply && member.ChildModel is not null)
            {
                expression =
                    field
                    + " is null ? current."
                    + name
                    + " : (("
                    + dialect.NestedContract(member)
                    + ")"
                    + field
                    + ")."
                    + dialect.NestedApplyMethod
                    + "(current."
                    + name
                    + ")";
            }
            else
            {
                expression =
                    field
                    + " is null ? current."
                    + name
                    + " : "
                    + field
                    + "."
                    + dialect.NestedApplyMethod
                    + "(current."
                    + name
                    + ")";
            }
            code.AppendLine(name + " = " + expression + ",");
        }
        code.AppendLine("};");
    }

    /// <summary>Aliases patch facade state as locals for relocated bodies.</summary>
    /// <remarks>Relocated Patch operations take the facade as an explicit <c>self</c> parameter.</remarks>
    internal static void AppendPatchSelfAliases(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        code.AppendLine(
            "var " + dialect.WholeFieldName + " = self." + dialect.WholeFieldName + ";"
        );
        foreach (var member in members)
        {
            var field = dialect.MemberField(member);
            code.AppendLine("var " + field + " = self." + field + ";");
        }
    }

    /// <summary>Emits the Optional apply path.</summary>
    /// <param name="target">Relocation target, or null for single-file emission.</param>
    public static void AppendPatchOptionalApply(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string methodDeclaration,
        SparseOperationTarget? target = null
    )
    {
        if (target is not null)
        {
            var optional = SparseFragmentPatchEmitter.OptionalFragment(dialect);
            code.AppendLine("/// <summary>Applies this patch to a sparse state.</summary>");
            code.AppendLine("/// <param name=\"current\">State to apply to.</param>");
            code.AppendLine("/// <returns>The state with the patch applied.</returns>");
            code.AppendLine(
                "public "
                    + optional
                    + " Apply("
                    + optional
                    + " current) => "
                    + target.PatchOperationsType
                    + ".Apply(this, current);"
            );
            AppendPatchOptionalApplyBody(
                target.PatchOperations,
                dialect,
                "internal static " + optional + " Apply(Patch self, " + optional + " current)",
                "self."
            );
            return;
        }

        AppendPatchOptionalApplyBody(code, dialect, methodDeclaration, string.Empty);
    }

    private static void AppendPatchOptionalApplyBody(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string methodDeclaration,
        string receiver
    )
    {
        if (receiver.Length == 0)
        {
            code.AppendLine("/// <summary>Applies this patch to a sparse state.</summary>");
            code.AppendLine("/// <param name=\"current\">State to apply to.</param>");
            code.AppendLine("/// <returns>The state with the patch applied.</returns>");
        }

        var optional = SparseFragmentPatchEmitter.OptionalFragment(dialect);
        code.AppendLine(methodDeclaration);
        code.OpenBlock();
        code.AppendLine("current = " + receiver + dialect.WholeFieldName + ".Apply(current);");
        code.AppendLine("if (" + receiver + dialect.MembersEmptyName + ") return current;");
        code.AppendLine(
            "var basis = current.IsPresent && current.Value is not null ? current.Value : new Fragment();"
        );
        code.AppendLine(
            "return "
                + optional
                + ".Present("
                + (receiver.Length == 0 ? "ApplyMembers(basis));" : "ApplyMembers(self, basis));")
        );
        code.CloseBlock();
    }
}
