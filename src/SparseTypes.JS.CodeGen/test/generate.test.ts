import assert from "node:assert/strict";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { execFileSync } from "node:child_process";
import os from "node:os";
import path from "node:path";
import { pathToFileURL } from "node:url";
import test from "node:test";
import { generate } from "../src/generate.js";
import type { OpenApiDocument } from "../src/openapi.js";

const orderModel = "Sample.Contracts.OrderDto";
const lineModel = "Sample.Contracts.OrderLineDto";
const schemaIdentity = "sha256:" + "a".repeat(64);

function document(): OpenApiDocument {
  const memberMetadata = (members: Record<string, any>[]) => ({
    version: 1,
    role: "members",
    model: orderModel,
    schemaIdentity,
    members,
  });
  const schema = (properties: Record<string, any>) => ({
    type: "object",
    properties,
  });
  return {
    openapi: "3.1.0",
    info: { title: "Test", version: "1.0" },
    servers: [{ url: "/" }],
    paths: {
      "/orders/{id}": {
        get: {
          operationId: "getOrder",
          parameters: [
            {
              name: "id",
              in: "path",
              required: true,
              schema: { type: "integer" },
            },
          ],
          responses: {
            "200": {
              description: "OK",
              content: {
                "application/json": {
                  schema: { $ref: "#/components/schemas/OrderDto" },
                },
              },
            },
          },
        },
        put: {
          operationId: "saveOrder",
          parameters: [
            {
              name: "id",
              in: "path",
              required: true,
              schema: { type: "integer" },
            },
            {
              name: "If-Match",
              in: "header",
              required: true,
              schema: { type: "string" },
            },
          ],
          requestBody: {
            required: true,
            content: {
              "application/json": {
                schema: { $ref: "#/components/schemas/ChangePayload" },
              },
            },
          },
          responses: {
            "200": {
              description: "OK",
              content: {
                "application/json": {
                  schema: { $ref: "#/components/schemas/OrderDto" },
                },
              },
            },
            "409": {
              description: "Conflict",
              content: {
                "application/json": {
                  schema: {
                    type: "array",
                    items: { $ref: "#/components/schemas/OrderConflict" },
                  },
                },
              },
            },
          },
        },
      },
    },
    components: {
      schemas: {
        OrderDto: schema({
          name: { type: "string" },
          notes: { type: ["string", "null"] },
          secret: { type: "string" },
          tags: { type: "array", items: { type: "string" } },
          quantities: {
            type: "object",
            additionalProperties: { type: "integer" },
          },
          prices: {
            type: "object",
            additionalProperties: { type: ["number", "null"] },
          },
          address: { $ref: "#/components/schemas/AddressDto" },
          lines: {
            type: "array",
            items: { $ref: "#/components/schemas/OrderLineDto" },
          },
          version: { type: "string" },
        }),
        AddressDto: schema({ city: { type: "string" } }),
        OrderLineDto: schema({
          id: { type: "integer" },
          key: { type: "integer" },
          temporaryId: { type: ["string", "null"] },
          quantity: { type: "integer" },
          unitPrice: { type: "number" },
        }),
        OrderLineMembers: {
          ...schema({}),
          "x-sparsetypes": {
            version: 1,
            role: "members",
            model: lineModel,
            schemaIdentity,
            members: [
              {
                name: "id",
                shape: "value",
                nullable: false,
                redactBefore: false,
              },
              {
                name: "key",
                shape: "value",
                nullable: false,
                redactBefore: false,
              },
              {
                name: "temporaryId",
                shape: "value",
                nullable: true,
                redactBefore: false,
              },
              {
                name: "quantity",
                shape: "value",
                nullable: false,
                redactBefore: false,
              },
              {
                name: "unitPrice",
                shape: "value",
                nullable: false,
                redactBefore: false,
                wireType: "decimal-string",
              },
            ],
          },
        },
        OrderLineChangePayload: {
          ...schema({}),
          "x-sparsetypes": {
            version: 1,
            role: "payload",
            model: lineModel,
            schemaIdentity,
            membersComponent: "OrderLineMembers",
          },
        },
        OrderConflict: schema({
          path: { type: "string" },
          kind: { type: "string" },
        }),
        ChangePayload: {
          ...schema({
            version: { const: "0.1" },
            schema: { type: "string" },
            change: { type: "object" },
          }),
          "x-sparsetypes": {
            version: 1,
            role: "payload",
            model: orderModel,
            schemaIdentity,
            membersComponent: "OrderMembers",
          },
        },
        OrderMembers: {
          ...schema({}),
          "x-sparsetypes": memberMetadata([
            {
              name: "name",
              shape: "value",
              nullable: false,
              redactBefore: false,
            },
            {
              name: "notes",
              shape: "value",
              nullable: true,
              redactBefore: false,
            },
            {
              name: "secret",
              shape: "value",
              nullable: false,
              redactBefore: true,
            },
            {
              name: "tags",
              shape: "sequence",
              nullable: false,
              redactBefore: false,
              elementWireType: "string",
            },
            {
              name: "quantities",
              shape: "dictionary",
              nullable: false,
              redactBefore: false,
              valueWireType: "integer-number",
            },
            {
              name: "prices",
              shape: "dictionary",
              nullable: false,
              redactBefore: false,
              valueNullable: true,
              valueWireType: "decimal-string",
            },
            {
              name: "address",
              shape: "nested",
              nullable: true,
              redactBefore: false,
              childModel: "Sample.Contracts.AddressDto",
            },
            {
              name: "lines",
              shape: "keyed",
              nullable: false,
              redactBefore: false,
              itemModel: lineModel,
              keyProperties: ["key"],
              keyType: "integer-number",
              unassignedKey: 0,
              temporaryKey: "temporaryId",
              ordered: true,
            },
          ]),
        },
        AddressMembers: {
          ...schema({}),
          "x-sparsetypes": {
            version: 1,
            role: "members",
            model: "Sample.Contracts.AddressDto",
            schemaIdentity,
            members: [
              {
                name: "city",
                shape: "value",
                nullable: false,
                redactBefore: false,
                wireType: "string",
              },
            ],
          },
        },
        AddressChangePayload: {
          ...schema({}),
          "x-sparsetypes": {
            version: 1,
            role: "payload",
            model: "Sample.Contracts.AddressDto",
            schemaIdentity,
            membersComponent: "AddressMembers",
          },
        },
      },
    },
  };
}

test("defaults generated files to the @sparsetypes/generated package", async () => {
  const workingDirectory = await mkdtemp(
    path.join(os.tmpdir(), "sparsetypes-codegen-default-"),
  );
  const previousDirectory = process.cwd();
  try {
    process.chdir(workingDirectory);
    await generate({ input: document() });
    const output = path.join(
      workingDirectory,
      "node_modules/@sparsetypes/generated",
    );
    const packageJson = JSON.parse(
      await readFile(path.join(output, "package.json"), "utf8"),
    );
    assert.equal(packageJson.name, "@sparsetypes/generated");
    assert.match(
      await readFile(path.join(output, "index.js"), "utf8"),
      /client\.js/,
    );
  } finally {
    process.chdir(previousDirectory);
    await rm(workingDirectory, { recursive: true, force: true });
  }
});

test("generates API types, model declarations, ChangePayload diff, and fetch client", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-"));
  try {
    await generate({ input: document(), output });
    const generated = await readFile(path.join(output, "changes.js"), "utf8");
    const apiTypes = await readFile(path.join(output, "api.d.ts"), "utf8");
    const clientTypes = await readFile(
      path.join(output, "client.d.ts"),
      "utf8",
    );
    assert.match(generated, /buildOrderDtoChangePayload/);
    assert.match(apiTypes, /export interface paths/);
    assert.match(clientTypes, /saveOrderRaw/);
    assert.match(clientTypes, /ApiError/);
    const modelsTypes = await readFile(
      path.join(output, "models.d.ts"),
      "utf8",
    );
    const indexTypes = await readFile(path.join(output, "index.d.ts"), "utf8");
    assert.match(modelsTypes, /OptionalOrderDto/);
    assert.match(indexTypes, /from "\.\/models\.js"/);
    await writeFile(
      path.join(output, "consumer.ts"),
      `
import { buildOrderDtoChangePayload, createClient, parseOrderDto } from "./index.js";
import type { OptionalOrderDto, OptionalOrderLineDto, OrderDto } from "./index.js";
const dto: OrderDto = { name: "Order" };
// @ts-expect-error DTO nullability follows the OpenAPI contract.
const invalidDto: OrderDto = { name: null };
const state: OptionalOrderDto = { name: undefined, lines: Object.freeze([]) };
const preciseLine: OptionalOrderLineDto = { key: 0, unitPrice: "12345678901234567890.1234" };
const order: OptionalOrderDto = { lines: [preciseLine], address: { city: "Tokyo" }, prices: { exact: "12345678901234567890.12", empty: null } };
buildOrderDtoChangePayload(state, state);
buildOrderDtoChangePayload(state, order);
parseOrderDto(dto);
const client = createClient();
void client.getOrder({ params: { path: { id: 1 } } });
void client.saveOrder({ params: { path: { id: 1 } }, headers: { "If-Match": "version" }, body: {} });
// @ts-expect-error OpenAPI requires the If-Match header.
void client.saveOrder({ params: { path: { id: 1 } }, body: {} });
// @ts-expect-error OpenAPI requires the request body.
void client.saveOrder({ params: { path: { id: 1 } }, headers: { "If-Match": "version" } });
`,
    );
    execFileSync(
      "tsgo",
      [
        "--ignoreConfig",
        "--noEmit",
        "--strict",
        "--exactOptionalPropertyTypes",
        "--lib",
        "ES2022,DOM",
        "--module",
        "ESNext",
        "--moduleResolution",
        "Bundler",
        path.join(output, "consumer.ts"),
      ],
      { stdio: "inherit" },
    );

    const changes = await import(
      pathToFileURL(path.join(output, "changes.js")).href
    );
    const before = Object.freeze({
      name: "Before",
      notes: null,
      lines: Object.freeze([
        Object.freeze({
          id: 1,
          key: 1,
          temporaryId: null,
          quantity: 1,
          unitPrice: 3,
        }),
        Object.freeze({
          id: 2,
          key: 2,
          temporaryId: null,
          quantity: 1,
          unitPrice: 4,
        }),
      ]),
    });
    const after = Object.freeze({
      name: "After",
      lines: Object.freeze([
        Object.freeze({
          id: 1,
          key: 1,
          temporaryId: null,
          quantity: 2,
          unitPrice: 3,
        }),
        Object.freeze({
          id: 0,
          key: 0,
          temporaryId: "f4ef9705-b5e2-41f6-a7ac-1f3b0a8c14de",
          quantity: 1,
          unitPrice: 5,
        }),
      ]),
    });
    const payload = changes.buildOrderDtoChangePayload(before, after);
    assert.deepEqual(payload, {
      version: "0.1",
      schema: schemaIdentity,
      change: {
        kind: "update",
        members: {
          name: {
            kind: "replace",
            before: { state: "value", value: "Before" },
            after: { state: "value", value: "After" },
          },
          notes: {
            kind: "replace",
            before: { state: "null" },
            after: { state: "missing" },
          },
          lines: {
            kind: "update",
            items: [
              {
                key: 1,
                kind: "update",
                beforeIndex: 0,
                afterIndex: 0,
                members: {
                  quantity: {
                    kind: "replace",
                    before: { state: "value", value: 1 },
                    after: { state: "value", value: 2 },
                  },
                },
              },
              {
                key: 0,
                kind: "add",
                afterIndex: 1,
                temporaryKey: "f4ef9705-b5e2-41f6-a7ac-1f3b0a8c14de",
                after: {
                  state: "value",
                  value: {
                    id: 0,
                    key: 0,
                    temporaryId: "f4ef9705-b5e2-41f6-a7ac-1f3b0a8c14de",
                    quantity: 1,
                    unitPrice: 5,
                  },
                },
              },
              {
                key: 2,
                kind: "remove",
                beforeIndex: 1,
                before: {
                  state: "value",
                  value: {
                    id: 2,
                    key: 2,
                    temporaryId: null,
                    quantity: 1,
                    unitPrice: 4,
                  },
                },
              },
            ],
            beforeOrder: [1, 2],
            afterOrder: [1, 0],
            tempBeforeOrder: [],
            tempAfterOrder: ["f4ef9705-b5e2-41f6-a7ac-1f3b0a8c14de"],
          },
        },
      },
    });
    assert.deepEqual(
      before.lines.map((line) => line.quantity),
      [1, 1],
    );
    assert.equal(changes.buildOrderDtoChangePayload(before, before), undefined);
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});

test("distinguishes missing from null and rejects duplicate keyed identities", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-"));
  try {
    await generate({ input: document(), output });
    const changes = await import(
      pathToFileURL(path.join(output, "changes.js")).href
    );
    const modelModule = await import(
      pathToFileURL(path.join(output, "models.js")).href
    );
    assert.deepEqual(modelModule.parseOrderDto({ name: "valid" }), {
      name: "valid",
    });
    assert.throws(
      () => modelModule.parseOrderDto({ name: 123 }),
      /name: has an invalid JSON type/,
    );
    const removed = changes.buildOrderDtoChangePayload({ name: "A" }, {});
    assert.deepEqual(removed.change.members.name.after, { state: "missing" });
    const setNull = changes.buildOrderDtoChangePayload(
      { notes: undefined },
      { notes: null },
    );
    assert.deepEqual(setNull.change.members.notes.before, { state: "missing" });
    assert.deepEqual(setNull.change.members.notes.after, { state: "null" });
    const decimalBefore = {
      lines: [
        {
          id: 1,
          key: 1,
          quantity: 1,
          unitPrice: "12345678901234567890.123400",
        },
      ],
    };
    const decimalAfter = {
      lines: [
        { id: 1, key: 1, quantity: 1, unitPrice: "12345678901234567890.1234" },
      ],
    };
    assert.equal(
      changes.buildOrderDtoChangePayload(decimalBefore, decimalAfter),
      undefined,
    );
    const precise = changes.buildOrderDtoChangePayload(decimalBefore, {
      lines: [
        {
          id: 1,
          key: 1,
          quantity: 1,
          unitPrice: "12345678901234567890.123401",
        },
      ],
    });
    assert.equal(
      precise.change.members.lines.items[0].members.unitPrice.after.value,
      "12345678901234567890.123401",
    );
    const priceChanges = changes.buildOrderDtoChangePayload(
      { prices: { unchanged: "1.00", removed: "2.00" } },
      { prices: { unchanged: "1", added: null } },
    );
    assert.deepEqual(priceChanges.change.members.prices.items, [
      { key: "added", kind: "add", after: { state: "null" } },
      {
        key: "removed",
        kind: "remove",
        before: { state: "value", value: "2.00" },
      },
    ]);
    const representableSnapshot = changes.buildOrderDtoChangePayload(
      { lines: [] },
      {
        lines: [
          {
            id: 0,
            key: 0,
            temporaryId: "f4ef9705-b5e2-41f6-a7ac-1f3b0a8c14de",
            quantity: 1,
            unitPrice: "0.1",
          },
        ],
      },
    );
    assert.equal(
      representableSnapshot.change.members.lines.items[0].after.value.unitPrice,
      0.1,
    );
    assert.throws(
      () =>
        changes.buildOrderDtoChangePayload(
          { lines: [] },
          {
            lines: [
              {
                id: 0,
                key: 0,
                temporaryId: "f4ef9705-b5e2-41f6-a7ac-1f3b0a8c14de",
                quantity: 1,
                unitPrice: "12345678901234567890.1234",
              },
            ],
          },
        ),
      /cannot preserve this decimal/,
    );
    assert.throws(
      () =>
        changes.buildOrderDtoChangePayload(
          {
            lines: [
              { id: 1, key: 1, quantity: 1 },
              { id: 1, key: 1, quantity: 2 },
            ],
          },
          { lines: [] },
        ),
      /Duplicate keyed collection identity/,
    );
    assert.throws(
      () => changes.buildOrderDtoChangePayload({}, { lines: [undefined] }),
      /cannot contain undefined/,
    );
    const cyclic: Record<string, unknown> = {};
    cyclic.self = cyclic;
    assert.throws(
      () => changes.buildOrderDtoChangePayload(cyclic, {}),
      /cyclic/,
    );
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});

test("redacts removed keyed values only when the member requests it", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-"));
  try {
    const input = document();
    const orderMembers = input.components.schemas.OrderMembers[
      "x-sparsetypes"
    ] as {
      members: Array<{ name: string; redactBefore?: boolean }>;
    };
    const lines = orderMembers.members.find(
      (member) => member.name === "lines",
    );
    assert.ok(lines);
    lines.redactBefore = true;

    await generate({ input, output });
    const changes = await import(
      pathToFileURL(path.join(output, "changes.js")).href
    );
    const payload = changes.buildOrderDtoChangePayload(
      {
        lines: [{ id: 1, key: 1, quantity: 1, unitPrice: 3 }],
      },
      { lines: [] },
    );

    assert.deepEqual(payload.change.members.lines.items[0].before, {
      state: "redacted",
    });
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});

test("generates fetch calls with structured errors, raw responses, and RequestInit overrides", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-"));
  try {
    await generate({ input: document(), output });
    const clientModule = await import(
      pathToFileURL(path.join(output, "client.js")).href
    );
    const calls: Array<{ url: RequestInfo | URL; init: RequestInit }> = [];
    const client = clientModule.createClient({
      baseUrl: "https://example.test/api",
      fetch: async (url: RequestInfo | URL, init?: RequestInit) => {
        calls.push({ url, init: init ?? {} });
        return new Response(JSON.stringify({ name: "Saved" }), {
          status: 200,
          headers: { "content-type": "application/json" },
        });
      },
    });
    const saved = await client.saveOrder(
      {
        params: { path: { id: 42 } },
        headers: { "If-Match": "v1" },
        body: { version: "0.1" },
      },
      { credentials: "include" },
    );
    assert.deepEqual(saved, { name: "Saved" });
    const firstCall = calls[0];
    assert.ok(firstCall);
    assert.equal(firstCall.url, "https://example.test/api/orders/42");
    assert.equal(firstCall.init.method, "PUT");
    assert.equal(firstCall.init.credentials, "include");
    const headers = new Headers(firstCall.init.headers);
    assert.equal(headers.get("if-match"), "v1");
    assert.equal(headers.get("content-type"), "application/json");
    assert.deepEqual(JSON.parse(String(firstCall.init.body)), {
      version: "0.1",
    });

    const validationClient = clientModule.createClient({
      fetch: async () => {
        throw new Error("unexpected fetch");
      },
    });
    await assert.rejects(
      validationClient.saveOrder({ params: { path: { id: 42 } }, body: {} }),
      /Missing header parameter 'If-Match'/,
    );
    await assert.rejects(
      validationClient.saveOrder({
        params: { path: { id: 42 } },
        headers: { "If-Match": "v1" },
      }),
      /Missing required request body/,
    );

    await assert.rejects(
      clientModule
        .createClient({
          fetch: async () =>
            new Response(JSON.stringify({ conflict: true }), {
              status: 409,
              headers: { "content-type": "application/json" },
            }),
        })
        .saveOrder({
          params: { path: { id: 42 } },
          headers: { "If-Match": "v1" },
          body: {},
        }),
      (error: unknown) => {
        assert.ok(error instanceof Error);
        const apiError = error as Error & {
          status: number;
          body: unknown;
          response: Response;
        };
        assert.equal(apiError.status, 409);
        assert.deepEqual(apiError.body, { conflict: true });
        assert.ok(apiError.response instanceof Response);
        return true;
      },
    );

    let rawResponse: Response | undefined;
    const rawClient = clientModule.createClient({
      fetch: async (_url: RequestInfo | URL, init?: RequestInit) => {
        assert.ok(init);
        assert.equal(init.method, "PATCH");
        assert.equal(new Headers(init.headers).has("content-type"), false);
        rawResponse = new Response("not parsed", { status: 409 });
        return rawResponse;
      },
    });
    const response = await rawClient.saveOrderRaw(
      {
        params: { path: { id: 42 } },
        headers: { "If-Match": "v1" },
        body: {},
      },
      { method: "PATCH", body: "raw" },
    );
    assert.equal(response, rawResponse);
    assert.equal(response?.bodyUsed, false);
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});

test("generates nested, sequence, dictionary, and redacted member changes safely", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-"));
  try {
    await generate({ input: document(), output });
    const changes = await import(
      pathToFileURL(path.join(output, "changes.js")).href
    );
    const beforeQuantities = Object.create(null);
    beforeQuantities.existing = 1;
    beforeQuantities.removed = 2;
    const afterQuantities = Object.create(null);
    afterQuantities.existing = 4;
    afterQuantities.added = 5;
    Object.defineProperty(afterQuantities, "__proto__", {
      value: 3,
      enumerable: true,
    });

    const payload = changes.buildOrderDtoChangePayload(
      {
        secret: "before",
        tags: ["first", "second"],
        quantities: beforeQuantities,
        address: { city: "Old" },
      },
      {
        secret: "after",
        tags: ["second", "first"],
        quantities: afterQuantities,
        address: { city: "New" },
      },
    );

    assert.deepEqual(payload.change.members.secret.before, {
      state: "redacted",
    });
    assert.deepEqual(payload.change.members.tags, {
      kind: "replace",
      before: { state: "value", value: ["first", "second"] },
      after: { state: "value", value: ["second", "first"] },
    });
    assert.deepEqual(payload.change.members.quantities.items, [
      {
        key: "existing",
        kind: "replace",
        before: { state: "value", value: 1 },
        after: { state: "value", value: 4 },
      },
      { key: "added", kind: "add", after: { state: "value", value: 5 } },
      { key: "__proto__", kind: "add", after: { state: "value", value: 3 } },
      {
        key: "removed",
        kind: "remove",
        before: { state: "value", value: 2 },
      },
    ]);
    assert.deepEqual(payload.change.members.address.members, {
      city: {
        kind: "replace",
        before: { state: "value", value: "Old" },
        after: { state: "value", value: "New" },
      },
    });
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});
