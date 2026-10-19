using SparseTypes;

static void Require(bool condition, string capability)
{
    if (!condition)
    {
        throw new InvalidOperationException("Failed: " + capability);
    }
}

// sample: tutorial-program
// 1. Start an edit session (retains the initial baseline)
var settings = new UserSettings
{
    DisplayName = "Alice",
    Theme = "light",
    RefreshInterval = 60,
};
var session = settings.CreateEditSession();

session.Observable.Theme = "dark";
Console.WriteLine($"Live Theme: {settings.Theme}, HasChanges: {session.HasChanges}");
Require(settings.Theme == "dark" && session.HasChanges, "edit writes through and marks session dirty");

// 2. Inspect what changed
var changes = session.CreateChangeSet();
Console.WriteLine(
    $"Theme changed: {changes.Theme.IsChanged} ({changes.Theme.Before.Value} -> {changes.Theme.After.Value})"
);
Console.WriteLine($"RefreshInterval changed: {changes.RefreshInterval.IsChanged}");
Require(changes.Theme.IsChanged, "ChangeSet detects the Theme edit");
Require(changes.Theme.Before.Value == "light", "ChangeSet retains the old value");
Require(changes.Theme.After.Value == "dark", "ChangeSet retains the new value");
Require(!changes.RefreshInterval.IsChanged, "unchanged property is not flagged");

// 3. Setting the property back to its baseline value clears HasChanges
session.Observable.Theme = "light";
Console.WriteLine($"After restoring 'light', HasChanges: {session.HasChanges}");
Require(!session.HasChanges, "restoring baseline value clears HasChanges");

// Edit two properties and revert them back to the baseline at once
session.Observable.Theme = "solarized";
session.Observable.RefreshInterval = 120;
session.RevertChanges();
Console.WriteLine(
    $"After RevertChanges -> Theme: {settings.Theme}, RefreshInterval: {settings.RefreshInterval}, HasChanges: {session.HasChanges}"
);
Require(
    settings.Theme == "light" && settings.RefreshInterval == 60 && !session.HasChanges,
    "RevertChanges restores baseline values and clears HasChanges"
);

[SparseModel]
public partial class UserSettings
{
    public string? DisplayName { get; set; }
    public string? Theme { get; set; }
    public int RefreshInterval { get; set; }
}
// /sample
