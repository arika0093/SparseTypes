import assert from "node:assert/strict";
import { mkdtemp, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { pathToFileURL } from "node:url";
import test from "node:test";
import { generate } from "../src/generate.js";
import type { OpenApiDocument } from "../src/openapi.js";

const schemaIdentity = "sha256:" + "c".repeat(64);
const model = "Temporal.Contracts.EventDto";

function document(): OpenApiDocument {
  const schema = (properties: Record<string, any>) => ({
    type: "object",
    properties,
  });
  return {
    openapi: "3.1.0",
    info: { title: "Temporal", version: "1.0" },
    servers: [{ url: "/" }],
    paths: {
      "/events": {
        put: {
          operationId: "saveEvent",
          requestBody: {
            required: true,
            content: {
              "application/json": {
                schema: { $ref: "#/components/schemas/EventChangePayload" },
              },
            },
          },
          responses: {
            "200": {
              description: "OK",
              content: {
                "application/json": {
                  schema: { $ref: "#/components/schemas/EventDto" },
                },
              },
            },
          },
        },
      },
    },
    components: {
      schemas: {
        EventDto: schema({
          id: { type: "string", format: "uuid" },
          at: { type: "string" },
          stamped: { type: "string" },
        }),
        EventMembers: {
          ...schema({}),
          "x-sparsetypes": {
            version: 1,
            role: "members",
            model,
            schemaIdentity,
            members: [
              { name: "id", shape: "value", nullable: false, redactBefore: false, wireType: "guid-string", comparison: "default" },
              { name: "at", shape: "value", nullable: false, redactBefore: false, wireType: "datetime-string", comparison: "default" },
              { name: "stamped", shape: "value", nullable: false, redactBefore: false, wireType: "datetimeoffset-string", comparison: "default" },
            ],
          },
        },
        EventChangePayload: {
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
            membersComponent: "EventMembers",
          },
        },
      },
    },
  };
}

test("temporal and Guid equality matches C# change comparisons", async () => {
  const output = await mkdtemp(path.join(os.tmpdir(), "sparsetypes-temporal-"));
  try {
    await generate({ input: document(), output });
    const changes = await import(pathToFileURL(path.join(output, "changes.js")).href);
    const build = changes.buildEventDtoChangePayload as (before: any, after: any) => any;

    // Guid: C# Guid equality is value-based, so case variations are unchanged.
    assert.equal(
      build(
        { id: "F4EF9705-B5E2-41F6-A7AC-1F3B0A8C14DE", at: "2024-05-01T12:00:00.0000000Z", stamped: "2024-05-01T12:00:00.0000000Z" },
        { id: "f4ef9705-b5e2-41f6-a7ac-1f3b0a8c14de", at: "2024-05-01T12:00:00.0000000Z", stamped: "2024-05-01T12:00:00.0000000Z" },
      ),
      undefined,
    );
    const guidChanged = build(
      { id: "f4ef9705-b5e2-41f6-a7ac-1f3b0a8c14de", at: "2024-05-01T12:00:00.0000000Z", stamped: "2024-05-01T12:00:00.0000000Z" },
      { id: "00000000-0000-0000-0000-000000000001", at: "2024-05-01T12:00:00.0000000Z", stamped: "2024-05-01T12:00:00.0000000Z" },
    );
    assert.ok(guidChanged.change.members.id);

    // DateTimeOffset: the same instant with different offsets is unchanged in C#.
    assert.equal(
      build(
        { id: "f4ef9705-b5e2-41f6-a7ac-1f3b0a8c14de", at: "2024-05-01T12:00:00.0000000Z", stamped: "2024-05-01T14:00:00.0000000+02:00" },
        { id: "f4ef9705-b5e2-41f6-a7ac-1f3b0a8c14de", at: "2024-05-01T12:00:00.0000000Z", stamped: "2024-05-01T12:00:00.0000000Z" },
      ),
      undefined,
    );
    const instantChanged = build(
      { id: "f4ef9705-b5e2-41f6-a7ac-1f3b0a8c14de", at: "2024-05-01T12:00:00.0000000Z", stamped: "2024-05-01T12:00:00.0000000Z" },
      { id: "f4ef9705-b5e2-41f6-a7ac-1f3b0a8c14de", at: "2024-05-01T12:00:00.0000000Z", stamped: "2024-05-01T12:00:00.0000001Z" },
    );
    assert.ok(instantChanged.change.members.stamped, "sub-millisecond ticks must not be rounded away");

    // DateTime: C# compares ticks only, so Utc vs Unspecified with the same
    // wall-clock value is unchanged; Kind stays on the wire (Z vs absent).
    assert.equal(
      build(
        { id: "f4ef9705-b5e2-41f6-a7ac-1f3b0a8c14de", at: "2024-05-01T12:00:00.0000000Z", stamped: "2024-05-01T12:00:00.0000000Z" },
        { id: "f4ef9705-b5e2-41f6-a7ac-1f3b0a8c14de", at: "2024-05-01T12:00:00.0000000", stamped: "2024-05-01T12:00:00.0000000Z" },
      ),
      undefined,
    );
    // Seven fractional digits are retained: a single-tick difference is a change.
    const tickChanged = build(
      { id: "f4ef9705-b5e2-41f6-a7ac-1f3b0a8c14de", at: "2024-05-01T12:00:00.0000000Z", stamped: "2024-05-01T12:00:00.0000000Z" },
      { id: "f4ef9705-b5e2-41f6-a7ac-1f3b0a8c14de", at: "2024-05-01T12:00:00.0000001Z", stamped: "2024-05-01T12:00:00.0000000Z" },
    );
    assert.ok(tickChanged.change.members.at);
  } finally {
    await rm(output, { recursive: true, force: true });
  }
});
