using System.Reflection;

namespace SparseTypes.Blazor.Tests;

public sealed class PropertyReaderTests
{
    [Test]
    public void ReadersUseLiveValuesAndVirtualDispatch()
    {
        var reader = SparsePropertyReader.Create(typeof(KeyBase).GetProperty(nameof(KeyBase.Key))!);
        var first = new DerivedKey { Value = "first" };
        var second = new DerivedKey { Value = "second" };
        reader(first).ShouldBe("first");
        reader(second).ShouldBe("second");
        first.Value = "updated";
        reader(first).ShouldBe("updated");
    }

    [Test]
    public void ReadersSupportNullableValuesAndExplicitInterfaceGetters()
    {
        var reader = SparsePropertyReader.Create(typeof(IKey).GetProperty(nameof(IKey.Key))!);
        reader(new ExplicitKey(7)).ShouldBe(7);
        reader(new ExplicitKey(null)).ShouldBeNull();
    }

    [Test]
    public void ValueTypeReadersPreserveTheBoxedInstance()
    {
        var reader = SparsePropertyReader.Create(typeof(CountingKey).GetProperty(nameof(CountingKey.Key))!);
        object key = new CountingKey();
        reader(key).ShouldBe(1);
        reader(key).ShouldBe(2);
    }

    [Test]
    public void GetterFailuresPreserveTargetInvocationExceptions()
    {
        var reader = SparsePropertyReader.Create(typeof(ThrowingKey).GetProperty(nameof(ThrowingKey.Key))!);
        var source = new ThrowingKey();
        Should.Throw<TargetInvocationException>(() => reader(source)).InnerException.ShouldBeSameAs(source.Failure);
    }

    public abstract class KeyBase
    {
        public abstract string Key { get; }
    }

    public sealed class DerivedKey : KeyBase
    {
        public string Value { get; set; } = string.Empty;

        public override string Key => Value;
    }

    public interface IKey
    {
        int? Key { get; }
    }

    public sealed class ExplicitKey(int? key) : IKey
    {
        int? IKey.Key => key;
    }

    public struct CountingKey
    {
        private int _reads;

        public int Key => ++_reads;
    }

    public sealed class ThrowingKey
    {
        public InvalidOperationException Failure { get; } = new("getter failure");

        public string Key => throw Failure;
    }
}
