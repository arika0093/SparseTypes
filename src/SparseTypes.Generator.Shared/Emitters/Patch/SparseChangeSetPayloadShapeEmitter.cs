using System.Collections.Immutable;
using System.Linq;

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
        var snapshotType = SparseChangeSetPayloadEmitter.PayloadName(modelType, "Snapshot");
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
        code.AppendLine("public sealed class " + snapshotType);
        code.OpenBlock();
        foreach (var member in members.Select(static contractMember => contractMember.Member))
        {
            var valueType = MemberValueType(member, dialect, implementationNamespace);
            AppendPropertyDoc(code, "Gets or sets the snapshot endpoint for " + member.Property.Name + ".");
            code.AppendLine("public " + endpoint + "<" + valueType + ">? " + SparseNaming.EscapeIdentifier(member.Property.Name) + " { get; set; }");
        }
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
        code.AppendLine("public " + endpoint + "<" + snapshotType + ">? Before { get; set; }");
        AppendPropertyDoc(code, "Gets or sets the after snapshot for a replacement.");
        code.AppendLine("public " + endpoint + "<" + snapshotType + ">? After { get; set; }");
        code.CloseBlock();
        code.AppendLine();
    }

    internal static void AppendChangeProperty(SharedIndentedBuilder code, string modelChangeType)
    {
        AppendPropertyDoc(code, "Gets or sets the typed model change.");
        code.AppendLine("public " + modelChangeType + "? Change { get => GetTypedChange(); set => SetTypedChange(value); }");
    }

    internal static void AppendFacadeBridge(
        SharedIndentedBuilder code,
        ImmutableArray<SparsePayloadMemberContract> members,
        string endpoint,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    )
    {
        var changeType = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "Change");
        var rootType = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "Root");
        var rootChangeType = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "RootChange");
        var snapshotType = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "Snapshot");
        var deltaType = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "MembersDelta");
        var modelChangeType = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "ModelChange");
        var kindType = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "ModelChangeKind");

        code.AppendLine(
            "internal static " + modelChangeType + "? FromLegacyChanges(global::System.Collections.Generic.List<" + changeType + ">? changes)"
        );
        code.OpenBlock();
        code.AppendLine("if (changes is null) return null;");
        code.AppendLine("if (changes.Count == 1 && changes[0] is " + rootChangeType + " whole)");
        code.OpenBlock();
        code.AppendLine(
            "return new "
                + modelChangeType
                + " { Kind = "
                + kindType
                + ".Replace, Before = MapRootEndpointToSnapshot(whole.Before), After = MapRootEndpointToSnapshot(whole.After) };"
        );
        code.CloseBlock();
        code.AppendLine("var members = new " + deltaType + "();");
        code.AppendLine("foreach (var change in changes)");
        code.OpenBlock();
        code.AppendLine("switch (change)");
        code.OpenBlock();
        foreach (var member in members.Select(static contractMember => contractMember.Member))
        {
            var memberType = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(dialect, modelType, "Change", member.Id);
            var property = SparseNaming.EscapeIdentifier(member.Property.Name);
            code.AppendLine("case " + memberType + " member" + member.Id + ":");
            code.AppendLine(
                "if (members."
                    + property
                    + " is not null) throw new global::System.ArgumentException(\"A model update cannot contain duplicate member changes.\", nameof(changes));"
            );
            code.AppendLine("members." + property + " = member" + member.Id + ";");
            code.AppendLine("break;");
        }
        code.AppendLine("default: throw new global::System.ArgumentException(\"A model update contains an unknown change.\", nameof(changes));");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("return new " + modelChangeType + " { Kind = " + kindType + ".Update, Members = members };");
        code.CloseBlock();
        code.AppendLine();

        code.AppendLine(
            "internal static global::System.Collections.Generic.List<" + changeType + ">? ToLegacyChanges(" + modelChangeType + "? change)"
        );
        code.OpenBlock();
        code.AppendLine("if (change is null) return null;");
        code.AppendLine("if (change.Kind == " + kindType + ".Replace)");
        code.OpenBlock();
        code.AppendLine(
            "if (change.Members is not null) throw new global::System.ArgumentException(\"A replacement cannot contain member changes.\", nameof(change));"
        );
        code.AppendLine(
            "if (change.Before is null || change.After is null) throw new global::System.ArgumentException(\"A replacement requires both endpoints.\", nameof(change));"
        );
        code.AppendLine(
            "return new global::System.Collections.Generic.List<"
                + changeType
                + "> { new "
                + rootChangeType
                + " { Before = MapSnapshotEndpointToRoot(change.Before), After = MapSnapshotEndpointToRoot(change.After) } };"
        );
        code.CloseBlock();
        code.AppendLine("if (change.Kind != " + kindType + ".Update) throw new global::System.ArgumentOutOfRangeException(nameof(change));");
        code.AppendLine(
            "if (change.Before is not null || change.After is not null) throw new global::System.ArgumentException(\"An update cannot contain replacement endpoints.\", nameof(change));"
        );
        code.AppendLine(
            "if (change.Members is null) throw new global::System.ArgumentException(\"An update requires a members object.\", nameof(change));"
        );
        code.AppendLine("var changes = new global::System.Collections.Generic.List<" + changeType + ">();");
        foreach (var member in members.Select(static contractMember => contractMember.Member))
        {
            var property = SparseNaming.EscapeIdentifier(member.Property.Name);
            code.AppendLine("if (change.Members." + property + " is not null) changes.Add(change.Members." + property + ");");
        }
        code.AppendLine("return changes;");
        code.CloseBlock();
        code.AppendLine();

        code.AppendLine(
            "internal static " + endpoint + "<" + snapshotType + ">? MapRootEndpointToSnapshot(" + endpoint + "<" + rootType + ">? endpointValue)"
        );
        code.OpenBlock();
        code.AppendLine("if (endpointValue is null) return null;");
        code.AppendLine(
            "return new "
                + endpoint
                + "<"
                + snapshotType
                + "> { State = endpointValue.State, Value = endpointValue.Value is null ? null : SnapshotFromRoot(endpointValue.Value) };"
        );
        code.CloseBlock();
        code.AppendLine();

        code.AppendLine(
            "internal static " + endpoint + "<" + rootType + ">? MapSnapshotEndpointToRoot(" + endpoint + "<" + snapshotType + ">? endpointValue)"
        );
        code.OpenBlock();
        code.AppendLine("if (endpointValue is null) return null;");
        code.AppendLine(
            "return new "
                + endpoint
                + "<"
                + rootType
                + "> { State = endpointValue.State, Value = endpointValue.Value is null ? null : RootFromSnapshot(endpointValue.Value) };"
        );
        code.CloseBlock();
        code.AppendLine();

        code.AppendLine("internal static " + snapshotType + " SnapshotFromRoot(" + rootType + " root)");
        code.OpenBlock();
        code.AppendLine("if (root is null) throw new global::System.ArgumentNullException(nameof(root));");
        code.AppendLine(
            "if (root.Members is null) throw new global::System.ArgumentException(\"A snapshot must contain its member collection.\", nameof(root));"
        );
        code.AppendLine("var snapshot = new " + snapshotType + "();");
        code.AppendLine("foreach (var member in root.Members)");
        code.OpenBlock();
        code.AppendLine("switch (member)");
        code.OpenBlock();
        foreach (var member in members.Select(static contractMember => contractMember.Member))
        {
            var memberType = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(dialect, modelType, "Change", member.Id);
            var property = SparseNaming.EscapeIdentifier(member.Property.Name);
            code.AppendLine("case " + memberType + " member" + member.Id + ":");
            code.AppendLine(
                "if (snapshot."
                    + property
                    + " is not null) throw new global::System.ArgumentException(\"A snapshot cannot contain duplicate members.\", nameof(root));"
            );
            code.AppendLine("snapshot." + property + " = member" + member.Id + ".Value;");
            code.AppendLine("break;");
        }
        code.AppendLine("default: throw new global::System.ArgumentException(\"A snapshot contains an unknown member.\", nameof(root));");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("return snapshot;");
        code.CloseBlock();
        code.AppendLine();

        code.AppendLine("internal static " + rootType + " RootFromSnapshot(" + snapshotType + " snapshot)");
        code.OpenBlock();
        code.AppendLine("if (snapshot is null) throw new global::System.ArgumentNullException(nameof(snapshot));");
        code.AppendLine("var root = new " + rootType + "();");
        foreach (var member in members.Select(static contractMember => contractMember.Member))
        {
            var memberType = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(dialect, modelType, "Change", member.Id);
            var property = SparseNaming.EscapeIdentifier(member.Property.Name);
            code.AppendLine(
                "if (snapshot." + property + " is not null) root.Members.Add(new " + memberType + " { Value = snapshot." + property + " });"
            );
        }
        code.AppendLine("return root;");
        code.CloseBlock();
        code.AppendLine();
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
