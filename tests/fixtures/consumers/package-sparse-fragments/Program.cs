using System;
using System.Collections.Generic;
using SparseTypes;

[SparseModel]
public partial class FragmentsConsumerAccount
{
    public string? Name { get; set; }
    public List<string> Tags { get; set; } = new List<string>();
}

internal static class Program
{
    private const string SuccessMarker = "SparseTypes.Fragments packed consumer passed.";

    private static void Main()
    {
        var before = new FragmentsConsumerAccount
        {
            Name = "before",
            Tags = new List<string> { "one" },
        };
        var after = new FragmentsConsumerAccount
        {
            Name = "after",
            Tags = new List<string> { "one", "two" },
        };
        var beforeState = Optional<FragmentsConsumerAccount.Fragment?>.Present(
            FragmentsConsumerAccount.Fragment.From(before)
        );
        var afterState = Optional<FragmentsConsumerAccount.Fragment?>.Present(
            FragmentsConsumerAccount.Fragment.From(after)
        );
        var patch = FragmentsConsumerAccount.Patch.Between(beforeState, afterState);
        var applied = patch.Apply(beforeState);
        var clone = applied.Value!.DeepClone();
        clone.Tags.Value!.Add("three");

        if (applied.Value.Tags.Value!.Count != 2)
        {
            throw new InvalidOperationException("DeepClone shared a mutable collection.");
        }

        if (applied.Value.ToModel().Name != "after")
        {
            throw new InvalidOperationException("Patch.Apply returned the wrong model state.");
        }

        if (patch.Invert(beforeState).Apply(applied).Value!.ToModel().Name != "before")
        {
            throw new InvalidOperationException("Patch.Invert did not restore the baseline.");
        }

        if (
            typeof(FragmentsConsumerAccount).GetNestedType("ChangeSet") is not null
            || typeof(FragmentsConsumerAccount.Patch).GetMethod("Rebase") is not null
        )
        {
            throw new InvalidOperationException("The Fragments product emitted an excluded API.");
        }

        Console.WriteLine(SuccessMarker);
    }
}
