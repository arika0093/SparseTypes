# EF Core mapping

This folder contains the simple EF Core mapping page and sample code. `OrderLineRow` edits line fields through `SparseField` and routes collection changes through the typed collection editor. The mapper owns DTO projection, including the flattened Notes read and line ordering. The submit action captures an immutable ChangeSet through the generated EditSession; after a successful save, the session reconciles against the persisted entity and the UI adopts authoritative values for untouched fields. Later draft edits remain pending after reconciliation.
