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
- EfCoreSample.Client: WPF order editor. The form binds to the live edit
  session's observable proxy; save ships a sparse change payload.
- EfCoreSample.AppHost: Aspire host wiring PostgreSQL (with pgweb), the
  server, and the client.

## Run

Start PostgreSQL, pgweb, the server, and the client through Aspire:

```bash
dotnet run --project samples/EfCoreSample/EfCoreSample.AppHost
```

The client starts on demand from the Aspire dashboard. The server listens on
`http://localhost:5000`, so `/` opens the Swagger UI and
`GET http://localhost:5000/orders/1` returns the seeded order, including its
concurrency token.

The server alone also runs standalone against any reachable PostgreSQL:

```bash
ConnectionStrings__orders="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=orders" dotnet run --project samples/EfCoreSample/EfCoreSample.Server
```

Without `ConnectionStrings__orders` the server falls back to the local
defaults shown above.

## Update contract

`PUT /orders/{id}` takes a change payload body and requires the version
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
