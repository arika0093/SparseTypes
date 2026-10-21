namespace SparseTypes.Fragments.Tests;

[SparseModel]
public partial class FragmentsAccount
{
    public string? Name { get; set; }
    public FragmentsAddress? Address { get; set; }
    public List<string> Tags { get; set; } = [];
}

[SparseModel]
public partial class FragmentsAddress
{
    public string? City { get; set; }
    public int ZipCode { get; set; }
}

[SparseModel]
public partial class FragmentsRoster
{
    public List<FragmentsRosterItem> Items { get; set; } = [];
}

[SparseModel]
public partial class FragmentsRosterItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;
    public int Count { get; set; }
}

[SparseModel]
public partial class FragmentsDictionary
{
    public Dictionary<string, int> Scores { get; set; } = [];
}

[SparseModel]
public partial class FragmentsImmutable
{
    public string Name { get; init; } = string.Empty;
}

public sealed class FragmentsProductTests
{
    [Test]
    public void FragmentOperationsPreservePresenceAndDeepCloneNestedValues()
    {
        var model = new FragmentsAccount
        {
            Name = null,
            Address = new FragmentsAddress { City = "Tokyo", ZipCode = 100 },
            Tags = ["one"],
        };
        var fragment = FragmentsAccount.Fragment.From(model);
        var clone = fragment.DeepClone();

        fragment.Name.IsPresent.ShouldBeTrue();
        fragment.Name.Value.ShouldBeNull();
        fragment.Address.Value!.City.Value.ShouldBe("Tokyo");
        clone.Tags.Value!.Add("two");
        fragment.Tags.Value.ShouldBe(["one"]);

        var merged = fragment.Merge(new FragmentsAccount.Fragment { Name = "updated" });
        merged.Name.Value.ShouldBe("updated");
        merged.Address.Value!.City.Value.ShouldBe("Tokyo");
    }

    [Test]
    public void PatchAlgebraSupportsApplyComposeAndInvertWithoutRebase()
    {
        var before = new FragmentsAccount
        {
            Name = "before",
            Address = new FragmentsAddress { City = "Tokyo", ZipCode = 100 },
        };
        var middle = new FragmentsAccount
        {
            Name = null,
            Address = new FragmentsAddress { City = "Osaka", ZipCode = 200 },
        };
        var after = new FragmentsAccount
        {
            Name = "after",
            Address = new FragmentsAddress { City = "Kyoto", ZipCode = 300 },
        };
        var beforeState = Optional<FragmentsAccount.Fragment?>.Present(
            FragmentsAccount.Fragment.From(before)
        );
        var middleState = Optional<FragmentsAccount.Fragment?>.Present(
            FragmentsAccount.Fragment.From(middle)
        );
        var afterState = Optional<FragmentsAccount.Fragment?>.Present(
            FragmentsAccount.Fragment.From(after)
        );
        var first = FragmentsAccount.Patch.Between(beforeState, middleState);
        var second = FragmentsAccount.Patch.Between(middleState, afterState);
        var composed = first.Compose(second);
        var applied = composed.Apply(beforeState);

        applied.Value!.ToModel().Name.ShouldBe("after");
        applied.Value.Address.Value!.City.Value.ShouldBe("Kyoto");
        composed.Invert(beforeState).Apply(applied).Value!.ToModel().Name.ShouldBe("before");

        typeof(FragmentsAccount.Patch).GetMethod("Rebase").ShouldBeNull();
        typeof(FragmentsAccount).GetNestedType("ChangeSet").ShouldBeNull();
    }

    [Test]
    public void KeyedCollectionPatchAppliesEditsAndAdditionsWithoutRebase()
    {
        var before = new FragmentsRoster
        {
            Items = [new FragmentsRosterItem { Id = "a", Count = 1 }],
        };
        var after = new FragmentsRoster
        {
            Items =
            [
                new FragmentsRosterItem { Id = "a", Count = 2 },
                new FragmentsRosterItem { Id = "b", Count = 3 },
            ],
        };
        var beforeState = Optional<FragmentsRoster.Fragment?>.Present(
            FragmentsRoster.Fragment.From(before)
        );
        var afterState = Optional<FragmentsRoster.Fragment?>.Present(
            FragmentsRoster.Fragment.From(after)
        );

        var result = FragmentsRoster
            .Patch.Between(beforeState, afterState)
            .Apply(beforeState)
            .Value!.ToModel();

        result.Items.Select(static item => (item.Id, item.Count)).ShouldBe([("a", 2), ("b", 3)]);
        typeof(FragmentsRoster.Patch.ItemsPatch).GetMethod("Rebase").ShouldBeNull();
    }

    [Test]
    public void DictionaryPatchAppliesUpdatesAndAdditionsWithoutRebase()
    {
        var before = new FragmentsDictionary
        {
            Scores = new Dictionary<string, int> { ["keep"] = 1, ["change"] = 2 },
        };
        var after = new FragmentsDictionary
        {
            Scores = new Dictionary<string, int>
            {
                ["keep"] = 1,
                ["change"] = 3,
                ["new"] = 4,
            },
        };
        var beforeState = Optional<FragmentsDictionary.Fragment?>.Present(
            FragmentsDictionary.Fragment.From(before)
        );
        var afterState = Optional<FragmentsDictionary.Fragment?>.Present(
            FragmentsDictionary.Fragment.From(after)
        );

        var result = FragmentsDictionary
            .Patch.Between(beforeState, afterState)
            .Apply(beforeState)
            .Value!.ToModel();

        result.Scores.ShouldBe(after.Scores);
        typeof(FragmentsDictionary.Patch.ScoresPatch).GetMethod("Rebase").ShouldBeNull();
    }

    [Test]
    public void ImmutableMemberReportsInPlaceFailureAndStillSupportsCopyApplication()
    {
        var model = new FragmentsImmutable { Name = "before" };
        var patch = new FragmentsImmutable.Patch { Name = "after" };

        var inPlace = patch.ApplyInPlace(model);

        inPlace.Succeeded.ShouldBeFalse();
        inPlace.UnsupportedMembers.ShouldContain("Name");
        model.Name.ShouldBe("before");
        patch.ApplyTo(model).Name.ShouldBe("after");
    }
}
