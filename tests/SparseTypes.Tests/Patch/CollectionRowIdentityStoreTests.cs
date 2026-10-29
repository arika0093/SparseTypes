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
