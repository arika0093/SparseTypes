using System.Text;
using System.Text.Json;
using SparseTypes;

Console.WriteLine(
    TerminalStyle.Title("*** ConsoleTour: a complete editing session with SparseTypes")
);
Console.WriteLine(
    TerminalStyle.Comment("// Each section explains the operation before showing its result.")
);

Console.WriteLine();
Console.WriteLine(TerminalStyle.Step(">> 1. Merge defaults with user-specific overrides."));
var defaults = new UserPreferences.Fragment
{
    DisplayName = "Guest",
    Theme = "light",
    RefreshInterval = 30,
    Notifications = new NotificationPreferences.Fragment { EmailEnabled = true },
};
var userOverrides = new UserPreferences.Fragment { DisplayName = "Alex", Theme = "dark" };
var defaultValues = defaults.ToModel();
var overrideValues = userOverrides.ToModel();
var effective = defaults.Merge(userOverrides).ToModel();
Console.WriteLine(
    TerminalStyle.Value(
        $"Defaults: {defaultValues.DisplayName}, {defaultValues.Theme}, refresh every {defaultValues.RefreshInterval}s"
    )
);
Console.WriteLine(
    TerminalStyle.Value($"Overrides: {overrideValues.DisplayName}, {overrideValues.Theme}")
);
Console.WriteLine(
    TerminalStyle.Value(
        $"Effective settings: {effective.DisplayName}, {effective.Theme}, refresh every {effective.RefreshInterval}s"
    )
);

Console.WriteLine();
Console.WriteLine(TerminalStyle.Step(">> 2. Edit the effective settings through an EditSession."));
Console.WriteLine(
    TerminalStyle.Comment("// The session tracks changes while the observable model is edited.")
);
var draft = effective.DeepClone();
var session = draft.CreateEditSession();
var history = session.EnableHistory();
Console.WriteLine(
    $"Dirty before editing: {TerminalStyle.State(session.HasChanges ? "yes" : "no", session.HasChanges)}"
);

Console.WriteLine(TerminalStyle.Comment("// These three edits are grouped as one history entry."));
history.Group(() =>
{
    session.BatchEdit(() =>
    {
        session.Observable.Theme = "high-contrast";
        session.Observable.RefreshInterval = 15;
        session.Observable.Notifications!.EmailEnabled = false;
    });
});

Console.WriteLine(
    $"Dirty after editing: {TerminalStyle.State(session.HasChanges ? "yes" : "no", session.HasChanges)}"
);
Console.WriteLine(TerminalStyle.Emphasis("Changes recorded by the session:"));
foreach (var change in session.CreateChangeSet().EnumerateChanges())
{
    Console.WriteLine(
        $"  {change.PathText}: {TerminalStyle.Before(change.Before.Value)} -> {TerminalStyle.After(change.After.Value)}"
    );
}

Console.WriteLine();
Console.WriteLine(TerminalStyle.Step(">> 3. Undo and redo the grouped edit."));
history.Undo();
Console.WriteLine(
    $"After undo, the draft is {TerminalStyle.State(session.HasChanges ? "dirty" : "clean", !session.HasChanges)}."
);
history.Redo();
Console.WriteLine(
    $"After redo, the draft is {TerminalStyle.State(session.HasChanges ? "dirty" : "clean", session.HasChanges)}."
);

Console.WriteLine();
Console.WriteLine(
    TerminalStyle.Step(">> 4. Serialize the changes as a ChangePayload, then read it back.")
);
Console.WriteLine(TerminalStyle.Comment("// This is the payload a client could send to an API."));
var submitted = session.CreateChangeSet();
var payloadTypeInfo = UserPreferences.PayloadJson.Default;
using var jsonStream = new MemoryStream();
using (var writer = new Utf8JsonWriter(jsonStream, new JsonWriterOptions { Indented = true }))
{
    JsonSerializer.Serialize(writer, submitted.ToPayload(), payloadTypeInfo);
}
var json = Encoding.UTF8.GetString(jsonStream.ToArray());
var incoming =
    JsonSerializer.Deserialize(json, payloadTypeInfo)?.ToChangeSet()
    ?? throw new InvalidOperationException("The change payload could not be read.");
Console.WriteLine(json);

Console.WriteLine();
Console.WriteLine(TerminalStyle.Step(">> 5. Rebase those changes onto a newer server copy."));
Console.WriteLine(
    TerminalStyle.Comment(
        "// The server changed DisplayName while this draft changed other fields."
    )
);
var latestServerState = effective.DeepClone();
latestServerState.DisplayName = "Alex Kim";
Console.WriteLine(TerminalStyle.Value($"Latest server name: {latestServerState.DisplayName}"));
var rebase = incoming.RebaseOnto(latestServerState);
if (rebase.HasConflicts)
{
    throw new InvalidOperationException($"Unexpected conflict at {rebase.Conflicts[0].PathText}.");
}

var merged = rebase.Rebased.ToPatch().ApplyTo(latestServerState);
Console.WriteLine(
    TerminalStyle.Value(
        $"Rebased result: {merged.DisplayName}, {merged.Theme}, refresh every {merged.RefreshInterval}s"
    )
);
Console.WriteLine(
    TerminalStyle.Comment(
        "// The server's name is kept, while the draft's non-conflicting edits are applied."
    )
);

Console.WriteLine();
Console.WriteLine(
    TerminalStyle.Step(">> 6. Remove an override so the default value takes effect again.")
);
Console.WriteLine(
    TerminalStyle.Before($"Theme before removing the override: {overrideValues.Theme}")
);
var resetTheme = new UserPreferences.Patch();
resetTheme.Theme.Remove();
var preferencesWithoutTheme = userOverrides.Apply(resetTheme);
var restoredDefaults = defaults.Merge(preferencesWithoutTheme).ToModel();
Console.WriteLine(
    TerminalStyle.After($"Theme after removing the override: {restoredDefaults.Theme}")
);
