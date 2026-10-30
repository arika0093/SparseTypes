namespace SparseTypes.Tests;

public sealed class TemporaryKeyEchoApplyTests
{
    [Test]
    public void AssignedEditAndItsInverseUpdateTheTemporaryAlias()
    {
        var temporary = Guid.NewGuid();
        var before = State(7, temporary, "before");
        var after = State(7, temporary, "after");
        var changes = TempOrderHolder.ChangeSet.Between(before, after);

        changes.ToPatch().Apply(before).Value!.ToModel().Lines.Single().Name.ShouldBe("after");
        changes
            .Invert()
            .ToPatch()
            .Apply(after)
            .Value!.ToModel()
            .Lines.Single()
            .Name.ShouldBe("before");
    }

    [Test]
    public void AssignedRemovalAlsoRemovesTheTemporaryAlias()
    {
        var before = State(7, Guid.NewGuid(), "before");
        var after = Optional<TempOrderHolder.Fragment?>.Present(
            TempOrderHolder.Fragment.From(new TempOrderHolder())
        );
        var changes = TempOrderHolder.ChangeSet.Between(before, after);

        changes.ToPatch().Apply(before).Value!.ToModel().Lines.ShouldBeEmpty();
        changes
            .Invert()
            .ToPatch()
            .Apply(after)
            .Value!.ToModel()
            .Lines.Single()
            .Name.ShouldBe("before");
    }

    [Test]
    public void TemporaryEditReplayedOnAnAssignmentUpdatesBothMaps()
    {
        var temporary = Guid.NewGuid();
        var pending = State(0, temporary, "before");
        var changes = TempOrderHolder.ChangeSet.Between(pending, State(0, temporary, "after"));
        var applied = changes.ToPatch().Apply(State(7, temporary, "before")).Value!.ToModel();

        applied.Lines.Single().Id.ShouldBe(7);
        applied.Lines.Single().Name.ShouldBe("after");
    }

    [Test]
    public void TemporaryRemovalReplayedOnAnAssignmentRemovesBothMaps()
    {
        var temporary = Guid.NewGuid();
        var pending = State(0, temporary, "before");
        var after = Optional<TempOrderHolder.Fragment?>.Present(
            TempOrderHolder.Fragment.From(new TempOrderHolder())
        );
        var changes = TempOrderHolder.ChangeSet.Between(pending, after);

        changes
            .ToPatch()
            .Apply(State(7, temporary, "before"))
            .Value!.ToModel()
            .Lines.ShouldBeEmpty();
    }

    [Test]
    public void LowercaseTemporaryKeyWirePayloadResolvesRow()
    {
        // The JavaScript codegen emits canonical lowercase D-form temporary
        // keys; they must resolve to the same rows as any other spelling.
        const string temporary = "f4ef9705-b5e2-41f6-a7ac-1f3b0a8c14de";
        var payload = PayloadJsonTestSupport.Deserialize<TempOrderHolder.ChangePayload>(
            """{"version":"0.1","change":{"kind":"update","members":{"lines":{"kind":"update","items":[{"key":0,"kind":"add","afterIndex":0,"temporaryKey":"__TEMP__","after":{"state":"value","value":{"Id":0,"TemporaryId":"__TEMP__","Name":"New"}}}],"beforeOrder":[],"afterOrder":[0],"tempBeforeOrder":[],"tempAfterOrder":["__TEMP__"]}}}}"""
                .Replace("__TEMP__", temporary)
        )!;
        payload.Validate().IsValid.ShouldBeTrue();
        payload.TryApplyMixedTo(new TempOrderHolder(), out var updated, out _).ShouldBeTrue();
        var line = updated!.Lines.Single();
        line.TemporaryId.ShouldBe(Guid.Parse(temporary));
        line.Name.ShouldBe("New");
        var applied = payload.ToPatch().Apply(
            Optional<TempOrderHolder.Fragment?>.Present(TempOrderHolder.Fragment.From(new TempOrderHolder()))
        );
        applied.Value!.ToModel().Lines.Single().TemporaryId.ShouldBe(Guid.Parse(temporary));
    }

    private static Optional<TempOrderHolder.Fragment?> State(int id, Guid temporary, string name) =>
        Optional<TempOrderHolder.Fragment?>.Present(
            TempOrderHolder.Fragment.From(
                new TempOrderHolder
                {
                    Lines =
                    [
                        new TempOrderLine
                        {
                            Id = id,
                            TemporaryId = temporary,
                            Name = name,
                        },
                    ],
                }
            )
        );
}
