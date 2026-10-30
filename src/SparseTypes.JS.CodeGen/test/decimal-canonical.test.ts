import assert from "node:assert/strict";
import { mkdtemp, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { pathToFileURL } from "node:url";
import test from "node:test";
import { generate } from "../src/generate.js";

const schemaIdentity = "sha256:" + "e".repeat(64);

function document(): any {
  const schema = (properties: Record<string, any>) => ({
    type: "object",
    properties,
  });
  return {
    openapi: "3.1.0",
    info: { title: "Decimal", version: "1.0" },
    servers: [{ url: "/" }],
    paths: {
      "/prices": {
        put: {
          operationId: "savePrice",
          requestBody: {
            required: true,
            content: { "application/json": { schema: { $ref: "#/components/schemas/PriceChangePayload" } } },
          },
          responses: {
            "200": {
              description: "OK",
              content: { "application/json": { schema: { $ref: "#/components/schemas/PriceDto" } } },
            },
          },
        },
      },
    },
    components: {
      schemas: {
        PriceDto: schema({
          amount: { type: "string" },
          count: { type: "string" },
        }),
        PriceMembers: {
          ...schema({}),
          "x-sparsetypes": {
            version: 1,
            role: "members",
            model: "Shop.Contracts.PriceDto",
            schemaIdentity,
            members: [
              { name: "amount", shape: "value", nullable: false, redactBefore: false, wireType: "decimal-string", comparison: "default" },
              { name: "count", shape: "value", nullable: false, redactBefore: false, wireType: "int64-string", comparison: "default" },
            ],
          },
        },
        PriceChangePayload: {
          ...schema({
            version: { const: "0.1" },
            schema: { type: "string" },
            change: { type: "object" },
          }),
          "x-sparsetypes": {
            version: 1,
            role: "payload",
            model: "Shop.Contracts.PriceDto",
            schemaIdentity,
            membersComponent: "PriceMembers",
          },
        },
      },
    },
  };
}

test("JS emits canonical decimal strings for numeric inputs", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-decimal-"));
  try {
    await generate({ input: document(), output });
    const changes = await import(pathToFileURL(path.join(output, "changes.js")).href);
    const build = changes.buildPriceDtoChangePayload as (before: any, after: any) => any;

    // Exponent-form numbers expand to canonical fixed-point strings.
    const big = build({}, { amount: 1e21, count: 42 });
    assert.equal(big.change.members.amount.after.value, "1000000000000000000000");
    assert.equal(big.change.members.count.after.value, "42");

    const tiny = build({}, { amount: 1e-7, count: 7 });
    assert.equal(tiny.change.members.amount.after.value, "0.0000001");

    // Canonical strings pass through unchanged with scale preserved.
    // (Strict canonical-form rejection of decimal strings such as "1e-1"
    // lives in the C# reader and is locked by NonCanonicalPayloadScalarStrings.)
    const scaled = build({}, { amount: "0.10", count: "7" });
    assert.equal(scaled.change.members.amount.after.value, "0.10");

    // Numbers outside the decimal range or precision are rejected so the
    // builder never emits a wire value the C# reader cannot decode.
    assert.throws(() => build({}, { amount: 1e30, count: 1 }), /out of range/);
    assert.throws(() => build({}, { amount: 5e-324, count: 1 }), /precision/);
    const maxish = build({}, { amount: 7.9e28, count: 1 });
    assert.equal(maxish.change.members.amount.after.value, "79000000000000000000000000000");
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});
