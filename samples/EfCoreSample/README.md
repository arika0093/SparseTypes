# EfCoreSample

Aspire sample showing one disconnected-edit round trip: the WPF client reads
an order, edits a draft through an edit session, and submits a change payload.
The server rebases the payload onto current state, applies it with a sparse
entity mapper, and persists it with Entity Framework Core over PostgreSQL.

## Projects

- EfCoreSample.Contracts: DTOs shared by client and server. The DTOs know
  nothing about EF Core.
- EfCoreSample.Server: Minimal API plus the mapper and the PostgreSQL context.
  The OpenAPI document includes the generated change payload schemas via
  `SparseTypes.OpenApi.AspNetCore`, and `/` serves the Swagger UI.
- EfCoreSample.WebClient: React and TypeScript order editor. It supports
  adding, editing, removing, and reordering order lines, including unsaved lines.
  The local `@sparsetypes/codegen` package generates its API client, model
  declarations, schema parsers, and `ChangePayload` diff functions from the
  server's build-time OpenAPI document.
- EfCoreSample.WinAppClient: WPF order editor and edit-session showcase. The form
  binds to the live edit session's observable proxy with changed-field
  highlighting and read-only gray cells; line buttons move the selected row up
  and down, and toolbar commands cover load,
  create, server refresh, undo/redo with history, save, payload preview,
  and reset. Save ships a sparse change payload, and overlapping edits
  open a conflict dialog that resolves each path to the submitted or the
  server value. The keyed line editor supports add, remove, duplicate,
  baseline reset, and restore of deleted rows. The selected-row dialog edits
  an independent child session and discards its changes when canceled. The
  window asks before closing with unsaved edits. The change count opens a
  path/before/after table.
- EfCoreSample.AppHost: Aspire host wiring PostgreSQL (with pgweb), the
  server, and the client.

## Run

Install the code generator and web-client dependencies, then start PostgreSQL,
pgweb, the server, and the clients through Aspire:

```bash
npm ci --prefix src/SparseTypes.JS.CodeGen
npm ci --prefix samples/EfCoreSample/EfCoreSample.WebClient
dotnet run --project samples/EfCoreSample/EfCoreSample.AppHost
```

The web client starts with the AppHost at `http://localhost:5173`. Its Vite
development server proxies API requests to the server. The WPF client remains
available to start from the Aspire dashboard. The server listens on
`http://localhost:5000`, so `/` opens the Swagger UI and
`GET http://localhost:5000/orders/1` returns the seeded order, including its
concurrency token.

Running the AppHost with `dotnet run` builds the server first. That build writes
`obj/openapi/order-api.json` and runs the web client's `generate:api` script.
The script builds the local codegen package and writes the generated module to
`EfCoreSample.WebClient/node_modules/@sparsetypes/generated` before Aspire
starts. The browser client imports that package and uses its generated response parser and
`buildOrderDtoChangePayload` function rather than maintaining its own keyed-line
diff and payload envelope.

The server alone also runs standalone against any reachable PostgreSQL:

```bash
ConnectionStrings__orders="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=orders" dotnet run --project samples/EfCoreSample/EfCoreSample.Server
```

Without `ConnectionStrings__orders` the server falls back to the local
defaults shown above.

## Update contract

`POST /orders` creates an order with placeholder values and returns `201`
with the persisted order and its `Location`. `PUT /orders/{id}` takes a
change payload body and requires the version
observed by the client as the `If-Match` header:

```text
PUT /orders/1
If-Match: 3d6f0b4c-9b8e-4f2a-a1c3-7e5d9f0b2c4a
Content-Type: application/json

{"version":"0.1","changes":[...]}
```

The response tells the client which kind of conflict happened:

- 409 Conflict: the submitted edits overlap current values member by
  member (semantic `ChangeSet.RebaseOnto` conflict). The body lists the
  conflicting paths. Resolve the paths against a fresh read.
- 412 Precondition Failed: another writer advanced the order first, so the
  conditional write matched zero rows. The submitted edits may be disjoint
  from the other writer's edits; the token still guards the read-to-save
  race. Re-read the order, rebase pending edits onto it, and submit again
  with the fresh version. Never resubmit the same payload blindly:
  keyed additions would insert twice.

The authoritative response carries the new version. Use it as the next
`If-Match` value, or simply re-read the order before the next edit round.

## Concurrency token

The token is application-managed: `OrderEntity.Version` is a `Guid` marked
with `IsConcurrencyToken`, and `SampleDbContext` assigns a new value on every
save that changes the order, its owned address, or its lines. EF can leave
the order unchanged when only an address or line changes, so the override
bumps the owning order explicitly. This makes the order row's concurrency
predicate cover those writes as well. The mapper keeps the token read-only
(`Map.Ignore` for writes, `Map.Read` for the projection) because only the
context advances it. Request cancellation propagates to the host instead
of mapping to 409 or 412.
