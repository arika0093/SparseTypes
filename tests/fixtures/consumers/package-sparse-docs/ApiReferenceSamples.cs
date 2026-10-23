using System.Text.Json;
using SparseTypes;

public static class ApiReferenceSamples
{
    public static void Run()
    {
        OptionalValues();
        Fragments();
        Patches();
        ChangeSets();
        Payloads();
        Sessions();
        Views();
        Paths();
        Collections();
        Rebase();
        Groups();
        Validation();
    }

    private static void OptionalValues()
    {
        // sample: reference-optional
        var missing = Optional<string?>.Missing;
        var explicitNull = Optional<string?>.Present(null);
        Optional<int> zero = 0;

        var fallback = missing.GetValueOrDefault("fallback");
        var stillNull = explicitNull.GetValueOrDefault("fallback");
        var operation = FragmentOperation<string?>.Set(null);
        var applied = operation.Apply(Optional<string?>.Missing);
        // /sample
        DocsCheck.Require(!missing.IsPresent && zero.IsPresent, "presence differs from default");
        DocsCheck.Require(fallback == "fallback" && stillNull is null, "fallback is missing-only");
        DocsCheck.Require(applied.IsPresent && applied.Value is null, "Set(null) stays present");
    }

    private static void Fragments()
    {
        // sample: reference-fragment
        var lower = new ReferenceSettings.Fragment { Theme = "light", RetryCount = 3 };
        var higher = new ReferenceSettings.Fragment { Theme = "dark" };
        var effective = lower.Merge(higher);

        var builder = effective.ToBuilder();
        builder.Theme = Optional<string?>.Present(null);
        var edited = builder.Build();
        var model = effective.ToModel();
        // /sample
        DocsCheck.Require(model.Theme == "dark" && model.RetryCount == 3, "higher layer wins");
        DocsCheck.Require(edited.Theme.IsPresent && edited.Theme.Value is null, "builder sets null");
        DocsCheck.Require(effective.Theme.Value == "dark", "builder leaves original unchanged");
    }

    private static void Patches()
    {
        // sample: reference-patch
        var layer = new ReferenceSettings.Fragment { Theme = "dark", RetryCount = 3 };
        var patch = new ReferenceSettings.Patch();
        patch.Theme.Remove();
        patch.RetryCount.Set(5);

        var updated = layer.Apply(patch);
        var undo = patch.Invert(Optional<ReferenceSettings.Fragment?>.Present(layer));
        var restored = updated.Apply(undo);
        // /sample
        DocsCheck.Require(!updated.Theme.IsPresent && updated.RetryCount.Value == 5, "patch operations");
        DocsCheck.Require(restored.Theme.Value == "dark" && restored.RetryCount.Value == 3, "patch inverse");
    }

    private static void ChangeSets()
    {
        // sample: reference-changeset
        var before = new ReferenceSettings { Theme = "light" };
        var after = new ReferenceSettings { Theme = "dark" };
        var changes = ReferenceSettings.ChangeSet.Between(before, after);

        var oldTheme = changes.Theme.Before.Value;
        var newTheme = changes.Theme.After.Value;
        var undo = changes.Invert();
        var roundTrip = changes.Compose(undo);
        // /sample
        DocsCheck.Require(oldTheme == "light" && newTheme == "dark", "typed endpoints");
        DocsCheck.Require(roundTrip.IsEmpty, "inverse cancels transition");
    }

    private static void Payloads()
    {
        // sample: reference-payload
        var before = new ReferenceSettings { Theme = "light" };
        var after = new ReferenceSettings { Theme = "dark" };
        var payload = before.CreateChangeSet(after).ToPayload();
        var json = JsonSerializer.Serialize(payload, ReferenceSettings.PayloadJson.Default);
        var received = JsonSerializer.Deserialize<ReferenceSettings.ChangePayload>(json, ReferenceSettings.PayloadJson.Default)!;

        var validation = received.Validate();
        var changes = received.ToChangeSet();
        // /sample
        DocsCheck.Require(validation.IsValid && changes.Theme.After.Value == "dark", "payload round trip");
    }

    private static void Sessions()
    {
        // sample: reference-session
        var model = new ReferenceSettings { Theme = "light" };
        var session = model.CreateEditSession();
        session.Observable.Theme = "dark";
        var submitted = session.CreateChangeSet();

        session.Observable.RetryCount = 5;
        session.AcceptChanges(submitted);
        var pending = session.CreateChangeSet();
        // /sample
        DocsCheck.Require(!pending.Theme.IsChanged && pending.RetryCount.IsChanged, "later edits retained");
    }

    private static void Views()
    {
        // sample: reference-views
        var model = new ReferenceSettings { Theme = "light" };
        var session = model.CreateEditSession();
        var view = session.Current;

        session.Observable.Theme = "dark";
        var currentTheme = view.Theme;
        var snapshot = model.DeepClone();
        session.Observable.Theme = "solarized";
        // /sample
        DocsCheck.Require(currentTheme == "dark" && view.Theme == "solarized", "view is live");
        DocsCheck.Require(snapshot.Theme == "dark", "clone is independent");
    }

    private static void Paths()
    {
        // sample: reference-paths
        var session = new ReferenceSettings().CreateEditSession();
        SparsePath<ReferenceSettings, string?> theme = session.Paths.Theme;
        var itemTitle = session.Paths.Items.Key("a").Title;
        var untyped = SparsePath.Root<ReferenceSettings>().Member("Items").Key("a").Member("Title");
        var samePath = itemTitle.UntypedPath == untyped;
        // /sample
        DocsCheck.Require(samePath && theme.Path.Depth == 1, "typed and untyped paths agree");
    }

    private static void Collections()
    {
        // sample: reference-collection
        var session = new ReferenceSettings().CreateEditSession();
        var editor = session.EditCollection<ReferenceItem, string>(session.Paths.Items);
        var added = editor.TryAdd(new ReferenceItem { Id = "a", Title = "first" }, out var addError);
        var duplicate = editor.TryAdd(new ReferenceItem { Id = "a" }, out var duplicateError);
        var found = editor.TryGetByKey("a", out var item, out var lookupError);
        // /sample
        DocsCheck.Require(added && addError == SparseCollectionEditError.None, "editor adds");
        DocsCheck.Require(!duplicate && duplicateError == SparseCollectionEditError.DuplicateKey, "duplicate rejected");
        DocsCheck.Require(found && lookupError == SparseCollectionEditError.None && item!.Title == "first", "key lookup");
    }

    private static void Rebase()
    {
        // sample: reference-rebase
        var baseline = new ReferenceSettings { Theme = "light" };
        var incoming = baseline.CreateChangeSet(new ReferenceSettings { Theme = "dark" });
        var current = new ReferenceSettings { Theme = "solarized" };
        var result = incoming.RebaseOnto(current);

        var resolution = ReferenceSettings.ChangeSet.BeginResolution(result, current);
        resolution.UseIncoming(ReferenceSettings.SparsePath.Theme);
        var resolved = resolution.TryBuild(out var changes, out var failure);
        // /sample
        DocsCheck.Require(result.HasConflicts && resolved && failure is null, "conflict resolved");
        DocsCheck.Require(changes!.TryApplyTo(current, out var updated) && updated.Theme == "dark", "resolution applies");
    }

    private static void Groups()
    {
        // sample: reference-group
        var session = new ReferenceSettings { Theme = "light" }.CreateEditSession();
        var group = new EditSessionGroup();
        var handle = group.Register("settings", session);
        session.Observable.Theme = "dark";

        var submission = group.Prepare();
        var submitted = submission.Get(handle).Changes;
        group.AcceptChanges(submission, handle);
        // /sample
        DocsCheck.Require(submitted.Theme.IsChanged && !session.HasChanges, "group acknowledges captured change");
    }

    private static void Validation()
    {
        // sample: reference-validation
        using var store = new SparseValidationStore<ReferenceSettings>();
        var path = ReferenceSettings.SparsePath.Theme.TypedPath;
        store.SetErrors(path, SparseValidationOrigins.ServerValidation, "Theme is unavailable.");
        var hasErrors = store.HasPathErrors(path);
        store.ClearOrigin(SparseValidationOrigins.ServerValidation);
        // /sample
        DocsCheck.Require(hasErrors && !store.HasErrors, "validation origins clear independently");
    }
}

// sample: reference-models
[SparseModel]
public partial class ReferenceSettings
{
    public string? Theme { get; set; }
    public int RetryCount { get; set; }
    public List<ReferenceItem> Items { get; set; } = new();
}

public partial class ReferenceItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}
// /sample
