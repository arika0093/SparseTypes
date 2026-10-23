using SparseTypes;

namespace SparseTypes.Tests.Mixed;

/// <summary>ComposeSequences overlap selection, ordering, and path-text corner cases.</summary>
/// <remarks>
/// The expectations encode the historical pairwise-scan behavior: unknown-match
/// first-consumption in second-sequence insertion order, replacements that move
/// the surviving whole member to the end, and sibling strings that are never
/// ancestors. These stay valid independent of the internal segment index.
/// </remarks>
public sealed class MixedComposeSequencesIndexTests
{
    [Test]
    public void AncestorFirstSideAbsorbsDescendantCandidatesAndLaterOverlaps()
    {
        // Bracket suffixes stay atomic: the keyed member prefix is the strict
        // ancestor of trailing member segments.
        var composed = MixedChangeAlgebra.ComposeSequences([Blind("Items[\"k\"]")], [Blind("Items[\"k\"].Name"), Blind("Items[\"k\"].Count")]);
        composed.Succeeded.ShouldBeTrue();
        composed.Composed.Count.ShouldBe(1);
        composed.Composed[0].Path.ShouldBe("Items[\"k\"]");
    }

    [Test]
    public void DescendantFirstSideIsReplacedByAncestorSecondSide()
    {
        var composed = MixedChangeAlgebra.ComposeSequences([Blind("Payload.Sub")], [Blind("Payload.Sub.Meta"), Blind("Payload")]);
        composed.Succeeded.ShouldBeTrue();
        composed.Composed.Count.ShouldBe(1);
        composed.Composed[0].Path.ShouldBe("Payload");
    }

    [Test]
    public void DisjointPathsKeepFirstThenSecondEnumerationOrder()
    {
        var composed = MixedChangeAlgebra.ComposeSequences([Blind("B1"), Blind("A1")], [Blind("C2"), Blind("D2")]);
        composed.Succeeded.ShouldBeTrue();
        composed.Composed.Select(static operation => operation.Path).ShouldBe(["B1", "A1", "C2", "D2"]);
    }

    [Test]
    public void NoRemovalsKeepTheStagedListAndEnumerationOrder()
    {
        var composed = MixedChangeAlgebra.ComposeSequences([Blind("First"), Blind("Nested.Host")], [Blind("Second"), Blind("Nested.Port")]);

        composed.Succeeded.ShouldBeTrue();
        composed.Composed.Select(static operation => operation.Path).ShouldBe(["First", "Nested.Host", "Second", "Nested.Port"]);
    }

    [Test]
    public void OneAncestorReplacementCompactsWithoutReorderingSurvivors()
    {
        var composed = MixedChangeAlgebra.ComposeSequences(
            [Blind("Left.Child"), Blind("Middle"), Blind("Right.Leaf")],
            [Blind("Left.Child.Meta"), Blind("Left")]
        );

        composed.Succeeded.ShouldBeTrue();
        composed.Composed.Select(static operation => operation.Path).ShouldBe(["Middle", "Right.Leaf", "Left"]);
    }

    [Test]
    public void ManyAncestorReplacementsPreserveSurvivorAndTrailingOrder()
    {
        const int replacementCount = 96;
        var first = new List<MixedMemberOperation>(replacementCount * 2);
        var second = new List<MixedMemberOperation>(replacementCount * 2);
        var expected = new List<string>(replacementCount * 2);
        for (var index = 0; index < replacementCount; index++)
        {
            var group = "Group" + index;
            first.Add(Blind(group + ".Child"));
            first.Add(Blind("Retained" + index));
            expected.Add("Retained" + index);
            second.Add(Blind(group + ".Child.Meta"));
            second.Add(Blind(group));
        }

        for (var index = 0; index < replacementCount; index++)
        {
            expected.Add("Group" + index);
        }

        var composed = MixedChangeAlgebra.ComposeSequences(first, second);

        composed.Succeeded.ShouldBeTrue();
        composed.Composed.Select(static operation => operation.Path).ShouldBe(expected);
    }

    [Test]
    public void WholeRootTransitionMixturesKeepRootAndContinuityOrder()
    {
        var firstRoot = new MixedMemberOperation("$root", MixedHistoryKind.Transition, MixedAfterKind.Value, true);
        var secondRoot = new MixedMemberOperation("$root", MixedHistoryKind.Transition, MixedAfterKind.Null, true);
        var composed = MixedChangeAlgebra.ComposeSequences([firstRoot, Blind("First.Member")], [secondRoot, Blind("Second.Member")]);

        composed.Succeeded.ShouldBeTrue();
        composed.Composed.ShouldHaveSingleItem();
        composed.Composed[0].Path.ShouldBe("$root");
        composed.Composed[0].IsWholeRoot.ShouldBeTrue();
        composed.PendingContinuityChecks.ShouldHaveSingleItem();
        composed.PendingContinuityChecks[0].Path.ShouldBe("$root");
    }

    [Test]
    public void EmptyFirstSequenceStillReplacesAncestorAndKeepsOrder()
    {
        var composed = MixedChangeAlgebra.ComposeSequences([], [Blind("Payload.Sub"), Blind("Payload.X"), Blind("Payload")]);
        composed.Succeeded.ShouldBeTrue();
        composed.Composed.Select(static operation => operation.Path).ShouldBe(["Payload.X", "Payload"]);
    }

    [Test]
    public void TransitionOverlapFailureKeepsUnrelatedDescendantAndClassifiesTheAncestor()
    {
        var composed = MixedChangeAlgebra.ComposeSequences([Transition("Payload")], [Transition("Payload.Meta"), Blind("Payload.Machine")]);
        composed.Succeeded.ShouldBeFalse();
        composed.Failures.ShouldHaveSingleItem();
        composed.Failures[0].Path.ShouldBe("Payload");
        var failureReason = composed.Failures[0].FailureReason;
        failureReason.ShouldNotBeNull();
        failureReason.ShouldContain("Payload");
        failureReason.ShouldContain("Payload.Meta");
        // The disjoint sibling survives; the transitioned parent and its
        // child are dropped without inventing values.
        composed.Composed.Select(static operation => operation.Path).ShouldBe(["Payload.Machine"]);
    }

    [Test]
    public void MultipleOverlapsConsumeTheFirstSecondSideEntryOnly()
    {
        var composed = MixedChangeAlgebra.ComposeSequences([Blind("Data")], [Blind("Data.Meta"), Blind("Data.Extra")]);
        composed.Succeeded.ShouldBeTrue();
        composed.Composed.Count.ShouldBe(1);
        composed.Composed[0].Path.ShouldBe("Data");
        composed.PendingContinuityChecks.ShouldBeEmpty();
    }

    [Test]
    public void AncestorCandidateWinsByInsertionOrderOverLaterSibling()
    {
        // "A" (ancestor, first insertion) is consumed as the overlap candidate
        // for the first-side descendant "A.C"; the kept ancestor then also
        // absorbs the sibling "A.B" in a second resolution.
        var composed = MixedChangeAlgebra.ComposeSequences([Blind("A.C")], [Blind("A"), Blind("A.B")]);
        composed.Succeeded.ShouldBeTrue();
        composed.Composed.Select(static operation => operation.Path).ShouldBe(["A"]);
    }

    [Test]
    public void ConsumedOverlapDoesNotMatchLaterFirstSideEntries()
    {
        var composed = MixedChangeAlgebra.ComposeSequences([Blind("A.X"), Blind("A.Y")], [Blind("A")]);

        composed.Succeeded.ShouldBeTrue();
        composed.Composed.Select(static operation => operation.Path).ShouldBe(["A", "A.Y"]);
    }

    [Test]
    public void ConsumedOverlapDoesNotCreateLaterTransitionFailure()
    {
        var composed = MixedChangeAlgebra.ComposeSequences([Blind("A.X"), Transition("A.Y")], [Blind("A")]);

        composed.Succeeded.ShouldBeTrue();
        composed.Failures.ShouldBeEmpty();
        composed.Composed.Select(static operation => operation.Path).ShouldBe(["A", "A.Y"]);
    }

    [Test]
    public void QuotedKeySegmentsStayAtomic()
    {
        // A dot inside a quoted bracket key is not a segment boundary.
        MixedChangeAlgebra.ComposeSequences([Blind("Items[\"a.b\"]")], [Blind("Items[\"a\"].Value")]).Composed.Count.ShouldBe(2);
        var nested = MixedChangeAlgebra.ComposeSequences([Blind("Items[\"k\"]")], [Blind("Items[\"k\"].Host")]);
        nested.Succeeded.ShouldBeTrue();
        nested.Composed.ShouldHaveSingleItem().Path.ShouldBe("Items[\"k\"]");
    }

    [Test]
    public void EscapedQuoteKeysCompareBySegment()
    {
        var composed = MixedChangeAlgebra.ComposeSequences([Blind("Items[\"k\\\"x\"]")], [Blind("Items[\"k\\\"x\"].Host")]);
        composed.Succeeded.ShouldBeTrue();
        composed.Composed.ShouldHaveSingleItem().Path.ShouldBe("Items[\"k\\\"x\"]");
    }

    [Test]
    public void KeyedSiblingsAreNeverAncestry()
    {
        var composed = MixedChangeAlgebra.ComposeSequences([Blind("Items[\"k\"]")], [Blind("Items[\"j\"].Value")]);
        composed.Succeeded.ShouldBeTrue();
        composed.Composed.Select(static operation => operation.Path).ShouldBe(["Items[\"k\"]", "Items[\"j\"].Value"]);
    }

    [Test]
    public void FirstMatchSelectionUsesInsertionOrderNotNearestAncestor()
    {
        // Both second-side entries are descendants of the first-side member;
        // whichever comes first in insertion order is consumed and resolved.
        var composed = MixedChangeAlgebra.ComposeSequences([Transition("Payload")], [Blind("Payload.X"), Transition("Payload.Y")]);
        composed.Succeeded.ShouldBeFalse();
        // The transition-overlap failure reports the first candidate pair.
        composed.Failures.ShouldHaveSingleItem();
        composed.Failures[0].Path.ShouldBe("Payload");
        var failureReason = composed.Failures[0].FailureReason;
        failureReason.ShouldNotBeNull();
        failureReason.ShouldContain("Payload.X");
        composed.Composed.Select(static operation => operation.Path).ShouldBe(["Payload.Y"]);
    }

    private static MixedMemberOperation Transition(string path, MixedAfterKind after = MixedAfterKind.Value) =>
        new(path, MixedHistoryKind.Transition, after);

    private static MixedMemberOperation Blind(string path, MixedAfterKind after = MixedAfterKind.Value) =>
        new(path, MixedHistoryKind.BlindSet, after);
}
