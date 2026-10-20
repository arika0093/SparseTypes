# SparseTypes Documentation

SparseTypes is a C# source generator for tracking field-level changes (`ChangeSet`), reconciling concurrent edits (`RebaseOnto` / `TryApplyTo`), and layering sparse configuration (`Fragment` / `Patch`).

This index organizes the documentation into four categories:

* **Tutorials**: Step-by-step runnable walkthroughs for first-time users.
* **How-to Guides**: Task-focused recipes for specific application scenarios.
* **Explanations**: Conceptual background and design trade-offs.
* **Reference**: Package APIs, generated model APIs, technical contracts, wire formats, and analyzer diagnostics.

## Tutorials

Start here to learn SparseTypes step by step in a console application:

1. [Track and revert edits with EditSession](tutorial/first-sparse-edit.md): Create an `EditSession`, mutate properties through `Observable`, inspect `ChangeSet` transitions, and revert changes.
2. [Send changes as JSON and rebase concurrent edits](tutorial/send-and-rebase-changes.md): Serialize a `ChangePayload` over the wire, merge non-overlapping concurrent edits, and inspect structured conflicts.
3. [Layer settings and reset overrides with Fragment and Patch](tutorial/layer-and-reset-settings.md): Merge multi-layer configuration fragments and use `Patch.Remove()` to fall back to lower layers.

## How-to Guides

Task-oriented guides for UI binding, API endpoints, and persistence:

* [Work with an edit session](how-to/edit-session.md): Submit captured snapshots, choose the right baseline acknowledgement (`AcceptChanges`, `TryReconcile`, `Reload`), undo/redo history, fork speculative drafts, and open child sessions.
* [Accept partial updates in an API](how-to/partial-updates.md): Receive `ChangePayload` requests, distinguish baseline-aware `ChangeSet` transitions from baseline-free `Patch` commands, and redact write-only secrets with `[SparseRedactBefore]`.
* [Compose layered settings](how-to/layered-settings.md): Layer defaults, environment overrides, and user preferences, and persist diffs against defaults.
* [Build a Blazor edit form](how-to/blazor-edit-form.md): Wire `EditSession` to Blazor's `EditContext`, surface rebase conflicts on form fields, and retry failed saves without losing user edits.
* [Use Blazor headless components](how-to/blazor-headless-components.md): Bind custom or third-party inputs with `SparseEdit`, `SparseField`, and `SparseCollection`.
* [Coordinate multi-session submissions](how-to/edit-session-groups.md): Group multiple independent edit sessions into a single validated submission with `EditSessionGroup`.
* [Apply ChangeSets to EF Core entities](how-to/efcore-mapping.md): Map DTO `ChangeSet` transitions onto tracked EF Core entities, correlate `[SparseTemporaryKey]` rows, and guard concurrency tokens.

## Explanations

Architecture and design concepts:

* [Edit sessions and state boundaries](explanation/edit-sessions.md): How baseline snapshots, live model mutations (`Observable`), immutable `ChangeSet` transitions, and persistence relate.
* [UI framework integration](explanation/ui-frameworks.md): How `EditSession` integrates with Blazor, WPF, WinUI, Avalonia, and MAUI, including `INotifyPropertyChanged` and collection facades.
* [Fragments and patches](explanation/fragments-and-patches.md): How `Fragment`, `Patch`, `ChangeSet`, and `ChangePayload` fit together and when to use each.
* [ChangeSet rebase](explanation/rebase.md): Three-way reconciliation rules, conflict types, path-based conflict resolution, and custom rebase policies.
* [Keyed collections](explanation/keyed-collections.md): Element identity with `[SparseKey]`, unassigned keys, client-generated `[SparseTemporaryKey]` rows, and typed collection transitions.
* [Merge strategies](explanation/merge-strategies.md): Built-in merge modes (`Replace`, `Deep`, `Append`, `SetUnion`), custom `FragmentMergeStrategy<T>`, value origins, and `[SparseCompare]`.
* [Cloning and ownership](explanation/cloning-and-ownership.md): Reference sharing vs deep snapshots across `Fragment.From`, `ToModel`, `Apply`, and `DeepClone`.
* [Edit-session groups](explanation/edit-session-groups.md): Capture purity, readiness vs submission validation, and baseline revision tracking across grouped sessions.

## Reference

Start with the [API reference index](reference/README.md) for package types and generated model companions.

### Package APIs

* [Optional and fragment operations](reference/values-and-operations.md): Presence, null, member commands, conversions, and ref helpers.
* [Model attributes](reference/attributes.md): Every model configuration attribute, its arguments, and declaration constraints.
* [Merge, comparison, and rebase policies](reference/merge-and-policies.md): Built-in modes and custom strategy contracts.
* [Rebase results and conflict resolution](reference/rebase-api.md): Result members, conflict values, options, and explicit decisions.
* [Paths](reference/paths.md): Typed identity, segment construction, parsing, and path queries.
* [Collection editors](reference/collection-editors.md): Positional, stable-key, and temporary-key editing with structured errors.
* [Session contracts, history, and groups](reference/session-contracts.md): Framework capabilities, Undo/Redo, child sessions, and submission capture.
* [Validation stores](reference/validation.md): Path/origin errors and conflict projection.
* [Payload runtime APIs](reference/payload-api.md): Endpoint, validation, wire token, and mixed-operation types.
* [OpenAPI integration](reference/openapi.md): Register generated `ChangePayload` schemas with ASP.NET Core OpenAPI or Swashbuckle.
* [Blazor API](reference/blazor-api.md): Component parameters, contexts, bindings, and EditContext extensions.

### Generated APIs

* [Generated API overview](reference/generated-api.md): Added model methods, extensions, placement, naming, and generation conditions.
* [T.Fragment and its builder](reference/generated-fragment.md): Sparse snapshots, layering, conversion, origins, and mutable builders.
* [T.Patch](reference/generated-patch.md): Commands, application, composition, inversion, and explicit-baseline rebase.
* [T.ChangeSet](reference/generated-changeset.md): Typed transitions, inspection, baseline-aware application, and resolution.
* [T.ChangePayload](reference/generated-payload.md): Transport factories, validation, conversion, and mixed application.
* [T.EditSession](reference/generated-edit-session.md): Capture, acknowledgement, reconciliation, reload, branches, history, and children.
* [T.Observable and T.ReadOnlyView](reference/generated-views.md): Write-through proxies, collection facades, and live read-only views.
* [Generated paths and transitions](reference/generated-paths-and-transitions.md): Shape-specific navigation and collection member APIs.

### Specifications and integration contracts

* [Model shapes](reference/model-shapes.md): `[SparseModel]` requirements, nested model promotion, `[SparseKey]` constraints, `[SparseIgnore]` vs `[JsonIgnore]`, and C# language version requirements.
* [Edit session state and observers](reference/edit-session-state.md): `FieldState`, `CollectionState`, `ObserveField`, `History` contract, notification batching, and generated type placement.
* [ChangePayload wire reference](reference/change-payload.md): Version `"0.1"` JSON envelope layout, endpoint states (`missing`, `null`, `value`, `redacted`), mixed-request composition rules, and validation.
* [Runtime descriptors](reference/descriptors.md): `ISparseModelMetadata`, `ISparsePropertyMetadata`, and `ISparseInstanceAccess` for reflection-free dynamic form builders and inspectors.
* [EF Core mapping reference](reference/efcore-mapping.md): `[SparseFragmentMapper<TEntity, TDto>]` configuration methods, missing/null policies, and supported shapes.
* [Analyzer diagnostics (`SPF001`-`SPF035`)](reference/analyzer.md): Complete table of compiler diagnostics, causes, and fixes.
