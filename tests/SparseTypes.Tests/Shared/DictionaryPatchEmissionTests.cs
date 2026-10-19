using System.Collections.Immutable;
using System.Threading;
using SparseTypes.Generator.Shared;

namespace SparseTypes.Tests.Shared;

/// <summary>Emitted-code regression tests for the dictionary Patch emitters.</summary>
/// <remarks>
/// Covers the shared compose, rebase, and removal-index emitters directly:
/// scalar compose must not index next removals, nested compose guards the
/// index, rebase drives pending operations without map scans, and both
/// cancellation paths probe the existing index before scanning.
/// </remarks>
public sealed class DictionaryPatchEmissionTests
{
    private const string KeyType = "global::System.Int32";
    private const string ScalarValueType = "global::System.String";
    private const string Comparer =
        "global::System.Collections.Generic.EqualityComparer<global::System.Int32>.Default";

    private static SparseTypeModel ScalarType(string name) =>
        new(name, name, name, IsReferenceType: true, IsFragmentModel: false, null);

    private static SparseMemberModel ScalarDictionaryMember()
    {
        var key = ScalarType(KeyType);
        var value = ScalarType(ScalarValueType);
        var dictionary = ScalarType(
            "global::System.Collections.Generic.Dictionary<global::System.Int32, global::System.String>"
        );
        var collection = new SparseCollectionInfo(
            SparseCollectionKind.Unsupported,
            SparseCloneCollectionKind.Dictionary,
            key,
            value,
            "System.Collections.Generic.Dictionary<TKey, TValue>",
            SparseCollectionSemantic.Dictionary,
            ImmutableArray<string>.Empty,
            KeyType,
            SparseKeyKind.None
        );
        return new SparseMemberModel(
            5,
            new SparsePropertyModel("Values", dictionary, JsonPropertyName: "Values"),
            null,
            SparseMergeModes.Replace,
            collection,
            null,
            null,
            false,
            true
        );
    }

    private static SparseMemberModel NestedDictionaryMember()
    {
        var key = ScalarType(KeyType);
        var value = new SparseTypeModel(
            "global::Ns.Item",
            "global::Ns.Item",
            "global::Ns.Item",
            IsReferenceType: true,
            IsFragmentModel: true,
            null,
            PatchApiPrefix: string.Empty
        );
        var dictionary = ScalarType(
            "global::System.Collections.Generic.Dictionary<global::System.Int32, global::Ns.Item>"
        );
        var collection = new SparseCollectionInfo(
            SparseCollectionKind.Unsupported,
            SparseCloneCollectionKind.Dictionary,
            key,
            value,
            "System.Collections.Generic.Dictionary<TKey, TValue>",
            SparseCollectionSemantic.Dictionary,
            ImmutableArray<string>.Empty,
            KeyType,
            SparseKeyKind.None
        );
        return new SparseMemberModel(
            6,
            new SparsePropertyModel("Servers", dictionary, JsonPropertyName: "Servers"),
            null,
            SparseMergeModes.Replace,
            collection,
            null,
            null,
            false,
            true
        );
    }

    private static SparseFragmentPatchEmitter.SparsePatchDialect FixtureDialect() =>
        new(
            "global::Downstream.",
            "__sparse_whole",
            "__SparseMembersEmpty",
            static member => "__sparse_patch_member_" + member.Id,
            static _ => string.Empty,
            "Apply",
            false,
            "global::Downstream.CompilerServices.DownstreamRuntime",
            "global::Downstream.DownstreamConflict",
            "global::Downstream.DownstreamConflictKind",
            static payload => "global::Downstream.DownstreamRebase<" + payload + ">",
            static member =>
                member.ChildFragmentType is null
                    ? "Patch"
                    : member.ChildFragmentType.Replace(
                        ".Fragment",
                        ".Patch",
                        StringComparison.Ordinal
                    ),
            static member => "global::Downstream.Delta_" + member.Id,
            PayloadImplementationContainerPrefix: "DownstreamInternal"
        );

    private static string EmitCompose(SparseMemberModel member, bool hasPatch)
    {
        var code = new SharedIndentedBuilder(CancellationToken.None);
        SparseDictionaryComposeEmitter.EmitDictionaryCompose(
            code,
            member,
            "ValuesPatch",
            KeyType,
            hasPatch ? "global::Ns.Item" : ScalarValueType,
            "global::System.Collections.Generic.Dictionary<global::System.Int32, global::System.String>",
            "global::Downstream.",
            "global::Downstream.FragmentOperation<global::System.Collections.Generic.Dictionary<global::System.Int32, global::System.String>>",
            "global::Downstream.FragmentOperationKind",
            Comparer,
            hasPatch
        );
        return code.ToString();
    }

    private static string EmitRebase(SparseMemberModel member, bool hasPatch)
    {
        var code = new SharedIndentedBuilder(CancellationToken.None);
        SparseDictionaryRebaseEmitter.EmitDictionaryRebase(
            code,
            member,
            "ValuesPatch",
            KeyType,
            hasPatch ? "global::Ns.Item" : ScalarValueType,
            "global::Downstream.",
            "global::Downstream.FragmentOperationKind",
            "global::Downstream.Optional<global::System.Collections.Generic.Dictionary<global::System.Int32, global::System.String>>",
            "global::Downstream.CompilerServices.DownstreamRuntime",
            Comparer,
            hasPatch,
            hasPatch ? "global::Ns.Item.Patch" : ScalarValueType,
            FixtureDialect()
        );
        return code.ToString();
    }

    [Test]
    public void Compose_ScalarDictionary_EmitsNoNextRemovedIndex()
    {
        var text = EmitCompose(ScalarDictionaryMember(), hasPatch: false);

        // The removal index exists only to filter first-patch nested edits,
        // which scalar dictionaries never carry.
        text.ShouldNotContain("__nextRemoved");
        // The next-set loop already drops shared keys from the removal set,
        // so no second first.__removed scan remains.
        text.ShouldNotContain(
            "next.__set is not null && next.__set.ContainsKey(k)) removed.Remove(k)"
        );
        // Next removals still drop composed set entries.
        text.ShouldContain(
            "if (next.__removed is not null && set is not null) foreach (var k in next.__removed) set.Remove(k);"
        );
    }

    [Test]
    public void Compose_NestedDictionary_EmitsGuardedNullableNextRemovedIndex()
    {
        var text = EmitCompose(NestedDictionaryMember(), hasPatch: true);

        // Nullable local allocated only when both sides need filtering.
        text.ShouldContain(">? __nextRemoved = (__edited is not null");
        text.ShouldContain("next.__removed.Count > 0)");
        text.ShouldContain("__nextRemoved is not null && __nextRemoved.Contains(kv.Key)");
        text.ShouldNotContain(
            "next.__set is not null && next.__set.ContainsKey(k)) removed.Remove(k)"
        );
        text.ShouldNotContain("global::SparseTypes");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Rebase_DrivesPendingOperationsWithoutMapScans(bool hasPatch)
    {
        var text = EmitRebase(
            hasPatch ? NestedDictionaryMember() : ScalarDictionaryMember(),
            hasPatch
        );

        // Operation order (Set, Edit, Remove) deduplicated by the comparer.
        text.ShouldContain("var __pendingSeen = new global::System.Collections.Generic.HashSet<");
        text.ShouldContain("var __pendingKeys = new global::System.Collections.Generic.List<");
        text.ShouldContain(
            "if (local.__set is not null) foreach (var k in local.__set.Keys) if (__pendingSeen.Add(k)) __pendingKeys.Add(k);"
        );
        text.ShouldContain(
            "if (local.__edited is not null) foreach (var k in local.__edited.Keys) if (__pendingSeen.Add(k)) __pendingKeys.Add(k);"
        );
        text.ShouldContain(
            "if (local.__removed is not null) foreach (var k in local.__removed) if (__pendingSeen.Add(k)) __pendingKeys.Add(k);"
        );
        // Direct per-key probes; no map traversal with early stop.
        text.ShouldContain("foreach (var k in __pendingKeys)");
        text.ShouldContain("if (!inBase && !inCurrent && !inDesired) continue;");
        text.ShouldNotContain("__mapIndex");
        text.ShouldNotContain("__pendingKeys.Remove(k)");
        text.ShouldNotContain("__pendingKeys.UnionWith");
        text.ShouldNotContain("global::SparseTypes");
    }

    [Test]
    public void CancelRemoval_StandaloneProbesIndexBeforeScanning()
    {
        var code = new SharedIndentedBuilder(CancellationToken.None);
        SparseDictionaryRemovalIndexEmitter.Emit(code, KeyType, Comparer);
        var text = code.ToString();

        text.ShouldContain("var slots = __removedLookup;");
        text.ShouldContain(
            "if (slots is not null && slots[__SparseRemovedSlot(key, slots)] == 0) return;"
        );
        // Indexed hits and unindexed lists keep the full reverse scan.
        text.ShouldContain("for (int index = __removed!.Count - 1; index >= 0; index--)");
    }

    [Test]
    public void CancelRemoval_GeneratedOnceProbesIndexBeforeScanning()
    {
        var text = SparseGeneratedOnceRemovalIndex.BuildSource(
            "Ns.Generated",
            CancellationToken.None
        );

        text.ShouldContain("var slots = lookup;");
        text.ShouldContain(
            "if (slots is not null && slots[RemovedSlot(removed, key, slots)] == 0) return;"
        );
        text.ShouldContain("for (int index = removed.Count - 1; index >= 0; index--)");
    }
}
