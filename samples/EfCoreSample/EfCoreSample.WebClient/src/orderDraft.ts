import type { components, paths } from "./generated/api";

type OrderResponse = components["schemas"]["OrderDto"];
type GeneratedChangePayload = paths["/orders/{id}"]["put"] extends {
  requestBody: { content: { "application/json": infer Payload } };
} ? Payload : never;
type GeneratedChangeMembers = Extract<GeneratedChangePayload["change"], { kind: "update" }>["members"];
type LineMembers = components["schemas"]["EfCoreSample_Contracts_OrderLineDto_MembersDelta"];
type LinesChange = Extract<NonNullable<GeneratedChangeMembers["lines"]>, { kind: "update" }>;
type KeyedLineChange = LinesChange["items"][number];
type LineSnapshot = components["schemas"]["EfCoreSample_Contracts_OrderLineDto"];
type ChangeMembers = GeneratedChangeMembers;
export type ChangePayload = GeneratedChangePayload;
export type ClientPaths = paths;

export interface EditableLine {
  id: number;
  temporaryId: string | null;
  quantity: string;
  unitPrice: string;
  subtotal: string;
}

export interface EditableOrder {
  name: string;
  notes: string | null;
  code: string;
  shipCity: string;
  version: string;
  lines: EditableLine[];
}

function readNumber(value: number | string | undefined, field: string): number {
  if (value === undefined || value === "") throw new Error(`The server returned an incomplete ${field}.`);
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) throw new Error(`The server returned an invalid ${field}.`);
  return parsed;
}

function readInteger(value: number | string | undefined, field: string): number {
  const parsed = readNumber(value, field);
  if (!Number.isSafeInteger(parsed)) throw new Error(`The server returned an invalid ${field}.`);
  return parsed;
}

function readDecimal(value: number | string | undefined, field: string): string {
  const parsed = readNumber(value, field);
  return String(parsed);
}

export function toEditableOrder(order: OrderResponse): EditableOrder {
  if (
    typeof order.name !== "string"
    || typeof order.code !== "string"
    || typeof order.shipCity !== "string"
    || typeof order.version !== "string"
  ) {
    throw new Error("The server returned an incomplete order.");
  }

  const lines = (order.lines ?? []).map((line): EditableLine => {
    const id = readInteger(line.id ?? line.key, "line ID");
    const quantity = readInteger(line.quantity, "line quantity");
    const unitPrice = readDecimal(line.unitPrice, "line unit price");
    const subtotal = readDecimal(line.subtotal ?? quantity * Number(unitPrice), "line subtotal");
    const temporaryId = line.temporaryId ?? null;
    if (temporaryId !== null && typeof temporaryId !== "string") {
      throw new Error("The server returned an invalid line temporary ID.");
    }
    return { id, temporaryId, quantity: String(quantity), unitPrice, subtotal };
  });

  return {
    name: order.name,
    notes: order.notes ?? null,
    code: order.code,
    shipCity: order.shipCity,
    version: order.version,
    lines,
  };
}

function lineIdentity(line: EditableLine): string {
  if (line.id !== 0) return `id:${line.id}`;
  if (!line.temporaryId) throw new Error("A new line is missing its temporary ID.");
  return `temporary:${line.temporaryId}`;
}

function createLineMap(lines: EditableLine[]) {
  const result = new Map<string, { line: EditableLine; index: number }>();
  lines.forEach((line, index) => {
    const identity = lineIdentity(line);
    if (result.has(identity)) throw new Error(`Duplicate line identity: ${identity}`);
    result.set(identity, { line, index });
  });
  return result;
}

function value<T>(item: T) {
  return { state: "value" as const, value: item };
}

function lineSnapshot(line: EditableLine): LineSnapshot {
  const quantity = readInteger(line.quantity, "line quantity");
  const unitPrice = readDecimal(line.unitPrice, "line unit price");
  const subtotal = readDecimal(line.subtotal, "line subtotal");
  return {
    id: line.id,
    key: line.id,
    temporaryId: line.temporaryId,
    quantity,
    unitPrice: Number(unitPrice),
    subtotal: Number(subtotal),
  };
}

function addedLineSnapshot(line: EditableLine): LineSnapshot {
  const temporaryId = line.temporaryId;
  if (!temporaryId) throw new Error("A new line is missing its temporary ID.");
  return {
    id: line.id,
    key: line.id,
    temporaryId,
    quantity: readInteger(line.quantity, "line quantity"),
    unitPrice: Number(readDecimal(line.unitPrice, "line unit price")),
    subtotal: Number(readDecimal(line.subtotal, "line subtotal")),
  };
}

function replaceValue<T>(before: T, after: T) {
  return {
    kind: "replace" as const,
    before: value(before),
    after: value(after),
  };
}

function lineMemberChanges(before: EditableLine, after: EditableLine): LineMembers {
  const changes: LineMembers = {};
  const beforeQuantity = readInteger(before.quantity, "line quantity");
  const afterQuantity = readInteger(after.quantity, "line quantity");
  if (beforeQuantity < 1 || afterQuantity < 1) throw new Error("Line quantity must be at least one.");
  if (beforeQuantity !== afterQuantity) changes.quantity = replaceValue(beforeQuantity, afterQuantity);

  const beforePrice = readDecimal(before.unitPrice, "line unit price");
  const afterPrice = readDecimal(after.unitPrice, "line unit price");
  if (Number(beforePrice) < 0 || Number(afterPrice) < 0) throw new Error("Unit price cannot be negative.");
  if (beforePrice !== afterPrice) changes.unitPrice = replaceValue(beforePrice, afterPrice);
  return changes;
}

function buildLinesChange(before: EditableLine[], after: EditableLine[]): LinesChange | undefined {
  const beforeByIdentity = createLineMap(before);
  const afterByIdentity = createLineMap(after);
  const items: KeyedLineChange[] = [];

  after.forEach((line, afterIndex) => {
    const identity = lineIdentity(line);
    const previous = beforeByIdentity.get(identity);
    if (!previous) {
      const temporaryId = line.temporaryId;
      if (!temporaryId) throw new Error("A new line is missing its temporary ID.");
      items.push({
        key: line.id,
        kind: "add",
        afterIndex,
        temporaryKey: temporaryId,
        after: { state: "value", value: addedLineSnapshot(line) },
      });
      return;
    }

    const members = lineMemberChanges(previous.line, line);
    const reordered = previous.index !== afterIndex;
    if (Object.keys(members).length > 0 || reordered) {
      items.push({
        key: line.id,
        kind: "update",
        beforeIndex: previous.index,
        afterIndex,
        ...(line.id === 0 && line.temporaryId ? { temporaryKey: line.temporaryId } : {}),
        ...(reordered ? { isReordered: true } : {}),
        ...(Object.keys(members).length > 0 ? { members } : {}),
      });
    }
  });

  before.forEach((line, beforeIndex) => {
    if (afterByIdentity.has(lineIdentity(line))) return;
    items.push({
      key: line.id,
      kind: "remove",
      beforeIndex,
      ...(line.id === 0 && line.temporaryId ? { temporaryKey: line.temporaryId } : {}),
      before: { state: "value", value: lineSnapshot(line) },
    });
  });

  if (items.length === 0) return undefined;
  return {
    kind: "update",
    items,
    beforeOrder: before.map((line) => line.id),
    afterOrder: after.map((line) => line.id),
    tempBeforeOrder: before.flatMap((line) => line.id === 0 && line.temporaryId ? [line.temporaryId] : []),
    tempAfterOrder: after.flatMap((line) => line.id === 0 && line.temporaryId ? [line.temporaryId] : []),
  };
}

function replaceNullable(before: string | null, after: string | null) {
  const endpoint = (item: string | null) =>
    item === null ? { state: "null" as const } : value(item);
  return { kind: "replace" as const, before: endpoint(before), after: endpoint(after) };
}

function buildChanges(before: EditableOrder, after: EditableOrder): ChangeMembers {
  const changes: ChangeMembers = {};
  if (before.name !== after.name) changes.name = replaceValue(before.name, after.name);
  if (before.notes !== after.notes) changes.notes = replaceNullable(before.notes, after.notes);
  if (before.code !== after.code) changes.code = replaceValue(before.code, after.code);
  if (before.shipCity !== after.shipCity) changes.shipCity = replaceValue(before.shipCity, after.shipCity);
  const lines = buildLinesChange(before.lines, after.lines);
  if (lines) changes.lines = lines;
  return changes;
}

export function buildChangePayload(before: EditableOrder, after: EditableOrder, schema: string): ChangePayload {
  return {
    version: "0.1",
    schema,
    change: { kind: "update", members: buildChanges(before, after) },
  };
}
