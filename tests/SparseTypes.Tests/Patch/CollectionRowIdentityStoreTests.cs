namespace SparseTypes.Tests;

public sealed class CollectionRowIdentityStoreTests
{
    [Test]
    public void UnkeyedRowsUseReferenceIdentityAndReleaseRowsThatDisappear()
    {
        var store = new SparseCollectionRowIdentityStore<EqualElement>();
        var first = new EqualElement();
        var second = new EqualElement();

        store.BeginRefresh();
        var firstIdentity = store.GetOrCreate(first, SparsePathSegment.At(0), null, isKeyed: false);
        var secondIdentity = store.GetOrCreate(second, SparsePathSegment.At(1), null, isKeyed: false);
        store.EndRefresh();

        firstIdentity.ShouldNotBe(secondIdentity);

        store.BeginRefresh();
        store.GetOrCreate(first, SparsePathSegment.At(0), null, isKeyed: false).ShouldBe(firstIdentity);
        store.EndRefresh();

        store.BeginRefresh();
        var reappearedIdentity = store.GetOrCreate(second, SparsePathSegment.At(1), null, isKeyed: false);
        store.EndRefresh();
        reappearedIdentity.ShouldNotBe(secondIdentity);
    }

    [Test]
    public void UnkeyedRepeatedReferenceGetsOneStableIdentityPerOccurrence()
    {
        var store = new SparseCollectionRowIdentityStore<EqualElement>();
        var repeated = new EqualElement();
        var other = new EqualElement();
        var lookalike = new EqualElement();

        Guid[] Refresh(params EqualElement[] items)
        {
            store.BeginRefresh();
            var result = items.Select((item, index) => store.GetOrCreate(item, SparsePathSegment.At(index), null, isKeyed: false)).ToArray();
            store.EndRefresh();
            return result;
        }

        var first = Refresh(repeated, other, repeated, lookalike);
        first.Distinct().Count().ShouldBe(4);
        Refresh(repeated, other, repeated, lookalike).ShouldBe(first);
        var reordered = Refresh(other, repeated, repeated, lookalike);
        reordered[0].ShouldBe(first[1]);
        reordered[1].ShouldBe(first[0]);
        reordered[2].ShouldBe(first[2]);
        reordered[3].ShouldBe(first[3]);
        var removed = Refresh(other, repeated);
        removed[0].ShouldBe(first[1]);
        removed[1].ShouldBe(first[0]);
        Refresh(other, repeated, repeated)[2].ShouldNotBe(first[2]);
    }

    [Test]
    public void KeyReconciliationRetainsIdentityAcrossTemporaryAndPermanentPaths()
    {
        var store = new SparseCollectionRowIdentityStore<EqualElement>();
        var item = new EqualElement();
        var temporaryKey = Guid.NewGuid();

        store.BeginRefresh();
        var temporaryIdentity = store.GetOrCreate(item, SparsePathSegment.TemporaryKeyOf(temporaryKey), temporaryKey, isKeyed: true);
        store.EndRefresh();

        store.BeginRefresh();
        var permanentIdentity = store.GetOrCreate(item, SparsePathSegment.KeyOf("assigned"), temporaryKey, isKeyed: true);
        store.EndRefresh();
        permanentIdentity.ShouldBe(temporaryIdentity);

        store.BeginRefresh();
        store.GetOrCreate(item, SparsePathSegment.KeyOf("assigned"), null, isKeyed: true).ShouldBe(temporaryIdentity);
        store.EndRefresh();
    }

    [Test]
    public void TemporaryAndPermanentKeysRemainDistinct()
    {
        var store = new SparseCollectionRowIdentityStore<EqualElement>();
        var item = new EqualElement();
        var key = Guid.NewGuid();

        store.BeginRefresh();
        var permanentIdentity = store.GetOrCreate(item, SparsePathSegment.KeyOf(key), null, isKeyed: true);
        var temporaryIdentity = store.GetOrCreate(item, SparsePathSegment.TemporaryKeyOf(key), key, isKeyed: true);
        store.EndRefresh();

        permanentIdentity.ShouldNotBe(temporaryIdentity);
    }

    private sealed class EqualElement
    {
        public override bool Equals(object? obj) => obj is EqualElement;

        public override int GetHashCode() => 0;
    }
}
