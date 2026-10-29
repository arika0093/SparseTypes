using SparseTypes.Generated;

namespace SparseTypes.Tests;

[SparseModel]
public partial class CollectionSecurityNullableHolder
{
    [SparseMerge(MergeMode.Replace)]
    public List<CollectionSecurityNullableRow?> Lines { get; set; } = [];
}

[SparseModel]
public sealed partial class CollectionSecurityNullableRow
{
    [SparseKey]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class CollectionSecurityIgnoreCaseComparer : IEqualityComparer<string>
{
    public bool Equals(string? x, string? y) => StringComparer.OrdinalIgnoreCase.Equals(x, y);

    public int GetHashCode(string obj) => StringComparer.OrdinalIgnoreCase.GetHashCode(obj);
}

[SparseModel]
[SparseCompare(typeof(string), typeof(CollectionSecurityIgnoreCaseComparer))]
public partial class CollectionSecurityComparedRow
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;
}

[SparseModel]
public partial class CollectionSecurityComparedHolder
{
    public List<CollectionSecurityComparedRow> Items { get; set; } = [];
}

public sealed class CollectionIdentitySecurityTests
{
    [Test]
    public void UnassignedAndAmbiguousKeysCannotResolveRowsOrMembers()
    {
        var first = new AssignedServer { Id = 0, Name = "first" };
        var second = new AssignedServer { Id = 0, Name = "second" };
        var assigned = new AssignedServer { Id = 7, Name = "assigned" };
        var model = new AssignedServerHolder { Items = [first, second, assigned] };
        var session = model.CreateEditSession();
        var root = SparsePath.Root<AssignedServerHolder>().Member("Items");
        session.Descriptors.ResolveModel(root.Key(0)).ShouldBeNull();
        session.Descriptors.Find(root.Key(0).Member("Name")).ShouldBeNull();
        session.Descriptors.ResolveModel(root.Key(7)).ShouldBeSameAs(assigned);

        first.Id = 7;
        session.Descriptors.ResolveModel(root.Key(7)).ShouldBeNull();
        session.Descriptors.Find(root.Key(7).Member("Name")).ShouldBeNull();
        var editor = session.EditCollection<AssignedServer, int>(AssignedServerHolder.SparsePath.Items);
        editor.TryRemoveByKey(7, out var error).ShouldBeFalse();
        error.ShouldBe(SparseCollectionEditError.UnknownKey);
        model.Items.ShouldBe([first, second, assigned]);

        first.Id = 8;
        session.Descriptors.ResolveModel(root.Key(7)).ShouldBeSameAs(assigned);
    }

    [Test]
    public void TemporaryKeysRequireOneNonemptyIdentity()
    {
        var temporaryKey = Guid.NewGuid();
        var first = new TempOrderLine { Id = 0, TemporaryId = temporaryKey };
        var second = new TempOrderLine { Id = 0, TemporaryId = Guid.NewGuid() };
        var model = new TempOrderHolder { Lines = [first, second] };
        var session = model.CreateEditSession();
        var path = SparsePath.Root<TempOrderHolder>().Member("Lines").TemporaryKey(temporaryKey);
        session.Descriptors.ResolveModel(path).ShouldBeSameAs(first);
        second.TemporaryId = temporaryKey;
        session.Descriptors.ResolveModel(path).ShouldBeNull();
        session.Descriptors.Find(path.Member("Name")).ShouldBeNull();

        first.TemporaryId = Guid.Empty;
        session.Descriptors.ResolveModel(path).ShouldBeSameAs(second);
        session.Descriptors.ResolveModel(SparsePath.Root<TempOrderHolder>().Member("Lines").TemporaryKey(Guid.Empty)).ShouldBeNull();
    }

    [Test]
    public void DuplicateNullSlotsAreRejectedWithoutNotifications()
    {
        var row = new CollectionSecurityNullableRow { Id = 7, Name = "assigned" };
        var model = new CollectionSecurityNullableHolder { Lines = [null, row] };
        var session = model.CreateEditSession();
        var editor = SparseCollectionEditors.EditKeyedCollection<CollectionSecurityNullableRow?, int>(
            session.Descriptors,
            CollectionSecurityNullableHolder.SparsePath.Lines,
            session.BatchEdit
        );
        var notifications = 0;
        session.TransitionObserved += _ => notifications++;

        editor.TryAdd(null, out var error).ShouldBeFalse();
        error.ShouldBe(SparseCollectionEditError.DuplicateKey);
        editor.TryInsert(1, null, out error).ShouldBeFalse();
        error.ShouldBe(SparseCollectionEditError.DuplicateKey);
        editor.TryReplace(1, null, out error).ShouldBeFalse();
        error.ShouldBe(SparseCollectionEditError.DuplicateKey);
        notifications.ShouldBe(0);
        session.HasChanges.ShouldBeFalse();
        model.Lines.ShouldBe([null, row]);
    }

    [Test]
    public void ReplacingTheExistingNullSlotDoesNotCollideWithItself()
    {
        var model = new CollectionSecurityNullableHolder { Lines = [null] };
        var session = model.CreateEditSession();
        session.Descriptors.TryGet("Lines", out var descriptor).ShouldBeTrue();
        descriptor.Array!.TrySetItem(0, null).ShouldBeTrue();
        model.Lines.ShouldBe([null]);
    }

    [Test]
    public void TheFirstNullSlotCanBeAddedAndRemovedByPosition()
    {
        var model = new CollectionSecurityNullableHolder();
        var session = model.CreateEditSession();
        session.Descriptors.TryGet("Lines", out var descriptor).ShouldBeTrue();
        var array = descriptor.Array!;
        array.TryAdd(null).ShouldBeTrue();
        model.Lines.ShouldBe([null]);
        array.TryAdd(null).ShouldBeFalse();
        array.TryRemoveAt(0).ShouldBeTrue();
        model.Lines.ShouldBeEmpty();
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void RepeatedMissingLookupsDoNotRebuildDictionaries()
    {
        var keyReads = 0;
        var hashCalls = 0;
        var resolverCalls = 0;
        var access = new SparseArrayDescriptorAccess
        {
            Count = () => 1000,
            GetItemKey = _ =>
            {
                keyReads++;
                return "row";
            },
            KeyEquals = static (left, right) => Equals(left, right),
            KeyHash = key =>
            {
                hashCalls++;
                return key!.GetHashCode();
            },
            IndexOfKey = _ =>
            {
                resolverCalls++;
                return -1;
            },
        };
        var array = new SparseArrayDescriptor(typeof(KeyedServer), false, access, keyType: typeof(string));
        array.IndexOfKey("missing").ShouldBe(-1);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1000; index++)
        {
            if (array.IndexOfKey("missing") != -1)
            {
                throw new InvalidOperationException("An absent key unexpectedly resolved.");
            }
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        allocated.ShouldBeLessThan(4096);
        keyReads.ShouldBe(0);
        hashCalls.ShouldBe(0);
        resolverCalls.ShouldBe(1001);
    }

    [Test]
    public void ComparisonsDoNotChangeMembershipOrOrderKeyIdentity()
    {
        var model = new CollectionSecurityComparedHolder
        {
            Items = [new CollectionSecurityComparedRow { Id = "Row" }, new CollectionSecurityComparedRow { Id = "row" }],
        };
        var session = model.CreateEditSession();
        session.Descriptors.TryGet("Items", out var descriptor).ShouldBeTrue();
        descriptor.Array!.IndexOfKey("Row").ShouldBe(0);
        descriptor.Array.IndexOfKey("row").ShouldBe(1);
        var patch = new CollectionSecurityComparedHolder.Patch();
        patch.Items.SetOrder(["row", "Row"]);
        var basis = Optional<CollectionSecurityComparedHolder.Fragment?>.Present(CollectionSecurityComparedHolder.Fragment.From(model));
        patch.Apply(basis).Value!.Items.Value!.Select(static row => row.Id).ShouldBe(["row", "Row"]);
        Should.Throw<InvalidOperationException>(() => patch.Items.SetOrder(["Row", "Row"]));
        patch.Apply(basis).Value!.Items.Value!.Select(static row => row.Id).ShouldBe(["row", "Row"]);
    }
}
