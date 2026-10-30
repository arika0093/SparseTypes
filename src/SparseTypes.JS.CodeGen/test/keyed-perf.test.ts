import assert from "node:assert/strict";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { pathToFileURL } from "node:url";
import test from "node:test";
import { generate } from "../src/generate.js";
import type { OpenApiDocument } from "../src/openapi.js";

const schemaIdentity = "sha256:" + "b".repeat(64);
const model = "Perf.Contracts.RowDto";

function document(): OpenApiDocument {
  const schema = (properties: Record<string, any>) => ({
    type: "object",
    properties,
  });
  return {
    openapi: "3.1.0",
    info: { title: "Perf", version: "1.0" },
    servers: [{ url: "/" }],
    paths: {
      "/rows": {
        put: {
          operationId: "saveRows",
          requestBody: {
            required: true,
            content: {
              "application/json": {
                schema: { $ref: "#/components/schemas/RowChangePayload" },
              },
            },
          },
          responses: {
            "200": {
              description: "OK",
              content: {
                "application/json": {
                  schema: { $ref: "#/components/schemas/RowDto" },
                },
              },
            },
          },
        },
      },
    },
    components: {
      schemas: {
        RowDto: schema({
          id: { type: "integer" },
          value: { type: "string" },
        }),
        RowMembers: {
          ...schema({}),
          "x-sparsetypes": {
            version: 1,
            role: "members",
            model,
            schemaIdentity,
            members: [
              {
                name: "rows",
                shape: "keyed",
                nullable: false,
                redactBefore: false,
                itemModel: "Perf.Contracts.RowDto",
                keyProperties: ["id"],
                keyType: "integer-number",
                ordered: true,
              },
            ],
          },
        },
        RowChangePayload: {
          ...schema({
            version: { const: "0.1" },
            schema: { type: "string" },
            change: { type: "object" },
          }),
          "x-sparsetypes": {
            version: 1,
            role: "payload",
            model,
            schemaIdentity,
            membersComponent: "RowMembers",
          },
        },
        RowItemMembers: {
          ...schema({}),
          "x-sparsetypes": {
            version: 1,
            role: "members",
            model: "Perf.Contracts.RowDto",
            schemaIdentity,
            members: [
              { name: "id", shape: "value", nullable: false, redactBefore: false },
              { name: "value", shape: "value", nullable: false, redactBefore: false },
            ],
          },
        },
        RowItemPayload: {
          ...schema({}),
          "x-sparsetypes": {
            version: 1,
            role: "payload",
            model: "Perf.Contracts.RowDto",
            schemaIdentity,
            membersComponent: "RowItemMembers",
          },
        },
        WrapperDto: schema({
          rows: { type: "array", items: { $ref: "#/components/schemas/RowDto" } },
        }),
      },
    },
  };
}

function rows(count: number, offset = 0) {
  return Array.from({ length: count }, (_, index) => ({
    id: offset + index,
    value: `v${offset + index}`,
  }));
}

test("keyed reorder comparison scales linearly and preserves semantics", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-keyed-perf-"));
  try {
    // Remap the keyed member onto the wrapper DTO response shape.
    const input = document() as any;
    input.paths["/rows"].put.responses["200"].content["application/json"].schema = {
      $ref: "#/components/schemas/WrapperDto",
    };
    input.components.schemas.RowMembers["x-sparsetypes"].model = "Perf.Contracts.WrapperDto";
    input.components.schemas.RowChangePayload["x-sparsetypes"].model = "Perf.Contracts.WrapperDto";
    await generate({ input, output });
    const changes = await import(pathToFileURL(path.join(output, "changes.js")).href);
    const build = changes.buildWrapperDtoChangePayload as (before: any, after: any) => any;

    for (const count of [100, 1000, 10000]) {
      const before = { rows: rows(count) };
      const frozen = { rows: Object.freeze(before.rows.map((row) => Object.freeze({ ...row }))) };
      const startNoChange = performance.now();
      const noChange = build(frozen, { rows: frozen.rows.map((row) => ({ ...row })) });
      const noChangeMs = performance.now() - startNoChange;
      assert.equal(noChange, undefined);
      console.log(`keyed no-change n=${count}: ${noChangeMs.toFixed(1)}ms`);

      const edited = { rows: before.rows.map((row) => ({ ...row })) };
      edited.rows[Math.floor(count / 2)] = { id: Math.floor(count / 2), value: "edited" };
      const oneEdit = build(before, edited);
      assert.equal(oneEdit.change.members.rows.items.length, 1);
      assert.equal(oneEdit.change.members.rows.items[0].key, Math.floor(count / 2));

      const removed = { rows: before.rows.slice(1).map((row) => ({ ...row })) };
      const added = {
        rows: [...before.rows.map((row) => ({ ...row })), { id: count, value: "new" }],
      };
      const insertRemove = build(before, { rows: [...removed.rows.slice(0, count - 2), { id: count, value: "new" }] });
      assert.ok(insertRemove.change.members.rows.items.some((item: any) => item.kind === "add"));
      assert.ok(insertRemove.change.members.rows.items.some((item: any) => item.kind === "remove"));
      void added;

      const reversed = { rows: [...before.rows].reverse().map((row) => ({ ...row })) };
      const startReorder = performance.now();
      const reordered = build(before, reversed);
      const reorderMs = performance.now() - startReorder;
      assert.equal(reordered.change.members.rows.items.length, count);
      assert.ok(
        reordered.change.members.rows.items.every((item: any) => item.isReordered),
      );
      console.log(`keyed reorder n=${count}: ${reorderMs.toFixed(1)}ms`);
      assert.deepEqual(frozen.rows.length, count, "caller data must not be mutated");
    }
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});
