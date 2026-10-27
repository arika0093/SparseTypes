using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SparseTypes.Blazor;
using SparseTypes.GeneratedApiFixtures;

namespace SparseTypes.Tests.PublicApi;

/// <summary>Approval tests for generated user APIs and the Blazor surface.</summary>
public sealed class GeneratedApiApprovalTests
{
    // Raw live-model access now hides behind ISparseEditSession<TModel>.
    private static T Raw<T>(SparseTypes.ISparseEditSession<T> session)
        where T : class => session.Model;

    private const string BlazorApprovalFile = "SparseTypes.Blazor.approved.txt";
    private const string EntityFrameworkCoreApprovalFile = "SparseTypes.EntityFrameworkCore.approved.txt";

    private static readonly Type[] FixtureModels =
    [
        typeof(CatalogScalar),
        typeof(CatalogNested),
        typeof(CatalogKeyedItem),
        typeof(CatalogKeyedRoster),
        typeof(CatalogDictionaries),
        typeof(CatalogImmutable),
    ];

    // Blazor ships net8.0 and net10.0 from identical sources (no #if in
    // SparseEditSessionExtensions.cs); one snapshot covers both target builds.
    [Test]
    public void BlazorSurface() => PublicApiCheck.CheckAssembly(typeof(SparseEditSessionExtensions).Assembly, BlazorApprovalFile);

    [Test]
    public void EntityFrameworkCoreSurface() =>
        PublicApiCheck.CheckAssembly(typeof(SparseTypes.EntityFrameworkCore.SparseEntityMapper<,>).Assembly, EntityFrameworkCoreApprovalFile);

    [Test]
    public void GeneratedUserApi() => PublicApiCheck.Check<CatalogScalar>();

    [Test]
    public void GeneratedApiCoversRepresentativeShapes()
    {
        foreach (var model in FixtureModels)
        {
            foreach (var nested in new[] { "Fragment", "Patch", "ChangeSet", "ChangePayload", "Observable", "ReadOnlyView", "EditSession" })
            {
                AssertNested(model, nested);
            }

            var fragmentBuilder = AssertNested(model, "FragmentBuilder");
            var fragment = AssertNested(model, "Fragment");
            fragment.GetMethod("ToBuilder")!.ReturnType.ShouldBe(fragmentBuilder);
            AssertMethods(fragmentBuilder, ["Build"]);
            model.GetNestedTypes().Select(static type => type.Name).ShouldNotContain("SparsePaths");

            var changeSet = AssertNested(model, "ChangeSet");
            var empty = changeSet.GetProperty("Empty", BindingFlags.Public | BindingFlags.Static);
            empty.ShouldNotBeNull($"Expected {model.Name}.ChangeSet.Empty.");
            empty!.PropertyType.ShouldBe(changeSet);
            AssertMethods(
                changeSet,
                [
                    "Between",
                    "FromPatch",
                    "ToPatch",
                    "Invert",
                    "Compose",
                    "RebaseOnto",
                    "TryApplyTo",
                    "TryApplyInPlace",
                    "ApplyInPlace",
                    "ApplyToBaseline",
                    "EnumerateChangedPaths",
                    "ToPayload",
                    "FromPayload",
                ]
            );

            var patch = AssertNested(model, "Patch");
            AssertMethods(patch, ["Between", "Apply", "ApplyTo"]);

            // The unified transport converts both directions explicitly.
            var changePayload = AssertNested(model, "ChangePayload");
            AssertMethods(changePayload, ["ToChangeSet", "ToPatch", "FromPatch"]);

            var extensions = AssertExtensionContainer(model);
            AssertMethods(extensions, ["CreateChangeSet", "CreateEditSession", "ToObservable"]);
        }

        // Typed per-key and per-member projections stay addressable by name.
        AssertNested(AssertNested(typeof(CatalogKeyedRoster), "ChangeSet"), "ItemsTransition");
        var dictionaryChanges = AssertNested(typeof(CatalogDictionaries), "ChangeSet");
        AssertNested(dictionaryChanges, "ScoresTransition");
        AssertNested(dictionaryChanges, "DetailsTransition");
        var keyedItemChanges = AssertNested(typeof(CatalogKeyedItem), "ChangeSet");
        keyedItemChanges.GetProperty("Id")!.PropertyType.ShouldBe(typeof(SparseTypes.SparseValueTransition<string>));

        var nestedReadOnlyView = new CatalogNested().CreateEditSession().Current.GetType();
        var metadataViewType = Nullable.GetUnderlyingType(nestedReadOnlyView.GetProperty(nameof(CatalogNested.Metadata))!.PropertyType);
        metadataViewType.ShouldNotBeNull();
        metadataViewType!.ShouldNotBe(typeof(CatalogPoco));
        var metadataElementType = Nullable.GetUnderlyingType(
            nestedReadOnlyView.GetProperty(nameof(CatalogNested.MetadataItems))!.PropertyType.GetGenericArguments()[0]
        );
        metadataElementType.ShouldBe(metadataViewType);
        metadataViewType.GetProperty(nameof(CatalogPoco.Label)).ShouldNotBeNull();

        var keyEntriesType = nestedReadOnlyView.GetProperty(nameof(CatalogNested.MetadataByKey))!.PropertyType;
        keyEntriesType.GetGenericTypeDefinition().ShouldBe(typeof(System.Collections.Generic.IReadOnlyCollection<>));
        var entryKeyType = keyEntriesType.GetGenericArguments()[0].GetGenericArguments()[0];
        entryKeyType.ShouldNotBe(typeof(CatalogPoco));
        entryKeyType.GetProperty(nameof(CatalogPoco.Label)).ShouldNotBeNull();
    }

    [Test]
    public void GeneratedEditSessionsExposeClassifiedSessionContracts()
    {
        // Generated sessions implement the runtime capability contracts. Pin the
        // generic surfaces here so the generator cannot emit a session contract
        // that the audience classification does not cover.
        foreach (var model in FixtureModels)
        {
            var changeSet = AssertNested(model, "ChangeSet");
            var session = AssertNested(model, "EditSession");
            var withChangeSet = typeof(SparseTypes.ISparseEditSessionWithChangeSet<,>).MakeGenericType(model, changeSet);
            session.GetInterfaces().ShouldContain(withChangeSet, $"{model.Name}.EditSession must expose {withChangeSet}.");
            session.GetInterfaces().ShouldContain(typeof(SparseTypes.ISparseEditSessionWithBaselineRevision));
            session.GetProperty(nameof(SparseTypes.ISparseEditSessionWithBaselineRevision.BaselineRevision)).ShouldNotBeNull();
        }

        var withChildStatus = typeof(SparseTypes.ISparseEditSessionWithChildStatus<,>).MakeGenericType(
            typeof(CatalogNested),
            typeof(SparseTypes.SparseConflict)
        );
        typeof(CatalogNested.EditSession).GetInterfaces().ShouldContain(withChildStatus);
        typeof(CatalogScalar.EditSession)
            .GetInterfaces()
            .ShouldNotContain(typeof(SparseTypes.ISparseEditSessionWithChildStatus<CatalogScalar, SparseTypes.SparseConflict>));
        typeof(CatalogScalar.EditSession).GetMethod("OpenChildSession", BindingFlags.Public | BindingFlags.Instance).ShouldBeNull();
        typeof(CatalogScalar.EditSession).GetMethod("ResolveChildModel", BindingFlags.Public | BindingFlags.Instance).ShouldBeNull();
        typeof(CatalogNested.EditSession).GetMethod("OpenChildSession", BindingFlags.Public | BindingFlags.Instance).ShouldNotBeNull();

        foreach (
            var definition in new[]
            {
                typeof(SparseTypes.ISparseEditSessionWithChangeSet<,>),
                typeof(SparseTypes.ISparseEditSessionWithChildStatus<,>),
                typeof(SparseTypes.ISparseEditSessionWithBaselineRevision),
            }
        )
        {
            definition.GetCustomAttribute<EditorBrowsableAttribute>()?.State.ShouldBe(EditorBrowsableState.Advanced);
        }
    }

    [Test]
    public void CreateEditSessionReturnsTheModelSpecificSubclass()
    {
        var session = new CatalogScalar().CreateEditSession();

        session.GetType().Namespace.ShouldBe("SparseTypes.GeneratedApiFixtures");
        session.GetType().Name.ShouldBe("EditSession");
        session.GetType().GetInterfaces().ShouldContain(typeof(SparseTypes.ISparseEditSession<CatalogScalar, CatalogScalar.ChangeSet>));
        session.Current.Name.ShouldBe(string.Empty);
        Raw(session).Name.ShouldBe(string.Empty);

        var baseline = new CatalogScalar { Name = "baseline" };
        var current = new CatalogScalar { Name = "current" };
        var separateBaselineSession = baseline.CreateEditSession(current);
        separateBaselineSession.GetType().ShouldBe(session.GetType());
        ReferenceEquals(Raw(separateBaselineSession), current).ShouldBeTrue();
        separateBaselineSession.Current.Name.ShouldBe("current");
    }

    [Test]
    public void EditSessionExposesCreateHistoryFactory()
    {
        // each generated session offers a typed opt-in history factory bound to
        // its own change set, without reflection.
        foreach (var model in FixtureModels)
        {
            var session = AssertNested(model, "EditSession");
            var factory = session.GetMethod("CreateHistory", BindingFlags.Public | BindingFlags.Instance);
            factory.ShouldNotBeNull($"Expected {model.Name}.EditSession.CreateHistory.");
            factory!.ReturnType.IsGenericType.ShouldBeTrue();
            factory.ReturnType.GetGenericTypeDefinition().ShouldBe(typeof(SparseTypes.SparseEditHistory<>));
            factory.ReturnType.GetGenericArguments()[0].ShouldBe(model.GetNestedType("ChangeSet"));
            var options = factory.GetParameters();
            options.Length.ShouldBe(1);
            options[0].ParameterType.ShouldBe(typeof(SparseTypes.EditHistoryOptions));
            options[0].HasDefaultValue.ShouldBeTrue();
        }

        // The factory result supports the documented undo/redo surface.
        var scalarHistory = new CatalogScalar().CreateEditSession().CreateHistory();
        scalarHistory.CanUndo.ShouldBeFalse();
        scalarHistory.CanRedo.ShouldBeFalse();
        scalarHistory.MaxEntries.ShouldBeGreaterThan(0);
        scalarHistory.Dispose();
    }

    [Test]
    public void EditSessionExposesSingleOwnedHistory()
    {
        // each generated session owns one opt-in history behind the
        // framework-neutral control contract.
        foreach (var model in FixtureModels)
        {
            var session = AssertNested(model, "EditSession");

            var historyProperty = session.GetProperty("History", BindingFlags.Public | BindingFlags.Instance);
            historyProperty.ShouldNotBeNull($"Expected {model.Name}.EditSession.History.");
            historyProperty!.PropertyType.ShouldBe(typeof(SparseTypes.ISparseEditHistory));

            var enable = session.GetMethod("EnableHistory", BindingFlags.Public | BindingFlags.Instance);
            enable.ShouldNotBeNull($"Expected {model.Name}.EditSession.EnableHistory.");
            enable!.ReturnType.ShouldBe(typeof(SparseTypes.ISparseEditHistory));
            var options = enable.GetParameters();
            options.Length.ShouldBe(1);
            options[0].ParameterType.ShouldBe(typeof(SparseTypes.EditHistoryOptions));
            options[0].HasDefaultValue.ShouldBeTrue();

            var disable = session.GetMethod("DisableHistory", BindingFlags.Public | BindingFlags.Instance);
            disable.ShouldNotBeNull($"Expected {model.Name}.EditSession.DisableHistory.");
            disable!.GetParameters().Length.ShouldBe(0);

            var availability = session.GetEvent("HistoryChanged", BindingFlags.Public | BindingFlags.Instance);
            availability.ShouldNotBeNull($"Expected {model.Name}.EditSession.HistoryChanged.");
            availability!.EventHandlerType.ShouldBe(typeof(Action));
        }

        var ownedSession = new CatalogScalar().CreateEditSession();
        Should.Throw<InvalidOperationException>(() => _ = ownedSession.History);
        var history = ownedSession.EnableHistory();
        history.ShouldNotBeNull();
        ReferenceEquals(history, ownedSession.History).ShouldBeTrue();
        (history is IDisposable).ShouldBeFalse();
        ownedSession.DisableHistory();
        Should.Throw<InvalidOperationException>(() => _ = ownedSession.History);
    }

    [Test]
    public void PayloadImplementationDtosAreGroupedAndHiddenFromIntelliSense()
    {
        // Stage 3: typed DTOs live in the per-model implementation namespace,
        // not nested in the annotated model.
        foreach (var model in FixtureModels)
        {
            model
                .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Where(static type => type.Name.EndsWith("SerializeObjects", StringComparison.Ordinal))
                .ShouldBeEmpty();
        }

        var containers = FixtureModels
            .Select(static model => model.GetNestedType("ChangePayload")?.BaseType?.DeclaringType)
            .Where(static type => type is not null)
            .Cast<Type>()
            .ToArray();

        containers.Length.ShouldBe(FixtureModels.Length);
        foreach (var container in containers)
        {
            container.Namespace.ShouldBe("SparseTypes.Generated");
        }

        var implementationTypes = containers
            .SelectMany(static container => container.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            .ToArray();
        implementationTypes.ShouldNotBeEmpty();
        foreach (var container in containers)
        {
            container.GetCustomAttribute<EditorBrowsableAttribute>()?.State.ShouldBe(EditorBrowsableState.Never);
        }

        foreach (var implementationType in implementationTypes)
        {
            implementationType.GetCustomAttribute<EditorBrowsableAttribute>()?.State.ShouldBe(EditorBrowsableState.Never);
        }

        var scalarContainer = typeof(CatalogScalar).GetNestedType("ChangePayload")!.BaseType!.DeclaringType!;
        scalarContainer.Name.ShouldEndWith("SerializeObjects");
        var scalarMemberChange = scalarContainer
            .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Single(static type => type.Name == "Change0");
        scalarMemberChange.DeclaringType.ShouldBe(scalarContainer);
    }

    [Test]
    public void ChangePayloadSupportsExternalSourceGeneration()
    {
        var before = new CatalogScalar { Name = "before", Count = 1 };
        var after = new CatalogScalar { Name = "after", Count = 1 };
        var payload = before.CreateChangeSet(after).ToPayload();
        var json = JsonSerializer.Serialize(payload, CatalogSerializerContext.Default.CatalogScalarChangePayload);

        var restored = JsonSerializer.Deserialize(json, CatalogSerializerContext.Default.CatalogScalarChangePayload);

        restored.ShouldNotBeNull();
        restored!.Change.ShouldNotBeNull();
        restored.ToChangeSet().IsEmpty.ShouldBeFalse();
    }

    [Test]
    public void NestedPayloadJsonHelpersExposeDefaultAndResolver()
    {
        // The discoverable model-nested helper exposes JsonTypeInfo and the
        // resolver interface; attributed converters also support external contexts.
        foreach (var model in FixtureModels)
        {
            var helper = model.GetNestedType("PayloadJson");
            helper.ShouldNotBeNull($"Expected {model.Name}.PayloadJson helper.");
            helper!.IsNestedPublic.ShouldBeTrue();
            helper.GetCustomAttribute<EditorBrowsableAttribute>().ShouldBeNull($"Expected {helper.Name} to stay discoverable.");
            var changePayload = model.GetNestedType("ChangePayload");
            changePayload.ShouldNotBeNull();
            changePayload!.GetCustomAttribute<EditorBrowsableAttribute>(inherit: false).ShouldBeNull();
            changePayload.GetCustomAttribute<EditorBrowsableAttribute>()!.State.ShouldBe(EditorBrowsableState.Never);
            helper.GetProperty("Default")!.PropertyType.ShouldBe(typeof(JsonTypeInfo<>).MakeGenericType(changePayload!));
            helper.GetProperty("Resolver")!.PropertyType.ShouldBe(typeof(IJsonTypeInfoResolver));

            var container = changePayload.BaseType!.DeclaringType!;
            var converter = model.Assembly.GetTypes().Single(type => type.Name == container.Name + "PayloadJsonConverter");
            converter.DeclaringType.ShouldBeNull();
            converter.IsPublic.ShouldBeTrue();
            changePayload.GetCustomAttribute<JsonConverterAttribute>()!.ConverterType.ShouldBe(converter);
            converter.GetConstructor(Type.EmptyTypes).ShouldNotBeNull();
            converter.GetCustomAttribute<EditorBrowsableAttribute>()!.State.ShouldBe(EditorBrowsableState.Never);
            var resolver = model.Assembly.GetTypes().Single(type => type.Name == container.Name + "PayloadJsonResolver");
            resolver.DeclaringType.ShouldBeNull();
            resolver.IsNotPublic.ShouldBeTrue();
            resolver.GetCustomAttribute<EditorBrowsableAttribute>()!.State.ShouldBe(EditorBrowsableState.Never);
            changePayload.GetProperty("Version")!.GetCustomAttribute<EditorBrowsableAttribute>()!.State.ShouldBe(EditorBrowsableState.Never);
            changePayload.GetProperty("Change").ShouldNotBeNull();
            changePayload.GetProperty("Changes").ShouldBeNull();
            changePayload.GetMethod("ToPatch")!.GetCustomAttribute<EditorBrowsableAttribute>().ShouldBeNull();
        }
    }

    [Test]
    public void SubmitApisStayAbsent()
    {
        var fixture = typeof(CatalogScalar).Assembly;
        var offenders = fixture
            .GetExportedTypes()
            .SelectMany(static type => type.GetMethods().Select(method => new { type, method }))
            .Where(static entry => entry.method.DeclaringType == entry.type)
            .Where(static entry => entry.method.Name == "Submit")
            .Select(static entry => $"{entry.type.FullName}.{entry.method.Name}")
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        offenders.ShouldBeEmpty();

        var blazor = typeof(SparseEditSessionExtensions).Assembly;
        var blazorOffenders = blazor
            .GetExportedTypes()
            .SelectMany(static type => type.GetMethods().Select(method => $"{type.FullName}.{method.Name}"))
            .Where(static name => name.Contains("Submit", StringComparison.Ordinal))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        blazorOffenders.ShouldBeEmpty();
    }

    private static Type AssertNested(Type declaring, string name)
    {
        var nested = declaring.GetNestedType(name);
        nested.ShouldNotBeNull($"Generated {declaring.Name}.{name} must stay public.");
        nested!.IsNestedPublic.ShouldBeTrue();
        return nested;
    }

    private static Type AssertExtensionContainer(Type model)
    {
        var container = model
            .Assembly.GetExportedTypes()
            .Where(candidate => candidate.DeclaringType is null && candidate.Name.StartsWith(model.Name + "Extensions_", StringComparison.Ordinal))
            .ToArray();
        container.Length.ShouldBe(1);
        return container[0];
    }

    private static void AssertMethods(Type type, string[] names)
    {
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(static method => method.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var name in names)
        {
            methods.Contains(name).ShouldBeTrue($"Expected {type.Name}.{name} to stay public.");
        }
    }
}

[JsonSerializable(typeof(CatalogScalar.ChangePayload), TypeInfoPropertyName = "CatalogScalarChangePayload")]
internal partial class CatalogSerializerContext : JsonSerializerContext { }
