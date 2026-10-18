using SparseTypes;

namespace SparseTypes.Tests;

public partial class PathSortEntry
{
    [SparseKey]
    public string Position { get; set; } = string.Empty;

    public int Value { get; set; }
}

[SparseModel]
public partial class PathSortHolder
{
    public List<PathSortEntry> Items { get; set; } = new();

    public Dictionary<string, int> Numbers { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Ordinal text ordering of generated EnumerateChangedPaths output.</summary>
/// <remarks>
/// Locks the historical contract: paths sort by rendered text, not numerically
/// ("Items[\"10\"]" before "Items[\"2\"]"), structured equality governs the
/// distinct-path set, and display-equal typed keys all stay.
/// </remarks>
public sealed class EnumerateChangedPathsSortTests
{
    private static PathSortEntry Item(int tag) => new() { Position = tag.ToString(), Value = tag };

    [Test]
    public void MixedChangesSortByOrdinalTextNotNumericOrder()
    {
        var before = SortState([Item(2), Item(10), Item(13)], []);
        var after = SortState(
            [
                new PathSortEntry { Position = "2", Value = 999 },
                new PathSortEntry { Position = "10", Value = 888 },
                Item(13),
            ],
            new Dictionary<string, int>
            {
                ["b"] = 1,
                ["a"] = 2,
                ["cc"] = 3,
            }
        );

        var texts = PathSortHolder
            .ChangeSet.Between(before, after)
            .EnumerateChangedPaths()
            .Select(static path => path.ToString())
            .ToList();

        // Text-ordinal ordering: "10" precedes "2"; keys keep source order
        // inside their own member's grouping only by text.
        texts.ShouldBe([
            "Items[\"10\"].Value",
            "Items[\"2\"].Value",
            "Numbers[\"a\"]",
            "Numbers[\"b\"]",
            "Numbers[\"cc\"]",
        ]);
        // Independent reference ordering must agree.
        texts.ShouldBe(texts.OrderBy(static text => text, StringComparer.Ordinal).ToList());
    }

    [Test]
    public void DisplayEqualTypedKeysSurviveDedupAndSortAsTies()
    {
        var before = Optional<CollidingKeyDictHolder.Fragment?>.Present(
            CollidingKeyDictHolder.Fragment.From(new CollidingKeyDictHolder { Scores = new() })
        );
        var after = Optional<CollidingKeyDictHolder.Fragment?>.Present(
            CollidingKeyDictHolder.Fragment.From(
                new CollidingKeyDictHolder
                {
                    Scores = new()
                    {
                        [new CollidingCompositeKey { Zone = 1, Rack = 2 }] = 1,
                        [new CollidingCompositeKey { Zone = 3, Rack = 4 }] = 2,
                    },
                }
            )
        );
        var changes = CollidingKeyDictHolder.ChangeSet.Between(before, after);

        var paths = changes.EnumerateChangedPaths();

        paths.Count.ShouldBe(2);
        paths[0].ShouldNotBe(paths[1]);
        paths[0].ToString().ShouldBe(paths[1].ToString());
    }

    [Test]
    public void RootTransitionSortsBeforeMemberText()
    {
        var paths = PathSortHolder
            .ChangeSet.Between(Optional<PathSortHolder.Fragment?>.Missing, SortState([], []))
            .EnumerateChangedPaths();

        paths.ShouldHaveSingleItem().ToString().ShouldBe("$root");
    }

    private static Optional<PathSortHolder.Fragment?> SortState(
        List<PathSortEntry> items,
        Dictionary<string, int> numbers
    ) =>
        Optional<PathSortHolder.Fragment?>.Present(
            PathSortHolder.Fragment.From(new PathSortHolder { Items = items, Numbers = numbers })
        );
}
