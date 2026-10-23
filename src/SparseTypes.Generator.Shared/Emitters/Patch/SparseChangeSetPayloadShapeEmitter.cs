using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits the public payload DTO roles and their legacy-core boundary mapping.</summary>
internal static class SparseChangeSetPayloadShapeEmitter
{
    internal static void AppendPublicRoles(
        SharedIndentedBuilder code,
        ImmutableArray<SparsePayloadMemberContract> members,
        string endpoint,
        string? modelType,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? implementationNamespace
    )
    {
        var rootType = SparseChangeSetPayloadEmitter.PayloadName(modelType, "Root");
        var deltaType = SparseChangeSetPayloadEmitter.PayloadName(modelType, "MembersDelta");
        var modelChangeType = SparseChangeSetPayloadEmitter.PayloadName(modelType, "ModelChange");
        var kindType = SparseChangeSetPayloadEmitter.PayloadName(modelType, "ModelChangeKind");
        var collectionChangeType = SparseChangeSetPayloadEmitter.PayloadName(modelType, "CollectionChange");
        var itemOperationType = SparseChangeSetPayloadEmitter.PayloadName(modelType, "ItemOperation");
        var payloadChangeType = SparseChangeSetPayloadEmitter.PayloadName(modelType, "Change");

        code.AppendLine("/// <summary>Base role for a collection replacement or keyed item update.</summary>");
        code.AppendLine("/// <typeparam name=\"TCollection\">The collection value type.</typeparam>");
        code.AppendLine("/// <typeparam name=\"TItem\">The item-operation type.</typeparam>");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine("public abstract class " + collectionChangeType + "<TCollection, TItem> : " + payloadChangeType);
        code.OpenBlock();
        AppendPropertyDoc(code, "Gets or sets the before collection endpoint.");
        code.AppendLine("public " + endpoint + "<TCollection>? Before { get; set; }");
        AppendPropertyDoc(code, "Gets or sets the after collection endpoint.");
        code.AppendLine("public " + endpoint + "<TCollection>? After { get; set; }");
        AppendPropertyDoc(code, "Gets or sets the item operations for an incremental update.");
        code.AppendLine("public global::System.Collections.Generic.List<TItem> Items { get; set; } = new();");
        code.CloseBlock();
        code.AppendLine();

        code.AppendLine("/// <summary>Base role for an operation on one keyed or dictionary item.</summary>");
        code.AppendLine("/// <typeparam name=\"TKey\">The item identity type.</typeparam>");
        code.AppendLine("/// <typeparam name=\"TValue\">The item value type.</typeparam>");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine("public abstract class " + itemOperationType + "<TKey, TValue>");
        code.OpenBlock();
        AppendPropertyDoc(code, "Gets or sets the item identity.");
        code.AppendLine("public TKey Key { get; set; } = default!;");
        AppendPropertyDoc(code, "Gets or sets the before endpoint.");
        code.AppendLine("public " + endpoint + "<TValue>? Before { get; set; }");
        AppendPropertyDoc(code, "Gets or sets the after endpoint.");
        code.AppendLine("public " + endpoint + "<TValue>? After { get; set; }");
        AppendPropertyDoc(code, "Gets or sets the item operation kind.");
        code.AppendLine("public " + dialect.RuntimeNamespace + "ChangePayloadItemKind Kind { get; set; }");
        code.CloseBlock();
        code.AppendLine();

        code.AppendLine("/// <summary>Operation kind for a model payload change.</summary>");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine("public enum " + kindType);
        code.OpenBlock();
        code.AppendLine("/// <summary>Updates only the listed members.</summary>");
        code.AppendLine("Update,");
        code.AppendLine("/// <summary>Replaces the entire model snapshot.</summary>");
        code.AppendLine("Replace");
        code.CloseBlock();
        code.AppendLine();

        code.AppendLine("/// <summary>Snapshot value of a model's transportable members.</summary>");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine("public sealed class " + rootType);
        code.OpenBlock();
        foreach (var member in members.Select(static contractMember => contractMember.Member))
        {
            var valueType = MemberValueType(member, dialect, implementationNamespace);
            AppendPropertyDoc(code, "Gets or sets the snapshot endpoint for " + member.Property.Name + ".");
            code.AppendLine("public " + endpoint + "<" + valueType + ">? " + SparseNaming.EscapeIdentifier(member.Property.Name) + " { get; set; }");
        }
        var readableMembers = members
            .Select(static contractMember => contractMember.Member)
            .Where(member => !member.Property.IsTransportIgnored && dialect.GetTransport(member.Property.Name) != SparseMemberTransport.WriteOnly)
            .ToImmutableArray();
        code.AppendLine("/// <summary>Builds a snapshot from a fragment.</summary>");
        code.AppendLine("/// <param name=\"value\">The fragment to snapshot.</param>");
        code.AppendLine("/// <returns>The generated snapshot.</returns>");
        code.AppendLine("public static " + rootType + " FromFragment(Fragment value)");
        code.OpenBlock();
        code.AppendLine("if (value is null) throw new global::System.ArgumentNullException(nameof(value));");
        code.AppendLine("var result = new " + rootType + "();");
        SparseChangeSetPayloadSnapshotEmitter.AppendFromFragmentMembers(
            code,
            readableMembers,
            endpoint,
            dialect.RuntimeNamespace,
            dialect,
            modelType,
            redactFlagged: false,
            implementationNamespace
        );
        code.AppendLine("return result;");
        code.CloseBlock();
        code.AppendLine("/// <summary>Converts this snapshot to a fragment.</summary>");
        code.AppendLine("/// <returns>The converted fragment.</returns>");
        code.AppendLine("public Fragment ToFragment()");
        code.OpenBlock();
        foreach (var member in readableMembers)
        {
            var property = SparseNaming.EscapeIdentifier(member.Property.Name);
            var endpointName = "this." + property;
            var path = SymbolDisplay.FormatLiteral(member.Property.Name, true);
            if (SparseChangeSetBasicsEmitter.IsNested(member))
            {
                var childRoot = SparseChangeSetPayloadEmitter.QualifiedChildPayloadType(
                    dialect,
                    member.ChildModel!.Value.NonNullableName,
                    "Root",
                    implementationNamespace
                );
                var childFragment = member.ChildFragmentType + "?";
                code.AppendLine(
                    "if ("
                        + endpointName
                        + "?.State == "
                        + dialect.RuntimeNamespace
                        + "ChangePayloadState.Redacted) throw new global::System.ArgumentException(\"A redacted root member cannot convert to a fragment.\", "
                        + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                        + ");"
                );
                code.AppendLine(
                    "var member"
                        + member.Id
                        + " = "
                        + endpointName
                        + "?.ToOptional() ?? "
                        + dialect.RuntimeNamespace
                        + "Optional<"
                        + childRoot
                        + "?>.Missing;"
                );
                code.AppendLine(
                    dialect.RuntimeNamespace
                        + "Optional<"
                        + childFragment
                        + "> rootMember"
                        + member.Id
                        + " = !member"
                        + member.Id
                        + ".IsPresent ? "
                        + dialect.RuntimeNamespace
                        + "Optional<"
                        + childFragment
                        + ">.Missing : "
                        + dialect.RuntimeNamespace
                        + "Optional<"
                        + childFragment
                        + ">.Present(member"
                        + member.Id
                        + ".Value is null ? null : member"
                        + member.Id
                        + ".Value.ToFragment());"
                );
            }
            else
            {
                var valueType = SparseChangeSetBasicsEmitter.FragmentValueType(member);
                code.AppendLine(
                    "if ("
                        + endpointName
                        + "?.State == "
                        + dialect.RuntimeNamespace
                        + "ChangePayloadState.Redacted) throw new global::System.ArgumentException(\"A redacted root member cannot convert to a fragment.\", "
                        + path
                        + ");"
                );
                code.AppendLine(
                    dialect.RuntimeNamespace
                        + "Optional<"
                        + valueType
                        + "> rootMember"
                        + member.Id
                        + " = "
                        + endpointName
                        + " is null ? "
                        + dialect.RuntimeNamespace
                        + "Optional<"
                        + valueType
                        + ">.Missing : "
                        + endpointName
                        + ".ToOptional();"
                );
            }
        }
        code.AppendLine("return new Fragment");
        code.OpenBlock();
        foreach (var member in readableMembers)
        {
            code.AppendLine(SparseNaming.EscapeIdentifier(member.Property.Name) + " = rootMember" + member.Id + ",");
        }
        code.Unindent().AppendLine("};");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine();

        code.AppendLine("/// <summary>Optional member operations for a model update.</summary>");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine("public sealed class " + deltaType);
        code.OpenBlock();
        foreach (var member in members.Select(static contractMember => contractMember.Member))
        {
            var memberType = SparseChangeSetPayloadEmitter.PayloadMemberName(modelType, "Change", member.Id);
            AppendPropertyDoc(code, "Gets or sets the optional change for " + member.Property.Name + ".");
            code.AppendLine("public " + memberType + "? " + SparseNaming.EscapeIdentifier(member.Property.Name) + " { get; set; }");
        }
        code.CloseBlock();
        code.AppendLine();

        code.AppendLine("/// <summary>Typed update or replacement for a model.</summary>");
        code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        code.AppendLine("public sealed class " + modelChangeType);
        code.OpenBlock();
        AppendPropertyDoc(code, "Gets or sets whether this is a member update or whole-model replacement.");
        code.AppendLine("public " + kindType + " Kind { get; set; }");
        AppendPropertyDoc(code, "Gets or sets the optional member operations for an update.");
        code.AppendLine("public " + deltaType + "? Members { get; set; }");
        AppendPropertyDoc(code, "Gets or sets the before snapshot for a replacement.");
        code.AppendLine("public " + endpoint + "<" + rootType + ">? Before { get; set; }");
        AppendPropertyDoc(code, "Gets or sets the after snapshot for a replacement.");
        code.AppendLine("public " + endpoint + "<" + rootType + ">? After { get; set; }");
        code.CloseBlock();
        code.AppendLine();
    }

    internal static void AppendChangeProperty(SharedIndentedBuilder code, string modelChangeType)
    {
        AppendPropertyDoc(code, "Gets or sets the typed model change.");
        code.AppendLine("public " + modelChangeType + "? Change { get => TypedChange; set => TypedChange = value; }");
    }

    private static string MemberValueType(
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? implementationNamespace
    ) =>
        SparseChangeSetBasicsEmitter.IsNested(member)
            ? SparseChangeSetPayloadEmitter.QualifiedChildPayloadType(
                dialect,
                member.ChildModel!.Value.NonNullableName,
                "Root",
                implementationNamespace
            ) + "?"
            : SparseChangeSetBasicsEmitter.FragmentValueType(member);

    private static void AppendPropertyDoc(SharedIndentedBuilder code, string summary) => code.AppendLine("/// <summary>" + summary + "</summary>");
}
