# `@sparsetypes/codegen`

[![npm Version](https://img.shields.io/npm/v/@sparsetypes/codegen?style=flat-square&logo=npm)](https://www.npmjs.com/package/@sparsetypes/codegen)

Generate dependency-free JavaScript modules and TypeScript declarations from OpenAPI 3.0 and 3.1 documents that include SparseTypes `ChangePayload` metadata.

The generated package contains:

- `api.d.ts`: OpenAPI path and component declarations produced with `openapi-typescript`.
- `models.js` and `models.d.ts`: model type aliases (`T`), sparse state types (`OptionalT`, `DeepOptional`, `DeepReadonly`), and runtime schema validators (`parseT`).
- `changes.js` and `changes.d.ts`: `buildTChangePayload(before, after)` functions that diff two client states into a version `"0.1"` `ChangePayload` envelope.
- `client.js` and `client.d.ts`: a `fetch`-based `createClient()` helper and `ApiError` class with typed operation methods (`operation(args, init)` and `operationRaw(args, init)`).
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

- `buildTChangePayload(before, after)` returns `undefined` when no tracked members changed.
- Omitted properties and properties set to `undefined` map to `{"state": "missing"}`. Explicit `null` maps to `{"state": "null"}`.
- Nested models, scalar sequences, dictionaries, and `[SparseKey]` collections follow the `x-sparsetypes` member metadata emitted in the OpenAPI document.
- Unassigned keyed rows that use `[SparseTemporaryKey]` require a non-empty UUID in their temporary key property and emit `temporaryKey` plus `tempBeforeOrder` / `tempAfterOrder` when needed.
- Members marked with `[SparseRedactBefore]` emit `{"state": "redacted"}` for their `before` endpoint.
- Redaction propagates into nested models, keyed collections, and dictionaries, so every descendant `before` endpoint is redacted while `after` values still travel. Edited keyed and model-valued dictionary items stay granular with redacted leaf `before` endpoints (per-item redacted endpoints are never paired with item edits); scalar dictionary edits and removals carry redacted item `before` endpoints as blind operations.
- No redacted collection operation requires whole-member replacement: granular blind edits, blind removals, and redacted nested updates are all accepted by the server. Blind whole-collection removal (a missing `after` with a redacted `before`) has no patch projection and throws a safe error naming only the member path instead of emitting an unprojectable payload.
- Precision-sensitive scalar wire types (`decimal`, `long`, `ulong`, and 64-bit enums) accept strings or safe numbers in `OptionalT` and serialize as canonical strings in change endpoints.

For server setup and OpenAPI schema registration, see the [OpenAPI integration guide](https://github.com/arika0093/SparseTypes/blob/main/docs/how-to/openapi.md).
