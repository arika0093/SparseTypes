using System.Text.Json;
using SparseTypes;

// Canonical compile-checked mirror of the root README.md.
// The `// sample: <id>` regions below match the same-id fenced blocks in
// README.md exactly after normalization; verify-sparsetypes-readme.sh
// fails on drift. Regions execute as top-level statements so documented
// results are verified, not just compiled. Keep the model shapes (Settings
// with [SparseModel], reachable partial DatabaseSettings) in sync
// with the README so CI fails when the public generated API drifts from the
// documented samples.

// Quick Start: reactive editing through EditSession, inspecting changes, and reverting.
// sample: readme-quickstart
var settings = new Settings
{
    Label = "default",
    Database = new() { Host = "db.local", Port = 5432 },
};

// 1. Start an edit session (captures the initial baseline)
var session = settings.CreateEditSession();

// 2. Edit through the observable proxy (writes through to settings)
session.Observable.Label = "production";
session.Observable.Database!.Port = 7432;

// 3. Inspect dirty state and typed transitions
var changes = session.CreateChangeSet();
Console.WriteLine(settings.Label);      // production
Console.WriteLine(session.HasChanges);  // True
Console.WriteLine(changes.Label.IsChanged);    // True
Console.WriteLine(changes.Database.IsChanged); // True
Console.WriteLine(changes.Database.Port.Before.Value); // 5432
Console.WriteLine(changes.Database.Port.After.Value);  // 7432
// Enumerate all changed locations
foreach (var change in changes.EnumerateChanges()) {
    Console.WriteLine(
        $"* {change.Path,-14} changed from {change.Before.Value,10} to {change.After.Value,10}");
    // Output:
    // * Database.Port  changed from       5432 to       7432
    // * Label          changed from    default to production
}

// 4. Revert edits back to the session baseline
session.RevertChanges();
Console.WriteLine(session.HasChanges); // False
Console.WriteLine(settings.Label);     // default

// /sample
var enumeratedChanges = changes.EnumerateChanges().ToArray();
Require(changes.Database.IsChanged, "database transition reports a change");
Require(
    enumeratedChanges.Select(change => change.Path.ToString()).SequenceEqual(new[] { "Database.Port", "Label" }),
    "changed locations are enumerated in the documented order"
);
Require(
    enumeratedChanges[0].Before.Value?.ToString() == "5432"
        && enumeratedChanges[0].After.Value?.ToString() == "7432"
        && enumeratedChanges[1].Before.Value?.ToString() == "default"
        && enumeratedChanges[1].After.Value?.ToString() == "production",
    "enumerated changes preserve before/after values"
);
Require(!session.HasChanges, "session reverted has no changes");
Require(settings.Label == "default", "revert restored Label");

// Re-apply edit for wire transport demonstration
session.Observable.Label = "production";
session.Observable.Database!.Port = 7432;
var outgoing = session.CreateChangeSet();

// Client/Server Edits: the typed payload crosses process boundaries as ordinary
// System.Text.Json with the generated payload metadata.
var json = JsonSerializer.Serialize(outgoing.ToPayload(), Settings.PayloadJson.Default);
var incoming = JsonSerializer.Deserialize<Settings.ChangePayload>(json, Settings.PayloadJson.Default)!.ToChangeSet();
Require(incoming.Label.IsChanged, "ChangeSet payload JSON round-trip preserves transitions");
var updated = Settings.Fragment.From(settings).Apply(incoming.ToPatch());
var rebased = incoming.RebaseOnto(updated);
Require(!rebased.HasConflicts, "rebase onto current has no conflicts");

Console.WriteLine("SparseTypes README consumer passed.");

static void Require(bool condition, string capability)
{
    if (!condition)
    {
        throw new InvalidOperationException("Failed: " + capability);
    }
}

// Mirrors the README Quick Start model. Reachable partial nested models
// auto-generate Fragment/Patch, so DatabaseSettings stays undecorated but
// partial because the samples construct DatabaseSettings.Fragment directly.
// sample: readme-quickstart
// ---------
[SparseModel]
public partial class Settings
{
    public string? Label { get; set; }
    public DatabaseSettings? Database { get; set; }
}

public partial class DatabaseSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
}
// /sample
