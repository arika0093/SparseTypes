# Collection editors

Package and namespace: `SparseTypes`. Editors provide path-addressed collection mutation through session descriptors. Each operation resolves the current backing collection and batches its notifications.

## Obtaining an editor

Generated sessions expose `session.EditCollection<TElement>(collectionPath)` and `session.EditCollection<TElement,TKey>(collectionPath)`. The second returns `SparseKeyedCollectionEditor<TElement,TKey>`.

For adapters, both classes have the constructor `(IDescriptorSet descriptors, SparsePath path, Action<Action> batch)`. Equivalent descriptor extensions are `EditCollection<TElement>(path, batch)` and `EditKeyedCollection<TElement,TKey>(path, batch)` on `SparseCollectionEditors`. `IDescriptorSet` is in `SparseTypes.Generated`.

These editors require a single-member collection path, such as `session.Paths.Items`. Constructor dependencies must be non-null; invalid path shape throws ArgumentException. They are not arbitrary nested-path mutation engines.

## SparseCollectionEditor<TElement>

| Member | Contract |
| --- | --- |
| `Path` | Addressed member. |
| `Count` | Current item count. Use IsAvailable to distinguish an unavailable collection from an empty one. |
| `CanAdd`, `CanInsert`, `CanMove`, `CanRemove`, `CanReplace` | Capabilities of the current backing collection. |
| `IsKeyed`, `KeyType`, `KeyPropertyNames` | Declared identity metadata. |
| `IsAvailable(out error)` | Resolves a supported, non-null collection. |
| `TryGetAt(int index, out TElement? value, out error)` | Reads the model element. |
| `TryAdd(TElement? item, out error)` | Appends an item. |
| `TryInsert(int index, TElement? item, out error)` | Inserts an item. |
| `TryReplace(int index, TElement? item, out error)` | Replaces one item. |
| `TryRemoveAt(int index, out error)` | Removes one item. |
| `TryMove(int oldIndex, int newIndex, out error)` | Moves an item. |

All `error` outputs above use `SparseCollectionEditError`. Protected `Batched(Action)` and `TryResolveArray(out IArrayDescriptor, out error)` support derived editors.

## SparseKeyedCollectionEditor<TElement,TKey>

Includes the base members and:

| Member | Contract |
| --- | --- |
| `ContainsKey(TKey? key)` | Whether assigned identity resolves. |
| `IndexOfKey(TKey? key)`, `IndexOfTemporaryKey(Guid temporaryKey)` | Current position, or -1 if not found. |
| `IsUnassignedKey(TKey? key)` | Whether the key is the declared sentinel. |
| `TryGetKeyAt(int index, out TKey? key, out error)` | Reads the key at a position. |
| `TryGetByKey(TKey? key, out TElement? value, out error)` | Finds a stable assigned row. |
| `TryGetByTemporaryKey(Guid temporaryKey, out TElement? value, out error)` | Finds an unassigned row by temporary identity. |
| `TryMoveByKey(TKey? key, int newIndex, out error)` | Moves the resolved row. |
| `TryRemoveByKey(TKey? key, out error)` | Removes the resolved row. |
| `TryRemoveByTemporaryKey(Guid temporaryKey, out error)` | Removes an unassigned row by temporary identity. |
| `TryReplaceByKey(TKey? key, TElement? item, out error)` | Replaces the resolved row. |

Unassigned sentinels are not stable lookup identities. Use temporary-key methods for rows awaiting server-assigned keys.

## Failure contract

Try methods return false with a structured error for rejected edits and leave the collection unchanged. Errors are `UnknownCollection`, `NotACollection`, `NullCollection`, `NotMutable`, `IndexOutOfRange`, `DuplicateKey`, `UnknownKey`, `UnassignedKey`, `UnknownTemporaryKey`, or `InvalidItem`; success uses `None`. Programmer errors, including using a keyed editor with an incompatible key type/shape, and unexpected provider exceptions propagate.

Capabilities can change when the backing property is replaced. Editors re-resolve it rather than retaining an accessor to a detached collection. A single editor mutation runs inside one batch; raw model mutations bypass this notification route.

## Example

Uses the [reference models](attributes.md#example-model).

<!-- sample: reference-collection -->
```csharp
var session = new ReferenceSettings().CreateEditSession();
var editor = session.EditCollection<ReferenceItem, string>(session.Paths.Items);
var added = editor.TryAdd(new ReferenceItem { Id = "a", Title = "first" }, out var addError);
var duplicate = editor.TryAdd(new ReferenceItem { Id = "a" }, out var duplicateError);
var found = editor.TryGetByKey("a", out var item, out var lookupError);
```
<!-- /sample -->

`added` and `found` are true. `duplicate` is false with `DuplicateKey`; the original row remains unchanged.

See [descriptors](descriptors.md) for supported collection shapes and [keyed collections](../explanation/keyed-collections.md) for identity rules.
