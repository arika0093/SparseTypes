# Blazor API

Package and namespace: `SparseTypes.Blazor`. Components are headless: they supply contexts and templates rather than input styling. Class models and generated session capabilities are required.

## Components

| Component | Role |
| --- | --- |
| [SparseEdit](#sparseedit) | Supplies an editing context, typed root paths, dirty state, and session history controls. |
| [SparseField](#sparsefield) | Binds one typed path to an input and exposes its change state and validation messages. |
| [SparseCollection](#sparsecollection) | Renders collection rows with stable UI identity and session-aware mutation operations. |
| [SparseNavigationGuard](#sparsenavigationguard) | Connects controller state to Blazor's standard internal and external navigation guards. |
| [SparseSessionScope](#sparsesessionscope) | Owns retained child editors and selects Draft or PassThrough behavior. |
| [SparseChildEdit / SparseChildEditDefault](#sparsechildedit-and-sparsechildeditdefault) | Opens a typed child editor inside a session scope. |

All components have parameterless constructors. SparseEdit requires a path-capable session, SparseField requires a resolvable typed path and edit scope, and SparseCollection requires a collection path and scope. Supplying an arbitrary CLR object is not a substitute for the generated capabilities.

## Example model and session

The examples use an order with a nested customer and keyed order lines. Model declarations need `using SparseTypes;` and `using System.ComponentModel.DataAnnotations;`.

<!-- sample: blazor-reference-models -->
```csharp
[SparseModel]
public partial class BlazorApiOrder
{
    [Required]
    public string Number { get; set; } = string.Empty;
    public BlazorApiCustomer Customer { get; set; } = new();
    public List<BlazorApiLine> Lines { get; set; } = new();
}

[SparseModel]
public partial class BlazorApiCustomer
{
    public string Name { get; set; } = string.Empty;
}

[SparseModel]
public partial class BlazorApiLine
{
    [SparseKey]
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
}
```
<!-- /sample -->

Each component example below uses this Razor setup. History is enabled on the session before rendering; SparseEdit does not enable it automatically. The examples are separate templates, so only the collection and child-draft examples need their additional `@code` members.

<!-- illustrative: shared setup compiled in BlazorApiReference.razor in the Blazor consumer fixture -->
```razor
@using Microsoft.AspNetCore.Components.Forms
@using SparseTypes
@using SparseTypes.Blazor

@code {
    private BlazorApiOrder.EditSession session = null!;

    protected override void OnInitialized()
    {
        var order = new BlazorApiOrder
        {
            Number = "ORD-1",
            Customer = new BlazorApiCustomer { Name = "Ada" },
            Lines = new List<BlazorApiLine>
            {
                new() { Sku = "SKU-1", Quantity = 1 },
            },
        };
        session = order.CreateEditSession();
        session.EnableHistory();
    }
}
```

## SparseEdit

`SparseEdit<TModel,TPaths>` takes Session and a typed ChildContent template, named `edit` here with Context. Session supplies the generic arguments, including the generated root path type. `TPaths` must implement `ISparseTypedPath<TModel,TModel>`.

SparseEdit supplies the EditContext; EditForm supplies the form element and Blazor validation cascade. It subscribes to session and history notifications so Changed, Undo, and Redo controls refresh after edits. The optional `Controller` parameter must reference a controller for the same session. Controller errors appear in `SparseFieldContext.ValidationMessages`, and controller state changes refresh the template.

<!-- illustrative: edit root compiled in BlazorApiReference.razor in the Blazor consumer fixture -->
```razor
<SparseEdit Session="session" Context="edit">
    <EditForm @key="session" EditContext="edit.EditContext">
        <DataAnnotationsValidator />
        <SparseField Path="edit.Path.Number" Context="number">
            <InputText Value="@number.Value"
                       ValueChanged="number.ValueChanged"
                       ValueExpression="number.ValueExpression" />
            <ValidationMessage For="number.ValueExpression" />
            @if (number.IsChanged)
            {
                <span>Number changed</span>
            }
        </SparseField>
        <p>@(edit.HasChanges ? "Changed" : "Clean")</p>
        <button type="button" disabled="@(!edit.CanUndo)" @onclick="edit.Undo">Undo</button>
        <button type="button" disabled="@(!edit.CanRedo)" @onclick="edit.Redo">Redo</button>
    </EditForm>
</SparseEdit>
```

Changing Number from `ORD-1` to `ORD-2` shows both change labels and enables Undo. Undo restores `ORD-1`, clears the semantic change labels, and enables Redo. Returning a value to its baseline also clears IsChanged even if Blazor still considers the input modified. The buttons use `type="button"` so they do not submit the form.

`@key="session"` recreates the form's input subtree when the active session changes. SparseEdit detaches its subscriptions on disposal; the session continues to own its history.

### Edit context members

`SparseEditContext<TModel,TPaths>` supplies:

| Members | Contract |
| --- | --- |
| `Session`, `Path`, `EditContext`, `HasChanges`, `Controller` | Active generated session, root navigation, original-model context, dirty state, and optional controller. |
| `History`, `CanUndo`, `CanRedo` | Optional session-owned history state. |
| `Undo()`, `Redo()` | Throw if history is disabled, empty, or replay conflicts. |
| `TryUndo(out conflicts)`, `TryRedo(out conflicts)` | Return false when unavailable or conflicting. |
| `TryGetStateSnapshot(out SparseChangeSetQueryView? snapshot, out string? error)` | Gets a shared state-query snapshot for this refresh generation. |
| `InvalidateStateSnapshot()` | Forces a new snapshot on the next query. |
| `Dispose()` | Detaches the context's subscriptions. |

The snapshot is fixed to a refresh generation, not a permanently live query.

## SparseField

`SparseField<TModel,TValue>` takes Path and a typed ChildContent template. The path supplies the root/value generic arguments; Scope and the optional SessionScopeContext are cascading parameters.

A field's IsChanged describes its addressed path. Undo and Redo belong to the enclosing session and may undo edits to other fields, so they use `edit` rather than the field context.

<!-- illustrative: field change state and session history compiled in BlazorApiReference.razor -->
```razor
<SparseEdit Session="session" Context="edit">
    <SparseField Path="edit.Path.Customer.Name" Context="name">
        <input @bind="name.Value" disabled="name.IsReadOnly" />
        @if (name.HasStateQueryError)
        {
            <p role="alert">@name.StateQueryError</p>
        }
        else if (name.IsChanged)
        {
            <span>Name changed</span>
            @if (name.Before.IsPresent)
            {
                <span>Was: @name.Before.Value</span>
            }
        }
    </SparseField>
    <button type="button" disabled="@(!edit.CanUndo)" @onclick="edit.Undo">Undo</button>
    <button type="button" disabled="@(!edit.CanRedo)" @onclick="edit.Redo">Redo</button>
</SparseEdit>
```

Before and After are Optional values: check IsPresent before reading Value. A failed state query is shown separately from an unchanged field.

Native inputs and controls that need only a value/callback can bind with `@bind` or `@bind-Value`. For InputText, InputNumber, and other Blazor InputBase controls, use the Value / ValueChanged / ValueExpression triple shown in the SparseEdit example. ValueExpression is available for member-only paths; keyed and indexed paths can have no expression. Use a value/callback input and render ValidationMessages explicitly for those paths.

### Field context members

`SparseFieldContext<TModel,TValue>` supplies:

| Members | Contract |
| --- | --- |
| `Value { get; set; }`, `ValueChanged`, `ValueExpression` | Standard Blazor input integration. Expressions may be absent for collection paths. |
| `Binding`, `Path`, `Field` | Underlying typed binding, path, and original-model FieldIdentifier. |
| `State`, `Before`, `After`, `IsChanged` | Sparse transition state. |
| `IsModified`, `ValidationMessages` | Blazor EditContext state, distinct from sparse baseline comparison. |
| `IsReadOnly`, `IsStale`, `HasStateQueryError`, `StateQueryError` | Availability and query diagnostics. |
| `TryGetValue(out value, out error)`, `TrySet(value, out error)`, `SetAsync(value)` | Read/write operations. |
| `TryGetCollectionState(out SparseCollectionState? state)` | Collection-state projection when applicable. |
| `Select(selector)`, `At(index, selector)`, `Key(key, selector)`, `TemporaryKey(guid, selector)` | Typed navigation using member-selector expressions. |

### Bind without a SparseField component

`session.Bind(path, editContext, options = null)` returns `SparseFieldBinding<TModel,TValue>`, or `SparseFieldBinding<TModel,object?>` for untyped paths. It can back an application's own input component:

<!-- sample: blazor-reference-binding -->
```csharp
var order = new BlazorApiOrder { Number = "ORD-1" };
var session = order.CreateEditSession();
var editContext = session.CreateEditContext();
var history = session.EnableHistory();
var number = session.Bind(BlazorApiOrder.SparsePath.Number, editContext);

await number.SetAsync("ORD-2");
var changed = session.FieldState(BlazorApiOrder.SparsePath.Number).IsChanged;

history.Undo();
var restored = number.Value;
history.Redo();
var reapplied = number.Value;
```
<!-- /sample -->

Here `changed` is true, `restored` is `ORD-1`, and `reapplied` is `ORD-2`. `SparseFieldBindingOptions` has init properties Disabled and UseBatchEdit, both false by default.

Bindings expose TypedPath, UntypedPath, EditContext, Field, Value, ValueExpression, IsNullable, IsReadOnly, IsStale, and settable Disabled. Methods are `TryGetField(out fieldId, out error)`, `TryGetValue(out value, out error)`, `TrySet(value, out error)`, `Set(value)`, and `SetAsync(value)`. Try methods report errors as strings; throwing reads/writes reject stale or incompatible bindings with InvalidOperationException.

Bindings resolve live paths, including nested replacement. A stale EditContext bound to another root is an error. Disabled blocks writes; ValueExpression targets the original model for compatible member paths rather than fabricating expressions for keyed/indexed access.

## SparseCollection

`SparseCollection<TModel,TElement>` takes a collection Path and RowTemplate. The path may address a nested collection below a keyed row or child model. EmptyContent renders when the collection has no rows; ErrorContent receives an enumeration error. Its generic arguments are explicit because an untyped SparsePath does not carry the element type for Razor inference.

The row's Item is for reading. Bind editable members through `row.Select(...)` and SparseField, and remove or move entries through row operations. Directly changing `row.Item.Quantity` bypasses the session's notification pipeline.

<!-- illustrative: collection template and mutation handlers compiled in BlazorApiReference.razor -->
```razor
<SparseEdit Session="session" Context="edit">
    <SparseCollection TModel="BlazorApiOrder" TElement="BlazorApiLine"
                      Path="edit.Path.Lines" @ref="lines">
        <RowTemplate Context="row">
            <div @key="row.UiKey">
                <span>@row.Item.Sku</span>
                <SparseField Path="row.Select(item => item.Quantity)" Context="quantity">
                    <input type="number" @bind="quantity.Value" />
                    @if (quantity.IsChanged)
                    {
                        <span>Quantity changed</span>
                    }
                </SparseField>
                <button type="button" @onclick="() => RemoveLine(row)">Remove</button>
            </div>
        </RowTemplate>
        <EmptyContent><p>No lines.</p></EmptyContent>
        <ErrorContent Context="error"><p role="alert">@error</p></ErrorContent>
    </SparseCollection>
    <button type="button" @onclick="AddLine">Add line</button>
    @if (collectionError is not null)
    {
        <p role="alert">@collectionError</p>
    }
</SparseEdit>

@code {
    private SparseCollection<BlazorApiOrder, BlazorApiLine> lines = null!;
    private string? collectionError;

    private void AddLine()
    {
        var item = new BlazorApiLine { Sku = Guid.NewGuid().ToString("N"), Quantity = 1 };
        collectionError = lines.TryAdd(item, out var error) ? null : error.ToString();
    }

    private void RemoveLine(SparseCollectionRow<BlazorApiOrder, BlazorApiLine> row)
    {
        collectionError = row.TryRemove(out var error) ? null : error.ToString();
    }
}
```

The component reference is assigned after rendering, before these button handlers run. A rejected add or removal displays its structured error. These operations participate in the same session history as scalar edits, so the SparseEdit Undo/Redo controls can reverse them.

### Collection and row members

`SparseCollection` exposes Rows, RemovedItems, CollectionState, QueryError, Editor, `TryAdd(item, out SparseCollectionEditError error)`, and Dispose. A row exposes Item, Index, Path, UiKey, optional ItemState, StateQueryError, controller `ValidationMessages`, `RequireItemState()`, `Select(selector)`, `TryMoveTo(newIndex, out error)`, and `TryRemove(out error)`.

UiKey supports rendering identity; use the model's SparseKey for domain identity. `RequireItemState` throws when state is unavailable instead of returning fabricated clean state.

## SparseNavigationGuard

`SparseNavigationGuard<TModel,TChangeSet>` takes an editor, `EnableExternalConfirmation`, and an optional `BeforeInternalNavigation` callback. Its `HasUnsavedWork` state includes committed changes, pending child or collection drafts, active controller operations, and unresolved persistence outcomes. The callback receives Blazor's `LocationChangingContext`; call `PreventNavigation()` when the application chooses to stop the transition. When no callback is supplied, internal navigation proceeds.

External confirmation is disabled by default. Setting `EnableExternalConfirmation` lets `NavigationLock` use the browser's standard confirmation. Browser restrictions still apply, and the component does not perform async saves or guarantee that unload callbacks run.

## SparseSessionScope

`SparseSessionScope<TModel>` takes Session, optional Mode, and ordinary ChildContent. It cascades the parent session and owns the child-editor registry. It does not create the field-editing context supplied by SparseEdit; SparseChildEdit creates that context for its child.

Each session can have only one SparseSessionScope owner. Place sibling child editors under that scope instead of creating a scope per child.

| Mode | Child-edit behavior |
| --- | --- |
| `Draft` | Retains edits in an independent child session until accepted into the parent. Default at the root. |
| `PassThrough` | Attempts to accept each child edit into the parent automatically. Change indicators use the parent baseline. |

Nested scopes inherit Mode unless they specify their own. In this PassThrough example, editing Name updates the parent customer after successful acceptance:

<!-- illustrative: pass-through session scope compiled in BlazorApiReference.razor -->
```razor
<SparseSessionScope Session="session" Mode="SparseChildEditMode.PassThrough">
    <SparseChildEditDefault Path="session.Paths.Customer" Context="customer">
        <SparseField Path="customer.Path.Name" Context="name">
            <input @bind="name.Value" />
        </SparseField>
        <span>@(customer.HasChanges ? "Customer changed" : "Customer clean")</span>
        @if (customer.Conflicts is not null)
        {
            <p role="alert">The customer edit conflicts with its parent.</p>
        }
    </SparseChildEditDefault>
    <p>Parent value: @session.Current.Customer.Name</p>
</SparseSessionScope>
```

PassThrough still uses conflict-aware child acceptance. A failed merge must be resolved; the mode does not make every write unconditional.

`SparseSessionScopeContext<TModel>` exposes Mode, HasChanges, Changed, `HasChangesFor(path)`, `OpenChildSession(parentSession, path)`, and `TryCloseChildSession(parentSession, path)`. `TryCloseChildSessionAtPath(parentSession, path)` provides the same close operation for an untyped `SparsePath`. Both close methods retain a child with pending changes. The context also exposes `TryGetDetachedChild(parentSession, path, out context)`, `GetDetachedChildPaths()`, and `DisposeDetachedChildren()`. Access the active context through the component's ScopeContext or its cascade.

## SparseChildEdit and SparseChildEditDefault

`SparseChildEdit<TParent,TChild,TChildPaths,TConflict,TChildSession>` takes a typed child Path and ChildContent template. ParentSession and ScopeContext come from the surrounding SparseSessionScope. The path and session types must match the generated child model.

`SparseChildEditDefault<TParent,TChild,TChildPaths,TChildSession>` fixes TConflict to SparseConflict, allowing the remaining arguments to be inferred from Path. It is the usual choice for SparseTypes models.

This Draft example keeps the customer's edits separate from the parent until Apply to parent succeeds. The child scope enables its own history in Draft mode, so its Undo/Redo buttons need no additional EnableHistory call.

<!-- illustrative: draft child template and acceptance handler compiled in BlazorApiReference.razor -->
```razor
<SparseSessionScope Session="session" Mode="SparseChildEditMode.Draft">
    <SparseChildEditDefault Path="session.Paths.Customer" Context="customer">
        <EditForm @key="customer.Session" EditContext="customer.EditContext">
            <SparseField Path="customer.Path.Name" Context="name">
                <InputText Value="@name.Value"
                           ValueChanged="name.ValueChanged"
                           ValueExpression="name.ValueExpression" />
                @if (name.IsChanged)
                {
                    <span>Name changed</span>
                }
            </SparseField>
            <p>@(customer.HasChanges ? "Customer draft changed" : "Customer draft clean")</p>
            <button type="button" disabled="@(!customer.CanUndo)" @onclick="customer.Undo">Undo</button>
            <button type="button" disabled="@(!customer.CanRedo)" @onclick="customer.Redo">Redo</button>
            <button type="button" @onclick="() => AcceptCustomer(customer)">Apply to parent</button>
            <button type="button" @onclick="customer.DiscardChanges">Discard draft</button>
        </EditForm>
    </SparseChildEditDefault>
    <p>Parent value: @session.Current.Customer.Name</p>
    @if (childError is not null)
    {
        <p role="alert">@childError</p>
    }
</SparseSessionScope>

@code {
    private string? childError;

    private void AcceptCustomer(ISparseChildEditContext<BlazorApiCustomer, SparseConflict> child)
    {
        childError = child.TryAcceptChanges(out var conflicts)
            ? null
            : conflicts is null
                ? "The child draft could not be accepted; its target may be unavailable."
                : $"The child has {conflicts.Count} conflict(s).";
    }
}
```

Child acceptance merges into the parent draft; it does not save or acknowledge the parent's baseline. Discard draft abandons the pending child edits while leaving the editor connected. Child EditForm elements must not be nested inside a parent EditForm; keep forms as siblings or use inputs without a child form.

### Child context members

`SparseChildEditContext` exposes Session, Path, EditContext, Mode, HasChanges, CanUndo, CanRedo, Conflicts, and the Accepted event. `ISparseChildEditContext<TModel>` is the consumer-facing contract when typed conflict details are not needed. It provides parameterless `TryAcceptChanges()` and exposes Conflicts as `IReadOnlyList<object>`. `ISparseChildEditContext<TModel,TConflict>` retains typed conflict access and `TryAcceptChanges(out conflicts)`.

In Draft mode, history controls operate on the child draft. In PassThrough mode they operate on the active parent history, so that parent session must have history enabled. Check CanUndo/CanRedo before invoking the throwing operations.

## EditContext and validation extensions

| Extension on a session | Contract |
| --- | --- |
| `CreateEditContext()` | Creates an EditContext for the original model. |
| `CreateValidationStore(editContext)` | Creates a store for that context. |
| `Field(path)` | Resolves typed or untyped paths to an original-model FieldIdentifier. |
| `AddValidationError(store, field, message)` | Accepts a FieldIdentifier, SparsePath, or typed path. |
| `AcceptChanges(editContext)` | Accepts current state and clears corresponding modified state. |
| `AcceptChanges(editContext, changes)` | Acknowledges only the submitted transition. |
| `AddConflicts(store, editContext, conflicts, options = null)` | Adds structured conflict messages and returns their count. |
| `ClearConflicts(store, editContext)` | Clears the supplied dedicated conflict store. |
| `ReplaceEditContext(staleContext)` | Creates a replacement for a fresh session/model. |

`SparseConflictValidationOptions` has Fallback (NearestParent by default), ClearStoreFirst (false), optional MessageFactory, and GetMessage(conflict). Fallback can be NearestParent or Model. Clearing one store does not clear other stores, such as DataAnnotations.

AcceptChanges(editContext, changes) marks the context unmodified only when the session is fully clean; later edits remain pending. AddValidationError adds a message but does not itself notify validation state changes; call `editContext.NotifyValidationStateChanged()` after adding application errors.

For more navigation examples, see [headless components](../how-to/blazor-headless-components.md). For submission, persistence, and server validation, see [Blazor edit forms](../how-to/blazor-edit-form.md).
