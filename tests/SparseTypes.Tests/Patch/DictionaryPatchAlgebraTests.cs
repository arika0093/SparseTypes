using SparseTypes;

namespace SparseTypes.Tests;

/// <summary>Dictionary Patch compose, rebase, and removal-cancellation behavior.</summary>
/// <remarks>
/// Guards the shared dictionary algebra against regressions: granular
/// composition matches sequential apply across set/remove/update overlap,
/// nested edits filter against following removals, rebase follows local
/// operation order with direct map probes, and indexed removal cancellation
/// keeps survivors intact across disjoint set entries.
/// </remarks>
public sealed class DictionaryPatchAlgebraTests
{
    private static Optional<Dictionary<string, int>> ScalarMap(
        params (string Key, int Value)[] entries
    )
    {
        var map = new Dictionary<string, int>();
        foreach (var (key, value) in entries)
        {
            map[key] = value;
        }

        return Optional<Dictionary<string, int>>.Present(map);
    }

    private static Optional<Dictionary<string, KeyedServer>> StructuralMap(
        params (string Key, string Id, string Name)[] entries
    )
    {
        var map = new Dictionary<string, KeyedServer>();
        foreach (var (key, id, name) in entries)
        {
            map[key] = new KeyedServer { Id = id, Name = name };
        }

        return Optional<Dictionary<string, KeyedServer>>.Present(map);
    }

    private static Optional<ScalarDictHolder.Fragment?> ScalarState(
        params (string Key, int Value)[] entries
    )
    {
        var map = new Dictionary<string, int>();
        foreach (var (key, value) in entries)
        {
            map[key] = value;
        }

        return Optional<ScalarDictHolder.Fragment?>.Present(
            ScalarDictHolder.Fragment.From(new ScalarDictHolder { Scores = map })
        );
    }

    private static ScalarDictHolder.Patch.ScoresPatch ScalarPatch(
        Action<ScalarDictHolder.Patch.ScoresPatch> build
    )
    {
        var patch = new ScalarDictHolder.Patch.ScoresPatch();
        build(patch);
        return patch;
    }

    [Test]
    public void ScalarCompose_MatchesSequentialApplyAcrossOverlapMatrix()
    {
        var basis = ScalarMap(("a", 1), ("b", 2), ("c", 3));
        var cases = new (
            string Name,
            Action<ScalarDictHolder.Patch.ScoresPatch> First,
            Action<ScalarDictHolder.Patch.ScoresPatch> Second
        )[]
        {
            ("remove-then-set", patch => patch.RemoveEntry("a"), patch => patch.SetEntry("a", 10)),
            ("set-then-remove", patch => patch.SetEntry("a", 10), patch => patch.RemoveEntry("a")),
            (
                "disjoint-operations",
                patch =>
                {
                    patch.SetEntry("a", 10);
                    patch.RemoveEntry("b");
                },
                patch =>
                {
                    patch.SetEntry("c", 30);
                    patch.UpdateEntry("c", 31);
                }
            ),
            (
                "update-update-overlap",
                patch => patch.UpdateEntry("c", 30),
                patch => patch.UpdateEntry("c", 31)
            ),
            (
                "update-then-set-overlap",
                patch => patch.UpdateEntry("c", 30),
                patch => patch.SetEntry("c", 300)
            ),
            (
                "overlapping-removals",
                patch =>
                {
                    patch.RemoveEntry("a");
                    patch.RemoveEntry("b");
                },
                patch =>
                {
                    patch.RemoveEntry("b");
                    patch.RemoveEntry("c");
                }
            ),
            (
                "set-overlap-then-remove",
                patch => patch.SetEntry("a", 10),
                patch =>
                {
                    patch.SetEntry("a", 20);
                    patch.RemoveEntry("b");
                }
            ),
            (
                "update-then-remove-then-set",
                patch => patch.UpdateEntry("c", 30),
                patch =>
                {
                    patch.RemoveEntry("c");
                    patch.SetEntry("c", 300);
                }
            ),
            (
                "duplicate-and-unknown-keys",
                patch =>
                {
                    patch.RemoveEntry("a");
                    patch.RemoveEntry("a");
                    patch.SetEntry("b", 20);
                },
                patch =>
                {
                    patch.RemoveEntry("ghost");
                    patch.SetEntry("ghost", 1);
                    patch.RemoveEntry("ghost");
                }
            ),
        };

        foreach (var (name, firstBuild, secondBuild) in cases)
        {
            var first = ScalarPatch(firstBuild);
            var second = ScalarPatch(secondBuild);
            var composed = first.Compose(second);

            var sequential = second.Apply(first.Apply(basis));
            var combined = composed.Apply(basis);
            ScalarDictHolder
                .Patch.ScoresPatch.Between(combined, sequential)
                .IsEmpty.ShouldBeTrue(name);
        }
    }

    [Test]
    public void ScalarCompose_WholeValueEarlyReturnsBypassGranularPath()
    {
        var basis = ScalarMap(("a", 1));
        var granular = ScalarPatch(patch => patch.SetEntry("a", 2));
        var whole = new ScalarDictHolder.Patch.ScoresPatch();
        whole.Set(new Dictionary<string, int> { ["z"] = 9 });

        var afterWhole = granular.Compose(whole);
        var wholeApplied = afterWhole.Apply(basis).Value!;
        wholeApplied.Count.ShouldBe(1);
        wholeApplied["z"].ShouldBe(9);

        var wholeFirst = new ScalarDictHolder.Patch.ScoresPatch();
        wholeFirst.Set(new Dictionary<string, int> { ["a"] = 5 });
        var beforeGranular = wholeFirst.Compose(granular);
        beforeGranular.Apply(basis).Value!["a"].ShouldBe(2);
    }

    [Test]
    public void NestedCompose_FiltersFirstEditsAgainstFollowingRemovals()
    {
        var basis = StructuralMap(("web", "s1", "Old"), ("db", "s2", "Db"));
        var first = new StructuralDictHolder.Patch.ServersPatch();
        first.Edit("web").Name = "New";
        first.Edit("db").Name = "Changed";

        // A following removal drops the filtered nested edit but keeps edits
        // for surviving keys.
        var removal = new StructuralDictHolder.Patch.ServersPatch();
        removal.RemoveEntry("web");
        var filtered = first.Compose(removal);
        var applied = filtered.Apply(basis).Value!;
        applied.ContainsKey("web").ShouldBeFalse();
        applied["db"].Name.ShouldBe("Changed");

        // Absent and empty first edits need no filtering: composing two empty
        // patches stays empty, and a removal-only second patch passes through.
        var empty = new StructuralDictHolder.Patch.ServersPatch();
        empty.Compose(new StructuralDictHolder.Patch.ServersPatch()).IsEmpty.ShouldBeTrue();
        var removalOnly = empty.Compose(removal);
        var removalApplied = removalOnly.Apply(basis).Value!;
        removalApplied.ContainsKey("web").ShouldBeFalse();
        removalApplied["db"].Name.ShouldBe("Db");
    }

    [Test]
    public void NestedCompose_FoldsSecondEditsOntoSetBase()
    {
        var basis = StructuralMap(("web", "s1", "Old"));
        var first = new StructuralDictHolder.Patch.ServersPatch();
        first.SetEntry("cache", new KeyedServer { Id = "c1", Name = "Cache" });

        var second = new StructuralDictHolder.Patch.ServersPatch();
        second.Edit("cache").Name = "Warmed";

        // Folding applies the nested edit to the composed set value instead
        // of keeping a separate edit entry.
        var composed = first.Compose(second);
        var applied = composed.Apply(basis).Value!;
        applied["cache"].Name.ShouldBe("Warmed");
        StructuralDictHolder
            .Patch.ServersPatch.Between(
                Optional<Dictionary<string, KeyedServer>>.Present(applied),
                StructuralMap(("web", "s1", "Old"), ("cache", "c1", "Warmed"))
            )
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void Rebase_SinglePendingKeyAtEndOfLargeMapKeepsLocalEdit()
    {
        var baseMap = new Dictionary<string, int>();
        for (var index = 0; index < 2000; index++)
        {
            baseMap["k" + index] = index;
        }

        var baseState = Optional<Dictionary<string, int>>.Present(baseMap);
        var currentState = Optional<Dictionary<string, int>>.Present(
            new Dictionary<string, int>(baseMap)
        );
        var local = ScalarPatch(patch => patch.SetEntry("k1999", 4242));

        var result = ScalarDictHolder.Patch.ScoresPatch.Rebase(baseState, local, currentState);

        result.HasConflicts.ShouldBeFalse();
        var rebased = result.Rebased.Apply(currentState).Value!;
        rebased["k1999"].ShouldBe(4242);
        rebased.Count.ShouldBe(2000);
    }

    [Test]
    public void Rebase_FollowsLocalOperationOrderForConflicts()
    {
        var baseState = ScalarMap(("alpha", 1), ("beta", 1));
        var currentState = ScalarMap(("alpha", 9), ("beta", 9));
        // Local operation order is set-then-remove while the base map orders
        // the keys remove-then-set: conflicts must follow the operations.
        var local = ScalarPatch(patch =>
        {
            patch.SetEntry("beta", 2);
            patch.RemoveEntry("alpha");
        });

        var result = ScalarDictHolder.Patch.ScoresPatch.Rebase(baseState, local, currentState);

        result.HasConflicts.ShouldBeTrue();
        result.Conflicts.Count.ShouldBe(2);
        result.Conflicts[0].PathText.ShouldContain("beta");
        result.Conflicts[1].PathText.ShouldContain("alpha");
    }

    [Test]
    public void Rebase_SkipsPendingKeysAbsentFromEveryMap()
    {
        var baseState = ScalarMap(("a", 1));
        var currentState = ScalarMap(("a", 1), ("b", 2));
        // Removing an unknown key changes nothing; the concurrent addition of
        // "b" is untouched by the local patch.
        var local = ScalarPatch(patch => patch.RemoveEntry("ghost"));

        var result = ScalarDictHolder.Patch.ScoresPatch.Rebase(baseState, local, currentState);

        result.HasConflicts.ShouldBeFalse();
        result.Rebased.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void Rebase_KeepsNewlyIntroducedKeyAndReportsAlreadyApplied()
    {
        var baseState = ScalarMap(("a", 1));

        // A key introduced by the local patch survives onto an unchanged
        // current state.
        var local = ScalarPatch(patch => patch.SetEntry("fresh", 7));
        var kept = ScalarDictHolder.Patch.ScoresPatch.Rebase(baseState, local, baseState);
        kept.HasConflicts.ShouldBeFalse();
        kept.Rebased.Apply(baseState).Value!["fresh"].ShouldBe(7);

        // Once current already contains the introduced value, the rebased
        // patch is a no-op.
        var applied = local.Apply(baseState);
        var replayed = ScalarDictHolder.Patch.ScoresPatch.Rebase(baseState, local, applied);
        replayed.HasConflicts.ShouldBeFalse();
        replayed.Rebased.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void Rebase_NestedEditRebasesWithoutConflictOnDisjointChange()
    {
        var baseState = StructuralMap(("web", "s1", "Old"), ("db", "s2", "Db"));
        var currentState = StructuralMap(("web", "s1", "Old"), ("db", "s2", "Remote"));
        var local = new StructuralDictHolder.Patch.ServersPatch();
        local.Edit("web").Name = "New";

        var result = StructuralDictHolder.Patch.ServersPatch.Rebase(baseState, local, currentState);

        result.HasConflicts.ShouldBeFalse();
        var applied = result.Rebased.Apply(currentState).Value!;
        applied["web"].Name.ShouldBe("New");
        applied["db"].Name.ShouldBe("Remote");
    }

    [Test]
    public void SetEntry_DisjointKeysKeepIndexedRemovalsIntact()
    {
        const int removals = 100;
        const int disjointSets = 200;
        var patch = new ScalarDictHolder.Patch.ScoresPatch();
        for (var index = 0; index < removals; index++)
        {
            patch.RemoveEntry("r" + index);
        }

        // None of these cancellations succeed; the removal index (built past
        // the 32-entry threshold) must stay valid throughout.
        for (var index = 0; index < disjointSets; index++)
        {
            patch.SetEntry("w" + index, index);
        }

        var basis = new Dictionary<string, int>();
        for (var index = 0; index < removals; index++)
        {
            basis["r" + index] = index;
        }

        var applied = patch.Apply(Optional<Dictionary<string, int>>.Present(basis)).Value!;
        foreach (var key in basis.Keys)
        {
            applied.ContainsKey(key).ShouldBeFalse();
        }

        for (var index = 0; index < disjointSets; index++)
        {
            applied["w" + index].ShouldBe(index);
        }

        // The index survived the misses: updating a removed entry still fails
        // while updating a set entry succeeds without touching removals.
        Should.Throw<InvalidOperationException>(() => patch.UpdateEntry("r0", -1));
        patch.UpdateEntry("w0", -1);
        var updated = patch.Apply(Optional<Dictionary<string, int>>.Present(basis)).Value!;
        updated["w0"].ShouldBe(-1);
        updated.ContainsKey("r0").ShouldBeFalse();
    }

    [Test]
    public void SetEntry_CancelsIndexedRemovalAndPreservesSurvivorOrder()
    {
        var patch = new ScalarDictHolder.Patch.ScoresPatch();
        for (var index = 0; index < 64; index++)
        {
            patch.RemoveEntry("r" + index);
        }

        patch.SetEntry("r10", 100);
        patch.SetEntry("r20", 200);

        var basis = new Dictionary<string, int>();
        for (var index = 0; index < 64; index++)
        {
            basis["r" + index] = index;
        }

        // Cancelled keys come back as set entries; every other removal stays.
        var applied = patch.Apply(Optional<Dictionary<string, int>>.Present(basis)).Value!;
        applied["r10"].ShouldBe(100);
        applied["r20"].ShouldBe(200);
        applied.ContainsKey("r11").ShouldBeFalse();
        applied.Count.ShouldBe(2);

        // Re-adding a survivor keeps cancellation working after the index was
        // invalidated by the earlier hits. Removing a set key only un-sets it
        // (early return), so the basis value shows through again.
        patch.RemoveEntry("r10");
        var reapplied = patch.Apply(Optional<Dictionary<string, int>>.Present(basis)).Value!;
        reapplied["r10"].ShouldBe(10);
        reapplied["r20"].ShouldBe(200);
    }

    [Test]
    public void SetEntry_SmallUnindexedListsCancelWithoutIndex()
    {
        var patch = new ScalarDictHolder.Patch.ScoresPatch();
        patch.RemoveEntry("a");
        patch.RemoveEntry("b");
        patch.SetEntry("a", 1);

        var applied = patch.Apply(ScalarMap(("a", 0), ("b", 0))).Value!;
        applied["a"].ShouldBe(1);
        applied.ContainsKey("b").ShouldBeFalse();
    }

    [Test]
    public void DictionaryAlgebra_RoundTripsThroughRootPatch()
    {
        var before = ScalarState(("a", 1), ("b", 2));
        var after = ScalarState(("b", 3), ("c", 4));
        var patch = ScalarDictHolder.Patch.Between(before, after);

        ScalarDictHolder.Patch.Between(patch.Apply(before), after).IsEmpty.ShouldBeTrue();
        ScalarDictHolder
            .Patch.Between(patch.Invert(before).Apply(patch.Apply(before)), before)
            .IsEmpty.ShouldBeTrue();

        // Root composition delegates to the member compose path above.
        var extra = ScalarDictHolder.Patch.Between(after, ScalarState(("b", 5), ("c", 4)));
        var composed = patch.Compose(extra);
        ScalarDictHolder
            .Patch.Between(composed.Apply(before), ScalarState(("b", 5), ("c", 4)))
            .IsEmpty.ShouldBeTrue();
    }
}
