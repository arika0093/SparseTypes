using SparseTypes;

namespace SparseTypes.Tests;

/// <summary>Bulk and order conflict resolution through generated appliers.</summary>
/// <remarks>
/// Covers indexed order application (#334) and per-build collection staging
/// (#335): exact output sequences, lazy duplicate reporting, failure
/// atomicity, and input non-mutation.
/// </remarks>
public sealed class RebaseResolutionBulkTests
{
    private static ResRoster Roster(params (string Id, decimal Price)[] rows) =>
        new()
        {
            Items = rows.Select(static row => new ResItem { Id = row.Id, Price = row.Price })
                .ToList(),
        };

    private static Optional<ResRoster.Fragment?> RosterState(ResRoster model) =>
        Optional<ResRoster.Fragment?>.Present(ResRoster.Fragment.From(model));

    private static string Describe(IEnumerable<SparseConflict> conflicts) =>
        string.Join(";", conflicts.Select(static conflict => conflict.PathText));

    private static string DescribeIds(ResRoster model) =>
        string.Join(",", model.Items.Select(static item => item.Id));

    [Test]
    public void Reverse_Order_Resolves_Through_Key_Index()
    {
        var baseRoster = Roster(("a", 1m), ("b", 2m), ("c", 3m), ("d", 4m), ("e", 5m));
        var edited = Roster(("e", 5m), ("d", 4m), ("c", 3m), ("b", 2m), ("a", 1m));
        var current = Roster(("a", 1m), ("b", 2m), ("c", 3m), ("d", 4m), ("e", 5m), ("f", 6m));

        var rebase = baseRoster.CreateChangeSet(edited).RebaseOnto(RosterState(current));
        Describe(rebase.Conflicts).ShouldBe("Items", "order conflict path");
        var resolution = ResRoster.ChangeSet.BeginResolution(rebase, current);

        SparsePath items = ResRoster.SparsePath.Items;
        resolution.UseIncoming(items);
        resolution
            .TryBuild(out var resolved, out var failure)
            .ShouldBeTrue($"failure: {failure?.Reason}");

        // The full desired order applies; the unmentioned row keeps its
        // source position at the end.
        var model = resolved!.ToPatch().Apply(RosterState(current)).Value!.ToModel();
        DescribeIds(model).ShouldBe("e,d,c,b,a,f");
    }

    [Test]
    public void Unknown_Order_Key_Fails_Resolution()
    {
        // Rebase detection rejects duplicate current keys before resolution,
        // so the order index reports unknown requested keys instead: the
        // incoming order mentions "b" while the working state deleted it.
        var baseRoster = Roster(("a", 1m), ("b", 2m));
        var edited = Roster(("b", 2m), ("a", 1m));
        var current = Roster(("a", 1m));

        var rebase = baseRoster.CreateChangeSet(edited).RebaseOnto(RosterState(current));
        rebase.HasConflicts.ShouldBeTrue(
            $"expected an order conflict, got: {Describe(rebase.Conflicts)}"
        );
        var resolution = ResRoster.ChangeSet.BeginResolution(rebase, current);

        // A row decision keeps "b" deleted when a row conflict exists; the
        // member decision then covers the order conflict.
        SparsePath deleted = ResRoster.SparsePath.Items.Key("b");
        if (resolution.HasConflict(deleted))
        {
            resolution.UseCurrent(deleted);
        }

        SparsePath items = ResRoster.SparsePath.Items;
        resolution.UseIncoming(items);
        var built = resolution.TryBuild(out var resolved, out var failure);
        built.ShouldBeFalse("the desired order mentions a deleted key");
        resolved.ShouldBeNull();
        failure.ShouldNotBeNull();
        failure.Reason.ShouldContain("unknown key");

        // Failed builds leave every input graph unchanged.
        current.Items.Count.ShouldBe(1);
        current.Items[0].Id.ShouldBe("a");
    }

    [Test]
    public void Duplicate_Current_Keys_Fail_Before_Resolution()
    {
        // Duplicate source keys never reach the order index: rebase detection
        // rejects them while aligning the current state.
        var baseRoster = Roster(("a", 1m), ("b", 2m));
        var edited = Roster(("b", 2m), ("a", 1m));
        var current = Roster(("a", 10m), ("a", 20m), ("b", 2m));

        string? message = null;
        try
        {
            baseRoster.CreateChangeSet(edited).RebaseOnto(RosterState(current));
        }
        catch (InvalidOperationException exception)
        {
            message = exception.Message;
        }

        message.ShouldNotBeNull();
        message.ShouldContain("Duplicate key");
    }

    [Test]
    public void Bulk_Leaf_Choices_Resolve_Every_Row()
    {
        var ids = Enumerable.Range(0, 10).Select(static index => "row" + index).ToArray();
        var baseRoster = Roster(ids.Select(static id => (id, 1m)).ToArray());
        var edited = Roster(ids.Select(static id => (id, 2m)).ToArray());
        var current = Roster(ids.Select(static id => (id, 3m)).ToArray());

        var rebase = baseRoster.CreateChangeSet(edited).RebaseOnto(RosterState(current));
        rebase.Conflicts.Count.ShouldBe(10);
        var resolution = ResRoster.ChangeSet.BeginResolution(rebase, current);

        foreach (var id in ids)
        {
            resolution.UseIncoming(ResRoster.SparsePath.Items.Key(id).Price);
        }

        resolution.DecidedCount.ShouldBe(10);
        resolution
            .TryBuild(out var resolved, out var failure)
            .ShouldBeTrue($"failure: {failure?.Reason}");

        var model = resolved!.ToPatch().Apply(RosterState(current)).Value!.ToModel();
        model.Items.Count.ShouldBe(10);
        foreach (var item in model.Items)
        {
            item.Price.ShouldBe(2m);
        }

        // The shared current graph is never mutated by staged resolution.
        foreach (var item in current.Items)
        {
            item.Price.ShouldBe(3m);
        }
    }

    [Test]
    public void Late_Failure_Leaves_Inputs_Unchanged_And_Retry_Succeeds()
    {
        var baseRoster = Roster(("a", 1m), ("b", 2m));
        var edited = Roster(("a", 20m), ("b", 20m));
        var current = Roster(("a", 30m), ("b", 30m));

        var rebase = baseRoster.CreateChangeSet(edited).RebaseOnto(RosterState(current));
        rebase.Conflicts.Count.ShouldBe(2);
        var resolution = ResRoster.ChangeSet.BeginResolution(rebase, current);

        resolution.UseIncoming(ResRoster.SparsePath.Items.Key("a").Price);
        var built = resolution.TryBuild(out var resolved, out var failure);
        built.ShouldBeFalse("one conflict is still undecided");
        resolved.ShouldBeNull();
        failure.ShouldNotBeNull();

        current.Items[0].Price.ShouldBe(30m);
        current.Items[1].Price.ShouldBe(30m);

        // Deciding the remainder on the same state retries cleanly.
        resolution.UseIncoming(ResRoster.SparsePath.Items.Key("b").Price);
        resolution
            .TryBuild(out var retried, out var retryFailure)
            .ShouldBeTrue($"failure: {retryFailure?.Reason}");
        var model = retried!.ToPatch().Apply(RosterState(current)).Value!.ToModel();
        model.Items[0].Price.ShouldBe(20m);
        model.Items[1].Price.ShouldBe(20m);
    }

    [Test]
    public void Bulk_Dictionary_Entries_Resolve()
    {
        var keys = Enumerable.Range(0, 10).Select(static index => "key" + index).ToArray();
        var baseDict = new ResDict { Scores = keys.ToDictionary(static key => key, _ => 1) };
        var edited = new ResDict { Scores = keys.ToDictionary(static key => key, _ => 2) };
        var current = new ResDict { Scores = keys.ToDictionary(static key => key, _ => 3) };
        Optional<ResDict.Fragment?> DictState(ResDict model) =>
            Optional<ResDict.Fragment?>.Present(ResDict.Fragment.From(model));

        var rebase = baseDict.CreateChangeSet(edited).RebaseOnto(DictState(current));
        rebase.Conflicts.Count.ShouldBe(10);
        var resolution = ResDict.ChangeSet.BeginResolution(rebase, current);

        foreach (var key in keys)
        {
            resolution.UseIncoming(ResDict.SparsePath.Scores.Key(key));
        }

        resolution
            .TryBuild(out var resolved, out var failure)
            .ShouldBeTrue($"failure: {failure?.Reason}");

        var model = resolved!.ToPatch().Apply(DictState(current)).Value!.ToModel();
        model.Scores.Count.ShouldBe(10);
        foreach (var key in keys)
        {
            model.Scores[key].ShouldBe(2);
        }

        foreach (var key in keys)
        {
            current.Scores[key].ShouldBe(3);
        }
    }

    [Test]
    public void Order_And_Leaf_Choices_Combine_In_One_Build()
    {
        var baseRoster = Roster(("a", 1m), ("b", 2m));
        var edited = Roster(("b", 20m), ("a", 1m));
        var current = Roster(("a", 1m), ("b", 30m));

        var rebase = baseRoster.CreateChangeSet(edited).RebaseOnto(RosterState(current));
        var resolution = ResRoster.ChangeSet.BeginResolution(rebase, current);

        SparsePath items = ResRoster.SparsePath.Items;
        resolution.UseIncoming(items);
        resolution.UseIncoming(ResRoster.SparsePath.Items.Key("b").Price);
        resolution
            .TryBuild(out var resolved, out var failure)
            .ShouldBeTrue($"failure: {failure?.Reason}");

        // The incoming order wins and the leaf edit resolves against the
        // reordered rows, regardless of choice application order.
        var model = resolved!.ToPatch().Apply(RosterState(current)).Value!.ToModel();
        DescribeIds(model).ShouldBe("b,a");
        model.Items[0].Price.ShouldBe(20m);
        model.Items[1].Price.ShouldBe(1m);
        current.Items[1].Price.ShouldBe(30m);
    }

    [Test]
    public void Two_Members_Stage_Independently()
    {
        var basePair = new ResPair
        {
            First = new()
            {
                new ResItem { Id = "a", Price = 1m },
            },
            Second = new()
            {
                new ResItem { Id = "x", Price = 1m },
            },
        };
        var editedPair = new ResPair
        {
            First = new()
            {
                new ResItem { Id = "a", Price = 2m },
            },
            Second = new()
            {
                new ResItem { Id = "x", Price = 2m },
            },
        };
        var currentPair = new ResPair
        {
            First = new()
            {
                new ResItem { Id = "a", Price = 3m },
            },
            Second = new()
            {
                new ResItem { Id = "x", Price = 3m },
            },
        };
        Optional<ResPair.Fragment?> PairState(ResPair model) =>
            Optional<ResPair.Fragment?>.Present(ResPair.Fragment.From(model));

        var rebase = basePair.CreateChangeSet(editedPair).RebaseOnto(PairState(currentPair));
        rebase.Conflicts.Count.ShouldBe(2);
        var resolution = ResPair.ChangeSet.BeginResolution(rebase, currentPair);

        resolution.UseIncoming(ResPair.SparsePath.First.Key("a").Price);
        resolution.UseIncoming(ResPair.SparsePath.Second.Key("x").Price);
        resolution
            .TryBuild(out var resolved, out var failure)
            .ShouldBeTrue($"failure: {failure?.Reason}");

        var model = resolved!.ToPatch().Apply(PairState(currentPair)).Value!.ToModel();
        model.First.ShouldHaveSingleItem();
        model.Second.ShouldHaveSingleItem();
        model.First[0].Price.ShouldBe(2m);
        model.Second[0].Price.ShouldBe(2m);
        currentPair.First[0].Price.ShouldBe(3m);
        currentPair.Second[0].Price.ShouldBe(3m);
    }
}
