using System.Collections.Immutable;
using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Fragment applier for path-based conflict resolution.</summary>
/// <remarks>Emits <c>__SparseTryApplyResolution</c>.</remarks>
internal static class SparseResolutionApplyEmitter
{
    internal static void AppendForwarderShell(
        SharedIndentedBuilder shell,
        string pathType,
        string conflict,
        string optionalObject,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target
    )
    {
        _ = conflict;
        _ = optionalObject;
        if (target is null)
        {
            return;
        }

        var workspace = dialect.ResolutionWorkspaceType;
        var workspaceParameter = WorkspaceParameter(workspace);
        var workspaceArgument = WorkspaceArgument(workspace);
        shell.AppendLine("/// <summary>Applies one resolved value to a fragment subtree (resolution infrastructure).</summary>");
        shell.AppendLine(
            "internal static bool __SparseTryApplyResolution(Fragment fragment, "
                + pathType
                + " path, int offset, "
                + optionalObject
                + " desired"
                + workspaceParameter
                + ", out Fragment? updated, out string? error) => "
                + target.ChangeSetOperationsType
                + ".__SparseTryApplyResolution(fragment, path, offset, desired"
                + workspaceArgument
                + ", out updated, out error);"
        );
    }

    /// <summary>Emits the workspace parameter declaration, or an empty string when the product does not stage.</summary>
    internal static string WorkspaceParameter(string? workspace) => workspace is null ? string.Empty : ", " + workspace + " workspace";

    /// <summary>Emits a workspace argument expression, or an empty string when the product does not stage.</summary>
    internal static string WorkspaceArgument(string? workspace) => workspace is null ? string.Empty : ", workspace";

    internal static void AppendApplier(
        SharedIndentedBuilder body,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment,
        string optionalObject,
        string conflict,
        string pathType,
        string? modelType,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target
    )
    {
        _ = optionalFragment;
        _ = conflict;
        _ = modelType;
        var segmentKind = runtime + "SparsePathSegmentKind";
        var workspace = dialect.ResolutionWorkspaceType;
        var workspaceParameter = WorkspaceParameter(workspace);
        body.AppendLine("/// <summary>Applies one resolved value to a fragment subtree (resolution infrastructure).</summary>");
        body.AppendLine(
            "internal static bool __SparseTryApplyResolution("
                + "Fragment fragment, "
                + pathType
                + " path, int offset, "
                + optionalObject
                + " desired"
                + workspaceParameter
                + ", out Fragment? updated, out string? error)"
        );
        body.OpenBlock();
        body.AppendLine("updated = null;");
        body.AppendLine("error = null;");
        body.AppendLine("if (" + (dialect.PathIsValueType ? "" : "path is null || ") + "offset < 0 || offset >= path.Depth)");
        body.OpenBlock();
        body.AppendLine("error = \"The resolution path is empty.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        body.AppendLine("var head = path.Segments[offset];");
        body.AppendLine("if (head.Kind != " + segmentKind + ".Member)");
        body.OpenBlock();
        body.AppendLine("error = \"The resolution path must start with a member segment.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        body.AppendLine("var builder = fragment.ToBuilder();");
        foreach (var member in members)
        {
            AppendMemberBranch(body, member, runtime, segmentKind, dialect, workspace);
        }

        body.AppendLine("error = \"Unknown member '\" + head.Name + \"'.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        SparseResolutionCollectionEmitter.AppendKeyHelpers(body, members);
    }

    private static void AppendMemberBranch(
        SharedIndentedBuilder body,
        SparseMemberModel member,
        string runtime,
        string segmentKind,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? workspace
    )
    {
        var esc = SparseNaming.EscapeIdentifier(member.Property.Name);
        var name = member.Property.Name;
        var literal = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(name, true);
        body.AppendLine("if (head.Kind == " + segmentKind + ".Member && global::System.StringComparer.Ordinal.Equals(head.Name, " + literal + "))");
        body.OpenBlock();
        if (IsNested(member) && !member.ChildIsStructural)
        {
            AppendNestedBranch(body, member, esc, name, dialect, workspace);
        }
        else if (IsKeyed(member))
        {
            SparseResolutionCollectionEmitter.AppendKeyedBranch(body, member, esc, name, runtime, workspace);
        }
        else if (IsDict(member))
        {
            SparseResolutionDictEmitter.AppendDictBranch(body, member, esc, name, runtime, workspace);
        }
        else if (IsSet(member) || IsCollection(member))
        {
            AppendWholeOnlyBranch(body, member, esc, name, runtime, "Member");
        }
        else if (IsNested(member))
        {
            AppendWholeOnlyBranch(body, member, esc, name, runtime, "Structural member");
        }
        else
        {
            AppendScalarBranch(body, member, esc, name, runtime);
        }

        body.CloseBlock();
    }

    private static bool IsCollection(SparseMemberModel member) => SparseKeyedCollectionEmitter.IsCollectionPatch(member);

    internal static string NonNullable(string typeName) =>
        typeName.EndsWith("?", StringComparison.Ordinal) ? typeName.Substring(0, typeName.Length - 1) : typeName;

    private static void AppendScalarBranch(SharedIndentedBuilder body, SparseMemberModel member, string esc, string name, string runtime)
    {
        var valueType = FragmentValueType(member);
        var optional = runtime + "Optional<" + valueType + ">";
        body.AppendLine("if (offset + 1 != path.Depth)");
        body.OpenBlock();
        body.AppendLine("error = \"Member '" + name + "' has no nested paths.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        AppendCheckedSlot(
            body,
            "builder." + esc,
            optional,
            valueType,
            name,
            member.Id,
            rejectNull: !member.Property.Type.IsReferenceType && !member.Property.IsNullable
        );
        body.AppendLine("updated = builder.Build();");
        body.AppendLine("return true;");
    }

    private static void AppendWholeOnlyBranch(
        SharedIndentedBuilder body,
        SparseMemberModel member,
        string esc,
        string name,
        string runtime,
        string label
    )
    {
        var valueType = FragmentValueType(member);
        var optional = runtime + "Optional<" + valueType + ">";
        body.AppendLine("if (offset + 1 != path.Depth)");
        body.OpenBlock();
        body.AppendLine("error = \"" + label + " '" + name + "' resolves as a whole; deeper paths are not supported.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        AppendCheckedSlot(body, "builder." + esc, optional, valueType, name, member.Id, rejectNull: false);
        body.AppendLine("updated = builder.Build();");
        body.AppendLine("return true;");
    }

    internal static void AppendCheckedSlot(
        SharedIndentedBuilder body,
        string slot,
        string optional,
        string valueType,
        string name,
        int id,
        bool rejectNull
    )
    {
        var checkType = NonNullable(valueType);
        body.AppendLine("if (!desired.IsPresent)");
        body.OpenBlock();
        body.AppendLine(slot + " = " + optional + ".Missing;");
        body.CloseBlock();
        body.AppendLine("else if (desired.Value is null)");
        body.OpenBlock();
        if (rejectNull)
        {
            body.AppendLine("error = \"Member '" + name + "' does not accept null.\";");
            body.AppendLine("return false;");
        }
        else
        {
            body.AppendLine(slot + " = " + optional + ".Present(default(" + valueType + ")!);");
        }
        body.CloseBlock();
        body.AppendLine("else if (desired.Value is " + checkType + " typed" + id + ")");
        body.OpenBlock();
        body.AppendLine(slot + " = " + optional + ".Present(typed" + id + ");");
        body.CloseBlock();
        body.AppendLine("else");
        body.OpenBlock();
        body.AppendLine("error = \"The resolution value for member '" + name + "' has an unsupported type.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
    }

    private static void AppendNestedBranch(
        SharedIndentedBuilder body,
        SparseMemberModel member,
        string esc,
        string name,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? workspace
    )
    {
        var childFragment = member.ChildFragmentType ?? "object";
        var childChangeSet = ChildChangeSet(member, dialect);
        var childModel = member.ChildModel?.NonNullableName ?? "object";
        var childOptional = dialect.RuntimeNamespace + "Optional<" + childFragment + "?>";
        body.AppendLine("if (offset + 1 == path.Depth)");
        body.OpenBlock();
        body.AppendLine("if (!desired.IsPresent)");
        body.OpenBlock();
        body.AppendLine("builder." + esc + " = " + childOptional + ".Missing;");
        body.CloseBlock();
        body.AppendLine("else if (desired.Value is null)");
        body.OpenBlock();
        body.AppendLine("builder." + esc + " = " + childOptional + ".Present((" + childFragment + "?)null);");
        body.CloseBlock();
        body.AppendLine("else if (desired.Value is " + childFragment + " whole" + member.Id + ")");
        body.OpenBlock();
        body.AppendLine("builder." + esc + " = " + childOptional + ".Present(whole" + member.Id + ");");
        body.CloseBlock();
        body.AppendLine("else if (desired.Value is " + childModel + " model" + member.Id + ")");
        body.OpenBlock();
        body.AppendLine("builder." + esc + " = " + childOptional + ".Present(" + childFragment + ".From(model" + member.Id + "));");
        body.CloseBlock();
        body.AppendLine("else");
        body.OpenBlock();
        body.AppendLine("error = \"The resolution value for member '" + name + "' has an unsupported type.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        body.AppendLine("updated = builder.Build();");
        body.AppendLine("return true;");
        body.CloseBlock();
        body.AppendLine("var child" + member.Id + " = builder." + esc + ";");
        body.AppendLine("if (!child" + member.Id + ".IsPresent || child" + member.Id + ".Value is null)");
        body.OpenBlock();
        body.AppendLine("error = \"Member '" + name + "' is absent and cannot resolve deeper paths.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        body.AppendLine(
            "if (!"
                + childChangeSet
                + ".__SparseTryApplyResolution(child"
                + member.Id
                + ".Value, path, offset + 1, desired"
                + WorkspaceArgument(workspace)
                + ", out var updatedChild"
                + member.Id
                + ", out error)) return false;"
        );
        body.AppendLine("if (updatedChild" + member.Id + " is null)");
        body.OpenBlock();
        body.AppendLine("error = \"The nested resolution produced no fragment.\";");
        body.AppendLine("return false;");
        body.CloseBlock();
        body.AppendLine("builder." + esc + " = " + childOptional + ".Present(updatedChild" + member.Id + ");");
        body.AppendLine("updated = builder.Build();");
        body.AppendLine("return true;");
    }
}
