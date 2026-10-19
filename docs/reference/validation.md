# Validation stores

Package and namespace: `SparseTypes`. `SparseValidationStore<TModel>` stores external validation errors by path and origin. It implements `INotifyDataErrorInfo` and IDisposable; it does not run model validation or persist changes.

## SparseValidationStore<TModel>

| Member | Contract |
| --- | --- |
| Parameterless constructor | Creates an empty store rooted at `typeof(TModel)`. |
| `HasErrors`, `IsDisposed`, `RootType` | Store status and root type. |
| `ErrorsChanged` | Standard `DataErrorsChangedEventArgs` notification. |
| `SetErrors(path, string origin, string message)` | Replaces errors for one path/origin. |
| `SetErrors(path, string origin, IEnumerable<object?> errors)` | Same with multiple error objects. |
| `ClearErrors(path, string? origin = null)` | Clears one origin, or all origins at the path. |
| `ClearOrigin(string origin)` | Clears that origin across paths. |
| `Clear()` | Clears the whole store. |
| `HasPathErrors(path)` | Whether the path or its descendants have errors. |
| `GetPathErrors(path)` | Error objects at the path and below it. |
| `GetAllErrors()` | All `SparseValidationError` entries. |
| `GetErrors(string? propertyName)` | `INotifyDataErrorInfo` query by textual property path. |
| `AddConflicts(IEnumerable<SparseConflict> conflicts, SparseValidationConflictOptions? options = null)` | Projects conflicts to errors; returns the number added. |
| `Dispose()` | Releases subscriptions/resources and ends use of the store. |

SetErrors and ClearErrors accept `SparsePath`, `SparsePath<TModel,TValue>`, or `IDescriptor`. HasPathErrors and GetPathErrors accept typed or untyped paths. `IDescriptor` belongs to `SparseTypes.Generated`.

ClearErrors clears the exact path, unlike subtree reads. Null or empty GetErrors names aggregate all errors. Untyped paths are stored exactly, even if their target was deleted or their root differs from TModel. SetErrors rejects null or empty origins; querying or changing errors after disposal throws ObjectDisposedException.

`SparseValidationError` is a record struct with init properties `Path`, `Origin`, and `Error`; `PathText` renders the path. Errors are arbitrary objects, not necessarily strings.

`SparseValidationOrigins` defines `LocalValidation = "local"`, `ServerValidation = "server"`, and `RebaseConflict = "conflict"`. Separate origins prevent a server refresh from clearing unrelated local errors.

## Conflict options

`SparseValidationConflictOptions` has init properties `Origin`, `ReplaceOriginFirst`, and optional `MessageFactory`. `GetMessage(conflict)` applies the configured formatter or the default. See [Blazor API](blazor-api.md) for the separate `ValidationMessageStore` integration.

## Example

Uses the [reference models](attributes.md#example-model).

<!-- sample: reference-validation -->
```csharp
using var store = new SparseValidationStore<ReferenceSettings>();
var path = ReferenceSettings.SparsePath.Theme.TypedPath;
store.SetErrors(path, SparseValidationOrigins.ServerValidation, "Theme is unavailable.");
var hasErrors = store.HasPathErrors(path);
store.ClearOrigin(SparseValidationOrigins.ServerValidation);
```
<!-- /sample -->

`hasErrors` is true. The store is empty after clearing its only origin.
