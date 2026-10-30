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
- Each SparseTypes model must be associated with an ordinary OpenAPI response DTO schema (see DTO association below).

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
- Keyed collections are typed as `TKeyedItem` (`TAssignedItem | TUnassignedItem` when the model uses a temporary key): stable `[SparseKey]` members are statically required, and unassigned rows additionally require the temporary key. `OptionalT` itself stays fully deep-optional, and runtime validation still rejects invalid identities from plain JavaScript callers.
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

### `parseT` validation coverage

`parseT(value)` validates a loaded DTO against its OpenAPI schema and returns
the value when it conforms. The validator is dependency-free and implements
the following subset:

| Keyword | OpenAPI 3.0 | OpenAPI 3.1 | Notes |
| --- | --- | --- | --- |
| `type`, `enum`, `const` | Yes | Yes | `type: ["T", "null"]` unions accept `null`; null-only schemas reject non-null values. |
| `nullable` | Yes | Ignored | 3.0 `nullable: true` permits `null` alongside `type`. In 3.1 use a `"null"` type union instead. |
| `required`, `properties`, `additionalProperties` | Yes | Yes | |
| `minProperties`, `maxProperties` | Yes | Yes | |
| `items`, `minItems`, `maxItems`, `uniqueItems` | Yes | Yes | Tuple-form `items` arrays are rejected at generation time. |
| `minLength`, `maxLength`, `pattern` | Yes | Yes | |
| `minimum`, `maximum` | Yes | Yes | |
| `exclusiveMinimum`, `exclusiveMaximum` | Boolean modifiers of `minimum`/`maximum` | Numeric bounds | Both dialect forms are enforced. |
| `multipleOf` | Yes | Yes | Positive divisors only; non-positive divisors fail validation. |
| `allOf`, `anyOf`, `oneOf`, `not` | Yes | Yes | Local `$ref` chains, including recursive schemas, are supported. |
| `format` | `uuid`, `date`, `date-time`, `email` | Same subset | Other formats are parsed as plain strings without format checks. |

Generation fails when a DTO schema uses validation-critical keywords that
`parseT` cannot enforce (`contains`, `prefixItems`, `patternProperties`,
`propertyNames`, `dependentRequired`, tuple-form `items`, and related
keywords), so invalid values are never silently accepted. External `$ref`
targets outside `#/components/schemas/` are likewise rejected. This keeps
the no-runtime-dependency goal: no external schema-validation library is
bundled with the generated code.

### DTO association

A `ChangePayload` request schema is linked to the ordinary DTO returned for
the model. The link is resolved as follows:

1. **Explicit metadata (preferred).** When the payload schema's
   `x-sparsetypes` metadata names `modelSchema` (a key of
   `#/components/schemas/`), that component is used as the DTO. This also
   covers API shapes where the payload endpoint returns `204`/no content,
   where the payload endpoint and the DTO endpoint are independent, or where
   the payload endpoint returns a wrapper/result object. An unknown
   component fails generation; a `modelSchema` that disagrees with a
   genuine-looking (member-overlapping) response DTO also fails with an
   error naming the model and the components.
2. **Legacy response inference (unambiguous only).** Otherwise the generator
   collects every `2xx`/`3xx` response schema of operations carrying the
   payload. Inference applies only when exactly one distinct component is
   found and it shares at least one property with the model's members (a
   response wrapper such as `SaveOrderResult` fails this check instead of
   being silently bound as the model). Ambiguous, wrapper-like, or missing
   mappings fail generation with an error naming the model; add
   `modelSchema` to resolve them.

Child models stay reachable from an associated parent through nested, keyed,
or dictionary properties, as before.

### Numeric precision and lossless DTOs

`ChangePayload` endpoints encode precision-sensitive scalars (`decimal`,
`long`, `ulong`, and 64-bit enums) as strings, so diffs never lose
precision. The ordinary response DTO is a different story: when it carries
those members as JSON numbers, `JSON.parse` rounds them **before** `parseT`
or `buildTChangePayload` runs, and no generated code can restore the lost
digits.

| DTO shape | Lossless? | Notes |
| --- | --- | --- |
| `decimal` as JSON string | Yes | Recommended. Editable states accept strings; payloads encode canonically. |
| `long`/`ulong`/64-bit enum as JSON string | Yes | Recommended. Same handling as `decimal`. |
| `long`/`ulong` as JSON number within ±2^53 | Yes, in practice | Exactly representable; safe numbers pass through unchanged. |
| `long`/`ulong` as JSON number beyond ±2^53 | No | `JSON.parse` rounds to the nearest `double`; precision is unrecoverable. |
| `decimal` as JSON number | No, beyond ~15-17 significant digits | High-scale values (for example `12345678901234567890.1234`) are rounded on parse. |
| Plain `double`/`float`/`int` as JSON number | Yes | Round-trips exactly as the `double` the server emitted. |

Recommended lossless configuration: model precision-sensitive members as
`type: string` in the DTO schemas. The generated `OptionalT` editable types
already accept those string forms (`StringElements`, `StringRecordValues`,
and `DeepOptional<T> | string`), `parseT` validates the DTO as usual, keyed
snapshots keep the strings, and `buildTChangePayload` encodes them
canonically. If the DTO instead uses JSON numbers for such members, the
generated client is lossy for out-of-range values: `encodeWireValue` throws
when a caller supplies an unsafe number (or a non-numeric value) where a
precision string is required, and keyed snapshots throw when a stored
string cannot be preserved as an OpenAPI number.

Pass `requireLosslessPrecision: true` to `generate()` to declare the
losslessness requirement: generation then fails with an error naming the
model and member when a precision-sensitive member is carried as a JSON
number (or an unresolvable schema) in the DTO. The default (`false`) keeps
the permissive behavior and documents the loss here instead of failing.

```ts
await generate({
  input: "./openapi/order-api.json",
  output: "node_modules/@sparsetypes/generated",
  requireLosslessPrecision: true,
});
```

For server setup and OpenAPI schema registration, see the [OpenAPI integration guide](https://github.com/arika0093/SparseTypes/blob/main/docs/how-to/openapi.md).
