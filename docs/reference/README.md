# API reference

This reference covers APIs supplied by the packages and APIs generated for your models. Pages list members by role, describe their effects and failure conditions, and include short examples. Longer workflows belong to the [how-to guides](../README.md#how-to-guides).

## Package APIs

Unless stated otherwise, these types belong to the `SparseTypes` package and namespace.

| Area | Reference |
| --- | --- |
| Presence and member commands | [Optional and fragment operations](values-and-operations.md) |
| Generator configuration on models | [Attributes](attributes.md), [model requirements](model-shapes.md), [diagnostics](analyzer.md) |
| Merge and equality customization | [Merge, comparison, and rebase policies](merge-and-policies.md) |
| Concurrent edits | [Rebase results and conflict resolution](rebase-api.md) |
| Navigation | [Paths](paths.md) |
| Collection mutation | [Collection editors](collection-editors.md) |
| Framework-neutral sessions | [Session contracts, history, and groups](session-contracts.md) |
| State, observers, and validation | [Edit session state](edit-session-state.md), [validation stores](validation.md) |
| Dynamic metadata and access | [Descriptors](descriptors.md) |
| Wire endpoint and validation types | [Payload runtime APIs](payload-api.md), [wire specification](change-payload.md) |
| Blazor components and bindings | [Blazor API](blazor-api.md) |
| EF Core configuration and execution | [EF Core mapping](efcore-mapping.md) |

## Generated APIs

Here, `T` means a model with SparseTypes support, not an actual generic type parameter accepted by the generator.

| Surface | Reference |
| --- | --- |
| Added model members, extension methods, generation conditions | [Generated API overview](generated-api.md) |
| Partial snapshots and layering | [T.Fragment and its builder](generated-fragment.md) |
| Baseline-free commands | [T.Patch](generated-patch.md) |
| Before-to-after transitions | [T.ChangeSet](generated-changeset.md) |
| Serializable changes | [T.ChangePayload](generated-payload.md) |
| Live editing against a baseline | [T.EditSession](generated-edit-session.md) |
| Observable and read-only access | [T.Observable and T.ReadOnlyView](generated-views.md) |
| Model-specific navigation and transition types | [Generated paths and transitions](generated-paths-and-transitions.md) |

## Coverage and naming

The member tables cover the application-facing types named on each page. Overloads with the same purpose are grouped, with their different inputs identified. Routine inherited .NET members and synthesized record equality members are not repeated.

Public accessibility alone does not make a type an application entry point. `CompilerServices`, operation kernels, descriptor construction delegates, session hooks, and generated serialization-object types support generated code or custom framework adapters. Prefer the model-facing APIs and descriptor interfaces; do not copy implementation type names into application code.

`SparseTypes.Generator.Shared` serves authors of other source generators. Its configuration and dialect contracts are documented in its [consumer README](../../src/SparseTypes.Generator.Shared/README.md), separately from this application API reference.

The public API approvals under `tests/SparseTypes.Tests.PublicApi/Approvals/` record signatures for compatibility checks. They supplement this reference; they do not describe ownership, notifications, or workflow semantics.
