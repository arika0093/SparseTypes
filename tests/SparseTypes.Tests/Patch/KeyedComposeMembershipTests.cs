using SparseTypes;

namespace SparseTypes.Tests;

/// <summary>Keyed Patch.Compose membership-index behavior.</summary>
/// <remarks>
/// Covers issues #354 (temporary removal membership sets) and #355
/// (assigned-key removal filtering and added-key membership in order
/// composition). All assertions pin exact ordered output, not just set
/// equality, and every composition is also checked against sequential apply
/// wherever sequential application is valid: a hand-built patch whose order
/// references keys removed by the preceding patch cannot be applied on its
/// own, so such scenarios assert the composed (fixed-up) patch instead.
/// Applied states are compared as model values because Patch.Between over
/// Apply-produced fragments reports phantom temp-adds (a pre-existing
/// Apply/Between representation quirk, unrelated to Compose).
/// </remarks>
public sealed class KeyedComposeMembershipTests
{
    private static TempOrderLine Assigned(int id, string name) => new() { Id = id, Name = name };

    private static TempOrderLine Pending(string name, Guid? temp = null) =>
        new() { Name = name, TemporaryId = temp ?? Guid.NewGuid() };

    private static Optional<TempOrderHolder.Fragment?> TF(params TempOrderLine[] items) =>
        Optional<TempOrderHolder.Fragment?>.Present(
            TempOrderHolder.Fragment.From(new TempOrderHolder { Lines = items.ToList() })
        );

    private static KeyedServer Server(string id, string name = "") =>
        new() { Id = id, Name = name };

    private static Optional<KeyedServerHolder.Fragment?> KF(params KeyedServer[] items) =>
        Optional<KeyedServerHolder.Fragment?>.Present(
            KeyedServerHolder.Fragment.From(new KeyedServerHolder { Items = items.ToList() })
        );

    private static List<(int Id, string Name, Guid? TemporaryId)> TempModel(
        Optional<TempOrderHolder.Fragment?> state
    ) =>
        state
            .Value!.ToModel()
            .Lines.Select(item => (item.Id, item.Name, item.TemporaryId))
            .ToList();

    private static List<(string Id, string Name)> KeyedModel(
        Optional<KeyedServerHolder.Fragment?> state
    ) => state.Value!.ToModel().Items.Select(item => (item.Id, item.Name)).ToList();

    private static void ShouldMatchSequentialTemp(
        Optional<TempOrderHolder.Fragment?> before,
        TempOrderHolder.Patch first,
        TempOrderHolder.Patch next
    )
    {
        var sequential = TempModel(next.Apply(first.Apply(before)));
        TempModel(first.Compose(next).Apply(before)).ShouldBe(sequential);
    }

    private static void ShouldMatchSequentialKeyed(
        Optional<KeyedServerHolder.Fragment?> before,
        KeyedServerHolder.Patch first,
        KeyedServerHolder.Patch next
    )
    {
        var sequential = KeyedModel(next.Apply(first.Apply(before)));
        KeyedModel(first.Compose(next).Apply(before)).ShouldBe(sequential);
    }

    [Test]
    public void TempComposePreservesFirstOccurrenceRemovalOrder()
    {
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        var t3 = Guid.NewGuid();
        var before = TF(Assigned(1, "x"), Pending("p1", t1), Pending("p2", t2), Pending("p3", t3));

        var first = new TempOrderHolder.Patch();
        first.Lines.RemoveByTemporaryKey(t1);
        first.Lines.RemoveByTemporaryKey(t2);
        var next = new TempOrderHolder.Patch();
        next.Lines.RemoveByTemporaryKey(t2);
        next.Lines.RemoveByTemporaryKey(t3);

        var composed = first.Compose(next);
        // Overlapping removal lists deduplicate with first-occurrence order.
        (composed.Lines.__removedTemp ?? []).ShouldBe([t1, t2, t3]);
        TempModel(composed.Apply(before)).Select(item => item.Name).ShouldBe(["x"]);
        ShouldMatchSequentialTemp(before, first, next);
    }

    [Test]
    public void TempComposeCancelsFirstAddAgainstNextRemoval()
    {
        var t1 = Guid.NewGuid();
        var before = TF(Assigned(1, "x"));

        var first = new TempOrderHolder.Patch();
        first.Lines.Add(Pending("p1", t1));
        var next = new TempOrderHolder.Patch();
        next.Lines.RemoveByTemporaryKey(t1);

        var composed = first.Compose(next);
        // A first-side addition canceled by a later removal leaves neither an
        // addition nor a net removal.
        (composed.Lines.__added ?? []).Count.ShouldBe(0);
        (composed.Lines.__removedTemp ?? []).Count.ShouldBe(0);
        composed.IsEmpty.ShouldBeTrue();
        ShouldMatchSequentialTemp(before, first, next);
    }

    [Test]
    public void TempComposeKeepsUnrelatedRemovalAndLaterAddition()
    {
        var t1 = Guid.NewGuid();
        var t9 = Guid.NewGuid();
        var before = TF(Assigned(1, "x"), Pending("p1", t1));

        var first = new TempOrderHolder.Patch();
        first.Lines.RemoveByTemporaryKey(t1);
        var next = new TempOrderHolder.Patch();
        next.Lines.Add(Pending("p9", t9));

        var composed = first.Compose(next);
        // A later re-addition with its own identity neither cancels the
        // earlier removal nor is canceled by it.
        (composed.Lines.__removedTemp ?? []).ShouldBe([t1]);
        (composed.Lines.__added ?? []).Select(item => item.Name).ShouldBe(["p9"]);
        TempModel(composed.Apply(before)).Select(item => item.Name).ShouldBe(["x", "p9"]);
        ShouldMatchSequentialTemp(before, first, next);
    }

    [Test]
    public void TempComposeDropsEditsCanceledByRemoval()
    {
        var t1 = Guid.NewGuid();
        var before = TF(Assigned(1, "x"), Pending("p1", t1));

        var first = new TempOrderHolder.Patch();
        first.Lines.EditByTemporaryKey(t1).Name = "p1 v2";
        var next = new TempOrderHolder.Patch();
        next.Lines.RemoveByTemporaryKey(t1);

        var composed = first.Compose(next);
        composed.Lines.__editedTemp.ShouldBeNull();
        (composed.Lines.__removedTemp ?? []).ShouldBe([t1]);
        TempModel(composed.Apply(before)).Select(item => item.Name).ShouldBe(["x"]);
        ShouldMatchSequentialTemp(before, first, next);
    }

    [Test]
    public void TempComposeMixedAssignedAndTemp()
    {
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        var t3 = Guid.NewGuid();
        var before = TF(Assigned(7, "a"), Assigned(9, "b"), Pending("p1", t1), Pending("p2", t2));

        var first = new TempOrderHolder.Patch();
        first.Lines.Remove(9);
        first.Lines.RemoveByTemporaryKey(t1);
        first.Lines.Add(Pending("p3", t3));
        var next = new TempOrderHolder.Patch();
        next.Lines.RemoveByTemporaryKey(t2);
        next.Lines.Edit(7).Name = "a2";

        var composed = first.Compose(next);
        (composed.Lines.__removed ?? []).ShouldBe([9]);
        (composed.Lines.__removedTemp ?? []).ShouldBe([t1, t2]);
        TempModel(composed.Apply(before)).Select(item => item.Name).ShouldBe(["a2", "p3"]);
        ShouldMatchSequentialTemp(before, first, next);
    }

    [Test]
    public void TempComposeEmptyInputs()
    {
        var before = TF(Assigned(1, "x"), Pending("p1"));

        var composed = new TempOrderHolder.Patch().Compose(new TempOrderHolder.Patch());
        composed.IsEmpty.ShouldBeTrue();
        TempModel(composed.Apply(before)).ShouldBe(TempModel(before));

        // One-sided assigned operations still compose with null temp indexes.
        var assigned = new TempOrderHolder.Patch();
        assigned.Lines.Edit(1).Name = "x2";
        TempModel(assigned.Compose(new TempOrderHolder.Patch()).Apply(before))
            .Select(item => item.Name)
            .ShouldBe(["x2", "p1"]);
        TempModel(new TempOrderHolder.Patch().Compose(assigned).Apply(before))
            .Select(item => item.Name)
            .ShouldBe(["x2", "p1"]);
    }

    [Test]
    public void TempDuplicateKeysStillRejected()
    {
        var temp = Guid.NewGuid();
        var patch = new TempOrderHolder.Patch();
        patch.Lines.Add(Pending("p1", temp));
        Should.Throw<InvalidOperationException>(() => patch.Lines.Add(Pending("p1 dup", temp)));

        var removal = new TempOrderHolder.Patch();
        removal.Lines.RemoveByTemporaryKey(temp);
        Should.Throw<InvalidOperationException>(() => removal.Lines.RemoveByTemporaryKey(temp));
    }

    [Test]
    public void TempComposeOverlappingRemovalsAtScale()
    {
        var temps = Enumerable.Range(0, 301).Select(_ => Guid.NewGuid()).ToList();
        var before = TF([
            Assigned(1, "x"),
            .. temps.Select((temp, index) => Pending("p" + index, temp)),
        ]);

        var first = new TempOrderHolder.Patch();
        foreach (var temp in temps.Take(201))
        {
            first.Lines.RemoveByTemporaryKey(temp);
        }
        var next = new TempOrderHolder.Patch();
        foreach (var temp in temps.Skip(100))
        {
            next.Lines.RemoveByTemporaryKey(temp);
        }

        var composed = first.Compose(next);
        // First-occurrence order across both sides, no quadratic re-scan.
        (composed.Lines.__removedTemp ?? []).ShouldBe(temps);
        TempModel(composed.Apply(before)).Select(item => item.Name).ShouldBe(["x"]);
        ShouldMatchSequentialTemp(before, first, next);
    }

    [Test]
    public void ComposeFiltersNextOrderByNetRemovalsExact()
    {
        var before = KF(Server("a"), Server("b"), Server("c"));

        var first = new KeyedServerHolder.Patch();
        first.Items.Remove("b");
        var next = new KeyedServerHolder.Patch();
        next.Items.SetOrder(["c", "a", "b"]);

        var composed = first.Compose(next);
        (composed.Items.__order ?? []).ShouldBe(["c", "a"]);
        // The next order references the removed key, so only the composed
        // (filtered) patch is appliable; sequential application is invalid.
        KeyedModel(composed.Apply(before)).Select(item => item.Id).ShouldBe(["c", "a"]);
    }

    [Test]
    public void ComposeAppendsMissingAddedKeysOnce()
    {
        var before = KF(Server("a"));

        // Added key absent from the order is appended exactly once. The first
        // member is composed at member level so an empty first patch cannot
        // bypass member composition via the model-level untouched-member
        // shortcut.
        var first = new KeyedServerHolder.Patch();
        var next = new KeyedServerHolder.Patch();
        next.Items.SetOrder(["a"]);
        next.Items.Add(Server("d", "D"));
        var composed = new KeyedServerHolder.Patch { Items = first.Items.Compose(next.Items) };
        (composed.Items.__order ?? []).ShouldBe(["a", "d"]);
        KeyedModel(composed.Apply(before)).Select(item => item.Id).ShouldBe(["a", "d"]);

        // Added key already represented in the order is not duplicated, and
        // sequential application agrees for this self-consistent patch.
        var covering = new KeyedServerHolder.Patch();
        covering.Items.SetOrder(["a", "d"]);
        covering.Items.Add(Server("d", "D"));
        var covered = new KeyedServerHolder.Patch
        {
            Items = new KeyedServerHolder.Patch().Items.Compose(covering.Items),
        };
        (covered.Items.__order ?? []).ShouldBe(["a", "d"]);
        KeyedModel(covered.Apply(before)).Select(item => item.Id).ShouldBe(["a", "d"]);
        ShouldMatchSequentialKeyed(before, new KeyedServerHolder.Patch(), covering);
    }

    [Test]
    public void ComposeAddOnlyPreservesSequentialAdditionState()
    {
        var before = KF(Server("a"));
        var first = new KeyedServerHolder.Patch();
        first.Items.Add(Server("b", "first"));
        var next = new KeyedServerHolder.Patch();
        next.Items.Add(Server("c", "second"));

        var composed = first.Compose(next);
        (composed.Items.__added ?? []).Select(static item => item.Id).ShouldBe(["b", "c"]);
        ShouldMatchSequentialKeyed(before, first, next);
    }

    [Test]
    public void ComposeAddThenRemoveCancelsAssignedElement()
    {
        var before = KF(Server("a", "old-a"));
        var first = new KeyedServerHolder.Patch();
        first.Items.Add(Server("b", "first-b"));
        var next = new KeyedServerHolder.Patch();
        next.Items.Remove("b");

        var composed = first.Compose(next);
        composed.IsEmpty.ShouldBeTrue();
        ShouldMatchSequentialKeyed(before, first, next);
    }

    [Test]
    public void ComposeRemoveOnlyPreservesFirstOccurrenceOrder()
    {
        var before = KF(Server("a"), Server("b"), Server("c"), Server("d"));
        var first = new KeyedServerHolder.Patch();
        first.Items.Remove("b");
        first.Items.Remove("a");
        var next = new KeyedServerHolder.Patch();
        next.Items.Remove("a");
        next.Items.Remove("c");

        var composed = first.Compose(next);
        (composed.Items.__removed ?? []).ShouldBe(["b", "a", "c"]);
        KeyedModel(composed.Apply(before)).Select(static item => item.Id).ShouldBe(["d"]);
        ShouldMatchSequentialKeyed(before, first, next);
    }

    [Test]
    public void ComposeOrderOnlyCopiesTheWinningOrder()
    {
        var before = KF(Server("a"), Server("b"), Server("c"));
        var first = new KeyedServerHolder.Patch();
        first.Items.SetOrder(["b", "a", "c"]);
        var next = new KeyedServerHolder.Patch();
        next.Items.SetOrder(["c", "b", "a"]);

        var composed = first.Compose(next);
        (composed.Items.__order ?? []).ShouldBe(["c", "b", "a"]);
        ShouldMatchSequentialKeyed(before, first, next);
        composed.Items.__order![0] = "a";
        (next.Items.__order ?? []).ShouldBe(["c", "b", "a"]);
    }

    [Test]
    public void ComposeEditThenRemoveDropsTheEdit()
    {
        var before = KF(Server("a", "old-a"), Server("b", "old-b"));
        var first = new KeyedServerHolder.Patch();
        first.Items.Edit("a").Name = "updated-a";
        var next = new KeyedServerHolder.Patch();
        next.Items.Remove("a");

        var composed = first.Compose(next);
        (composed.Items.__removed ?? []).ShouldBe(["a"]);
        KeyedModel(composed.Apply(before)).Select(static item => item.Id).ShouldBe(["b"]);
        ShouldMatchSequentialKeyed(before, first, next);
    }

    [Test]
    public void ComposeEditOnlyPreservesSequentialEdits()
    {
        var before = KF(Server("a", "old-a"), Server("b", "old-b"));
        var first = new KeyedServerHolder.Patch();
        first.Items.Edit("a").Name = "first-a";
        var next = new KeyedServerHolder.Patch();
        next.Items.Edit("a").Name = "next-a";
        next.Items.Edit("b").Name = "next-b";

        var composed = first.Compose(next);
        KeyedModel(composed.Apply(before)).ShouldBe([("a", "next-a"), ("b", "next-b")]);
        ShouldMatchSequentialKeyed(before, first, next);
    }

    [Test]
    public void ComposeEditOfAddedElementPreservesSequentialState()
    {
        var before = KF(Server("a", "old-a"));
        var first = new KeyedServerHolder.Patch();
        first.Items.Add(Server("b", "first-b"));
        var next = new KeyedServerHolder.Patch();
        next.Items.Edit("b").Name = "next-b";

        var composed = first.Compose(next);
        KeyedModel(composed.Apply(before)).ShouldBe([("a", "old-a"), ("b", "next-b")]);
        ShouldMatchSequentialKeyed(before, first, next);
    }

    [Test]
    public void ComposeRemovalFilteringUsesDefaultKeyEquality()
    {
        // The configured key comparer is EqualityComparer<T>.Default at every
        // construction site, so removal filtering and added-key membership
        // are case-sensitive for string keys. These assertions pin that
        // contract: a change to a case-insensitive comparer must update them
        // deliberately rather than silently changing Compose semantics.
        var before = KF(Server("a"), Server("A"), Server("b"));

        var first = new KeyedServerHolder.Patch();
        first.Items.Remove("A");
        var next = new KeyedServerHolder.Patch();
        next.Items.SetOrder(["a", "A", "b"]);
        var composed = first.Compose(next);
        // "a" survives the removal of "A": default (case-sensitive) equality.
        (composed.Items.__order ?? []).ShouldBe(["a", "b"]);
        KeyedModel(composed.Apply(before)).Select(item => item.Id).ShouldBe(["a", "b"]);

        // "a" is appended even though "A" is already ordered.
        var adder = new KeyedServerHolder.Patch();
        adder.Items.SetOrder(["A"]);
        adder.Items.Add(Server("a", "lower"));
        var added = new KeyedServerHolder.Patch
        {
            Items = new KeyedServerHolder.Patch().Items.Compose(adder.Items),
        };
        (added.Items.__order ?? []).ShouldBe(["A", "a"]);
    }

    [Test]
    public void ComposeDuplicateOrderKeysStillRejected()
    {
        var patch = new KeyedServerHolder.Patch();
        Should.Throw<InvalidOperationException>(() => patch.Items.SetOrder(["a", "a"]));
    }

    [Test]
    public void ComposeLargeOrderWithRemovalsAndAdditions()
    {
        var keys = Enumerable.Range(0, 200).Select(index => "k" + index).ToList();
        var before = KF(keys.Select(key => Server(key)).ToArray());

        var first = new KeyedServerHolder.Patch();
        foreach (var key in keys.Where((_, index) => index % 2 == 0))
        {
            first.Items.Remove(key);
        }
        var next = new KeyedServerHolder.Patch();
        next.Items.SetOrder(keys);
        next.Items.Add(Server("extra", "E"));

        var composed = first.Compose(next);
        var expected = keys.Where((_, index) => index % 2 == 1).Concat(["extra"]).ToList();
        (composed.Items.__order ?? []).ShouldBe(expected);
        KeyedModel(composed.Apply(before)).Select(item => item.Id).ShouldBe(expected);
    }

    [Test]
    public void TempComposeOrderKeepsWireAlignment()
    {
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        var b0 = TF(Assigned(7, "existing"));
        var b1 = TF(Assigned(7, "existing"), Pending("first", t1));

        // Single-step diffs from a temp-free baseline carry aligned order
        // slots; multi-step temp diffs do not correlate temporary identity,
        // so the second side stays hand-built here.
        var first = TempOrderHolder.Patch.Between(b0, b1);
        var next = new TempOrderHolder.Patch();
        next.Lines.Add(Pending("second", t2));

        var composed = first.Compose(next);
        // The composed order preserves the winning side exactly, with
        // __orderTemp slots aligned one-to-one with __order entries: the
        // first-side addition is already represented, so only the next-side
        // addition extends the order.
        var order = composed.Lines.__order ?? [];
        var orderTemp = composed.Lines.__orderTemp ?? [];
        order.Count.ShouldBe(orderTemp.Count);
        order.ShouldBe([7, 0, 0]);
        orderTemp.ShouldBe([null, t1, t2]);
        TempModel(composed.Apply(b0))
            .Select(item => item.Name)
            .ShouldBe(["existing", "first", "second"]);
        ShouldMatchSequentialTemp(b0, first, next);
    }

    [Test]
    public void TempComposeNextOrderFiltersAssignedRemovals()
    {
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        var t3 = Guid.NewGuid();
        var b1 = TF(Assigned(7, "existing"), Pending("first", t1));
        var b2 = TF(Assigned(7, "existing"), Pending("first", t1), Pending("second", t2));

        var first = new TempOrderHolder.Patch();
        first.Lines.Remove(7);
        first.Lines.Add(Pending("p3", t3));
        // Patch.Between does not correlate temporary identity across states,
        // so the next side re-lists its unassigned items; the composed order
        // below is still deterministic, but the patch is not appliable and
        // only its state is asserted.
        var next = TempOrderHolder.Patch.Between(b1, b2);

        var composed = first.Compose(next);
        (composed.Lines.__removed ?? []).ShouldBe([7]);
        (composed.Lines.__removedTemp ?? []).Count.ShouldBe(0);
        // The assigned removal filters the copied order; temporary slots are
        // kept, and only the first-side addition (absent from the winning
        // order) extends it.
        var order = composed.Lines.__order ?? [];
        var orderTemp = composed.Lines.__orderTemp ?? [];
        order.ShouldBe([0, 0, 0]);
        orderTemp.ShouldBe([t1, t2, t3]);
        (composed.Lines.__added ?? []).Select(item => item.Name).ShouldContain("p3");
    }

    [Test]
    public void TempComposeOrderFiltersRemovedTemporarySlots()
    {
        var t1 = Guid.NewGuid();
        var t9 = Guid.NewGuid();
        var before = TF(Assigned(7, "existing"));

        // Public flows cannot produce an order slot for a removed temporary
        // identity (Between never emits one, SetOrder clears orderTemp), so
        // this wire-level pin sets the slot directly; assertions read the
        // same internal fields used throughout these tests.
        var first = new TempOrderHolder.Patch();
        first.Lines.Add(Pending("p1", t1));
        first.Lines.__order = [7, 0, 0];
        first.Lines.__orderTemp = [null, t9, t1];
        var next = new TempOrderHolder.Patch();
        next.Lines.RemoveByTemporaryKey(t9);

        var composed = first.Compose(next);
        (composed.Lines.__removedTemp ?? []).ShouldBe([t9]);
        var order = composed.Lines.__order ?? [];
        var orderTemp = composed.Lines.__orderTemp ?? [];
        order.ShouldBe([7, 0]);
        orderTemp.ShouldBe([null, t1]);
        TempModel(composed.Apply(before)).Select(item => item.Name).ShouldBe(["existing", "p1"]);
    }
}
