using SparseTypes;

namespace SparseTypes.Tests;

[SparseModel]
internal sealed partial class SharedReferenceBetweenEntry
{
    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }
}

[SparseModel]
internal sealed partial class SharedReferenceBetweenHolder
{
    public Dictionary<string, SharedReferenceBetweenEntry> Entries { get; set; } = new();

    public Dictionary<string, int> Scores { get; set; } = new();
}

public sealed class ModelBetweenReferenceFastPathTests
{
    [Test]
    public void SharedReferencesAndEquivalentCopiesPreserveBetweenResults()
    {
        var entries = new Dictionary<string, SharedReferenceBetweenEntry>
        {
            ["item"] = new SharedReferenceBetweenEntry { Name = "first", Count = 1 },
        };
        var scores = new Dictionary<string, int> { ["item"] = 1 };
        var sharedBefore = new SharedReferenceBetweenHolder { Entries = entries, Scores = scores };
        var sharedAfter = new SharedReferenceBetweenHolder { Entries = entries, Scores = scores };

        SharedReferenceBetweenHolder
            .ChangeSet.Between(sharedBefore, sharedAfter)
            .IsEmpty.ShouldBeTrue();

        var copiedBefore = new SharedReferenceBetweenHolder
        {
            Entries = new Dictionary<string, SharedReferenceBetweenEntry>
            {
                ["item"] = new SharedReferenceBetweenEntry { Name = "first", Count = 1 },
            },
            Scores = new Dictionary<string, int> { ["item"] = 1 },
        };
        var copiedAfter = new SharedReferenceBetweenHolder
        {
            Entries = new Dictionary<string, SharedReferenceBetweenEntry>
            {
                ["item"] = new SharedReferenceBetweenEntry { Name = "first", Count = 1 },
            },
            Scores = new Dictionary<string, int> { ["item"] = 1 },
        };

        SharedReferenceBetweenHolder
            .ChangeSet.Between(copiedBefore, copiedAfter)
            .IsEmpty.ShouldBeTrue();

        copiedAfter.Entries["item"].Count = 2;
        SharedReferenceBetweenHolder
            .ChangeSet.Between(copiedBefore, copiedAfter)
            .IsEmpty.ShouldBeFalse();
    }
}
