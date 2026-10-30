# `@sparsetypes/codegen`

[![npm Version](https://img.shields.io/npm/v/@sparsetypes/codegen?style=flat-square&logo=npm)](https://www.npmjs.com/package/@sparsetypes/codegen)

Generate dependency-free JavaScript modules and TypeScript declarations from OpenAPI 3.0 and 3.1 documents that include SparseTypes `ChangePayload` metadata.

The generated package contains:

- `api.d.ts`: OpenAPI path and component declarations produced with `openapi-typescript`.
- `models.js` and `models.d.ts`: model type aliases (`T`), sparse state types (`OptionalT`, `DeepOptional`, `DeepReadonly`), and runtime schema validators (`parseT`).
- `changes.js` and `changes.d.ts`: `buildTChangePayload(before, after)` functions that diff two client states into a version `"0.1"` `ChangePayload` envelope.
- `client.js` and `client.d.ts`: a `fetch`-based `createClient()` helper and `ApiError` class with typed operation methods (`operation(args, init)` and `operationRaw(args, init)`). Method names derive from `operationId` (sanitized to camelCase, with a path-derived fallback and numeric suffixes for collisions); each name and its `Raw` variant share one namespace, so `foo` plus `fooRaw` yields `foo` and `fooRaw2` instead of overwriting an operation.
- `index.js`, `index.d.ts`, and `package.json`: an ES module entry point (`@sparsetypes/generated` by default).

The generated code has no runtime npm dependencies and runs in browsers and Node.js.

## Requirements

- Node.js 20 or later to run the generator.
- An OpenAPI 3.0 or 3.1 JSON document emitted by a server that registers `T.PayloadJson.Schema` with `SparseTypes.OpenApi.AspNetCore` or `SparseTypes.OpenApi.Swashbuckle`.
- Each SparseTypes model must also appear in an ordinary OpenAPI response schema (or be reachable from one through nested, keyed, or dictionary properties) so the generator can associate the `ChangePayload` metadata with its DTO schema.

## Install

```bash
npm install --save-dev @sparsetypes/codegen
```

## CLI usage

Pass the OpenAPI JSON path to `sparsetypes-codegen`. By default, files are written to `node_modules/@sparsetypes/generated` so application code can import `@sparsetypes/generated` directly:

```bash
npx sparsetypes-codegen ./openapi/order-api.json
```

Use `--output` to write the files to another directory:

```bash
npx sparsetypes-codegen ./openapi/order-api.json --output ./src/generated
```

## Programmatic API

```ts
import { generate } from "@sparsetypes/codegen";

await generate({
  input: "./openapi/order-api.json",
  output: "node_modules/@sparsetypes/generated",
});
```

`input` accepts a file path, a ` file:` `URL`, or a parsed OpenAPI document object.

## Using the generated module

```ts
import {
  ApiError,
  buildOrderDtoChangePayload,
  createClient,
  parseOrderDto,
  type OptionalOrderDto,
} from "@sparsetypes/generated";

const api = createClient({ baseUrl: "https://example.test" });

const loaded = parseOrderDto(
  await api.getOrder({ params: { path: { id: 1 } } }),
);

const draft: OptionalOrderDto = {
  ...loaded,
  name: "Updated name",
};

const body = buildOrderDtoChangePayload(loaded, draft);
if (body) {
  try {
    const saved = parseOrderDto(
      await api.saveOrder({
        params: { path: { id: 1 } },
        headers: { "If-Match": `"${loaded.version}"` },
        body,
      }),
    );
  } catch (error) {
    if (error instanceof ApiError && error.status === 409) {
      console.error("Conflict:", error.body);
    }
  }
}
```

### Diff and payload semantics

`createClient()` serializes `args.body` according to the single media type
declared in `requestBody.content`, and sets `Content-Type` to that media type:

- `application/json` (or any `+json` suffix): `JSON.stringify`. The declared
  type is used as-is when several JSON-compatible types are listed.
- `text/*`: the body must be a string and is sent unchanged (never JSON
  encoded).
- `application/octet-stream`: the body must be a `Blob`, `ArrayBuffer`,
  array-buffer view, or string, and is sent unchanged.
- `application/x-www-form-urlencoded`: the body must be a plain object
  (array values become repeated keys) or `URLSearchParams`.
- `multipart/form-data`: the body must be `FormData` or a plain object.
  Plain objects are converted field-by-field (`Blob` values pass through,
  other objects are JSON encoded). `Content-Type` is left for `fetch` to set
  so the multipart boundary is preserved; `null`/`undefined` fields are
  skipped.

Operations declaring any other request media type, more than one request
serialization kind, or an unsupported 2xx/3xx response media type fail code
generation with an error naming the operation and the offending type.

Responses are decoded from the response `Content-Type` header: JSON media is
parsed, `text/*` is returned as text, `204`/`205`/`304` and empty bodies
yield `undefined`, and any other binary media is returned as a `Blob` (binary
is never silently converted to text). The `*Raw` methods always return the
untouched `Response`, so callers can decode custom media themselves.

- `buildTChangePayload(before, after)` returns `undefined` when no tracked members changed.
- Omitted properties and properties set to `undefined` map to `{"state": "missing"}`. Explicit `null` maps to `{"state": "null"}`.
- Nested models, scalar sequences, dictionaries, and `[SparseKey]` collections follow the `x-sparsetypes` member metadata emitted in the OpenAPI document.
- Unassigned keyed rows that use `[SparseTemporaryKey]` require a non-empty UUID in their temporary key property and emit `temporaryKey` plus `tempBeforeOrder` / `tempAfterOrder` when needed.
- GUID keys and temporary IDs compare and emit in canonical lowercase D-form (matching C# `Guid` equality): a case-only spelling change is not a change, duplicates are detected after normalization, and an invalid GUID on a GUID-typed key fails with an error. Other snapshots keep their original spelling.
- Members marked with `[SparseRedactBefore]` emit `{"state": "redacted"}` for their `before` endpoint.
- Redaction propagates into nested models, keyed collections, and dictionaries, so every descendant `before` endpoint is redacted while `after` values still travel. Edited keyed and model-valued dictionary items stay granular with redacted leaf `before` endpoints (per-item redacted endpoints are never paired with item edits); scalar dictionary edits and removals carry redacted item `before` endpoints as blind operations.
- No redacted collection operation requires whole-member replacement: granular blind edits, blind removals, and redacted nested updates are all accepted by the server. Blind whole-collection removal (a missing `after` with a redacted `before`) has no patch projection and throws a safe error naming only the member path instead of emitting an unprojectable payload.
- Precision-sensitive scalar wire types (`decimal`, `long`, `ulong`, and 64-bit enums) accept canonical strings or safe numbers in `OptionalT` and serialize as canonical strings in change endpoints. Canonical form is `[-]digits[.fraction]` for decimals (no exponent, plus sign, whitespace, or leading zeros; scale up to 28, at most 29 significant digits) and `[-]digits` for 64-bit integers within bounds; non-canonical inputs (exponent, `+1`, `001`, whitespace) throw a `TypeError` instead of rounding. Decimal scale (trailing fraction zeros) is preserved and negative zero is emitted as `0` with its scale.

### Request and response media types

`createClient()` serializes `args.body` according to the single media type
declared in `requestBody.content`, and sets `Content-Type` to that media type:

- `application/json` (or any `+json` suffix): `JSON.stringify`. The declared
  type is used as-is when several JSON-compatible types are listed.
- `text/*`: the body must be a string and is sent unchanged (never JSON
  encoded).
- `application/octet-stream`: the body must be a `Blob`, `ArrayBuffer`,
  array-buffer view, or string, and is sent unchanged.
- `application/x-www-form-urlencoded`: the body must be a plain object
  (array values become repeated keys) or `URLSearchParams`.
- `multipart/form-data`: the body must be `FormData` or a plain object.
  Plain objects are converted field-by-field (`Blob` values pass through,
  other objects are JSON encoded). `Content-Type` is left for `fetch` to set
  so the multipart boundary is preserved; `null`/`undefined` fields are
  skipped.

Operations declaring any other request media type, more than one request
serialization kind, or an unsupported 2xx/3xx response media type fail code
generation with an error naming the operation and the offending type.

Responses are decoded from the response `Content-Type` header: JSON media is
parsed, `text/*` is returned as text, `204`/`205`/`304` and empty bodies
yield `undefined`, and any other binary media is returned as a `Blob` (binary
is never silently converted to text). The `*Raw` methods always return the
untouched `Response`, so callers can decode custom media themselves.

### Base URLs and server overrides

Each operation uses its resolved server URL as its default base: an
Operation-level `servers` entry wins over a PathItem-level entry, which wins
over the document-level `servers`. The first entry of the winning `servers`
array is used. Server variables (`{name}`) are substituted from their
`default` values (validated against `enum`); a variable without a default or
with a default outside its `enum` fails code generation. Relative server URLs
are resolved against the nearest absolute ancestor server URL, or kept
relative otherwise.

Passing `baseUrl` to `createClient({ baseUrl })` overrides every server URL
for all operations, which is useful for proxies, mocks, and tests. Operation
paths and query parameters are appended to whichever base is in effect, so a
trailing slash on the base never produces a double slash.

For server setup and OpenAPI schema registration, see the [OpenAPI integration guide](https://github.com/arika0093/SparseTypes/blob/main/docs/how-to/openapi.md).
