# T.Observable and T.ReadOnlyView

Generated class-model views expose the live model through notifying or read-only access. Neither is an isolated snapshot.

## Observable

| Member | Contract |
| --- | --- |
| `Observable(T value, Action? onChanged = null, Action? onRawModelAccess = null)` | Creates a proxy around an existing model. Session-created proxies connect callbacks automatically. |
| `T Model` | Raw mutable instance. Access invokes the raw-access callback when configured. |
| Generated member getters | Read through to the current model; structural members may return child proxies or collection facades. |
| Generated member setters | Exist only for supported public writable members. Mutate the model and notify on effective changes. |
| `Replace<Member>(declaredCollectionType value)` | Replaces supported proxied collection properties whose getter returns an observable facade. |
| `PropertyChanged` | Standard INotifyPropertyChanged event. |

Init-only members remain read-only on the proxy. Nested child edits notify the relevant parent route. Direct writes to the raw model do not emit observable transitions, so automatic history and observers require mutations through the proxy or supported editors.

`SparseObservableList<TModel,TView>` and `SparseObservableDictionary<TKey,TModel,TView>` are runtime collection facades returned for supported mutable structural collections. They implement familiar list/dictionary interfaces plus INotifyCollectionChanged, INotifyPropertyChanged, and IDisposable. Use the facade returned by the generated property rather than manually constructing one.

List operations include indexer replacement, Add, Insert, Remove, RemoveAt, Clear, Move, Count, IsReadOnly, Contains, IndexOf, CopyTo, and enumeration. Dictionary operations include keyed get/set, Add (key/value or KeyValuePair), Remove (key or KeyValuePair), Clear, TryGetValue, ContainsKey, Contains, CopyTo, Count, IsReadOnly, Keys, Values, and enumeration. These operate on the backing model collection; assigned duplicate keys in keyed sequences are rejected. Dispose detaches facade resources.

The list additionally exposes Model, UnsafeModel, IsDisposed, IsFixedSize, AddModel(item), InsertModel(index, item), SetModel(index, item), and NotifyReset(reportChange = true). The dictionary exposes Model, UnsafeModel, IsDisposed, AddModel(key, value), SetModel(key, value), and NotifyReset(reportChange = true). Model-valued methods avoid requiring a proxy as input. UnsafeModel bypasses the raw-access callback and is for controlled infrastructure access. NotifyReset is a bulk-invalidation hook for adapters, not automatic interception of raw collection writes.

## ReadOnlyView

| Member | Contract |
| --- | --- |
| `ReadOnlyView(T model)` | Wraps a live model. |
| Generated getter-only properties | Read current values. Nested generated models use read-only views; supported collection shapes use read-only projections. |

The view does not offer setters, but another owner can still mutate its model. Read-only projection does not make atomic arbitrary reference values deeply immutable. Use DeepClone when values must remain independent of later edits.

Session.Current supplies the read-only root view. Reading it avoids intentionally handing out the root model for uncontrolled mutation.

## Example

Uses the [reference models](attributes.md#example-model).

<!-- sample: reference-views -->
```csharp
var model = new ReferenceSettings { Theme = "light" };
var session = model.CreateEditSession();
var view = session.Current;

session.Observable.Theme = "dark";
var currentTheme = view.Theme;
var snapshot = model.DeepClone();
session.Observable.Theme = "solarized";
```
<!-- /sample -->

`currentTheme` is `"dark"`. The retained view now reads `"solarized"`; the clone still has `"dark"`.

For notification and cache details, see [edit session state](edit-session-state.md#notification-and-cache-contract). For dynamic access and stale accessor behavior, see [descriptors](descriptors.md).
