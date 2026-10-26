using System.Reflection;
using SparseTypes.EntityFrameworkCore.Mapping;

namespace SparseTypes.EntityFrameworkCore.Tests;

public sealed class CompiledMappingAccessTests
{
    [Test]
    public void FragmentConvertersReadEachInstanceAndDoNotRetainSnapshots()
    {
        var converter = new SparseSnapshotConverter<WidgetDto>();
        var first = WidgetDto.Fragment.From(new WidgetDto { Name = "First" });
        var second = WidgetDto.Fragment.From(new WidgetDto { Name = "Second" });

        converter.TryConvert(first, out var firstSnapshot).ShouldBeTrue();
        converter.TryConvert(second, out var secondSnapshot).ShouldBeTrue();
        firstSnapshot.ShouldNotBeNull().Name.ShouldBe("First");
        secondSnapshot.ShouldNotBeNull().Name.ShouldBe("Second");
        firstSnapshot.ShouldNotBeSameAs(secondSnapshot);

        converter.TryConvert(firstSnapshot, out var direct).ShouldBeTrue();
        direct.ShouldBeSameAs(firstSnapshot);
    }

    [Test]
    public void UnsupportedAndNullSnapshotsRemainUnresolved()
    {
        var converter = new SparseSnapshotConverter<WidgetDto>();
        for (var iteration = 0; iteration < 2; iteration++)
        {
            converter.TryConvert(null, out var snapshot).ShouldBeFalse();
            snapshot.ShouldBeNull();
            converter.TryConvert("unsupported", out snapshot).ShouldBeFalse();
            snapshot.ShouldBeNull();
            converter.TryConvert(new NullSnapshot(), out snapshot).ShouldBeFalse();
            snapshot.ShouldBeNull();
        }
    }

    [Test]
    public void ConversionFailuresPreserveTargetInvocationExceptions()
    {
        var converter = new SparseSnapshotConverter<WidgetDto>();
        var source = new ThrowingSnapshot();
        var exception = Should.Throw<TargetInvocationException>(() => converter.TryConvert(source, out _));
        exception.InnerException.ShouldBeSameAs(source.Failure);
    }

    [Test]
    public void CompiledConstructorsCreateIndependentInstancesAndPreserveFailures()
    {
        var create = ReflectionAccessCompiler.CreateFactory<object>(typeof(WidgetEntity).GetConstructor(Type.EmptyTypes)!);
        var first = create();
        first.ShouldBeOfType<WidgetEntity>();
        create().ShouldNotBeSameAs(first);

        var throwing = ReflectionAccessCompiler.CreateFactory<object>(typeof(ThrowingEntity).GetConstructor(Type.EmptyTypes)!);
        Should.Throw<TargetInvocationException>(() => throwing()).InnerException.ShouldBeOfType<InvalidOperationException>();
    }

    [Test]
    public void AbstractConstructorsFailOnlyWhenCreationIsRequested()
    {
        var create = ReflectionAccessCompiler.CreateFactory<object>(typeof(AbstractEntity).GetConstructor(Type.EmptyTypes)!);
        Should.Throw<MemberAccessException>(() => create());
    }

    [Test]
    public void ValueTypeSnapshotConvertersPreserveTheBoxedInstance()
    {
        var converter = new SparseSnapshotConverter<WidgetDto>();
        object source = new CountingSnapshot();
        converter.TryConvert(source, out var first).ShouldBeTrue();
        converter.TryConvert(source, out var second).ShouldBeTrue();
        first.ShouldNotBeNull().Name.ShouldBe("1");
        second.ShouldNotBeNull().Name.ShouldBe("2");
    }

    public abstract class AbstractEntity
    {
        public AbstractEntity() { }
    }

    public struct CountingSnapshot
    {
        private int _reads;

        public WidgetDto ToModel() => new() { Name = (++_reads).ToString(System.Globalization.CultureInfo.InvariantCulture) };
    }

    public sealed class NullSnapshot
    {
        public WidgetDto? ToModel() => null;
    }

    public sealed class ThrowingSnapshot
    {
        public InvalidOperationException Failure { get; } = new("snapshot failure");

        public WidgetDto ToModel() => throw Failure;
    }

    public sealed class ThrowingEntity
    {
        public ThrowingEntity() => throw new InvalidOperationException("constructor failure");
    }
}
