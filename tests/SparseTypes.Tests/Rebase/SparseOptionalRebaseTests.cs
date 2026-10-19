namespace SparseTypes.Tests;

public sealed class SparseOptionalRebaseTests
{
    [Test]
    public void BaselineMatchSelectsDesiredEndpoint()
    {
        var applied = SparseOptionalRebase.TryResolve(
            Optional<int>.Present(1),
            Optional<int>.Present(2),
            Optional<int>.Present(1),
            SparseRebaseMode.FailOnConflict,
            SparseRebaseMode.PreferIncoming,
            SparseRebaseMode.PreferCurrent,
            static (left, right) => left == right,
            out var after,
            out var conflict
        );

        applied.ShouldBeTrue();
        after.ShouldBe(Optional<int>.Present(2));
        conflict.ShouldBeFalse();
    }

    [Test]
    public void DivergentEndpointsConflictInFailOnConflictMode()
    {
        var applied = SparseOptionalRebase.TryResolve(
            Optional<int>.Present(1),
            Optional<int>.Present(2),
            Optional<int>.Present(3),
            SparseRebaseMode.FailOnConflict,
            SparseRebaseMode.PreferIncoming,
            SparseRebaseMode.PreferCurrent,
            static (left, right) => left == right,
            out var after,
            out var conflict
        );

        applied.ShouldBeFalse();
        after.IsPresent.ShouldBeFalse();
        conflict.ShouldBeTrue();
    }

    [Test]
    public void PreferModesResolveOnlyTheirRequestedDivergence()
    {
        var incomingApplied = SparseOptionalRebase.TryResolve(
            Optional<int>.Present(1),
            Optional<int>.Present(2),
            Optional<int>.Present(3),
            SparseRebaseMode.PreferIncoming,
            SparseRebaseMode.PreferIncoming,
            SparseRebaseMode.PreferCurrent,
            static (left, right) => left == right,
            out var incoming,
            out var incomingConflict
        );
        var currentApplied = SparseOptionalRebase.TryResolve(
            Optional<int>.Present(1),
            Optional<int>.Present(2),
            Optional<int>.Present(3),
            SparseRebaseMode.PreferCurrent,
            SparseRebaseMode.PreferIncoming,
            SparseRebaseMode.PreferCurrent,
            static (left, right) => left == right,
            out _,
            out var currentConflict
        );

        incomingApplied.ShouldBeTrue();
        incoming.ShouldBe(Optional<int>.Present(2));
        incomingConflict.ShouldBeFalse();
        currentApplied.ShouldBeFalse();
        currentConflict.ShouldBeFalse();
    }

    [Test]
    public void PresenceAndPresentNullRemainDistinct()
    {
        var applied = SparseOptionalRebase.TryResolve(
            Optional<string?>.Missing,
            Optional<string?>.Present(null),
            Optional<string?>.Missing,
            SparseRebaseMode.Default,
            SparseRebaseMode.PreferIncoming,
            SparseRebaseMode.PreferCurrent,
            static (left, right) => left == right,
            out var after,
            out var conflict
        );

        applied.ShouldBeTrue();
        after.ShouldBe(Optional<string?>.Present(null));
        conflict.ShouldBeFalse();
    }
}
