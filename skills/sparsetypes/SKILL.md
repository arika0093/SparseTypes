---
name: sparsetypes
description: Use SparseTypes in C# applications for partial updates, edit sessions, change tracking, concurrent rebase, and layered settings. Covers generated model APIs and optional Blazor and EF Core integration.
---

# Use SparseTypes

Use this guide when implementing application code that consumes SparseTypes. Start with the project's package version, target framework, model declarations, and required behaviour. Preserve existing package versions unless the task requires an upgrade.

Documentation links below resolve within this repository. When installing this skill without the repository's documentation, consult the [upstream documentation](https://github.com/arika0093/SparseTypes/tree/main/docs), using the tag or commit matching the installed package when available. Check API availability against that version before using examples from main.

## Set up the consumer

- Add the `SparseTypes` package to the consuming project. It bundles the source generator; ordinary consumers do not need a separate generator package or `SparseTypes.Generator.Shared`.
- Use `using SparseTypes;` for attributes and runtime types. Import the model's namespace for generated extension methods.
- Check the compiler requirements: Roslyn 4.3.1 or later and C# 9 or later. Older target frameworks may need an explicit `LangVersion`. See [compatibility](../../README.md#packages-and-compatibility).
- Declare a root model with `[SparseModel]` as a top-level, non-generic, non-abstract `partial` class or struct. Class construction needs a parameterless constructor or a supported constructor matching public readable properties by name and type.
- Use class models with writable properties for live edit sessions. Structs and init-only members support fewer editing operations; snapshot support does not imply in-place mutation support.
- Make reachable nested models `partial` for member-level changes. They are promoted automatically without their own attribute. For an atomic nested value, explicitly configure `[SparseMerge(MergeMode.Replace)]`.

Read [model shapes](../../docs/reference/model-shapes.md) for constructor, required-member, and nested-type constraints. Follow the [first edit tutorial](../../docs/tutorial/first-sparse-edit.md) for a working example.

## Choose the change representation

Here `T` denotes the application's model. Companion types are nested types such as `Settings.Fragment`; do not invent a generic `Fragment<Settings>` API.

| Application task | Use | Retained information |
| --- | --- | --- |
| Track a form's live edits, dirty state, revert, or Undo/Redo | `model.CreateEditSession()` | Live model and baseline snapshot |
| Compare two states, inspect history, undo, or detect concurrent edits | `T.ChangeSet.Between(before, after)` or `before.CreateChangeSet(after)` | Before and after endpoints |
| Send requested updates without a baseline | `T.Patch` | Commands only |
| Store partial state or layer configuration | `T.Fragment` | Member presence and values |
| Send commands or transitions over JSON | `T.ChangePayload` | Versioned wire representation |

Use the smallest surface that satisfies the task. A service accepting patches does not need an edit session. See [generated APIs](../../docs/reference/generated-api.md) for signatures and generation conditions; use factories, extension syntax, and `var` instead of naming generated implementation containers.

## Preserve missing, null, and removal

`Optional<T>.Missing` means no contribution. `Optional<T>.Present(value)` supplies a value, including an explicit null where allowed. Check presence before reading `.Value`; do not reduce these states to a nullable property.

A patch member left untouched keeps the current value. `Set(value)` supplies a value, `Set(null)` supplies explicit null, and `Remove()` removes the contribution. An omitted fragment member is not a removal command. Whole-root missing or null transitions need the presence-aware APIs using `Optional<T.Fragment?>`.

For layered settings, merge from lower to higher priority with `lower.Merge(higher)`, then materialise with `ToModel()`. To reset an override, apply a removal patch to the layer that owns it and merge the layers again. Removing a value from the already merged result cannot recover the lower layer's value.

`Fragment.Diff(before, after)` captures changed after-values. Choose `ChangeSet` when before-values or explicit removals matter. Default merge behaviour is deep for nested generated models and replacement for scalars and ordinary collections; check collection-specific policies before choosing append or union.

See [layered settings](../../docs/how-to/layered-settings.md), [merge strategies](../../docs/explanation/merge-strategies.md), and [operation types](../../docs/reference/values-and-operations.md).

## Edit and save a live model

`CreateEditSession()` captures the baseline but edits the supplied instance. Bind inputs to `session.Observable`; its writes update that instance. Use `DeepClone()` before creating the session, or `session.Fork()`, when edits must remain isolated. A read-only view is live, not a snapshot.

Capture `submitted = session.CreateChangeSet()` before starting an asynchronous save. Send that captured transition, then acknowledge only after persistence succeeds. If saving fails, retain the pending edits.

| Save outcome | Acknowledgement |
| --- | --- |
| Submitted transition persisted unchanged while the user continued editing | `session.AcceptChanges(submitted)` preserves later pending edits |
| Current values match persisted state and no later edits need preserving | `session.AcceptChanges()` adopts the current values as baseline |
| Server returned authoritative state and no in-flight edits need preserving | Create a fresh session from the returned model |
| Server assigned keys to temporary-key rows while editing continued | `TryReconcile(submitted, persisted, out error)` |
| Server normalised values while editing continued | `TryReconcileAndApply(submitted, persisted, out conflicts)` |

Check reconciliation results before treating a save as acknowledged. Neither `AcceptChanges` overload performs I/O. Calling the parameterless overload after an in-flight save can incorrectly mark newer, unsaved edits as accepted.

Use `Reload(serverState)` for external updates while retaining pending edits; inspect conflicts. Use `RevertChanges()` to restore the baseline, and `EnableHistory()` when Undo/Redo is required. Observable writes participate in notifications and history; do not assume direct model mutations behave identically.

Read [edit-session workflows](../../docs/how-to/edit-session.md) for fork, child-session, and reconciliation details, and [cloning and ownership](../../docs/explanation/cloning-and-ownership.md) before relying on graph isolation. Init-only fragment storage does not freeze referenced mutable objects.

## Receive updates and handle conflicts

For baseline-aware requests, use `ChangeSet.TryApplyTo(current, out updated, out conflicts)` or inspect `RebaseOnto(current)` before applying its result. `RebaseOnto` does not modify the current model. Handle reported conflicts according to the application's policy.

`Patch.ApplyTo(current)` returns a model without comparing historical values. `ChangeSet.ToPatch()` discards before-state checks; do not use that conversion to bypass a conflict in a baseline-aware endpoint. Both model-returning application APIs leave the input unchanged. In-place APIs have separate capability and failure contracts, especially for init-only members.

Persist accepted updates under the application's concurrency token or equivalent transaction policy. A successful in-memory rebase does not make the subsequent database write atomic.

See [partial-update endpoints](../../docs/how-to/partial-updates.md) and [rebase semantics](../../docs/explanation/rebase.md).

## Transport changes with System.Text.Json

- Export transitions with `changes.ToPayload()` and commands with `T.ChangePayload.FromPatch(patch)`. Serialise the generated payload rather than the in-memory `ChangeSet` or `Patch`.
- Receive `T.ChangePayload`, validate its structure, and choose `ToChangeSet()`, `ToPatch()`, or `TryApplyMixedTo()` according to the endpoint contract. Blind commands and redacted before-values cannot reconstruct a complete `ChangeSet`.
- Treat `Validate()` as structural validation. Authorisation, domain validation, and concurrency checks remain application responsibilities. The payload version describes the wire format, not the model revision.
- `[SparseRedactBefore]` hides before-values on the wire, but after-values still travel. `[JsonIgnore]` retains local sparse behaviour; exporting changes to ignored members throws. Use `[SparseIgnore]` when a member should be excluded from the generated sparse surface entirely, subject to model-shape constraints.
- JSON support depends on a `System.Text.Json` reference. For Native AOT, follow the documented `JsonSerializerContext` registration for each generated payload type.

Read [payload APIs](../../docs/reference/generated-payload.md) and the [wire and AOT reference](../../docs/reference/change-payload.md) when implementing transport.

## Edit collections by identity

For element-level edits to structural sequences, declare exactly one publicly readable `[SparseKey]` property on each element type. Use stable identity, not array positions. Represent a composite key through a single computed key property. Structural sequences without identity require an explicit whole-value strategy or produce a generator diagnostic; scalar sequences and dictionaries have different semantics.

For database-assigned keys, consult the temporary-key workflow before adding rows: `[SparseKey(Unassigned = ...)]` and `[SparseTemporaryKey]` have specific declaration and reconciliation requirements. Do not invent permanent keys or match persisted rows by position.

See [keyed collections](../../docs/explanation/keyed-collections.md) and [collection editors](../../docs/reference/collection-editors.md) for insertion, deletion, reordering, and pending additions.

## Add framework integration when needed

| Task | Package and guide | Constraint |
| --- | --- | --- |
| Bind a Blazor form and validation | `SparseTypes.Blazor`: [edit form](../../docs/how-to/blazor-edit-form.md), [headless components](../../docs/how-to/blazor-headless-components.md) | Check target-framework compatibility before adding the package |
| Coordinate independent form sections | [Edit-session groups](../../docs/how-to/edit-session-groups.md) | Follow submission and acknowledgement scopes for the participating sessions |
| Apply DTO changes to tracked EF Core entities | `SparseTypes.EntityFrameworkCore`: [mapping guide](../../docs/how-to/efcore-mapping.md) | Use a complete projection for rebase and saved responses; filtered `IncludeFor` collections are not a complete DTO |

## Verify the consumer change

Build the consuming project so the generator runs. Resolve diagnostics using the [analyser reference](../../docs/reference/analyzer.md); do not hand-edit generated files or suppress shape errors to make an unsupported model compile.

Run the application's relevant tests. For changed update semantics, cover omitted members, explicit null, and removal separately. For concurrent saves, check both independent edits and same-member conflicts; for edit sessions, check that post-submit edits remain dirty after acknowledgement. Include payload round-trips or keyed additions and reorders when those paths changed.

Use examples linked from the [documentation index](../../docs/README.md). Their registered samples are backed by executable consumer fixtures in this repository; adapt the sample matching the task instead of guessing overloads or generated helper names.
