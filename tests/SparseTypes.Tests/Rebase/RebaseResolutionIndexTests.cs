using SparseTypes;

namespace SparseTypes.Tests;

/// <summary>Indexed conflict and decision lookups behind the resolution surface.</summary>
/// <remarks>Synthetic conflicts cover shapes rebase detection cannot produce, such as duplicate paths.</remarks>
public sealed class RebaseResolutionIndexTests
{
    private static ResOrder Order(decimal price = 1m) => new() { Number = "n", Price = price };

    private static SparseConflict Scalar(SparsePath path, string? reason) =>
        new(
            path,
            SparseConflictKind.Scalar,
            Optional<object?>.Present(1m),
            Optional<object?>.Present(2m),
            Optional<object?>.Present(3m),
            reason
        );

    private static ResOrder.ChangeSet.Resolution Resolution(params SparseConflict[] conflicts) =>
        new(
            new RebaseResult<ResOrder.ChangeSet>(
                ResOrder.ChangeSet.Between(Order(), Order()),
                conflicts
            ),
            Optional<ResOrder.Fragment?>.Present(ResOrder.Fragment.From(Order()))
        );

    private static string? Message(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }

    [Test]
    public void Root_Decision_Is_Nearest_Until_Exact_Overrides()
    {
        var root = SparsePath.Root<ResOrder>();
        var price = root.Member("Price");
        var retry = root.Member("Retry");
        var resolution = Resolution(Scalar(price, null), Scalar(retry, null));

        resolution.HasConflictsUnder(root).ShouldBeTrue();
        resolution.HasConflictsUnder(price).ShouldBeFalse();
        resolution.HasConflictsAffecting(root).ShouldBeTrue();
        resolution.HasConflictsAffecting(root.Member("Number")).ShouldBeFalse();
        resolution.GetState(price).ShouldBe(SparseConflictState.Unresolved);

        // The root decision covers both children as the nearest decision.
        resolution.UseCurrent(root);
        resolution.GetState(price).ShouldBe(SparseConflictState.ResolvedUseCurrent);
        resolution.GetState(retry).ShouldBe(SparseConflictState.ResolvedUseCurrent);
        resolution.EnumerateUnresolvedConflicts().ShouldBeEmpty();
        resolution.DecidedCount.ShouldBe(1);

        // A deeper decision overrides the ancestor for the exact path only.
        resolution.UseIncoming(price);
        resolution.GetState(price).ShouldBe(SparseConflictState.ResolvedUseIncoming);
        resolution.GetState(retry).ShouldBe(SparseConflictState.ResolvedUseCurrent);
        resolution.DecidedCount.ShouldBe(2);
    }

    [Test]
    public void ClearDecision_Falls_Back_To_Covering_Ancestor()
    {
        var root = SparsePath.Root<ResOrder>();
        var price = root.Member("Price");
        var resolution = Resolution(Scalar(price, null));

        resolution.UseIncoming(price);
        resolution.UseCurrent(root);

        // The nearer exact decision wins over the covering ancestor.
        resolution.GetState(price).ShouldBe(SparseConflictState.ResolvedUseIncoming);
        resolution.DecidedCount.ShouldBe(2);

        // Clearing the exact path falls back to the covering ancestor.
        resolution.ClearDecision(price).ShouldBeTrue();
        resolution.GetState(price).ShouldBe(SparseConflictState.ResolvedUseCurrent);
        resolution.DecidedCount.ShouldBe(1);

        // Clearing the ancestor leaves nothing covering.
        resolution.ClearDecision(root).ShouldBeTrue();
        resolution.GetState(price).ShouldBe(SparseConflictState.Unresolved);
        resolution.ClearDecision(root).ShouldBeFalse();
        resolution.DecidedCount.ShouldBe(0);
    }

    [Test]
    public void Typed_Keys_With_Equal_Display_Text_Do_Not_Collide()
    {
        var items = SparsePath.Root<ResOrder>().Member("Items");
        var textKey = items.Key("7");
        var numberKey = items.Key(7);
        var resolution = Resolution(Scalar(textKey, null), Scalar(numberKey, null));

        resolution.GetState(items.Key("other")).ShouldBe(SparseConflictState.NoConflict);
        resolution.GetState(textKey).ShouldBe(SparseConflictState.Unresolved);
        resolution.GetState(numberKey).ShouldBe(SparseConflictState.Unresolved);

        resolution.UseIncoming(numberKey);
        resolution.GetState(numberKey).ShouldBe(SparseConflictState.ResolvedUseIncoming);

        // The string-typed conflict stays unresolved while the int-typed one
        // resolves, despite identical display text.
        var unresolved = resolution.EnumerateUnresolvedConflicts();
        unresolved.Count.ShouldBe(1);
        unresolved[0].PathText.ShouldBe("Items[\"7\"]");
    }

    [Test]
    public void Duplicate_Conflict_Paths_Keep_First_And_All_Results()
    {
        var price = SparsePath.Root<ResOrder>().Member("Price");
        var first = Scalar(price, "first");
        var second = Scalar(price, "second");
        var resolution = Resolution(first, second);

        resolution.EnumerateConflicts().Count.ShouldBe(2);
        resolution.FindConflict(price).ShouldBeSameAs(first);
        resolution.HasConflict(price).ShouldBeTrue();
        resolution.HasConflictsUnder(price).ShouldBeFalse();

        resolution.UseIncoming(price);
        resolution.EnumerateUnresolvedConflicts().ShouldBeEmpty();
        resolution.GetState(price).ShouldBe(SparseConflictState.ResolvedUseIncoming);
    }

    [Test]
    public void Decision_Validation_Messages_Stay_Pinned()
    {
        var child = SparsePath.Root<ResOrder>().Member("Child");
        var deep = child.Member("Deep");
        var far = child.Member("Far");
        var resolution = Resolution(Scalar(deep, null));

        var custom = Message(() => resolution.Remove(child));
        custom.ShouldNotBeNull();
        custom.ShouldContain("Custom values require an exact conflict path");

        var missingCustom = Message(() => resolution.Remove(far));
        missingCustom.ShouldNotBeNull();
        missingCustom.ShouldContain("No conflict exists at or under 'Child.Far'");

        var missingIncoming = Message(() => resolution.UseIncoming(far));
        missingIncoming.ShouldNotBeNull();
        missingIncoming.ShouldContain("No conflict exists at or under 'Child.Far'");

        // Ancestor decisions stay valid for subtree coverage.
        Should.NotThrow(() => resolution.UseIncoming(child));
    }

    [Test]
    public void Temporary_Key_Decisions_Stay_Distinct()
    {
        var items = SparsePath.Root<ResOrder>().Member("Items");
        var first = items.TemporaryKey(Guid.NewGuid());
        var second = items.TemporaryKey(Guid.NewGuid());
        var resolution = Resolution(Scalar(first, null), Scalar(second, null));

        resolution.HasConflict(first).ShouldBeTrue();
        resolution.HasConflict(second).ShouldBeTrue();
        resolution.HasConflictsUnder(items).ShouldBeTrue();
        resolution.HasConflictsUnder(first).ShouldBeFalse();

        // Temporary identities share no slot: deciding one row leaves the
        // other unresolved.
        resolution.UseIncoming(first);
        resolution.GetState(first).ShouldBe(SparseConflictState.ResolvedUseIncoming);
        resolution.GetState(second).ShouldBe(SparseConflictState.Unresolved);
        var unresolved = resolution.EnumerateUnresolvedConflicts();
        unresolved.Count.ShouldBe(1);
        unresolved[0].Path.ShouldBe(second);
    }

    [Test]
    public void Conflicts_Under_Different_Root_Types_Stay_Isolated()
    {
        var orderPrice = SparsePath.Root<ResOrder>().Member("Price");
        var rosterItems = SparsePath.Root<ResRoster>().Member("Items");
        var resolution = Resolution(Scalar(orderPrice, null), Scalar(rosterItems, null));

        resolution.HasConflictsUnder(SparsePath.Root<ResOrder>()).ShouldBeTrue();
        resolution.HasConflictsUnder(SparsePath.Root<ResRoster>()).ShouldBeTrue();
        resolution.HasConflictsAffecting(orderPrice).ShouldBeTrue();
        resolution.HasConflictsAffecting(rosterItems).ShouldBeTrue();

        // A decision rooted at one model never covers the other root.
        resolution.UseCurrent(SparsePath.Root<ResOrder>());
        resolution.GetState(orderPrice).ShouldBe(SparseConflictState.ResolvedUseCurrent);
        resolution.GetState(rosterItems).ShouldBe(SparseConflictState.Unresolved);

        resolution.UseIncoming(SparsePath.Root<ResRoster>());
        resolution.EnumerateUnresolvedConflicts().ShouldBeEmpty();
        resolution.DecidedCount.ShouldBe(2);
    }

    [Test]
    public void Exact_Decisions_Win_Over_Ancestors_At_Scale()
    {
        var items = SparsePath.Root<ResOrder>().Member("Items");
        var conflicts = new SparseConflict[100];
        var exact = new SparsePath[100];
        for (var index = 0; index < conflicts.Length; index++)
        {
            exact[index] = items.Key(index).Member("Price");
            conflicts[index] = Scalar(exact[index], null);
        }

        var resolution = Resolution(conflicts);
        resolution.UseCurrent(SparsePath.Root<ResOrder>());

        // Every exact registration overrides the covering ancestor for its
        // own path only.
        for (var index = 0; index < exact.Length; index++)
        {
            resolution.UseIncoming(exact[index]);
        }

        resolution.DecidedCount.ShouldBe(101);
        for (var index = 0; index < exact.Length; index++)
        {
            resolution.GetState(exact[index]).ShouldBe(SparseConflictState.ResolvedUseIncoming);
        }

        resolution.EnumerateUnresolvedConflicts().ShouldBeEmpty();
    }

    [Test]
    public void Redacted_Descendant_Rejects_Ancestor_Coverage()
    {
        var child = SparsePath.Root<ResOrder>().Member("Child");
        var deep = child.Member("Deep");
        var resolution = Resolution(
            new SparseConflict(
                deep,
                SparseConflictKind.RedactedBefore,
                Optional<object?>.Missing,
                Optional<object?>.Missing,
                Optional<object?>.Missing,
                null
            ),
            Scalar(child.Member("Other"), null)
        );

        var incoming = Message(() => resolution.UseIncoming(child));
        incoming.ShouldNotBeNull();
        incoming.ShouldContain("Redacted-before conflicts carry no plaintext");

        var custom = Message(() => resolution.Remove(deep));
        custom.ShouldNotBeNull();
        custom.ShouldContain("Redacted-before conflicts carry no plaintext");
        resolution.HasConflictsUnder(child).ShouldBeTrue();

        // Keep-current covers the subtree without writing redacted values.
        Should.NotThrow(() => resolution.UseCurrent(child));
        resolution.EnumerateUnresolvedConflicts().ShouldBeEmpty();
    }
}
