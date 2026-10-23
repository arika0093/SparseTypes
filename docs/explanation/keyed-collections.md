# Keyed Collections

SparseTypes patches a structural list element by element only when each element has a stable key. Array positions are not stable identity: inserting an item at the front shifts every later index even though those existing items are the same logical objects.

This page explains how SparseTypes distinguishes atomic collections from keyed collections, how `[SparseKey]` defines identity, how additions, removals, edits, and reorders work, and how `[SparseTemporaryKey]` identifies client-created rows before the database assigns their permanent key. For key declaration rules and diagnostics, see [Model shapes](../reference/model-shapes.md#keyed-collection-identity-constraints).

<!-- sample: keyed-first-models -->
```csharp
using SparseTypes;

[SparseModel]
public partial class Fleet
{
    public List<Server> Servers { get; set; } = new();
}

public partial class Server
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;
}
```
<!-- /sample -->

<!-- sample: keyed-first -->
```csharp
var before = new Fleet
{
    Servers = new()
    {
        new Server { Id = "a", Host = "old" },
    },
};
var after = new Fleet
{
    Servers = new()
    {
        new Server { Id = "a", Host = "new" },
        new Server { Id = "b" },
    },
};

var changes = before.CreateChangeSet(after); // add/remove/edit by key
if (!changes.TryApplyTo(before, out var applied))
{
    throw new InvalidOperationException("The keyed changes conflict.");
}

// applied.Servers.Count == 2
// applied.Servers.Single(s => s.Id == "a").Host == "new"
```
<!-- /sample -->

## Atomic vs Keyed Collections

The collection kind determines how patches apply to its elements:

| Collection kind | Examples | Patch semantics |
| --- | --- | --- |
| Scalar/atomic sequence | `List<string>`, `int[]` | Whole value: a patch sets or removes the entire collection |
| Dictionary/map | `Dictionary<string, int>` | Keyed by `TKey` inherently; entries patch by key unless explicitly configured as `Replace` |
| Structural sequence **with** a key | `List<Server>` where `Server` declares `[SparseKey]` | Keyed: add/remove/edit by element, reorder by key order |
| Structural sequence **without** a key | `List<Server>` with no key declared | Generator error (`SPF011`): declare a key, or explicitly opt into `Replace`, `Append`, `SetUnion`, or a custom strategy on the member |

Choose keyed patching when clients edit, add, remove, or reorder individual items and the server must reconcile those edits against concurrent state: the `ChangeSet` carries per-key operations that rebase element-wise. Choose whole replacement (`Replace`, `Append`, `SetUnion`, or a custom strategy) when the collection is written as a single value, such as an append-only log or a computed snapshot.

<!-- illustrative: shape illustration; shown without surrounding file context and does not compile as written -->
```csharp
[SparseModel]
public partial class Settings
{
    [SparseMerge(MergeMode.Append)]
    public List<AuditEntry> History { get; set; } = new();
}
```

## Declaring Identity

Mark one publicly readable instance property per element type with `[SparseKey]`. That property's type is the key type, compared with `EqualityComparer<TKey>.Default`.

### Ordinary scalar keys and composite keys

The `Server` model above uses a single writable `Id` property. A composite key is expressed as a single computed property whose type holds every component, such as a value tuple or a `readonly record struct`:

<!-- sample: keyed-composite-model -->
```csharp
using SparseTypes;

[SparseModel]
public partial class KeyedOrder
{
    public List<KeyedOrderLine> Lines { get; set; } = new();
}

public partial class KeyedOrderLine
{
    public string TenantId { get; set; } = string.Empty;

    public int LineNumber { get; set; }

    public string Name { get; set; } = string.Empty;

    [SparseKey]
    public (string TenantId, int LineNumber) Key => (TenantId, LineNumber);
}

public readonly record struct KeyedSku
{
    public string Code { get; init; }
}

public partial class KeyedProduct
{
    public string RawCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    [SparseKey]
    public KeyedSku Key => new() { Code = RawCode.ToUpperInvariant() };
}

[SparseModel]
public partial class KeyedCatalog
{
    public List<KeyedProduct> Products { get; set; } = new();
}
```
<!-- /sample -->

Comparison applies to the whole key value, so `(TenantId, LineNumber)` and `(LineNumber, TenantId)` are distinct key shapes. A computed value-object key such as `KeyedSku` lets you normalize identity (for example, case-folding `RawCode`) while keeping the underlying business properties editable. See [Model shapes](../reference/model-shapes.md#keyed-collection-identity-constraints) for the full list of compile-time key constraints (`SPF011`-`SPF024`, `SPF031`-`SPF032`).

## Add, Remove, Edit, and Reorder

`before.CreateChangeSet(after)` derives per-element operations by comparing the `before` and `after` key sets, and `changes.ToPatch().ApplyTo(before)` replays them onto a model (after which `applied.CreateChangeSet(after).IsEmpty` is `true`):

* **Add**: An element whose key exists only in `after` is added with its full fragment state.
* **Remove**: An element whose key exists only in `before` is removed by key.
* **Edit**: An element present in both is diffed through its generated nested `ChangeSet`, so only modified properties travel.
* **Reorder**: The resulting sequence order is determined by the final key order (`AfterOrder`). Reversing `["a", "b"]` to `["b", "a"]` produces a non-empty transition with `OrderChanged == true`.

### Avoiding unintended order changes

Because element order is part of collection state, sorting a list in place for UI display produces a change set with `OrderChanged == true`. To avoid recording view-only sorting as a data change, either sort in a presentation projection (such as `.OrderBy(...)`) without mutating the model list, or normalize the list order by a persisted sort property before calling `CreateChangeSet()`.

### Duplicate keys and key changes

* **Duplicate keys are invalid**: A collection containing the same key twice has no well-defined identity; diffing or applying against it throws `InvalidOperationException`.
* **Changing a key is remove + add**: Modifying the `[SparseKey]` property of an existing element from `"a"` to `"b"` records the removal of `"a"` and the addition of `"b"`. Keep `[SparseKey]` on an immutable surrogate identifier (such as a database ID or GUID) and put editable business codes on regular properties.

## Observe Typed Collection Transitions

A keyed collection transition exposes typed projections (`Added`, `Removed`, `Edited`, `BeforeOrder`, `AfterOrder`, `OrderChanged`, and `GetChange(key)`) and enumerates changed items:

<!-- sample: keyed-typed -->
```csharp
var before = new Fleet
{
    Servers = new()
    {
        new Server { Id = "a", Host = "A" },
        new Server { Id = "b", Host = "B" },
    },
};
var after = new Fleet
{
    Servers = new()
    {
        new Server { Id = "b", Host = "B2" },
        new Server { Id = "c", Host = "C" },
    },
};

var changes = before.CreateChangeSet(after);
var servers = changes.Servers;
// servers.Added.Single().Id == "c"
// servers.Removed.Single().Id == "a"
// servers.Edited["b"].Host.After.Value == "B2"
// servers.BeforeOrder.SequenceEqual(["a", "b"])
// servers.AfterOrder.SequenceEqual(["b", "c"])
// servers.OrderChanged == true
foreach (var item in servers)
{
    if (item.IsEdited)
    {
        Console.WriteLine(item.Edit.Host.IsChanged);
    }
}
var edited = servers.GetChange("b");
// edited.IsEdited == true
// edited.Edit.Host.After.Value == "B2"
```
<!-- /sample -->

Generic consumers that operate across model types without naming generated classes can inspect the same transition through `ISparseChangeSet.TryGetKeyedChange<TKey, TElement, TElementChange>` and `ISparseTemporaryKeyedChange<TKey, TElement, TElementChange>`:

<!-- sample: keyed-transition-contracts -->
```csharp
var before = new Fleet
{
    Servers = new()
    {
        new Server { Id = "a", Host = "A" },
        new Server { Id = "b", Host = "B" },
    },
};
var after = new Fleet
{
    Servers = new()
    {
        new Server { Id = "b", Host = "B2" },
        new Server { Id = "c", Host = "C" },
    },
};

// The same transition through the shared keyed contract.
ISparseChangeSet changes = before.CreateChangeSet(after);
if (changes.TryGetKeyedChange<string, Server, Server.ChangeSet>("Servers", out var servers))
{
    // servers.Added.Single().Id == "c"
    // servers.GetChange("b").Edit.Host.After.Value == "B2"
    foreach (var item in servers.EnumerateItems())
    {
        Console.WriteLine($"{item.Key}: added={item.IsAdded}, edited={item.IsEdited}");
    }
}

var pendingId = Guid.NewGuid();
var pendingBefore = new DocsTempOrder();
var pendingAfter = new DocsTempOrder
{
    Lines = new()
    {
        new DocsTempLine { Name = "new", TemporaryId = pendingId },
    },
};
ISparseChangeSet pending = pendingBefore.CreateChangeSet(pendingAfter);
```
<!-- /sample -->

## Unassigned Keys

When the database assigns primary keys on insert, newly added client rows do not yet have a permanent ID. Declare an unassigned sentinel on `[SparseKey(Unassigned = ...)]` (or use a nullable key type where `null` is the sentinel):

<!-- sample: keyed-unassigned-model -->
```csharp
[SparseModel]
public partial class PendingFleet
{
    public List<PendingServer> Servers { get; set; } = new();
}

public partial class PendingServer
{
    [SparseKey(Unassigned = 0)]
    public int Id { get; set; }

    public string Host { get; set; } = string.Empty;
}
```
<!-- /sample -->

Every element in `after` with `Id == 0` is treated as a new addition. Without `[SparseTemporaryKey]`, an unassigned sentinel cannot appear in a baseline collection (`before`), because multiple `Id == 0` rows cannot be distinguished from one another on subsequent diffs. After sending the change set and saving on the server, start a fresh edit session from the persisted state returned by the server:

<!-- sample: keyed-unassigned-flow -->
```csharp
var before = new PendingFleet { Servers = new() { new PendingServer { Id = 4 } } };
var after = new PendingFleet
{
    Servers = new()
    {
        new PendingServer { Id = 0, Host = "client-1" },
        new PendingServer { Id = 4, Host = "saved" },
        new PendingServer { Id = 0, Host = "client-2" },
    },
};
var changes = before.CreateChangeSet(after);
// Send changes.ToPayload() to the server; the server inserts the rows,
// assigns database IDs, and may normalize or reorder them.
// Do not acknowledge the unassigned transition with AcceptChanges.
// Replace with the authoritative state and start a fresh session:
var persisted = new PendingFleet
{
    Servers = new()
    {
        new PendingServer { Id = 11, Host = "client-1" },
        new PendingServer { Id = 4, Host = "saved" },
        new PendingServer { Id = 12, Host = "client-2" },
    },
};
var session = persisted.CreateEditSession();
// session.HasChanges == false
```
<!-- /sample -->

## Temporary Identity for Pending Additions

When newly added rows must remain individually editable, reorderable, or addressable by path before the database assigns their permanent key, add a `Guid?` property marked with `[SparseTemporaryKey]` alongside `[SparseKey(Unassigned = ...)]`:

<!-- sample: keyed-temporary-model -->
```csharp
using SparseTypes;

[SparseModel]
public partial class KeyedOrderBook
{
    public List<KeyedOrderLineDto> Lines { get; set; } = new();
}

public partial class KeyedOrderLineDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [SparseKey(Unassigned = 0)]
    public int Key => Id;

    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }
}
```
<!-- /sample -->

Assign `TemporaryId = Guid.NewGuid()` when creating a new row on the client. While `Id == 0`, the temporary `Guid` identifies the element across `AcceptChanges`, `Fork`, `ChangePayload` serialization, typed transitions (`GetTemporaryChange(Guid)`), and typed paths (`.TemporaryKey(Guid)`). Once `Id` is non-zero, the permanent key takes precedence and `TemporaryId` is ignored for equality and diffing:

<!-- sample: keyed-temporary-flow -->
```csharp
var book = new KeyedOrderBook
{
    Lines = new()
    {
        new KeyedOrderLineDto { Id = 7, Name = "saved" },
    },
};
var session = book.CreateEditSession();

var first = new KeyedOrderLineDto { Name = "first", TemporaryId = Guid.NewGuid() };
var second = new KeyedOrderLineDto { Name = "second", TemporaryId = Guid.NewGuid() };
book.Lines.Add(first);
book.Lines.Add(second);

var pending = session.CreateChangeSet();
// pending.Lines.GetTemporaryChange(first.TemporaryId!.Value).IsAdded == true
// pending.Lines.GetTemporaryChange(second.TemporaryId!.Value).IsAdded == true

session.AcceptChanges(pending);

book.Lines.Single(item => item.TemporaryId == second.TemporaryId).Name = "second v2";
var edited = session.CreateChangeSet();
// edited.Lines.GetTemporaryChange(second.TemporaryId!.Value).IsEdited == true

var path = KeyedOrderBook.SparsePath.Lines.TemporaryKey(first.TemporaryId!.Value).Name;
// path addresses the pending row by its temporary Guid, never by position

var wire = JsonSerializer.Serialize(
    pending.ToPayload(),
    KeyedOrderBook.PayloadJson.Default);
var restored = JsonSerializer
    .Deserialize<KeyedOrderBook.ChangePayload>(wire, KeyedOrderBook.PayloadJson.Default)!
    .ToChangeSet();
// restored.Lines.GetTemporaryChange(first.TemporaryId!.Value).IsAdded == true
```
<!-- /sample -->

## Saving Pending Additions with Reconcile

When the user continues editing while a save request containing new `[SparseTemporaryKey]` rows is in flight, acknowledge the server response with `session.TryReconcile(submitted, persisted, out reconcileError)` (or `TryReconcileAndApply` if server-normalized property values should also be adopted). `TryReconcile` correlates each submitted unassigned row with the persisted row that echoes the same `TemporaryId`, writes the assigned permanent `Id` into the live model, adopts `persisted` as the baseline, and preserves post-submit edits:

<!-- sample: keyed-reconcile-flow -->
```csharp
var draft = new KeyedOrderBook
{
    Lines = new()
    {
        new KeyedOrderLineDto { Id = 7, Name = "saved" },
    },
};
var edit = draft.CreateEditSession();

var added = new KeyedOrderLineDto { Name = "new", TemporaryId = Guid.NewGuid() };
draft.Lines.Add(added);
var submitted = edit.CreateChangeSet();

// Editing continues while the save is in flight: rename the pending
// row and add one more row.
draft.Lines.Single(item => item.TemporaryId == added.TemporaryId).Name = "new v2";
var late = new KeyedOrderLineDto { Name = "late", TemporaryId = Guid.NewGuid() };
draft.Lines.Add(late);

// The server inserts the submitted row, assigns Id 11, and echoes the
// temporary Guid on the ordinary response DTO.
var persisted = new KeyedOrderBook
{
    Lines = new()
    {
        new KeyedOrderLineDto { Id = 7, Name = "SAVED" },
        new KeyedOrderLineDto
        {
            Id = 11,
            Name = "NEW",
            TemporaryId = added.TemporaryId,
        },
    },
};

if (!edit.TryReconcile(submitted, persisted, out var reconcileError))
{
    throw new InvalidOperationException(reconcileError);
}

// The post-submit rename replays onto the assigned row: local field
// values win while the server contributes the identity.
// draft.Lines.Single(item => item.TemporaryId == added.TemporaryId).Id == 11
// edit.HasChanges == true: the late row stays pending with its own Guid
```
<!-- /sample -->

### Server mapping: echoing TemporaryId on response DTOs

Your database entity does not need a `TemporaryId` column. Record the `(TemporaryId, Entity)` pair in memory when mapping incoming added DTOs to entities, save to assign database IDs, and copy each `TemporaryId` back onto the response DTO matching the assigned `Id`:

<!-- sample: keyed-server-models -->
```csharp
public sealed class KeyedLineEntity
{
    public int Id;

    public string Name = string.Empty;
}
```
<!-- /sample -->

<!-- sample: keyed-server-mapping -->
```csharp
var requested = new KeyedOrderBook
{
    Lines = new()
    {
        new KeyedOrderLineDto { Id = 7, Name = "saved" },
        new KeyedOrderLineDto { Name = "new", TemporaryId = Guid.NewGuid() },
    },
};

// The server maps each DTO line onto a tracked entity. The entity
// carries no TemporaryId; the pair list holds the correlation.
var tracked = new List<(Guid TemporaryId, KeyedLineEntity Entity)>();
var entities = requested
    .Lines.Select(dto =>
    {
        var entity = new KeyedLineEntity { Id = dto.Id, Name = dto.Name };
        if (entity.Id == 0 && dto.TemporaryId.HasValue)
        {
            tracked.Add((dto.TemporaryId.Value, entity));
        }
        return entity;
    })
    .ToList();

// SaveChanges assigns the permanent keys.
var nextId = 11;
foreach (var entity in entities)
{
    if (entity.Id == 0)
    {
        entity.Id = nextId++;
    }
}

// The response maps entities back onto the ordinary DTO shape and
// restores each new row's temporary Guid by its assigned key. The
// lookup is keyed by assigned permanent key, so the server may
// re-query before building the response.
var savedById = tracked.ToDictionary(pair => pair.Entity.Id, pair => pair.TemporaryId);
var response = new KeyedOrderBook
{
    Lines = entities
        .Select(entity => new KeyedOrderLineDto
        {
            Id = entity.Id,
            Name = entity.Name,
            TemporaryId = savedById.TryGetValue(entity.Id, out var temp) ? temp : null,
        })
        .ToList(),
};
// response.Lines.Single(line => line.Id == 11).TemporaryId == requested.Lines[1].TemporaryId
```
<!-- /sample -->

When using `SparseTypes.EntityFrameworkCore`, `mapper.ApplyWithKeyCorrelation(...)` automates this correlation for tracked EF Core navigations; see [EF Core mapping](../how-to/efcore-mapping.md).
