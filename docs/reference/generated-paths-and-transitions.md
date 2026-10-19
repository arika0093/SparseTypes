# Generated paths and transitions

Generated APIs follow the declared member shape. A scalar transition, nested model transition, and keyed collection transition have different members.

## Typed navigation

Use `T.SparsePath` or `session.Paths` to start navigation. Generated builders live under SparseTypes.Generated, but application code can use `var` or `ISparseTypedPath<T,TValue>` without naming their concrete types.

| Shape | Navigation |
| --- | --- |
| Scalar | `session.Paths.Name` produces a typed value path. |
| Nested generated model | `session.Paths.Child.Name` continues into child members. |
| Sequence | `At(index)` addresses the current position. |
| Keyed sequence | `Key(key)` addresses stable assigned identity. |
| Temporary-key sequence | `TemporaryKey(guid)` addresses an unassigned row. |
| Dictionary | `Key(key)` addresses an entry; structural values allow further member navigation. |

Builders expose TypedPath, UntypedPath, CanWriteModelValueInPlace, and WriteModelValueInPlace(current, updated), and convert to untyped paths. Structural child paths also implement `ISparseChildEditPath` for opening typed child sessions. Its Clone, CreateEditSession, and CreateRootPaths members connect generated session infrastructure.

See [runtime paths](paths.md) for equality, text, and parsing rules, including the distinction between typed key identity and rendered text.

## Scalar, nested, and set transitions

| Shape | Transition surface |
| --- | --- |
| Scalar or atomic value | `SparseValueTransition<TValue>`: IsChanged, Before, After. |
| Nested generated model | Child ChangeSet, exposing child member transitions and aggregate IsEmpty/IsChanged. |
| Set | Generated transition: IsChanged, IsEmpty, Before, After, Added, Removed. |

`SparseValueTransition<T>` implements `ISparseValueChange<T>` and has constructor `(Optional<T> before, Optional<T> after, bool isChanged)`. Applications normally obtain it from ChangeSet properties.

## Keyed sequence transitions

The generated member transition implements `ISparseKeyedChange<TKey,TElement,TElementChange>` and provides typed enumeration.

| Member | Meaning |
| --- | --- |
| `IsEmpty`, `IsChanged` | Aggregate change status. |
| `Added`, `Removed` | Model elements added or removed. |
| `Edited` | Key-to-child-change map. |
| `BeforeOrder`, `AfterOrder`, `OrderChanged` | Stable key ordering and its change flag. |
| `Before`, `After` | Presence-aware whole-collection endpoint projections on the generated concrete transition. |
| `GetChange(TKey key)` | Item transition for one assigned key; empty if unchanged/absent. |
| `EnumerateItems()` | Interface-level item enumeration. The generated concrete transition is also enumerable. |
| `GetTemporaryChange(Guid temporaryKey)` | Available with temporary identity through ISparseTemporaryKeyedChange. |

Item transitions expose Key, TemporaryKey, Before, After, Edit, BeforeIndex, AfterIndex, IsEmpty, IsChanged, IsAdded, IsRemoved, IsEdited, and IsReordered, plus static Empty. Edit carries the child transition; reordering and editing can both be true.

## Dictionary transitions

`ISparseDictionaryChange<TKey,TValue,TEdited>` exposes IsEmpty, IsChanged, Added, Removed, Edited, `GetChange(key)`, and `EnumerateEntries()`. Added/Removed are key-to-value maps; Edited is a key-to-edit map. The generated concrete transition additionally exposes Before and After collection endpoint projections and supports enumeration.

`ISparseDictionaryItemChange<TKey,TValue>` exposes Key, Before, After, IsEmpty, IsChanged, IsAdded, IsRemoved, and IsEdited. Structural item transitions additionally implement `ISparseStructuralDictionaryItemChange<TKey,TValue,TEdit>` with Edit. Dictionaries do not have sequence-order transitions.

## Keyed sequence patch commands

For a property Items, the nested command type is normally `T.Patch.ItemsPatch`, obtained from `patch.Items`. It has a parameterless constructor and IsEmpty.

| Member | Contract |
| --- | --- |
| `Add(TElement element)` | Adds a model element. |
| `Remove(TKey key)` | Removes by assigned key. |
| `TElement.Patch Edit(TKey key)` | Gets the command for editing an existing structural element. |
| `SetOrder(IEnumerable<TKey> keys)` | Specifies the resulting assigned-key order. |
| `Set(TCollection value)` | Replaces the entire collection contribution. |
| `Remove()` | Removes the entire collection contribution. |
| Implicit conversion from TCollection | Whole-collection Set. |
| `Apply(Optional<TCollection> current)` | Returns a presence-aware collection result. |
| `Compose(next)`, static `Compose(first, second)` | Sequential collection command composition. |
| `Invert(Optional<TCollection> baseline)` | Inverse using known baseline values. |
| Static `Between(before, after)` | Derives commands from optional collection endpoints. |
| Static `Rebase(baseState, local, currentState)` | Explicit-baseline reconciliation of collection commands. |

TCollection means the actual declared supported collection type, including nullability. Temporary-key models add `RemoveByTemporaryKey(Guid)` and `EditByTemporaryKey(Guid)`; atomic element shapes instead offer `Update(TElement element)` and `UpdateByTemporaryKey(TElement element)` where applicable. Empty temporary identities are rejected. SetOrder carries key values, not temporary identities: sentinel entries cannot identify temporary items on their own. Diff-derived transitions retain that additional identity when supported.

Whole replacement/removal and granular editing cannot be combined arbitrarily in the same member command. Invalid operation combinations throw InvalidOperationException; the typed command is not an unchecked log of arbitrary requests.

## Dictionary patch commands

For a dictionary member, the generated nested patch has a parameterless constructor, IsEmpty, whole-collection Set/Remove, implicit conversion from the dictionary type, Apply, Compose (instance and static), Invert, static Between, and static Rebase, with the same optional-state convention as keyed sequences.

| Member-specific operation | Contract |
| --- | --- |
| `SetEntry(TKey key, TValue value)` | Adds or replaces an entry. |
| `RemoveEntry(TKey key)` | Removes an entry. |
| `TValue.Patch Edit(TKey key)` | Edits a generated structural value. |
| `UpdateEntry(TKey key, TValue value)` | Updates an atomic value where no child Patch exists. |

For application-driven edits, [collection editors](collection-editors.md) offer a uniform API with structured errors. For transport/diff-derived commands, `ChangeSet.ToPatch()` preserves generated collection operations without manually constructing implementation types. The checked [keyed examples](../explanation/keyed-collections.md) show direct typed collection commands.

## State projections

Transition endpoints describe the captured change. `session.CollectionState(path)` and `changeSet.FieldState(path)` project changes into UI-friendly state, while `session.ObserveField(path)` subscribes to later invalidations. Their full members and lifetimes are listed in [edit session state](edit-session-state.md).
