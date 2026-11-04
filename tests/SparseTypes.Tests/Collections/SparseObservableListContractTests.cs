using System.Collections;

namespace SparseTypes.Tests;

public sealed class SparseObservableListContractTests
{
    private sealed class FixedSizeList<T>(params T[] items) : IList<T>, IList
    {
        private readonly T[] _items = items;

        public T this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }

        object? IList.this[int index]
        {
            get => _items[index];
            set => _items[index] = (T)value!;
        }

        public int Count => _items.Length;
        public bool IsReadOnly => false;
        public bool IsFixedSize => true;
        public bool IsSynchronized => false;
        public object SyncRoot => this;

        public int IndexOf(T item) => Array.IndexOf(_items, item);

        public bool Contains(T item) => IndexOf(item) >= 0;

        public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

        public void CopyTo(Array array, int index) => _items.CopyTo(array, index);

        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        public void Add(T item) => throw new NotSupportedException();

        public void Insert(int index, T item) => throw new NotSupportedException();

        public bool Remove(T item) => throw new NotSupportedException();

        public void RemoveAt(int index) => throw new NotSupportedException();

        public void Clear() => throw new NotSupportedException();

        int IList.Add(object? value) => throw new NotSupportedException();

        bool IList.Contains(object? value) => throw new NotSupportedException();

        int IList.IndexOf(object? value) => throw new NotSupportedException();

        void IList.Insert(int index, object? value) => throw new NotSupportedException();

        void IList.Remove(object? value) => throw new NotSupportedException();
    }

    private static SparseObservableList<TItem, TItem> Create<TItem>(IList<TItem> model) =>
        new(model, static (value, _) => value, static view => view, static () => { });

    [Test]
    public void NonGenericIsFixedSize_ReflectsFixedSizeMutableModel()
    {
        var list = Create<string>(new FixedSizeList<string>("a"));
        IList raw = list;

        list.IsReadOnly.ShouldBeFalse();
        list.IsFixedSize.ShouldBeTrue();
        raw.IsFixedSize.ShouldBeTrue();
    }

    [Test]
    public void NonGenericIsFixedSize_IsFalseForGrowableModel()
    {
        IList raw = Create<string>(new List<string> { "a" });

        raw.IsFixedSize.ShouldBeFalse();
    }

    [Test]
    public void NonGenericIsFixedSize_IsTrueForReadOnlyModel()
    {
        IList raw = Create<string>(new List<string> { "a" }.AsReadOnly());

        raw.IsFixedSize.ShouldBeTrue();
    }

}
