namespace SparseTypes.Tests;

public sealed class DescriptorKeyLookupCacheTests
{
    [Test]
    public void AssignedKeyLookupRejectsRawDuplicatesAndTracksExternalChanges()
    {
        var first = new KeyedServer { Id = "a", Name = "first" };
        var second = new KeyedServer { Id = "b", Name = "second" };
        var model = new KeyedServerHolder { Items = [first, second] };
        var session = model.CreateEditSession();

        session.Descriptors.TryGet(nameof(KeyedServerHolder.Items), out var items).ShouldBeTrue();
        var array = items.Array.ShouldNotBeNull();
        array!.IndexOfKey("b").ShouldBe(1);

        // Mutating a preceding key can make identity ambiguous without
        // changing either the collection count or its item references.
        first.Id = "b";
        array.IndexOfKey("b").ShouldBe(-1);

        first.Id = "a";
        array.IndexOfKey("b").ShouldBe(1);

        // Raw list reorders preserve Count and collection reference.
        model.Items.Reverse();
        array.IndexOfKey("b").ShouldBe(0);

        // Key edits also change which identities resolve.
        second.Id = "c";
        array.IndexOfKey("b").ShouldBe(-1);
        array.IndexOfKey("c").ShouldBe(0);
    }

    [Test]
    public void TemporaryKeyLookupRejectsRawDuplicatesAndTracksChanges()
    {
        var firstTemporaryKey = Guid.NewGuid();
        var secondTemporaryKey = Guid.NewGuid();
        var first = new TempOrderLine { Id = 1, TemporaryId = firstTemporaryKey };
        var second = new TempOrderLine { Id = 2, TemporaryId = secondTemporaryKey };
        var model = new TempOrderHolder { Lines = [first, second] };
        var session = model.CreateEditSession();

        session.Descriptors.TryGet(nameof(TempOrderHolder.Lines), out var lines).ShouldBeTrue();
        var array = lines.Array.ShouldNotBeNull();
        array!.IndexOfTemporaryKey(secondTemporaryKey).ShouldBe(1);

        first.TemporaryId = secondTemporaryKey;
        array.IndexOfTemporaryKey(secondTemporaryKey).ShouldBe(-1);

        model.Lines.Reverse();
        array.IndexOfTemporaryKey(secondTemporaryKey).ShouldBe(-1);

        model.Lines.Insert(0, null!);
        array.IndexOfTemporaryKey(secondTemporaryKey).ShouldBe(-1);

        second.TemporaryId = firstTemporaryKey;
        first.TemporaryId = firstTemporaryKey;
        array.IndexOfTemporaryKey(secondTemporaryKey).ShouldBe(-1);
        array.IndexOfTemporaryKey(firstTemporaryKey).ShouldBe(-1);
    }
}
