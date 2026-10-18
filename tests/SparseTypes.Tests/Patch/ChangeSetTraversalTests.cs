using SparseTypes;

namespace SparseTypes.Tests;

/// <summary>Push/pop traversal for flattened change-set enumeration.</summary>
/// <remarks>
/// Pins exact entry sequences across member shapes so the shared traversal
/// can replace per-level materialization without changing results.
/// </remarks>
public sealed class ChangeSetTraversalTests
{
    private static Optional<ResRoster.Fragment?> RosterState(ResRoster model) =>
        Optional<ResRoster.Fragment?>.Present(ResRoster.Fragment.From(model));

    private static ResRoster Roster(params (string Id, decimal Price)[] rows) =>
        new()
        {
            Items = rows.Select(static row => new ResItem { Id = row.Id, Price = row.Price })
                .ToList(),
        };

    private static string Describe(IEnumerable<string> texts) => string.Join(";", texts);

    [Test]
    public void Nested_Changes_Flatten_With_Full_Paths()
    {
        var before = Optional<ResNestedRoot.Fragment?>.Present(
            new ResNestedRoot.Fragment
            {
                Label = Optional<string>.Present("before"),
                Customer = Optional<ResCustomer.Fragment?>.Present(
                    new ResCustomer.Fragment { Name = Optional<string>.Present("ann") }
                ),
            }
        );
        var after = Optional<ResNestedRoot.Fragment?>.Present(
            new ResNestedRoot.Fragment
            {
                Label = Optional<string>.Present("after"),
                Customer = Optional<ResCustomer.Fragment?>.Present(
                    new ResCustomer.Fragment { Name = Optional<string>.Present("bob") }
                ),
            }
        );

        var entries = ResNestedRoot.ChangeSet.Between(before, after).EnumerateChanges().ToList();
        entries.Count.ShouldBe(2);
        entries[0].PathText.ShouldBe("Customer.Name");
        entries[0].Kind.ShouldBe(ResNestedRoot.ChangeSet.ChangeKind.Changed);
        entries[0].Before.Value.ShouldBe("ann");
        entries[0].After.Value.ShouldBe("bob");
        entries[1].PathText.ShouldBe("Label");
        entries[1].Kind.ShouldBe(ResNestedRoot.ChangeSet.ChangeKind.Changed);
        entries[1].Before.Value.ShouldBe("before");
        entries[1].After.Value.ShouldBe("after");
    }

    [Test]
    public void Whole_Child_Reports_One_Member_Entry()
    {
        var before = Optional<ResNestedRoot.Fragment?>.Present(
            new ResNestedRoot.Fragment
            {
                Label = Optional<string>.Present("same"),
                Customer = Optional<ResCustomer.Fragment?>.Missing,
            }
        );
        var after = Optional<ResNestedRoot.Fragment?>.Present(
            new ResNestedRoot.Fragment
            {
                Label = Optional<string>.Present("same"),
                Customer = Optional<ResCustomer.Fragment?>.Present(
                    new ResCustomer.Fragment { Name = Optional<string>.Present("ann") }
                ),
            }
        );

        var entries = ResNestedRoot.ChangeSet.Between(before, after).EnumerateChanges().ToList();
        entries.ShouldHaveSingleItem();
        entries[0].PathText.ShouldBe("Customer");
        entries[0].Kind.ShouldBe(ResNestedRoot.ChangeSet.ChangeKind.Added);
    }

    [Test]
    public void Keyed_Changes_Flatten_With_Order_Entry_Last()
    {
        var before = Roster(("a", 1m), ("b", 2m));
        var after = Roster(("b", 20m), ("c", 3m));

        var entries = ResRoster
            .ChangeSet.Between(RosterState(before), RosterState(after))
            .EnumerateChanges()
            .ToList();

        var texts = entries.Select(static entry => entry.PathText).ToList();
        Describe(texts).ShouldBe("Items[\"b\"].Price;Items[\"c\"];Items[\"a\"];Items");
        entries[0].Kind.ShouldBe(ResRoster.ChangeSet.ChangeKind.Changed);
        entries[0].Before.Value.ShouldBe(2m);
        entries[0].After.Value.ShouldBe(20m);
        entries[1].Kind.ShouldBe(ResRoster.ChangeSet.ChangeKind.Added);
        entries[2].Kind.ShouldBe(ResRoster.ChangeSet.ChangeKind.Removed);
        entries[3].Kind.ShouldBe(ResRoster.ChangeSet.ChangeKind.Order);
    }

    [Test]
    public void Dictionary_Changes_Flatten_Per_Entry()
    {
        var before = new ResDict
        {
            Scores = new() { ["a"] = 1, ["b"] = 2 },
        };
        var after = new ResDict
        {
            Scores = new() { ["b"] = 20, ["c"] = 3 },
        };
        Optional<ResDict.Fragment?> DictState(ResDict model) =>
            Optional<ResDict.Fragment?>.Present(ResDict.Fragment.From(model));

        var entries = ResDict
            .ChangeSet.Between(DictState(before), DictState(after))
            .EnumerateChanges()
            .ToList();

        var texts = entries.Select(static entry => entry.PathText).ToList();
        Describe(texts).ShouldBe("Scores[\"c\"];Scores[\"a\"];Scores[\"b\"]");
        entries[0].Kind.ShouldBe(ResDict.ChangeSet.ChangeKind.Added);
        entries[1].Kind.ShouldBe(ResDict.ChangeSet.ChangeKind.Removed);
        entries[2].Kind.ShouldBe(ResDict.ChangeSet.ChangeKind.Changed);
        entries[2].Before.Value.ShouldBe(2);
        entries[2].After.Value.ShouldBe(20);
    }

    [Test]
    public void Set_Changes_Report_Added_Before_Removed()
    {
        var before = new ResLabels
        {
            Labels = new HashSet<string> { "x", "y" },
        };
        var after = new ResLabels
        {
            Labels = new HashSet<string> { "y", "z" },
        };
        Optional<ResLabels.Fragment?> LabelsState(ResLabels model) =>
            Optional<ResLabels.Fragment?>.Present(ResLabels.Fragment.From(model));

        var entries = ResLabels
            .ChangeSet.Between(LabelsState(before), LabelsState(after))
            .EnumerateChanges()
            .ToList();

        entries.Count.ShouldBe(2);
        entries[0].PathText.ShouldBe("Labels[\"z\"]");
        entries[0].Kind.ShouldBe(ResLabels.ChangeSet.ChangeKind.Added);
        entries[1].PathText.ShouldBe("Labels[\"x\"]");
        entries[1].Kind.ShouldBe(ResLabels.ChangeSet.ChangeKind.Removed);
    }

    [Test]
    public void Temporary_And_Assigned_Rows_Keep_Distinct_Paths()
    {
        var temporary = Guid.NewGuid();
        var before = new TempOrderHolder
        {
            Lines = new()
            {
                new TempOrderLine { Id = 1, Name = "before" },
            },
        };
        var after = new TempOrderHolder
        {
            Lines = new()
            {
                new TempOrderLine { Id = 1, Name = "after" },
                new TempOrderLine
                {
                    Id = 0,
                    TemporaryId = temporary,
                    Name = "fresh",
                },
            },
        };
        Optional<TempOrderHolder.Fragment?> TempState(TempOrderHolder model) =>
            Optional<TempOrderHolder.Fragment?>.Present(TempOrderHolder.Fragment.From(model));

        var entries = TempOrderHolder
            .ChangeSet.Between(TempState(before), TempState(after))
            .EnumerateChanges()
            .ToList();

        entries.Count.ShouldBe(3);
        entries[0]
            .Path.ShouldBe(
                SparsePath.Root<TempOrderHolder>().Member("Lines").Key(1).Member("Name")
            );
        entries[0].Kind.ShouldBe(TempOrderHolder.ChangeSet.ChangeKind.Changed);
        entries[1]
            .Path.ShouldBe(
                SparsePath.Root<TempOrderHolder>().Member("Lines").TemporaryKey(temporary)
            );
        entries[1].Kind.ShouldBe(TempOrderHolder.ChangeSet.ChangeKind.Added);
        entries[2].PathText.ShouldBe("Lines");
        entries[2].Kind.ShouldBe(TempOrderHolder.ChangeSet.ChangeKind.Order);
    }

    [Test]
    public void Enumeration_Is_Repeatable_And_Consistent_Across_Surfaces()
    {
        var before = Roster(("a", 1m), ("b", 2m));
        var after = Roster(("b", 20m), ("c", 3m));
        var changes = ResRoster.ChangeSet.Between(RosterState(before), RosterState(after));

        // Scratch ownership stays per enumeration: repeated walks agree.
        var first = changes.EnumerateChanges().ToList();
        var second = changes.EnumerateChanges().ToList();
        second.Count.ShouldBe(first.Count);
        for (var index = 0; index < first.Count; index++)
        {
            second[index].Path.ShouldBe(first[index].Path);
            second[index].Kind.ShouldBe(first[index].Kind);
            second[index].Before.ShouldBe(first[index].Before);
            second[index].After.ShouldBe(first[index].After);
        }

        ISparseChangeSet root = changes;
        var records = root.EnumerateChangeRecords();
        records.Count.ShouldBe(first.Count);
        for (var index = 0; index < first.Count; index++)
        {
            records[index].Path.ShouldBe(first[index].Path);
            ((int)records[index].Kind).ShouldBe((int)first[index].Kind);
        }

        var paths = changes.EnumerateChangedPaths();
        paths.Count.ShouldBe(first.Select(static entry => entry.Path).Distinct().Count());
    }
}
