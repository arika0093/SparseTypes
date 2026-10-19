using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.Components.Forms;

namespace SparseTypes.Blazor.Tests;

public sealed class BlazorConflictValidationTests
{
    private static T Raw<T>(ISparseEditSession<T> session)
        where T : class => session.Model;

    private static OrderDto Order() =>
        new()
        {
            Number = "ORD-1",
            Customer = new OrderCustomer { Name = "Ada", Email = "ada@example.com" },
            Lines = new()
            {
                new OrderLine
                {
                    Sku = "a",
                    Quantity = 1,
                    Price = 10m,
                },
                new OrderLine
                {
                    Sku = "b",
                    Quantity = 2,
                    Price = 20m,
                },
            },
            Contacts = new() { ["billing"] = new OrderCustomer { Name = "Ada" } },
        };

    private static SparseConflict Conflict(SparsePath path, string? reason = null) =>
        new(
            path,
            SparseConflictKind.Scalar,
            Optional<object?>.Missing,
            Optional<object?>.Missing,
            Optional<object?>.Missing,
            reason
        );

    [Test]
    public void ScalarReloadConflictMapsToField()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);
        var notified = 0;
        context.OnValidationStateChanged += (_, _) => notified++;

        Raw(session).Number = "LOCAL";
        var server = Order();
        server.Number = "SERVER";
        var reload = session.Reload(server);
        reload.HasConflicts.ShouldBeTrue();
        reload.Conflicts.Single().PathText.ShouldBe(nameof(OrderDto.Number));

        var added = session.AddConflicts(store, context, reload.Conflicts);
        added.ShouldBe(1);
        context
            .GetValidationMessages(session.Field(OrderDto.SparsePath.Number))
            .Single()
            .ShouldContain("Conflict at 'Number'");
        context.GetValidationMessages().ShouldNotBeEmpty();
        notified.ShouldBe(1);
    }

    [Test]
    public void NestedKeyedAndDictionaryConflictsMapToFields()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);

        var nested = SparsePath
            .Root<OrderDto>()
            .Member(nameof(OrderDto.Customer))
            .Member(nameof(OrderCustomer.Name));
        var keyed = SparsePath
            .Root<OrderDto>()
            .Member(nameof(OrderDto.Lines))
            .Key("b")
            .Member(nameof(OrderLine.Quantity));
        var dictionary = SparsePath
            .Root<OrderDto>()
            .Member(nameof(OrderDto.Contacts))
            .Key("billing")
            .Member(nameof(OrderCustomer.Name));

        var added = session.AddConflicts(
            store,
            context,
            [Conflict(nested), Conflict(keyed), Conflict(dictionary)]
        );
        added.ShouldBe(3);

        context
            .GetValidationMessages(session.Field(OrderDto.SparsePath.Customer.Name))
            .Single()
            .ShouldContain("Customer.Name");
        context
            .GetValidationMessages(session.Field(OrderDto.SparsePath.Lines.Key("b").Quantity))
            .Single()
            .ShouldContain("Lines[\"b\"].Quantity");
        context
            .GetValidationMessages(session.Field(OrderDto.SparsePath.Contacts.Key("billing").Name))
            .Single()
            .ShouldContain("Contacts[\"billing\"].Name");
    }

    [Test]
    public void RemovedKeyFallsBackToNearestParent()
    {
        var session = Order().CreateEditSession();
        Raw(session).Lines.RemoveAll(line => line.Sku == "a");
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);

        var removed = SparsePath
            .Root<OrderDto>()
            .Member(nameof(OrderDto.Lines))
            .Key("a")
            .Member(nameof(OrderLine.Quantity));
        Should.Throw<ArgumentException>(() => session.Field(removed));

        var added = session.AddConflicts(store, context, [Conflict(removed)]);
        added.ShouldBe(1);
        context.GetValidationMessages().ShouldNotBeEmpty();
        context.GetValidationMessages(session.Field(OrderDto.SparsePath.Lines)).ShouldNotBeEmpty();
    }

    [Test]
    public void RemovedKeyModelFallbackSurfacesAtModelLevel()
    {
        var session = Order().CreateEditSession();
        Raw(session).Lines.RemoveAll(line => line.Sku == "a");
        var model = Raw(session);
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);
        var options = new SparseConflictValidationOptions
        {
            Fallback = SparseConflictFallbackMode.Model,
        };

        var removed = SparsePath
            .Root<OrderDto>()
            .Member(nameof(OrderDto.Lines))
            .Key("a")
            .Member(nameof(OrderLine.Quantity));

        session.AddConflicts(store, context, [Conflict(removed)], options).ShouldBe(1);
        context.GetValidationMessages(new FieldIdentifier(model, string.Empty)).ShouldNotBeEmpty();
    }

    [Test]
    public void MissingNestedModelFallsBackToParent()
    {
        var session = Order().CreateEditSession();
        Raw(session).Customer = null!;
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);

        var nested = SparsePath
            .Root<OrderDto>()
            .Member(nameof(OrderDto.Customer))
            .Member(nameof(OrderCustomer.Name));
        Should.Throw<ArgumentException>(() => session.Field(nested));

        session.AddConflicts(store, context, [Conflict(nested)]).ShouldBe(1);
        context
            .GetValidationMessages(session.Field(OrderDto.SparsePath.Customer))
            .ShouldNotBeEmpty();
    }

    [Test]
    public void RootAndForeignPathsSurfaceAtModelLevel()
    {
        var session = Order().CreateEditSession();
        var model = Raw(session);
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);
        var modelField = new FieldIdentifier(model, string.Empty);

        var root = SparsePath.Root<OrderDto>();
        var foreign = SparsePath.Root<Team>().Member(nameof(Team.Name));

        session
            .AddConflicts(
                store,
                context,
                [
                    new SparseConflict(
                        root,
                        SparseConflictKind.WholeContribution,
                        Optional<object?>.Missing,
                        Optional<object?>.Missing,
                        Optional<object?>.Missing,
                        null
                    ),
                    Conflict(foreign),
                ]
            )
            .ShouldBe(2);
        context.GetValidationMessages(modelField).Count().ShouldBe(2);
    }

    [Test]
    public void ConflictStoreStaysDistinctFromManualMessages()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var conflictStore = session.CreateValidationStore(context);
        var manualStore = session.CreateValidationStore(context);

        var options = new SparseConflictValidationOptions
        {
            MessageFactory = static conflict => $"CONFLICT:{conflict.PathText}",
        };
        session.AddConflicts(
            conflictStore,
            context,
            [Conflict(SparsePath.Root<OrderDto>().Member(nameof(OrderDto.Number)))],
            options
        );
        manualStore.Add(session.Field(OrderDto.SparsePath.Number), "Number is required.");

        context
            .GetValidationMessages(session.Field(OrderDto.SparsePath.Number))
            .ShouldContain("CONFLICT:Number");
        context
            .GetValidationMessages(session.Field(OrderDto.SparsePath.Number))
            .ShouldContain("Number is required.");

        session.ClearConflicts(conflictStore, context);
        var remaining = context.GetValidationMessages(session.Field(OrderDto.SparsePath.Number));
        remaining.ShouldContain("Number is required.");
        remaining.ShouldNotContain("CONFLICT:Number");
    }

    [Test]
    public void PreserveClearAndRepeatedValidation()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);
        var conflicts = new[]
        {
            Conflict(SparsePath.Root<OrderDto>().Member(nameof(OrderDto.Number))),
        };

        session.AddConflicts(store, context, conflicts).ShouldBe(1);
        session.AddConflicts(store, context, conflicts).ShouldBe(1);
        context.GetValidationMessages().Count().ShouldBe(2);

        var clearing = new SparseConflictValidationOptions { ClearStoreFirst = true };
        session.AddConflicts(store, context, conflicts, clearing).ShouldBe(1);
        context.GetValidationMessages().Count().ShouldBe(1);

        session.ClearConflicts(store, context);
        context.GetValidationMessages().ShouldBeEmpty();
    }

    [Test]
    public void ReplacedStoreStartsFreshAfterClearingOld()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var oldStore = session.CreateValidationStore(context);
        var conflicts = new[]
        {
            Conflict(SparsePath.Root<OrderDto>().Member(nameof(OrderDto.Number))),
        };

        session.AddConflicts(oldStore, context, conflicts).ShouldBe(1);
        session.ClearConflicts(oldStore, context);

        var freshStore = session.CreateValidationStore(context);
        session.AddConflicts(freshStore, context, conflicts).ShouldBe(1);
        context.GetValidationMessages().Count().ShouldBe(1);
    }

    [Test]
    public void FreshServerStateReplacesContextAndRejectsStale()
    {
        var session = Order().CreateEditSession();
        var staleContext = session.CreateEditContext();
        var staleStore = session.CreateValidationStore(staleContext);
        void Validate(object? sender, ValidationRequestedEventArgs args) => staleStore.Clear();
        staleContext.OnValidationRequested += Validate;

        var persisted = Order();
        persisted.Number = "ORD-2";
        staleContext.OnValidationRequested -= Validate;
        var freshSession = persisted.CreateEditSession();
        var freshContext = freshSession.ReplaceEditContext(staleContext);
        var freshStore = freshSession.CreateValidationStore(freshContext);

        ReferenceEquals(freshContext.Model, persisted).ShouldBeTrue();
        ReferenceEquals(staleContext.Model, Raw(session)).ShouldBeTrue();

        var conflicts = new[]
        {
            Conflict(SparsePath.Root<OrderDto>().Member(nameof(OrderDto.Number))),
        };
        Should.Throw<ArgumentException>(() =>
            freshSession.AddConflicts(staleStore, staleContext, conflicts)
        );
        Should.Throw<ArgumentException>(() =>
            session.AddConflicts(freshStore, freshContext, conflicts)
        );
        freshSession.AddConflicts(freshStore, freshContext, conflicts).ShouldBe(1);
        freshContext.GetValidationMessages().ShouldNotBeEmpty();

        Should.Throw<ArgumentException>(() =>
            freshSession.ReplaceEditContext(freshSession.CreateEditContext())
        );
    }

    [Test]
    public void ExistingValidationHelpersKeepWorkingAlongsideConflicts()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);

        session.AddValidationError(
            store,
            session.Field(OrderDto.SparsePath.Number),
            "Server rejected the order number."
        );
        session
            .AddConflicts(
                store,
                context,
                [Conflict(SparsePath.Root<OrderDto>().Member(nameof(OrderDto.Number)))]
            )
            .ShouldBe(1);

        var messages = context.GetValidationMessages(session.Field(OrderDto.SparsePath.Number));
        messages.ShouldContain("Server rejected the order number.");
        messages.ShouldContain(m => m.Contains("Conflict at 'Number'"));
    }

    private static Case6Catalog Catalog() =>
        new()
        {
            Title = "catalog",
            Section = new()
            {
                Tiers =
                [
                    new Case6Tier
                    {
                        Code = "t1",
                        Label = "Tier one",
                        UnitPrice = 10m,
                        Bands = [new Case6Band { Code = "b1", Percent = 5m }],
                    },
                ],
            },
        };

    [Test]
    public void DeepMissingKeySuffixFallsBackToNearestCollections()
    {
        var session = Catalog().CreateEditSession();
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);

        var missingTier = SparsePath
            .Root<Case6Catalog>()
            .Member(nameof(Case6Catalog.Section))
            .Member(nameof(Case6Section.Tiers))
            .Key("missing")
            .Member(nameof(Case6Tier.Bands))
            .Key("b1")
            .Member(nameof(Case6Band.Percent));
        Should.Throw<ArgumentException>(() => session.Field(missingTier));

        session.AddConflicts(store, context, [Conflict(missingTier)]).ShouldBe(1);
        context
            .GetValidationMessages(session.Field(Case6Catalog.SparsePath.Section.Tiers))
            .ShouldNotBeEmpty();
        context.GetValidationMessages().ShouldNotBeEmpty();
    }

    [Test]
    public void MissingKeyFallbackThrowsNoResolutionExceptions()
    {
        var session = Catalog().CreateEditSession();
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);
        var missingTier = SparsePath
            .Root<Case6Catalog>()
            .Member(nameof(Case6Catalog.Section))
            .Member(nameof(Case6Section.Tiers))
            .Key("missing")
            .Member(nameof(Case6Tier.Bands))
            .Key("b1")
            .Member(nameof(Case6Band.Percent));

        // Warm up outside the handler so one-off initialization never counts.
        session.AddConflicts(store, context, [Conflict(missingTier)]).ShouldBe(1);

        // Expected missing targets must resolve through one non-throwing walk:
        // no shortened-prefix retry may throw out of conflict projection.
        // The frame filter keeps parallel tests' own expected throws (for
        // example session.Field on missing paths) out of the count.
        var projectionThrows = 0;
        void Handler(object? sender, FirstChanceExceptionEventArgs e)
        {
            if (
                e.Exception is ArgumentException argument
                && argument.Message.Contains("does not resolve", StringComparison.Ordinal)
                && argument.StackTrace?.Contains("ResolveConflictField", StringComparison.Ordinal)
                    == true
            )
            {
                Interlocked.Increment(ref projectionThrows);
            }
        }

        AppDomain.CurrentDomain.FirstChanceException += Handler;
        try
        {
            for (var index = 0; index < 25; index++)
            {
                session.AddConflicts(store, context, [Conflict(missingTier)]).ShouldBe(1);
            }
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= Handler;
        }

        projectionThrows.ShouldBe(0);
    }

    [Test]
    public void DeletedInnerKeyKeepsOuterKeyNearest()
    {
        var session = Catalog().CreateEditSession();
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);

        var innerMissing = SparsePath
            .Root<Case6Catalog>()
            .Member(nameof(Case6Catalog.Section))
            .Member(nameof(Case6Section.Tiers))
            .Key("t1")
            .Member(nameof(Case6Tier.Bands))
            .Key("b2")
            .Member(nameof(Case6Band.Percent));

        session.AddConflicts(store, context, [Conflict(innerMissing)]).ShouldBe(1);
        context
            .GetValidationMessages(
                session.Field(Case6Catalog.SparsePath.Section.Tiers.Key("t1").Bands)
            )
            .ShouldNotBeEmpty();
    }

    [Test]
    public void CollectionEntryEndingPathsMapToOwningMember()
    {
        var session = Catalog().CreateEditSession();
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);

        var trailingKey = SparsePath
            .Root<Case6Catalog>()
            .Member(nameof(Case6Catalog.Section))
            .Member(nameof(Case6Section.Tiers))
            .Key("t1");

        session.AddConflicts(store, context, [Conflict(trailingKey)]).ShouldBe(1);
        context
            .GetValidationMessages(session.Field(Case6Catalog.SparsePath.Section.Tiers))
            .ShouldNotBeEmpty();
    }

    [Test]
    public void NullNestedDeepModelKeepsRootMemberNearest()
    {
        var session = Catalog().CreateEditSession();
        Raw(session).Section = null!;
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);

        var deep = SparsePath
            .Root<Case6Catalog>()
            .Member(nameof(Case6Catalog.Section))
            .Member(nameof(Case6Section.Tiers))
            .Key("t1")
            .Member(nameof(Case6Tier.Bands));

        session.AddConflicts(store, context, [Conflict(deep)]).ShouldBe(1);
        context
            .GetValidationMessages(session.Field(Case6Catalog.SparsePath.Section))
            .ShouldNotBeEmpty();
    }

    [Test]
    public void RootMissingMemberFallsBackToModelLevelInBothModes()
    {
        var missing = SparsePath.Root<Case6Catalog>().Member("NotGenerated");
        var session = Catalog().CreateEditSession();
        var model = Raw(session);
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);
        var modelField = new FieldIdentifier(model, string.Empty);
        Should.Throw<ArgumentException>(() => session.Field(missing));

        session.AddConflicts(store, context, [Conflict(missing)]).ShouldBe(1);
        context.GetValidationMessages(modelField).ShouldNotBeEmpty();

        var otherStore = session.CreateValidationStore(context);
        var options = new SparseConflictValidationOptions
        {
            Fallback = SparseConflictFallbackMode.Model,
        };
        session.AddConflicts(otherStore, context, [Conflict(missing)], options).ShouldBe(1);
        context.GetValidationMessages(modelField).Count().ShouldBe(2);
    }

    [Test]
    public void OutOfRangeIndexFallsBackToMember()
    {
        var session = Catalog().CreateEditSession();
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);

        var outOfRange = SparsePath
            .Root<Case6Catalog>()
            .Member(nameof(Case6Catalog.Section))
            .Member(nameof(Case6Section.Tiers))
            .At(9)
            .Member(nameof(Case6Tier.Label));
        Should.Throw<ArgumentException>(() => session.Field(outOfRange));

        session.AddConflicts(store, context, [Conflict(outOfRange)]).ShouldBe(1);
        context
            .GetValidationMessages(session.Field(Case6Catalog.SparsePath.Section.Tiers))
            .ShouldNotBeEmpty();
    }

    [Test]
    public void MissingTemporaryKeyFallsBackToMember()
    {
        var session = new BlazorDraftBoard
        {
            Drafts = [new BlazorDraftLine { Id = 1, Title = "one" }],
        }.CreateEditSession();
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);

        var temporary = SparsePath
            .Root<BlazorDraftBoard>()
            .Member(nameof(BlazorDraftBoard.Drafts))
            .TemporaryKey(Guid.NewGuid())
            .Member(nameof(BlazorDraftLine.Title));

        session.AddConflicts(store, context, [Conflict(temporary)]).ShouldBe(1);
        context
            .GetValidationMessages(session.Field(BlazorDraftBoard.SparsePath.Drafts))
            .ShouldNotBeEmpty();
    }

    [Test]
    public void MissingDictionaryKeyFallsBackBeforeSuffix()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);

        var missingKey = SparsePath
            .Root<OrderDto>()
            .Member(nameof(OrderDto.Contacts))
            .Key("nope")
            .Member(nameof(OrderCustomer.Email));

        session.AddConflicts(store, context, [Conflict(missingKey)]).ShouldBe(1);
        context
            .GetValidationMessages(session.Field(OrderDto.SparsePath.Contacts))
            .ShouldNotBeEmpty();
    }

    [Test]
    public void ModelLevelFallbackKeepsResolvedFieldsWhenResolvable()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);
        var options = new SparseConflictValidationOptions
        {
            Fallback = SparseConflictFallbackMode.Model,
        };
        var resolved = SparsePath.Root<OrderDto>().Member(nameof(OrderDto.Number));

        session.AddConflicts(store, context, [Conflict(resolved)], options).ShouldBe(1);
        context.GetValidationMessages(session.Field(OrderDto.SparsePath.Number)).ShouldNotBeEmpty();
    }

    [Test]
    public void ThrowingGetterSurfacesInsteadOfSilentFallback()
    {
        var order = new BlazorThrowingOrder
        {
            Number = "ok",
            Customer = new OrderCustomer { Name = "Ada" },
        };
        var session = order.CreateEditSession();
        var context = session.CreateEditContext();
        var store = session.CreateValidationStore(context);
        var nested = SparsePath
            .Root<BlazorThrowingOrder>()
            .Member(nameof(BlazorThrowingOrder.Customer))
            .Member(nameof(OrderCustomer.Name));

        // Fail the getter through an instance sentinel so no shared static
        // state is involved.
        order.Number = "boom";

        // A user getter failure is unexpected, not a missing target: both
        // fallback modes surface it instead of mapping a parent field.
        var failure = Should.Throw<TargetInvocationException>(() =>
            session.AddConflicts(store, context, [Conflict(nested)])
        );
        failure.InnerException.ShouldBeOfType<InvalidOperationException>();
        var modelOptions = new SparseConflictValidationOptions
        {
            Fallback = SparseConflictFallbackMode.Model,
        };
        Should.Throw<TargetInvocationException>(() =>
            session.AddConflicts(store, context, [Conflict(nested)], modelOptions)
        );
        context.GetValidationMessages().ShouldBeEmpty();
    }
}
