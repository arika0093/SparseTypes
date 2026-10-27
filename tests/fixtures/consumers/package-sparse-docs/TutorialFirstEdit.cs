using System.Text.Json;
using SparseTypes;

// Compile-checked tutorial samples for the three progressive tutorials:
// 1. first-sparse-edit.md
// 2. send-and-rebase-changes.md
// 3. layer-and-reset-settings.md
public static class TutorialFirstEditSamples
{
    public static void Run()
    {
        FirstEditTutorial();
        SendAndRebaseTutorial();
        LayerAndResetTutorial();
        HistoryFlow();
    }

    private static void FirstEditTutorial()
    {
        // sample: tutorial-editsession
        var settings = new UserSettings
        {
            DisplayName = "Alice",
            Theme = "light",
            RefreshInterval = 60,
        };

        // 1. Start an edit session (retains the initial baseline)
        var session = settings.CreateEditSession();

        session.Observable.Theme = "dark";
        Console.WriteLine($"Live Theme: {settings.Theme}, HasChanges: {session.HasChanges}");
        // Output: Live Theme: dark, HasChanges: True
        // /sample
        DocsCheck.Require(session.HasChanges, "session tracks changes");
        DocsCheck.Require(settings.Theme == "dark", "observable writes through to live model");

        // sample: tutorial-changeset
        // 2. Inspect what changed
        var changes = session.CreateChangeSet();

        Console.WriteLine($"Theme changed: {changes.Theme.IsChanged} ({changes.Theme.Before.Value} -> {changes.Theme.After.Value})");
        Console.WriteLine($"RefreshInterval changed: {changes.RefreshInterval.IsChanged}");
        // Output:
        // Theme changed: True (light -> dark)
        // RefreshInterval changed: False
        // /sample
        DocsCheck.Require(changes.Theme.IsChanged, "Theme changed");
        DocsCheck.Require(changes.Theme.Before.Value == "light", "Before preserves old value");
        DocsCheck.Require(changes.Theme.After.Value == "dark", "After preserves new value");
        DocsCheck.Require(!changes.RefreshInterval.IsChanged, "RefreshInterval untouched");

        // sample: tutorial-revert-and-restore
        // 3. Setting the property back to its baseline value clears HasChanges
        session.Observable.Theme = "light";
        Console.WriteLine($"After restoring 'light', HasChanges: {session.HasChanges}");
        // Output: After restoring 'light', HasChanges: False

        // Edit two properties and revert them back to the baseline at once
        session.Observable.Theme = "solarized";
        session.Observable.RefreshInterval = 120;
        session.RevertChanges();
        Console.WriteLine(
            $"After RevertChanges -> Theme: {settings.Theme}, RefreshInterval: {settings.RefreshInterval}, HasChanges: {session.HasChanges}"
        );
        // Output: After RevertChanges -> Theme: light, RefreshInterval: 60, HasChanges: False
        // /sample
        DocsCheck.Require(!session.HasChanges, "RevertChanges clears HasChanges");
        DocsCheck.Require(settings.Theme == "light" && settings.RefreshInterval == 60, "RevertChanges restores baseline values");
    }

    private static void SendAndRebaseTutorial()
    {
        // sample: tutorial-payload
        var clientSettings = new UserSettings
        {
            DisplayName = "Alice",
            Theme = "light",
            RefreshInterval = 60,
        };
        var clientSession = clientSettings.CreateEditSession();
        clientSession.Observable.Theme = "dark";

        // 1. Serialize the transition to a JSON wire payload
        var outgoingChanges = clientSession.CreateChangeSet();
        var payload = outgoingChanges.ToPayload();
        var json = JsonSerializer.Serialize(payload, UserSettings.PayloadJson.Default);
        Console.WriteLine($"Wire JSON: {json}");

        // 2. Deserialize on the server into a typed ChangeSet
        var receivedPayload = JsonSerializer.Deserialize<UserSettings.ChangePayload>(json, UserSettings.PayloadJson.Default)!;
        var incomingChanges = receivedPayload.ToChangeSet();
        Console.WriteLine($"Received Theme change: {incomingChanges.Theme.Before.Value} -> {incomingChanges.Theme.After.Value}");
        // Output:
        // Wire JSON: {"version":"0.1","change":{"kind":"update","members":{"theme":{"kind":"replace","before":{"state":"value","value":"light"},"after":{"state":"value","value":"dark"}}}}}
        // Received Theme change: light -> dark
        // /sample
        DocsCheck.Require(incomingChanges.Theme.IsChanged, "incoming changes preserve Theme edit");
        DocsCheck.Require(incomingChanges.Theme.After.Value == "dark", "incoming changes preserve Theme value");

        // sample: tutorial-rebase
        // 3. Reconcile disjoint concurrent changes
        var serverCurrent = new UserSettings
        {
            DisplayName = "Alice Cooper",
            Theme = "light",
            RefreshInterval = 60,
        };

        var rebaseResult = incomingChanges.RebaseOnto(serverCurrent);
        var merged = rebaseResult.Rebased.ToPatch().ApplyTo(serverCurrent);
        Console.WriteLine($"Disjoint rebase -> HasConflicts: {rebaseResult.HasConflicts}, DisplayName: {merged.DisplayName}, Theme: {merged.Theme}");
        // Output:
        // Disjoint rebase -> HasConflicts: False, DisplayName: Alice Cooper, Theme: dark
        // /sample
        DocsCheck.Require(!rebaseResult.HasConflicts, "rebase has no conflicts");
        DocsCheck.Require(merged.DisplayName == "Alice Cooper" && merged.Theme == "dark", "disjoint rebase preserves both edits");

        // sample: tutorial-rebase-conflict
        // 4. Detect an overlapping edit on the same property
        var serverConflicting = new UserSettings
        {
            DisplayName = "Alice Cooper",
            Theme = "high-contrast",
            RefreshInterval = 60,
        };

        var conflictResult = incomingChanges.RebaseOnto(serverConflicting);
        if (conflictResult.HasConflicts)
        {
            var conflict = conflictResult.Conflicts[0];
            Console.WriteLine(
                $"Conflict at {conflict.PathText}: base={conflict.BaseValue.Value}, local={conflict.LocalValue.Value}, current={conflict.CurrentValue.Value}"
            );
        }
        // Output:
        // Conflict at Theme: base=light, local=dark, current=high-contrast
        // /sample
        DocsCheck.Require(conflictResult.HasConflicts, "overlapping edit reports conflict");
        DocsCheck.Require(conflictResult.Conflicts[0].PathText == "Theme", "conflict identifies Theme");
    }

    private static void LayerAndResetTutorial()
    {
        // sample: tutorial-fragment-patch
        // 1. Define a default layer and a user override layer
        var defaultLayer = new UserSettings.Fragment
        {
            DisplayName = "Guest",
            Theme = "light",
            RefreshInterval = 30,
        };

        // RefreshInterval is omitted (Missing), DisplayName is explicitly null, and Theme is "dark"
        var userOverride = new UserSettings.Fragment { DisplayName = (string?)null, Theme = "dark" };

        var effective = defaultLayer.Merge(userOverride);
        Console.WriteLine(
            $"Merged -> DisplayName present: {effective.DisplayName.IsPresent} (value: {effective.DisplayName.Value ?? "null"}), Theme: {effective.Theme.Value}, RefreshInterval: {effective.RefreshInterval.Value}"
        );
        // Output:
        // Merged -> DisplayName present: True (value: null), Theme: dark, RefreshInterval: 30
        // /sample
        DocsCheck.Require(effective.DisplayName.IsPresent && effective.DisplayName.Value is null, "explicit null overrides default");
        DocsCheck.Require(effective.Theme.Value == "dark", "override wins");
        DocsCheck.Require(effective.RefreshInterval.Value == 30, "missing falls through");

        // sample: tutorial-fragment-remove-and-materialize
        // 2. Remove the user's Theme override so the default layer shows through again
        var resetTheme = new UserSettings.Patch();
        resetTheme.Theme.Remove();

        var updatedOverride = userOverride.Apply(resetTheme);
        // updatedOverride.Theme.IsPresent == false

        var revealed = defaultLayer.Merge(updatedOverride);
        var finalSettings = revealed.ToModel();
        Console.WriteLine($"After Remove -> Theme: {finalSettings.Theme}, RefreshInterval: {finalSettings.RefreshInterval}");
        // Output:
        // After Remove -> Theme: light, RefreshInterval: 30
        // /sample
        DocsCheck.Require(!updatedOverride.Theme.IsPresent, "Remove clears Theme override");
        DocsCheck.Require(finalSettings.Theme == "light" && finalSettings.RefreshInterval == 30, "lower layer Theme revealed after Remove");
    }

    private static void HistoryFlow()
    {
        var probe = new UserSettings { Theme = "light" }.CreateEditSession();
        DocsCheck.RequireThrowsInvalidOperation(() => _ = probe.History, "history off by default");

        // sample: tutorial-history
        var historyModel = new UserSettings { Theme = "light", RefreshInterval = 60 };
        var historySession = historyModel.CreateEditSession();
        var history = historySession.EnableHistory();

        historySession.Observable.Theme = "dark";
        historySession.Observable.RefreshInterval = 120;
        // history.CanUndo == true

        history.Undo();
        // historyModel.RefreshInterval == 60 (RefreshInterval edit undone; Theme remains "dark")

        history.Redo();
        // historyModel.RefreshInterval == 120 (RefreshInterval edit restored)
        // /sample
        DocsCheck.Require(history.CanUndo, "edit retained in history");
        history.Undo();
        DocsCheck.Require(historyModel.RefreshInterval == 60 && historyModel.Theme == "dark", "undo restores latest step");
        history.Redo();
        DocsCheck.Require(historyModel.RefreshInterval == 120, "redo reapplies edit");
        historySession.DisableHistory();
        DocsCheck.RequireThrowsInvalidOperation(() => _ = historySession.History, "disable releases history");
    }
}

// sample: tutorial-models
[SparseModel]
public partial class UserSettings
{
    public string? DisplayName { get; set; }

    public string? Theme { get; set; }

    public int RefreshInterval { get; set; }
}

// /sample
