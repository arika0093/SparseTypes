import assert from "node:assert/strict";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { pathToFileURL } from "node:url";
import test from "node:test";
import { generate } from "../src/generate.js";
import { metadataOf, refName } from "../src/openapi.js";
import type { OpenApiDocument } from "../src/openapi.js";

const schemaIdentity = "sha256:" + "d".repeat(64);
const orderModel = "Sample.Contracts.OrderDto";
const lineModel = "Sample.Contracts.OrderLineDto";

function payloadMetadata(model: string, membersComponent: string, dto: string) {
  return {
    version: 1,
    role: "payload",
    model,
    schemaIdentity,
    membersComponent,
    dto,
  };
}

// Mirrors what the C# transformer pipeline publishes: the payload metadata
// carries an explicit resolvable model-to-DTO association. The PUT operation
// below answers 204 with no response schema, so response-shape inference
// cannot identify the DTO; the explicit mapping can.
function document(withOkResponse = false): OpenApiDocument {
  const schema = (properties: Record<string, any>) => ({
    type: "object",
    properties,
  });
  return {
    openapi: "3.1.0",
    info: { title: "DTO association", version: "1.0" },
    servers: [{ url: "/" }],
    paths: {
      "/orders/{id}": {
        get: {
          operationId: "getOrder",
          parameters: [{ name: "id", in: "path", required: true, schema: { type: "integer" } }],
          responses: {
            "200": {
              description: "OK",
              content: { "application/json": { schema: { $ref: "#/components/schemas/OrderDto" } } },
            },
          },
        },
        put: {
          operationId: "saveOrder",
          parameters: [{ name: "id", in: "path", required: true, schema: { type: "integer" } }],
          requestBody: {
            required: true,
            content: { "application/json": { schema: { $ref: "#/components/schemas/ChangePayload" } } },
          },
          responses: withOkResponse
            ? {
                "200": {
                  description: "OK",
                  content: { "application/json": { schema: { $ref: "#/components/schemas/OrderDto" } } },
                },
              }
            : { "204": { description: "No content" } },
        },
      },
    },
    components: {
      schemas: {
        OrderDto: schema({
          name: { type: "string" },
          lines: { type: "array", items: { $ref: "#/components/schemas/OrderLineDto" } },
        }),
        OrderLineDto: schema({
          id: { type: "integer" },
          quantity: { type: "integer" },
        }),
        OrderMembers: {
          ...schema({}),
          "x-sparsetypes": {
            version: 1,
            role: "members",
            model: orderModel,
            schemaIdentity,
            members: [
              { name: "name", shape: "value", nullable: false, redactBefore: false },
              {
                name: "lines",
                shape: "keyed",
                nullable: false,
                redactBefore: false,
                itemModel: lineModel,
                keyProperties: ["id"],
                keyType: "integer-number",
                ordered: false,
              },
            ],
          },
        },
        ChangePayload: {
          ...schema({
            version: { const: "0.1" },
            schema: { type: "string" },
            change: { type: "object" },
          }),
          "x-sparsetypes": payloadMetadata(orderModel, "OrderMembers", "OrderDto"),
        },
        OrderLineMembers: {
          ...schema({}),
          "x-sparsetypes": {
            version: 1,
            role: "members",
            model: lineModel,
            schemaIdentity,
            members: [
              { name: "id", shape: "value", nullable: false, redactBefore: false },
              { name: "quantity", shape: "value", nullable: false, redactBefore: false },
            ],
          },
        },
        OrderLineChangePayload: {
          ...schema({}),
          "x-sparsetypes": payloadMetadata(lineModel, "OrderLineMembers", "OrderLineDto"),
        },
      },
    },
  };
}

function resolveDto(document: OpenApiDocument, payloadComponent: string): string {
  const schema = document.components.schemas[payloadComponent];
  const metadata = metadataOf(schema);
  assert.ok(metadata, `${payloadComponent} must carry x-sparsetypes metadata`);
  const dto = (metadata as Record<string, any>).dto;
  assert.equal(typeof dto, "string", `${payloadComponent} must publish an explicit dto association`);
  assert.ok(document.components.schemas[dto], `dto component '${dto}' must exist in the document`);
  return dto;
}

test("explicit SparseModel-to-DTO associations resolve without response inference", async () => {
  const input = document();
  assert.equal(resolveDto(input, "ChangePayload"), "OrderDto");
  assert.equal(resolveDto(input, "OrderLineChangePayload"), "OrderLineDto");

  // The explicit mapping stays valid for OpenAPI 3.0 documents too.
  const as30 = JSON.parse(JSON.stringify(input));
  as30.openapi = "3.0.0";
  assert.equal(resolveDto(as30, "ChangePayload"), "OrderDto");

  // The associated DTO component matches the model the operation exposes.
  const getResponse = input.paths["/orders/{id}"].get.responses["200"].content["application/json"].schema;
  assert.equal(refName(getResponse), "OrderDto");
});

test("documents carrying dto associations still generate isolated TS consumers", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-dto-"));
  try {
    await generate({ input: document(true), output });
    const changes = await import(pathToFileURL(path.join(output, "changes.js")).href);
    const payload = changes.buildOrderDtoChangePayload({ name: "Before" }, { name: "After" });
    assert.equal(payload.change.members.name.after.value, "After");
    const models = await readFile(path.join(output, "models.d.ts"), "utf8");
    assert.match(models, /OptionalOrderDto/);
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});
