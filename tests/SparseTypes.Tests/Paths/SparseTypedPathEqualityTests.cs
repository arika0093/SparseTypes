namespace SparseTypes.Tests;

/// <summary>Boxed equality contract between typed and untyped paths.</summary>
public sealed class SparseTypedPathEqualityTests
{
    private static readonly SparsePath Untyped = SparsePath.Root<Settings>().Member("Label");

    [Test]
    public void BoxedEqualsIsSymmetricBetweenTypedAndUntyped()
    {
        object typed = new SparsePath<Settings, string>(Untyped);
        object untyped = Untyped;

        typed.Equals(untyped).ShouldBeFalse();
        untyped.Equals(typed).ShouldBeFalse();
    }

    [Test]
    public void BoxedEqualsDoesNotBridgeDifferentValueTypes()
    {
        object asString = new SparsePath<Settings, string>(Untyped);
        object asInt = new SparsePath<Settings, int>(Untyped);
        object untyped = Untyped;

        asString.Equals(untyped).ShouldBeFalse();
        untyped.Equals(asInt).ShouldBeFalse();
        asString.Equals(asInt).ShouldBeFalse();
    }

    [Test]
    public void MixedBoxedHashSetDoesNotDependOnInsertionOrder()
    {
        object typed = new SparsePath<Settings, string>(Untyped);
        object untyped = Untyped;

        var typedFirst = new HashSet<object> { typed, untyped };
        var untypedFirst = new HashSet<object> { untyped, typed };
        typedFirst.Count.ShouldBe(2);
        untypedFirst.Count.ShouldBe(2);
    }

    [Test]
    public void TypedComparisonsKeepWorking()
    {
        var typed = new SparsePath<Settings, string>(Untyped);
        var same = new SparsePath<Settings, string>(SparsePath.Root<Settings>().Member("Label"));

        typed.Equals(same).ShouldBeTrue();
        (typed == same).ShouldBeTrue();
        typed.Equals((object)same).ShouldBeTrue();
        typed.Equals(Untyped).ShouldBeTrue();
        SparsePath implicitUntyped = typed;
        implicitUntyped.ShouldBe(Untyped);
        typed.GetHashCode().ShouldBe(Untyped.GetHashCode());
    }
}
