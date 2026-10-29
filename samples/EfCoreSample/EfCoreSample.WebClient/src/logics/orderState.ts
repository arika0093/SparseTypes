import {
  parseOrderDto,
  type OrderDto,
  type OrderLineDto,
} from "@sparsetypes/generated";

export type EditableLine = Required<OrderLineDto>;
export type EditableOrder = Required<Omit<OrderDto, "lines">> & {
  lines: EditableLine[];
};

function readNumber(value: number | undefined, field: string): number {
  if (value === undefined || !Number.isFinite(value))
    throw new Error(`The server returned an invalid ${field}.`);
  return value;
}

function readInteger(value: number | undefined, field: string): number {
  const parsed = readNumber(value, field);
  if (!Number.isSafeInteger(parsed))
    throw new Error(`The server returned an invalid ${field}.`);
  return parsed;
}

export function toEditableOrder(value: unknown): EditableOrder {
  const order = parseOrderDto(value);
  if (
    typeof order.name !== "string" ||
    typeof order.code !== "string" ||
    typeof order.shipCity !== "string" ||
    typeof order.version !== "string"
  ) {
    throw new Error("The server returned an incomplete order.");
  }

  const lines = (order.lines ?? []).map((line): EditableLine => {
    const id = readInteger(line.id, "line ID");
    const key = readInteger(line.key ?? id, "line key");
    const temporaryId = line.temporaryId ?? null;
    if (temporaryId !== null && typeof temporaryId !== "string") {
      throw new Error("The server returned an invalid line temporary ID.");
    }
    return {
      ...line,
      id,
      key,
      temporaryId,
      quantity: readInteger(line.quantity, "line quantity"),
      unitPrice: readNumber(line.unitPrice, "line unit price"),
      subtotal: readNumber(line.subtotal, "line subtotal"),
    };
  });

  return {
    ...order,
    name: order.name,
    notes: order.notes ?? null,
    code: order.code,
    shipCity: order.shipCity,
    displayTotal: order.displayTotal ?? null,
    address: order.address ?? null,
    version: order.version,
    lines,
  };
}
