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
          contacts: {
            type: "object",
            additionalProperties: { $ref: "#/components/schemas/ChildDto" },
          },
          lines: {
            type: "array",
            items: { $ref: "#/components/schemas/OrderLineDto" },
          },
          version: { type: "string" },
        }),
        AddressDto: schema({ city: { type: "string" } }),
        ChildDto: schema({
          name: { type: "string" },
          secret: { type: "string" },
        }),
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
              name: "contacts",
              shape: "dictionary",
              nullable: true,
              redactBefore: true,
              valueNullable: true,
              itemModel: "Sample.Contracts.ChildDto",
              keyProperties: [],
              ordered: false,
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
        ChildMembers: {
          ...schema({}),
          "x-sparsetypes": {
            version: 1,
            role: "members",
            model: "Sample.Contracts.ChildDto",
            schemaIdentity,
            members: [
              {
                name: "name",
                shape: "value",
                nullable: false,
                redactBefore: false,
              },
              {
                name: "secret",
                shape: "value",
                nullable: false,
                redactBefore: true,
              },
            ],
          },
        },
        ChildChangePayload: {
          ...schema({}),
          "x-sparsetypes": {
            version: 1,
            role: "payload",
            model: "Sample.Contracts.ChildDto",
            schemaIdentity,
            membersComponent: "ChildMembers",
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

test("compares precision-sensitive sequences element by element", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-"));
  try {
    const input = document();
    const members = (
      input.components.schemas.OrderMembers["x-sparsetypes"] as {
        members: Array<Record<string, unknown>>;
      }
    ).members;
    members.push(
      {
        name: "decimals",
        shape: "sequence",
        nullable: false,
        redactBefore: false,
        elementWireType: "decimal-string",
      },
      {
        name: "longs",
        shape: "sequence",
        nullable: false,
        redactBefore: false,
        elementWireType: "int64-string",
      },
      {
        name: "ulongs",
        shape: "sequence",
        nullable: true,
        redactBefore: false,
        elementWireType: "uint64-string",
      },
    );
    const dto = input.components.schemas.OrderDto as {
      properties: Record<string, unknown>;
    };
    dto.properties = {
      ...dto.properties,
      decimals: { type: "array", items: { type: "string" } },
      longs: { type: "array", items: { type: "string" } },
      ulongs: { type: "array", items: { type: "string" } },
    };
    await generate({ input, output });
    const changes = await import(
      pathToFileURL(path.join(output, "changes.js")).href
    );
    assert.equal(
      changes.buildOrderDtoChangePayload(
        { decimals: ["1.0", "2.50"] },
        { decimals: ["1.00", "2.5"] },
      ),
      undefined,
    );
    assert.equal(
      changes.buildOrderDtoChangePayload(
        { longs: ["1", "2"] },
        { longs: ["1", "2"] },
      ),
      undefined,
    );
    assert.equal(
      changes.buildOrderDtoChangePayload(
        { ulongs: ["0", "18446744073709551615"] },
        { ulongs: ["0", "18446744073709551615"] },
      ),
      undefined,
    );
    const decimalDiff = changes.buildOrderDtoChangePayload(
      { decimals: ["1.0", "2.0"] },
      { decimals: ["1.0", "2.5"] },
    );
    assert.deepEqual(decimalDiff.change.members.decimals.after, {
      state: "value",
      value: ["1.0", "2.5"],
    });
    const longDiff = changes.buildOrderDtoChangePayload(
      { longs: ["1", "2"] },
      { longs: ["1", "3"] },
    );
    assert.ok(longDiff.change.members.longs);
    const ulongDiff = changes.buildOrderDtoChangePayload(
      { ulongs: ["1"] },
      { ulongs: ["2"] },
    );
    assert.ok(ulongDiff.change.members.ulongs);
    assert.equal(
      changes.buildOrderDtoChangePayload(
        { decimals: [null, "1.0"] },
        { decimals: [null, "1.00"] },
      ),
      undefined,
    );
    const nullDiff = changes.buildOrderDtoChangePayload(
      { decimals: [null, "1.0"] },
      { decimals: [null, "2.0"] },
    );
    assert.ok(nullDiff.change.members.decimals);
    assert.equal(
      changes.buildOrderDtoChangePayload({ decimals: [] }, { decimals: [] }),
      undefined,
    );
    const emptyDiff = changes.buildOrderDtoChangePayload(
      { decimals: [] },
      { decimals: ["1.0"] },
    );
    assert.ok(emptyDiff.change.members.decimals);
    assert.equal(
      changes.buildOrderDtoChangePayload(
        { decimals: ["1.0", "1.0"] },
        { decimals: ["1.00", "1.00"] },
      ),
      undefined,
    );
    const lengthDiff = changes.buildOrderDtoChangePayload(
      { longs: ["1"] },
      { longs: ["1", "2"] },
    );
    assert.ok(lengthDiff.change.members.longs);
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});

test("encodes query parameters exactly once and preserves style separators", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-"));
  try {
    const input = document();
    input.paths["/search"] = {
      get: {
        operationId: "search",
        parameters: [
          { name: "term", in: "query", schema: { type: "string" }, style: "form", explode: true },
          { name: "tags", in: "query", schema: { type: "array", items: { type: "string" } }, style: "form", explode: true },
          { name: "ids", in: "query", schema: { type: "array", items: { type: "string" } }, style: "form", explode: false },
          { name: "coords", in: "query", schema: { type: "array", items: { type: "string" } }, style: "spaceDelimited", explode: false },
          { name: "pipes", in: "query", schema: { type: "array", items: { type: "string" } }, style: "pipeDelimited", explode: false },
          { name: "filter", in: "query", schema: { type: "object" }, style: "deepObject", explode: true },
          { name: "objExplode", in: "query", schema: { type: "object" }, style: "form", explode: true },
          { name: "objFlat", in: "query", schema: { type: "object" }, style: "form", explode: false },
        ],
        responses: {
          "200": { description: "OK", content: { "application/json": { schema: { type: "string" } } } },
        },
      },
    };
    await generate({ input, output });
    const clientModule = await import(
      pathToFileURL(path.join(output, "client.js")).href
    );
    let seenUrl = "";
    const client = clientModule.createClient({
      baseUrl: "https://example.test",
      fetch: async (url: RequestInfo | URL) => {
        seenUrl = String(url);
        return new Response(JSON.stringify("ok"), {
          status: 200,
          headers: { "content-type": "application/json" },
        });
      },
    });
    await client.search({
      params: {
        query: {
          term: "a&b",
          tags: ["x&y", "c d"],
          ids: ["a&b", "c"],
          coords: ["p", "q&r"],
          pipes: ["m|n", "o p"],
          filter: { q: "a&b", r: "c d" },
          objExplode: { a: "x&y", b: "z z" },
          objFlat: { k1: "v&1", k2: "v 2" },
        },
      },
    });
    const query = seenUrl.split("?")[1] ?? "";
    // Scalar: exactly one encoding pass.
    assert.ok(query.includes("term=a%26b"), seenUrl);
    assert.ok(!query.includes("%2526"), seenUrl);
    // Form explode arrays repeat the name with single-encoded items.
    assert.ok(query.includes("tags=x%26y"), seenUrl);
    assert.ok(query.includes("tags=c%20d"), seenUrl);
    // Form non-explode arrays keep raw comma separators.
    assert.ok(query.includes("ids=a%26b,c"), seenUrl);
    // Delimited styles preserve their separators.
    assert.ok(query.includes("coords=p%20q%26r"), seenUrl);
    assert.ok(query.includes("pipes=m%7Cn%7Cno%20p") || query.includes("pipes=m|n|o%20p") || query.includes("pipes=m%7Cn|o%20p"), seenUrl);
    // Deep objects encode brackets once and values once.
    assert.ok(query.includes("filter%5Bq%5D=a%26b"), seenUrl);
    assert.ok(query.includes("filter%5Br%5D=c%20d"), seenUrl);
    // Form explode objects spread properties; non-explode keeps commas.
    assert.ok(query.includes("a=x%26y"), seenUrl);
    assert.ok(query.includes("objFlat=k1%2Cv%261%2Ck2%2Cv%202") || query.includes("objFlat=k1,v%261,k2,v%202"), seenUrl);
    // Reserved characters, percent, plus, spaces and unicode round-trip.
    let secondUrl = "";
    const probe = clientModule.createClient({
      baseUrl: "https://example.test",
      fetch: async (url: RequestInfo | URL) => {
        secondUrl = String(url);
        return new Response(JSON.stringify("ok"), {
          status: 200,
          headers: { "content-type": "application/json" },
        });
      },
    });
    await probe.search({ params: { query: { term: "100% a+b caf\u00e9" } } });
    assert.ok(secondUrl.includes("term=100%25%20a%2Bb%20caf%C3%A9"), secondUrl);
    assert.ok(!secondUrl.includes("%2525"), secondUrl);
    const parsed = new URL(secondUrl);
    assert.equal(parsed.searchParams.get("term"), "100% a+b caf\u00e9");
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});

test("keeps path serialization single-encoded", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-"));
  try {
    const input = document();
    input.paths["/files/{name}"] = {
      get: {
        operationId: "getFile",
        parameters: [
          { name: "name", in: "path", required: true, schema: { type: "string" }, style: "simple" },
        ],
        responses: {
          "200": { description: "OK", content: { "application/json": { schema: { type: "string" } } } },
        },
      },
    };
    await generate({ input, output });
    const clientModule = await import(
      pathToFileURL(path.join(output, "client.js")).href
    );
    let seenUrl = "";
    const client = clientModule.createClient({
      baseUrl: "https://example.test",
      fetch: async (url: RequestInfo | URL) => {
        seenUrl = String(url);
        return new Response(JSON.stringify("ok"), {
          status: 200,
          headers: { "content-type": "application/json" },
        });
      },
    });
    await client.getFile({ params: { path: { name: "a/b?c d" } } });
    assert.equal(seenUrl, "https://example.test/files/a%2Fb%3Fc%20d");
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});
test("preserves undefined-as-missing dictionary removals", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-"));
  try {
    const input = document();
    const members = (
      input.components.schemas.OrderMembers["x-sparsetypes"] as {
        members: Array<Record<string, unknown>>;
      }
    ).members;
    members.push({
      name: "bigCounts",
      shape: "dictionary",
      nullable: false,
      redactBefore: false,
      valueWireType: "int64-string",
    });
    members.push({
      name: "profiles",
      shape: "dictionary",
      nullable: false,
      redactBefore: false,
      itemModel: lineModel,
    });
    const dto = input.components.schemas.OrderDto as {
      properties: Record<string, unknown>;
    };
    dto.properties = {
      ...dto.properties,
      bigCounts: { type: "object", additionalProperties: { type: "string" } },
      profiles: {
        type: "object",
        additionalProperties: { $ref: "#/components/schemas/OrderLineDto" },
      },
    };
    await generate({ input, output });
    const changes = await import(
      pathToFileURL(path.join(output, "changes.js")).href
    );
    // An own undefined value reaches the diff as an absent key, so the
    // removal is detected instead of becoming a no-op.
    const removed = changes.buildOrderDtoChangePayload(
      { quantities: { theme: 1 } },
      { quantities: { theme: undefined } },
    );
    assert.deepEqual(removed.change.members.quantities.items, [
      { key: "theme", kind: "remove", before: { state: "value", value: 1 } },
    ]);
    // Explicit null is a present value, never conflated with undefined.
    const nulled = changes.buildOrderDtoChangePayload(
      { prices: { exact: "1.00" } },
      { prices: { exact: null } },
    );
    assert.deepEqual(nulled.change.members.prices.items, [
      {
        key: "exact",
        kind: "replace",
        before: { state: "value", value: "1.00" },
        after: { state: "null" },
      },
    ]);
    // Absent keys on both sides are a no-op; present keys added from absent.
    assert.equal(
      changes.buildOrderDtoChangePayload(
        { quantities: {} },
        { quantities: {} },
      ),
      undefined,
    );
    const added = changes.buildOrderDtoChangePayload(
      { quantities: {} },
      { quantities: { theme: 2 } },
    );
    assert.deepEqual(added.change.members.quantities.items, [
      { key: "theme", kind: "add", after: { state: "value", value: 2 } },
    ]);
    // Precision-sensitive dictionary values compare canonically and still
    // honor undefined-as-missing removals.
    assert.equal(
      changes.buildOrderDtoChangePayload(
        { prices: { exact: "1.00" } },
        { prices: { exact: "1" } },
      ),
      undefined,
    );
    const priceRemoved = changes.buildOrderDtoChangePayload(
      { prices: { exact: "1.00", gone: "2.00" } },
      { prices: { exact: "1", gone: undefined } },
    );
    assert.deepEqual(priceRemoved.change.members.prices.items, [
      {
        key: "gone",
        kind: "remove",
        before: { state: "value", value: "2.00" },
      },
    ]);
    assert.equal(
      changes.buildOrderDtoChangePayload(
        { bigCounts: { c: "42" } },
        { bigCounts: { c: "42" } },
      ),
      undefined,
    );
    const bigRemoved = changes.buildOrderDtoChangePayload(
      { bigCounts: { c: "42" } },
      { bigCounts: { c: undefined } },
    );
    assert.deepEqual(bigRemoved.change.members.bigCounts.items, [
      { key: "c", kind: "remove", before: { state: "value", value: "42" } },
    ]);
    // Nested-model dictionary values diff by members and remove by snapshot.
    const profileEdited = changes.buildOrderDtoChangePayload(
      { profiles: { home: { id: 1, key: 1, quantity: 1, unitPrice: 3 } } },
      { profiles: { home: { id: 1, key: 1, quantity: 2, unitPrice: 3 } } },
    );
    assert.ok(profileEdited.change.members.profiles.items[0].members);
    const profileRemoved = changes.buildOrderDtoChangePayload(
      { profiles: { home: { id: 1, key: 1, quantity: 1, unitPrice: 3 } } },
      { profiles: { home: undefined } },
    );
    assert.equal(profileRemoved.change.members.profiles.items[0].kind, "remove");
    assert.equal(
      changes.buildOrderDtoChangePayload(
        { profiles: { home: { id: 1, key: 1, quantity: 1, unitPrice: 3 } } },
        { profiles: { home: { id: 1, key: 1, quantity: 1, unitPrice: 3 } } },
      ),
      undefined,
    );
    // Null-prototype dictionaries behave like plain objects, while prototype
    // chains are rejected instead of silently inheriting entries.
    const nullProto = Object.create(null);
    nullProto.kept = 1;
    assert.equal(
      changes.buildOrderDtoChangePayload(
        { quantities: { kept: 1 } },
        { quantities: nullProto },
      ),
      undefined,
    );
    const inherited = Object.create({ inherited: 1 });
    inherited.kept = 1;
    assert.throws(
      () =>
        changes.buildOrderDtoChangePayload(
          { quantities: { kept: 1 } },
          { quantities: inherited },
        ),
      /plain JSON objects/,
    );
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});
test("resolves reusable component parameters at path and operation levels", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-"));
  try {
    const input = document();
    input.components.parameters = {
      WidgetId: {
        name: "id",
        in: "path",
        required: true,
        schema: { type: "integer" },
      },
      SearchTerm: {
        name: "q",
        in: "query",
        schema: { type: "string" },
      },
      WidgetVersion: {
        name: "If-Match",
        in: "header",
        required: true,
        schema: { type: "string" },
      },
      "a/b": {
        name: "slash",
        in: "query",
        schema: { type: "string" },
      },
    };
    input.paths["/widgets/{id}"] = {
      parameters: [{ $ref: "#/components/parameters/WidgetId" }],
      get: {
        operationId: "getWidget",
        parameters: [
          { $ref: "#/components/parameters/SearchTerm" },
          { $ref: "#/components/parameters/WidgetVersion" },
          { $ref: "#/components/parameters/a~1b" },
        ],
        responses: {
          "200": { description: "OK", content: { "application/json": { schema: { type: "string" } } } },
        },
      },
      put: {
        operationId: "replaceWidget",
        parameters: [
          { $ref: "#/components/parameters/WidgetVersion" },
          { name: "mode", in: "query", schema: { type: "string" } },
        ],
        responses: {
          "200": { description: "OK", content: { "application/json": { schema: { type: "string" } } } },
        },
      },
    };
    await generate({ input, output });
    const clientTypes = await readFile(
      path.join(output, "client.d.ts"),
      "utf8",
    );
    assert.match(clientTypes, /getWidget/);
    assert.match(clientTypes, /replaceWidget/);
    const generated = await readFile(path.join(output, "client.js"), "utf8");
    assert.match(generated, /SearchTerm|\"q\"/);
    assert.ok(!generated.includes("$ref"), "runtime manifest must not contain unresolved refs");
    const clientModule = await import(
      pathToFileURL(path.join(output, "client.js")).href
    );
    let seenUrl = "";
    let seenHeaders = new Headers();
    const client = clientModule.createClient({
      baseUrl: "https://example.test",
      fetch: async (url: RequestInfo | URL, init: RequestInit) => {
        seenUrl = String(url);
        seenHeaders = new Headers(init.headers);
        return new Response(JSON.stringify("ok"), {
          status: 200,
          headers: { "content-type": "application/json" },
        });
      },
    });
    await client.getWidget({
      params: { path: { id: 7 }, query: { q: "hello", slash: "x" } },
      headers: { "If-Match": "v1" },
    });
    assert.equal(seenUrl, "https://example.test/widgets/7?q=hello&slash=x");
    assert.equal(seenHeaders.get("if-match"), "v1");
    // Operation-level inline parameters work alongside shared references.
    await client.replaceWidget({
      params: { path: { id: 7 }, query: { mode: "fast" } },
      headers: { "If-Match": "v2" },
    });
    assert.equal(seenUrl, "https://example.test/widgets/7?mode=fast");
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});

test("rejects missing, broken, cyclic, and external parameter references", async () => {
  const badRef = async (parameters: Array<Record<string, unknown>>, pattern: RegExp) => {
    const input = document();
    input.paths["/broken"] = {
      get: {
        operationId: "getBroken",
        parameters,
        responses: { "200": { description: "OK" } },
      },
    };
    await assert.rejects(generate({ input, output: await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-")) }), pattern);
  };
  await badRef(
    [{ $ref: "#/components/parameters/Missing" }],
    /Unresolved OpenAPI parameter reference '#\/components\/parameters\/Missing'/,
  );
  await badRef(
    [{ $ref: "#/components/schemas/OrderDto" }],
    /Unsupported OpenAPI parameter reference '#\/components\/schemas\/OrderDto'/,
  );
  await badRef(
    [{ $ref: "https://example.test/shared.json#/components/parameters/Other" }],
    /Unsupported external OpenAPI parameter reference/,
  );
  const cyclic = document();
  cyclic.components.parameters = {
    LoopA: { $ref: "#/components/parameters/LoopB" },
    LoopB: { $ref: "#/components/parameters/LoopA" },
  };
  cyclic.paths["/broken"] = {
    get: {
      operationId: "getBroken",
      parameters: [{ $ref: "#/components/parameters/LoopA" }],
      responses: { "200": { description: "OK" } },
    },
  };
  await assert.rejects(
    generate({ input: cyclic, output: await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-")) }),
    /Circular OpenAPI parameter reference/,
  );
});
test("honors SparseRedactBefore across nested models and dictionary edits", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-"));
  try {
    const input = document();
    const members = (
      input.components.schemas.OrderMembers["x-sparsetypes"] as {
        members: Array<Record<string, unknown>>;
      }
    ).members;
    members.push(
      {
        name: "secrets",
        shape: "dictionary",
        nullable: false,
        redactBefore: true,
        valueWireType: "string",
      },
      {
        name: "vault",
        shape: "nested",
        nullable: true,
        redactBefore: true,
        childModel: "Sample.Contracts.AddressDto",
      },
      {
        name: "crew",
        shape: "keyed",
        nullable: false,
        redactBefore: true,
        itemModel: lineModel,
        keyProperties: ["key"],
        keyType: "integer-number",
        unassignedKey: 0,
        temporaryKey: "temporaryId",
        ordered: true,
      },
      {
        name: "profiles",
        shape: "dictionary",
        nullable: false,
        redactBefore: true,
        itemModel: lineModel,
      },
    );
    const dto = input.components.schemas.OrderDto as {
      properties: Record<string, unknown>;
    };
    dto.properties = {
      ...dto.properties,
      secrets: { type: "object", additionalProperties: { type: "string" } },
      vault: { $ref: "#/components/schemas/AddressDto" },
      crew: {
        type: "array",
        items: { $ref: "#/components/schemas/OrderLineDto" },
      },
      profiles: {
        type: "object",
        additionalProperties: { $ref: "#/components/schemas/OrderLineDto" },
      },
    };
    await generate({ input, output });
    const changes = await import(
      pathToFileURL(path.join(output, "changes.js")).href
    );
    const b = changes.buildOrderDtoChangePayload;
    const sentinel = "SECRET-before-9f3a";
    const leaked = (payload: unknown) =>
      JSON.stringify(payload).includes(sentinel);
    // Scalar dictionary edits and removals redact item before endpoints.
    const dictEdit = b(
      { secrets: { k: sentinel } },
      { secrets: { k: "rotated" } },
    );
    assert.deepEqual(dictEdit.change.members.secrets.items, [
      {
        key: "k",
        kind: "replace",
        before: { state: "redacted" },
        after: { state: "value", value: "rotated" },
      },
    ]);
    assert.equal(leaked(dictEdit), false);
    const dictRemove = b({ secrets: { k: sentinel } }, { secrets: {} });
    assert.deepEqual(dictRemove.change.members.secrets.items, [
      { key: "k", kind: "remove", before: { state: "redacted" } },
    ]);
    assert.equal(leaked(dictRemove), false);
    // Nested redacted updates redact every nested before endpoint.
    const nestedUpdate = b(
      { address: { city: "same" }, vault: { city: sentinel } },
      { address: { city: "same" }, vault: { city: "rotated" } },
    );
    assert.deepEqual(nestedUpdate.change.members.vault, {
      kind: "update",
      members: {
        city: {
          kind: "replace",
          before: { state: "redacted" },
          after: { state: "value", value: "rotated" },
        },
      },
    });
    assert.equal(leaked(nestedUpdate), false);
    // Redacted keyed edits collapse to a whole-member blind set because
    // per-item redacted before endpoints with edits are rejected server-side.
    const keyedEdit = b(
      {
        crew: [
          { id: 1, key: 1, temporaryId: null, quantity: 1, unitPrice: 3 },
        ],
      },
      {
        crew: [
          { id: 1, key: 1, temporaryId: null, quantity: 2, unitPrice: 3 },
        ],
      },
    );
    assert.equal(keyedEdit.change.members.crew.kind, "update");
    const crewItem = keyedEdit.change.members.crew.items[0];
    assert.equal(crewItem.key, 1);
    assert.equal(crewItem.kind, "update");
    assert.deepEqual(crewItem.members.quantity, {
      kind: "replace",
      before: { state: "redacted" },
      after: { state: "value", value: 2 },
    });
    assert.equal(leaked(keyedEdit), false);
    // Removal-only keyed changes stay granular with redacted before endpoints.
    const keyedRemove = b(
      {
        crew: [
          { id: 1, key: 1, temporaryId: null, quantity: 1, unitPrice: 3 },
        ],
      },
      { crew: [] },
    );
    assert.equal(keyedRemove.change.members.crew.kind, "update");
    assert.deepEqual(
      keyedRemove.change.members.crew.items[0].before,
      { state: "redacted" },
    );
    // Model-valued dictionary edits also require whole-member replacement.
    const dictModelEdit = b(
      {
        profiles: {
          p: { id: 1, key: 1, temporaryId: null, quantity: 1, unitPrice: 3 },
        },
      },
      {
        profiles: {
          p: { id: 1, key: 1, temporaryId: null, quantity: 2, unitPrice: 3 },
        },
      },
    );
    assert.equal(dictModelEdit.change.members.profiles.kind, "update");
    assert.deepEqual(dictModelEdit.change.members.profiles.items, [
      {
        key: "p",
        kind: "update",
        members: {
          quantity: {
            kind: "replace",
            before: { state: "redacted" },
            after: { state: "value", value: 2 },
          },
        },
      },
    ]);
    assert.equal(leaked(dictModelEdit), false);
    // Whole-root before snapshots redact when any member is redacted.
    const rootReplace = b(
      { name: "a", secrets: { k: sentinel } },
      null,
    );
    assert.deepEqual(rootReplace.change.before, { state: "redacted" });
    assert.equal(rootReplace.change.after.state, "null");
    assert.equal(leaked(rootReplace), false);
    // Unchanged redacted members emit nothing at all.
    assert.equal(
      b(
        { name: "a", secrets: { k: sentinel } },
        { name: "b", secrets: { k: sentinel } },
      ).change.members.secrets,
      undefined,
    );
    const labelOnly = b(
      { name: "a", secrets: { k: sentinel } },
      { name: "b", secrets: { k: sentinel } },
    );
    assert.equal(leaked(labelOnly), false);
    // Blind whole-collection removal fails early with a safe error.
    assert.throws(
      () => b({ secrets: { k: sentinel } }, {}),
      (error: unknown) => {
        assert.ok(error instanceof TypeError);
        assert.match(
          (error as Error).message,
          /blind whole-collection removal/,
        );
        assert.equal((error as Error).message.includes(sentinel), false);
        return true;
      },
    );
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});
test("converts keyed DTO snapshots using integer and nullable schema types", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-"));
  try {
    const input = document();
    // DTO schemas under test: integer, nullable number, nullable integer.
    input.components.schemas.OrderLineDto.properties.quantity = {
      type: "integer",
    };
    input.components.schemas.OrderLineDto.properties.unitPrice = {
      type: ["number", "null"],
    };
    input.components.schemas.OrderLineDto.properties.id = {
      type: ["integer", "null"],
    };
    const lineMembers = input.components.schemas.OrderLineMembers[
      "x-sparsetypes"
    ] as { members: Array<Record<string, unknown>> };
    lineMembers.members.find((member) => member.name === "quantity")!.wireType =
      "int64-string";
    lineMembers.members.find((member) => member.name === "id")!.wireType =
      "uint64-string";
    await generate({ input, output });
    const changes = await import(
      pathToFileURL(path.join(output, "changes.js")).href
    );
    const models = await import(
      pathToFileURL(path.join(output, "models.js")).href
    );
    const line = (overrides: Record<string, unknown>) => ({
      id: "7",
      key: 1,
      temporaryId: null,
      quantity: "123",
      unitPrice: "0.5",
      ...overrides,
    });
    // Keyed addition snapshots convert string states to DTO numbers.
    const added = changes.buildOrderDtoChangePayload(
      { lines: [] },
      {
        lines: [
          {
            ...line({ id: "7", quantity: "123", unitPrice: "0.5" }),
            key: 0,
            temporaryId: "f4ef9705-b5e2-41f6-a7ac-1f3b0a8c14de",
          },
        ],
      },
    );
    assert.equal(added.change.members.lines.items[0].after.value.quantity, 123);
    assert.equal(
      added.change.members.lines.items[0].after.value.unitPrice,
      0.5,
    );
    assert.equal(added.change.members.lines.items[0].after.value.id, 7);
    models.parseOrderLineDto(added.change.members.lines.items[0].after.value);
    // Nullable entries survive the round trip.
    const nullable = changes.buildOrderDtoChangePayload(
      { lines: [] },
      {
        lines: [
          {
            ...line({ quantity: "8", unitPrice: null, id: null }),
            key: 0,
            temporaryId: "f4ef9705-b5e2-41f6-a7ac-1f3b0a8c14de",
          },
        ],
      },
    );
    assert.equal(
      nullable.change.members.lines.items[0].after.value.quantity,
      8,
    );
    assert.equal(
      nullable.change.members.lines.items[0].after.value.unitPrice,
      null,
    );
    // Keyed removal snapshots convert as well.
    const removed = changes.buildOrderDtoChangePayload(
      {
        lines: [
          {
            ...line({ quantity: "9", unitPrice: "1.5", id: "11" }),
            key: 0,
            temporaryId: "f4ef9705-b5e2-41f6-a7ac-1f3b0a8c14de",
          },
        ],
      },
      { lines: [] },
    );
    assert.equal(
      removed.change.members.lines.items[0].before.value.quantity,
      9,
    );
    // Unsafe integers fail early instead of rounding.
    assert.throws(
      () =>
        changes.buildOrderDtoChangePayload(
          { lines: [] },
          {
            lines: [
              {
                ...line({ quantity: "9007199254740993" }),
                key: 0,
                temporaryId: "f4ef9705-b5e2-41f6-a7ac-1f3b0a8c14de",
              },
            ],
          },
        ),
      /cannot preserve this integer/,
    );
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});
test("emits canonical precision-sensitive scalar encodings", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-"));
  try {
    const input = document();
    const lineMembers = input.components.schemas.OrderLineMembers[
      "x-sparsetypes"
    ] as { members: Array<Record<string, unknown>> };
    lineMembers.members.find((member) => member.name === "quantity")!.wireType =
      "int64-string";
    lineMembers.members.find((member) => member.name === "id")!.wireType =
      "uint64-string";
    await generate({ input, output });
    const changes = await import(
      pathToFileURL(path.join(output, "changes.js")).href
    );
    const item = (overrides: Record<string, unknown>) => ({
      id: "1",
      key: 1,
      temporaryId: null,
      quantity: "1",
      unitPrice: "1.00",
      ...overrides,
    });
    const changeUnitPrice = (before: unknown, after: unknown) =>
      changes.buildOrderDtoChangePayload(
        { lines: [item({ unitPrice: before })] },
        { lines: [item({ unitPrice: after })] },
      ).change.members.lines.items[0].members.unitPrice;
    // Canonical decimals preserve scale.
    assert.deepEqual(changeUnitPrice("1.00", "2.50"), {
      kind: "replace",
      before: { state: "value", value: "1.00" },
      after: { state: "value", value: "2.50" },
    });
    // Negative zero normalizes while preserving scale.
    assert.equal(changeUnitPrice("1.00", "-0.00").after.value, "0.00");
    assert.equal(changeUnitPrice("1.00", "-0").after.value, "0");
    // Non-canonical decimal spellings throw before a payload is built.
    for (const bad of ["1e3", "+1", "001", " 1", "1 ", ".5", "5.", "NaN"]) {
      assert.throws(() => changeUnitPrice("1.00", bad), /canonical|finite/);
    }
    // Decimal bounds: scale, precision, and range are enforced.
    assert.throws(
      () => changeUnitPrice("1.00", `1.${"0".repeat(29)}`),
      /scale/,
    );
    assert.throws(() => changeUnitPrice("1.00", "1".repeat(30)), /29/);
    assert.throws(
      () => changeUnitPrice("1.00", "79228162514264337593543950336"),
      /out of range/,
    );
    // JS numbers with exponent forms or non-finite values throw.
    assert.throws(() => changeUnitPrice("1.00", 1e21), /canonical/);
    assert.throws(() => changeUnitPrice("1.00", NaN), /finite/);
    assert.equal(changeUnitPrice(3, 5).after.value, "5");
    const changeQuantity = (before: unknown, after: unknown) =>
      changes.buildOrderDtoChangePayload(
        { lines: [item({ quantity: before })] },
        { lines: [item({ quantity: after })] },
      ).change.members.lines.items[0].members.quantity;
    // 64-bit integers reject non-canonical spellings and out-of-range values.
    for (const bad of ["001", "+1", " 1", "1 ", "1e3", "1.0"]) {
      assert.throws(() => changeQuantity("1", bad), /canonical/);
    }
    assert.throws(
      () => changeQuantity("1", "9223372036854775808"),
      /out of range/,
    );
    assert.equal(
      changeQuantity("1", "-9223372036854775808").after.value,
      "-9223372036854775808",
    );
    assert.equal(changeQuantity("1", 42).after.value, "42");
    assert.throws(() => changeQuantity("1", 2 ** 53), /strings/);
    const changeId = (before: unknown, after: unknown) =>
      changes.buildOrderDtoChangePayload(
        { lines: [item({ id: before })] },
        { lines: [item({ id: after })] },
      ).change.members.lines.items[0].members.id;
    assert.throws(() => changeId("1", "-1"), /canonical/);
    assert.equal(
      changeId("1", "18446744073709551615").after.value,
      "18446744073709551615",
    );
    assert.throws(() => changeId("1", "18446744073709551616"), /out of range/);
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});
test("emits NodeNext-compatible relative imports in generated declarations", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-"));
  try {
    await generate({ input: document(), output });
    for (const file of [
      "changes.d.ts",
      "models.d.ts",
      "client.d.ts",
      "index.d.ts",
    ]) {
      const text = await readFile(path.join(output, file), "utf8");
      const specifiers = [
        ...text.matchAll(/(?:import|export)[^;]*?from\s+["']([^"']+)["']/g),
      ].map((match) => match[1]);
      for (const specifier of specifiers) {
        if (specifier.startsWith(".")) {
          assert.match(
            specifier,
            /\.js$/,
            `${file} has a non-NodeNext relative specifier '${specifier}'`,
          );
        }
      }
    }
    await writeFile(
      path.join(output, "nodenext-consumer.ts"),
      `
import { buildOrderDtoChangePayload, createClient, parseOrderDto } from "./index.js";
import type { OptionalOrderDto } from "./index.js";
const dto = parseOrderDto({ name: "Order" });
const state: OptionalOrderDto = { name: undefined };
buildOrderDtoChangePayload(state, dto);
createClient().getOrder({ params: { path: { id: 1 } } });
`,
    );
    execFileSync(
      "tsgo",
      [
        "--ignoreConfig",
        "--noEmit",
        "--strict",
        "--skipLibCheck",
        "false",
        "--lib",
        "ES2022,DOM",
        "--module",
        "NodeNext",
        "--moduleResolution",
        "NodeNext",
        "--allowImportingTsExtensions",
        "false",
        path.join(output, "nodenext-consumer.ts"),
      ],
      { stdio: "inherit" },
    );
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});
test("reserves Raw method names when deduplicating operations", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-"));
  try {
    const input = document();
    const operation = (
      operationId: string | undefined,
      responseSchema = "#/components/schemas/OrderDto",
    ) => ({
      ...(operationId === undefined ? {} : { operationId }),
      responses: {
        "200": {
          description: "OK",
          content: { "application/json": { schema: { $ref: responseSchema } } },
        },
      },
    });
    // Note: duplicate operationIds are rejected by openapi-typescript during
    // generate(), so the client emitters are exercised directly here.
    const { emitClientDts, emitClientJs } = await import("../src/client.js");
    const doc = {
      ...input,
      paths: {
        "/a": { get: operation("foo") },
        "/b": { get: operation("fooRaw") },
        "/c": { get: operation("foo") },
        "/d": { get: operation(undefined) },
        "/e": { get: operation("foo2") },
      },
    };
    const clientJs = emitClientJs(doc);
    const clientTypes = emitClientDts(doc);
    await writeFile(path.join(output, "client.mjs"), clientJs);
    const clientModule = await import(
      pathToFileURL(path.join(output, "client.mjs")).href
    );
    const calls: string[] = [];
    const client = clientModule.createClient({
      baseUrl: "https://example.test",
      fetch: async (url: RequestInfo | URL) => {
        calls.push(String(url));
        return new Response(JSON.stringify({}), {
          status: 200,
          headers: { "content-type": "application/json" },
        });
      },
    });
    // `foo` reserves `fooRaw`, so the explicit `fooRaw` and the duplicate
    // `foo` are renamed deterministically instead of overwriting each other.
    // A pre-existing `foo2` forces the duplicate onto `foo22`.
    assert.deepEqual(Object.keys(client).sort(), [
      "foo",
      "foo2",
      "foo22",
      "foo22Raw",
      "foo2Raw",
      "fooRaw",
      "fooRaw2",
      "fooRaw2Raw",
      "getD",
      "getDRaw",
    ]);
    await client.foo();
    await client.fooRaw2();
    await client.foo2();
    await client.foo22();
    await client.getD();
    assert.deepEqual(calls, [
      "https://example.test/a",
      "https://example.test/b",
      "https://example.test/c",
      "https://example.test/e",
      "https://example.test/d",
    ]);
    for (const name of ["foo", "fooRaw2", "foo2", "foo22", "getD"]) {
      assert.match(clientTypes, new RegExp(`\\b${name}\\(`));
    }
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});
test("merges PathItem parameters with operation overrides", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-"));
  try {
    const input = document();
    input.components.parameters = {
      XMode: {
        name: "X-Mode",
        in: "header",
        required: false,
        schema: { type: "string" },
      },
      XModeRequired: {
        name: "X-Mode",
        in: "header",
        required: true,
        schema: { type: "string" },
      },
    };
    input.paths["/search"] = {
      parameters: [
        {
          name: "filter",
          in: "query",
          required: false,
          schema: { type: "string" },
        },
        {
          name: "page",
          in: "query",
          required: false,
          schema: { type: "integer" },
        },
        { $ref: "#/components/parameters/XMode" },
      ],
      get: {
        operationId: "searchOrders",
        parameters: [
          {
            name: "filter",
            in: "query",
            required: true,
            style: "spaceDelimited",
            schema: { type: "array", items: { type: "string" } },
          },
          // Same name in a different location is a distinct parameter.
          {
            name: "page",
            in: "header",
            required: false,
            schema: { type: "string" },
          },
          { $ref: "#/components/parameters/XModeRequired" },
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
    };
    await generate({ input, output });
    const clientModule = await import(
      pathToFileURL(path.join(output, "client.js")).href
    );
    const calls: Array<{ url: string; headers: Headers }> = [];
    const client = clientModule.createClient({
      baseUrl: "https://example.test",
      fetch: async (url: RequestInfo | URL, init?: RequestInit) => {
        calls.push({ url: String(url), headers: new Headers(init?.headers) });
        return new Response(JSON.stringify([]), {
          status: 200,
          headers: { "content-type": "application/json" },
        });
      },
    });
    await client.searchOrders({
      params: { query: { filter: ["x", "y"] } },
      headers: { "X-Mode": "a" },
    });
    // The operation definition wins: a single spaceDelimited `filter` key
    // (not the PathItem form serialization), plus the unoverridden `page`.
    // Spaces are single-encoded as %20 per the canonical query encoding.
    assert.equal(calls[0].url, "https://example.test/search?filter=x%20y");
    assert.equal(calls[0].headers.get("x-mode"), "a");
    // Requiredness follows the override: `filter` and `X-Mode` are required.
    await assert.rejects(
      client.searchOrders({ headers: { "X-Mode": "a" } }),
      /Missing query parameter 'filter'/,
    );
    await assert.rejects(
      client.searchOrders({ params: { query: { filter: ["x"] } } }),
      /Missing header parameter 'X-Mode'/,
    );
    // The generated types expose the merged operation.
    const clientTypes = await readFile(
      path.join(output, "client.d.ts"),
      "utf8",
    );
    assert.match(clientTypes, /searchOrders/);
    // The runtime manifest carries each (name, in) identity exactly once,
    // with the operation definition winning.
    const clientJs = await readFile(path.join(output, "client.js"), "utf8");
    assert.equal(clientJs.split('"name": "filter"').length - 1, 1);
    assert.match(
      clientJs,
      /"name": "X-Mode",\s*"in": "header"[\s\S]*?"required": true/,
    );
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});
test("serializes non-JSON request and response media types without double encoding", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-"));
  try {
    const input = document();
    input.paths["/upload"] = {
      post: {
        operationId: "uploadFile",
        requestBody: {
          required: true,
          content: {
            "multipart/form-data": { schema: { type: "object" } },
          },
        },
        responses: {
          "200": {
            description: "OK",
            content: { "text/plain": { schema: { type: "string" } } },
          },
        },
      },
    };
    input.paths["/notes"] = {
      post: {
        operationId: "saveNote",
        requestBody: {
          required: true,
          content: {
            "application/x-www-form-urlencoded": { schema: { type: "object" } },
          },
        },
        responses: {
          "200": {
            description: "OK",
            content: { "text/plain": { schema: { type: "string" } } },
          },
        },
      },
    };
    input.paths["/blob"] = {
      put: {
        operationId: "putBlob",
        requestBody: {
          required: true,
          content: {
            "application/octet-stream": {
              schema: { type: "string", format: "binary" },
            },
          },
        },
        responses: {
          "200": {
            description: "OK",
            content: {
              "application/octet-stream": {
                schema: { type: "string", format: "binary" },
              },
            },
          },
        },
      },
    };
    input.paths["/empty"] = {
      delete: {
        operationId: "deleteThing",
        responses: { "204": { description: "No content" } },
      },
    };
    await generate({ input, output });
    const clientModule = await import(
      pathToFileURL(path.join(output, "client.js")).href
    );
    const seen: Array<{ url: unknown; init: RequestInit }> = [];
    const client = clientModule.createClient({
      baseUrl: "https://example.test",
      fetch: async (url: RequestInfo | URL, init?: RequestInit) => {
        seen.push({ url, init: init ?? {} });
        const headers = new Headers(init?.headers);
        const contentType = headers.get("content-type") ?? "";
        if (init?.body instanceof FormData) {
          return new Response("stored", {
            status: 200,
            headers: { "content-type": "text/plain" },
          });
        }
        if (contentType === "application/x-www-form-urlencoded") {
          return new Response(String(init?.body), {
            status: 200,
            headers: { "content-type": "text/plain" },
          });
        }
        if ((init?.method ?? "GET").toUpperCase() === "DELETE") {
          return new Response(null, { status: 204 });
        }
        return new Response("binary-bytes", {
          status: 200,
          headers: { "content-type": "application/octet-stream" },
        });
      },
    });

    const form = new FormData();
    form.append("file", new Blob(["data"], { type: "text/plain" }), "a.txt");
    const stored = await client.uploadFile({ body: form });
    assert.equal(stored, "stored");
    // The client must not set Content-Type itself so fetch can add the boundary.
    assert.equal(new Headers(seen[0]?.init.headers).has("content-type"), false);
    assert.ok(seen[0]?.init.body instanceof FormData);

    const echoed = await client.saveNote({ body: { title: "hi", count: 2 } });
    assert.equal(echoed, "title=hi&count=2");
    assert.equal(
      new Headers(seen[1]?.init.headers).get("content-type"),
      "application/x-www-form-urlencoded",
    );

    const bytes = new Uint8Array([1, 2, 3]);
    const downloaded = await client.putBlob({ body: bytes });
    assert.ok(downloaded instanceof Blob);
    assert.equal(
      new Headers(seen[2]?.init.headers).get("content-type"),
      "application/octet-stream",
    );
    assert.deepEqual(
      [...new Uint8Array(await downloaded.arrayBuffer())],
      [98, 105, 110, 97, 114, 121, 45, 98, 121, 116, 101, 115],
    );

    const removed = await client.deleteThing({});
    assert.equal(removed, undefined);

    await assert.rejects(
      client.saveNote({ body: "already=encoded" }),
      /must be a plain object or URLSearchParams/,
    );
    await assert.rejects(
      client.uploadFile({ body: ["not", "form"] }),
      /must be FormData or a plain object/,
    );
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});

test("rejects unsupported or ambiguous media types with actionable diagnostics", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-"));
  try {
    const xml = document();
    xml.paths["/legacy"] = {
      post: {
        operationId: "postXml",
        requestBody: {
          content: { "application/xml": { schema: { type: "string" } } },
        },
        responses: { "200": { description: "OK" } },
      },
    };
    await assert.rejects(
      generate({ input: xml, output }),
      /unsupported request media.*application\/xml/,
    );

    const image = document();
    image.paths["/legacy"] = {
      get: {
        operationId: "getImage",
        responses: {
          "200": {
            description: "OK",
            content: {
              "image/png": { schema: { type: "string", format: "binary" } },
            },
          },
        },
      },
    };
    await assert.rejects(
      generate({ input: image, output }),
      /unsupported success media.*image\/png/,
    );

    const mixed = document();
    mixed.paths["/legacy"] = {
      post: {
        operationId: "postMixed",
        requestBody: {
          content: {
            "application/json": { schema: { type: "object" } },
            "text/plain": { schema: { type: "string" } },
          },
        },
        responses: { "200": { description: "OK" } },
      },
    };
    await assert.rejects(
      generate({ input: mixed, output }),
      /multiple request serialization kinds/,
    );
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});
test("resolves referenced requestBodies and responses", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-"));
  try {
    const input = document();
    input.components.requestBodies = {
      SaveOrder: {
        required: true,
        content: {
          "application/json": {
            schema: { $ref: "#/components/schemas/ChangePayload" },
          },
        },
      },
    };
    input.components.responses = {
      OrderResponse: {
        description: "OK",
        content: {
          "application/json": {
            schema: { $ref: "#/components/schemas/OrderDto" },
          },
        },
      },
      OrderDeleted: { description: "No content" },
      OrderConflict: {
        description: "Conflict",
        content: {
          "application/json": {
            schema: { $ref: "#/components/schemas/OrderConflict" },
          },
        },
      },
    };
    const put = input.paths["/orders/{id}"].put;
    put.requestBody = { $ref: "#/components/requestBodies/SaveOrder" };
    put.responses = {
      "200": { $ref: "#/components/responses/OrderResponse" },
      "204": { $ref: "#/components/responses/OrderDeleted" },
      "409": { $ref: "#/components/responses/OrderConflict" },
    };
    input.components.parameters = {
      OrderId: {
        name: "id",
        in: "path",
        required: true,
        schema: { type: "integer" },
      },
    };
    input.paths["/orders/{id}"].get.parameters = [
      { $ref: "#/components/parameters/OrderId" },
    ];
    await generate({ input, output });
    const generated = await readFile(path.join(output, "changes.js"), "utf8");
    assert.match(generated, /buildOrderDtoChangePayload/);
    const clientSource = await readFile(path.join(output, "client.js"), "utf8");
    assert.match(clientSource, /"bodyRequired": true/);
    assert.match(clientSource, /"application\/json"/);

    const clientModule = await import(
      pathToFileURL(path.join(output, "client.js")).href
    );
    const requiredClient = clientModule.createClient({
      fetch: async () => {
        throw new Error("unexpected fetch");
      },
    });
    await assert.rejects(
      requiredClient.saveOrder({
        params: { path: { id: 1 } },
        headers: { "If-Match": "v1" },
      }),
      /Missing required request body/,
    );
    const emptyClient = clientModule.createClient({
      fetch: async () => new Response(null, { status: 204 }),
    });
    assert.equal(
      await emptyClient.saveOrder({
        params: { path: { id: 1 } },
        headers: { "If-Match": "v1" },
        body: {},
      }),
      undefined,
    );
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});

test("supports escaped, chained, and invalid local references", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-"));
  try {
    const escaped = document();
    escaped.components.requestBodies = {
      "Save/Special~Order": {
        required: true,
        content: {
          "application/json": {
            schema: { $ref: "#/components/schemas/ChangePayload" },
          },
        },
      },
      AliasOrder: {
        $ref: "#/components/requestBodies/Save~1Special~0Order",
      },
    };
    const put = escaped.paths["/orders/{id}"].put;
    put.requestBody = { $ref: "#/components/requestBodies/AliasOrder" };
    await generate({ input: escaped, output });
    const clientSource = await readFile(path.join(output, "client.js"), "utf8");
    assert.match(clientSource, /"bodyRequired": true/);

    const missing = document();
    missing.paths["/orders/{id}"].put.requestBody = {
      $ref: "#/components/requestBodies/DoesNotExist",
    };
    await assert.rejects(
      generate({ input: missing, output }),
      /DoesNotExist.*is not defined/,
    );

    const cyclic = document();
    cyclic.components.requestBodies = {
      First: { $ref: "#/components/requestBodies/Second" },
      Second: { $ref: "#/components/requestBodies/First" },
    };
    cyclic.paths["/orders/{id}"].put.requestBody = {
      $ref: "#/components/requestBodies/First",
    };
    await assert.rejects(generate({ input: cyclic, output }), /is cyclic/);

    const external = document();
    external.paths["/orders/{id}"].put.requestBody = {
      $ref: "https://example.test/openapi.json#/components/requestBodies/SaveOrder",
    };
    await assert.rejects(
      generate({ input: external, output }),
      /only local '#\/\.\.\.' references are supported/,
    );
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});
test("honors Operation and PathItem server overrides", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-"));
  try {
    const input = document();
    input.servers = [{ url: "https://api.example.com/v1" }];
    input.paths["/orders/{id}"].servers = [
      { url: "https://orders.example.com/v2/" },
    ];
    input.paths["/orders/{id}"].put.servers = [
      {
        url: "{protocol}://ops.example.com/{base}",
        variables: {
          protocol: { default: "https" },
          base: { default: "v3", enum: ["v3"] },
        },
      },
    ];
    input.paths["/orders/{id}"].get.parameters.push({
      name: "verbose",
      in: "query",
      schema: { type: "boolean" },
    });
    await generate({ input, output });
    const clientSource = await readFile(path.join(output, "client.js"), "utf8");
    assert.match(clientSource, /https:\/\/orders\.example\.com\/v2/);
    assert.match(clientSource, /https:\/\/ops\.example\.com\/v3/);

    const clientModule = await import(
      pathToFileURL(path.join(output, "client.js")).href
    );
    const urls: string[] = [];
    const client = clientModule.createClient({
      fetch: async (url: RequestInfo | URL) => {
        urls.push(String(url));
        return new Response(JSON.stringify({}), {
          status: 200,
          headers: { "content-type": "application/json" },
        });
      },
    });
    await client.getOrder({
      params: { path: { id: 7 }, query: { verbose: true } },
    });
    await client.saveOrder({
      params: { path: { id: 7 } },
      headers: { "If-Match": "v1" },
      body: {},
    });
    assert.deepEqual(urls, [
      "https://orders.example.com/v2/orders/7?verbose=true",
      "https://ops.example.com/v3/orders/7",
    ]);

    const overridden: string[] = [];
    const proxy = clientModule.createClient({
      baseUrl: "https://proxy.example.test/root/",
      fetch: async (url: RequestInfo | URL) => {
        overridden.push(String(url));
        return new Response(JSON.stringify({}), {
          status: 200,
          headers: { "content-type": "application/json" },
        });
      },
    });
    await proxy.getOrder({ params: { path: { id: 7 } } });
    await proxy.saveOrder({
      params: { path: { id: 7 } },
      headers: { "If-Match": "v1" },
      body: {},
    });
    assert.deepEqual(overridden, [
      "https://proxy.example.test/root/orders/7",
      "https://proxy.example.test/root/orders/7",
    ]);
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});

test("resolves relative servers and rejects bad server variables", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-"));
  try {
    const relative = document();
    relative.servers = [{ url: "https://api.example.com/v1" }];
    relative.paths["/orders/{id}"].put.servers = [{ url: "inner" }];
    relative.paths["/orders/{id}"].get.servers = [{ url: "/alt" }];
    await generate({ input: relative, output });
    const clientSource = await readFile(path.join(output, "client.js"), "utf8");
    assert.match(clientSource, /https:\/\/api\.example\.com\/v1\/inner/);
    assert.match(clientSource, /https:\/\/api\.example\.com\/alt/);

    const undeclared = document();
    undeclared.paths["/orders/{id}"].put.servers = [
      { url: "https://{missing}.example.com", variables: {} },
    ];
    await assert.rejects(
      generate({ input: undeclared, output }),
      /variable 'missing'.*no entry/,
    );

    const noDefault = document();
    noDefault.paths["/orders/{id}"].put.servers = [
      {
        url: "https://{region}.example.com",
        variables: { region: { enum: ["a", "b"] } },
      },
    ];
    await assert.rejects(
      generate({ input: noDefault, output }),
      /without a default value/,
    );

    const badEnum = document();
    badEnum.paths["/orders/{id}"].put.servers = [
      {
        url: "https://{region}.example.com",
        variables: { region: { default: "c", enum: ["a", "b"] } },
      },
    ];
    await assert.rejects(
      generate({ input: badEnum, output }),
      /not in its enum/,
    );
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});

test("emits replace for nullable nested-model dictionary value transitions", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-codegen-"));
  try {
    await generate({ input: document(), output });
    const changes = await import(
      pathToFileURL(path.join(output, "changes.js")).href
    );

    const created = changes.buildOrderDtoChangePayload(
      { contacts: { primary: null } },
      { contacts: { primary: { name: "Alice", secret: "s1" } } },
    );
    assert.deepEqual(created.change.members.contacts.items, [
      {
        key: "primary",
        kind: "replace",
        before: { state: "redacted" },
        after: {
          state: "value",
          value: { name: "Alice", secret: "s1" },
        },
      },
    ]);

    const cleared = changes.buildOrderDtoChangePayload(
      { contacts: { primary: { name: "Alice", secret: "s1" } } },
      { contacts: { primary: null } },
    );
    assert.deepEqual(cleared.change.members.contacts.items, [
      {
        key: "primary",
        kind: "replace",
        before: { state: "redacted" },
        after: { state: "null" },
      },
    ]);

    const added = changes.buildOrderDtoChangePayload(
      { contacts: {} },
      { contacts: { primary: { name: "Alice", secret: "s1" } } },
    );
    assert.deepEqual(added.change.members.contacts.items, [
      {
        key: "primary",
        kind: "add",
        after: {
          state: "value",
          value: { name: "Alice", secret: "s1" },
        },
      },
    ]);

    // Explicit undefined has no JSON representation, so it behaves like an
    // absent key and produces an add.
    const fromUndefined = changes.buildOrderDtoChangePayload(
      { contacts: { primary: undefined } },
      { contacts: { primary: { name: "Alice", secret: "s1" } } },
    );
    assert.deepEqual(fromUndefined.change.members.contacts.items, [
      {
        key: "primary",
        kind: "add",
        after: {
          state: "value",
          value: { name: "Alice", secret: "s1" },
        },
      },
    ]);

    assert.equal(
      changes.buildOrderDtoChangePayload(
        { contacts: { primary: null } },
        { contacts: { primary: null } },
      ),
      undefined,
    );

    const edited = changes.buildOrderDtoChangePayload(
      { contacts: { primary: { name: "Alice", secret: "s1" } } },
      { contacts: { primary: { name: "Bob", secret: "s1" } } },
    );
    // The contacts member requests redaction, so the nested update stays
    // granular but every leaf before endpoint is blind (issue 384).
    assert.deepEqual(edited.change.members.contacts.items, [
      {
        key: "primary",
        kind: "update",
        members: {
          name: {
            kind: "replace",
            before: { state: "redacted" },
            after: { state: "value", value: "Bob" },
          },
        },
      },
    ]);

    const redactedEdit = changes.buildOrderDtoChangePayload(
      { contacts: { primary: { name: "Alice", secret: "s1" } } },
      { contacts: { primary: { name: "Alice", secret: "s2" } } },
    );
    assert.deepEqual(
      redactedEdit.change.members.contacts.items[0].members.secret.before,
      { state: "redacted" },
    );

    assert.throws(
      () =>
        changes.buildOrderDtoChangePayload(
          { contacts: { primary: 42 } },
          { contacts: { primary: null } },
        ),
      /must be objects, null, or missing/,
    );

    // The payload survives JSON serialization with its wire shape intact.
    const roundTripped = JSON.parse(JSON.stringify(created));
    assert.deepEqual(roundTripped.change.members.contacts.items, [
      {
        key: "primary",
        kind: "replace",
        before: { state: "redacted" },
        after: {
          state: "value",
          value: { name: "Alice", secret: "s1" },
        },
      },
    ]);
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});
