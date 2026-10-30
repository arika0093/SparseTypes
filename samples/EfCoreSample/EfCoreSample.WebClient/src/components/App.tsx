import { useState } from "react";
import {
  Accordion,
  Alert,
  Button,
  Container,
  Group,
  Paper,
  Stack,
  Text,
  TextInput,
  Title,
} from "@mantine/core";
import {
  ApiError,
  buildOrderDtoChangePayload,
  createClient,
} from "@sparsetypes/generated";
import OrderEditor from "./OrderEditor";
import JsonHighlight from "./JsonHighlight";
import {
  toEditableOrder,
  type EditableLine,
  type EditableOrder,
} from "../logics/orderState";

const api = createClient();

export default function App() {
  const [orderId, setOrderId] = useState("1");
  const [original, setOriginal] = useState<EditableOrder>();
  const [draft, setDraft] = useState<EditableOrder>();
  const [status, setStatus] = useState("Load an order to begin.");
  const [statusColor, setStatusColor] = useState<"blue" | "green" | "red">(
    "blue",
  );
  const [busy, setBusy] = useState(false);

  function report(message: string, color: "blue" | "green" | "red" = "blue") {
    setStatus(message);
    setStatusColor(color);
  }

  async function load() {
    setBusy(true);
    try {
      const id = Number(orderId);
      if (!Number.isSafeInteger(id) || id < 1)
        throw new Error("Enter a positive order ID.");
      const loaded = toEditableOrder(
        await api.getOrdersById({ params: { path: { id } } }),
      );
      setOriginal(loaded);
      setDraft(loaded);
      report(`Loaded order ${id}.`, "green");
    } catch (error) {
      report(error instanceof Error ? error.message : String(error), "red");
    } finally {
      setBusy(false);
    }
  }

  async function save() {
    if (!original || !draft) return;
    setBusy(true);
    try {
      for (const line of draft.lines) {
        if (!Number.isSafeInteger(line.quantity) || line.quantity < 1)
          throw new Error("Line quantity must be at least one.");
        if (!Number.isFinite(line.unitPrice) || line.unitPrice < 0)
          throw new Error("Unit price cannot be negative.");
      }

      const body = buildOrderDtoChangePayload(original, draft);
      if (!body) {
        report("There are no changes to save.");
        return;
      }

      try {
        const saved = toEditableOrder(
          await api.putOrdersById({
            params: { path: { id: Number(orderId) } },
            headers: { "If-Match": `"${original.version}"` },
            body,
          }),
        );
        setOriginal(saved);
        setDraft(saved);
        report("Saved.", "green");
      } catch (error) {
        if (!(error instanceof ApiError)) throw error;
        report(
          error.status === 409
            ? `Overlapping edits need resolution: ${JSON.stringify(error.body)}`
            : error.status === 412
              ? "The order changed on the server. Reload it before saving again."
              : `Save failed: ${JSON.stringify(error.body)}`,
          "red",
        );
      }
    } catch (error) {
      report(error instanceof Error ? error.message : String(error), "red");
    } finally {
      setBusy(false);
    }
  }

  function editField(
    field: "name" | "notes" | "code" | "shipCity",
    value: string,
  ) {
    setDraft((current) => {
      if (!current) return current;
      if (field === "notes") return { ...current, notes: value || null };
      return { ...current, [field]: value };
    });
  }

  function updateLine(
    index: number,
    patch: Partial<Pick<EditableLine, "quantity" | "unitPrice">>,
  ) {
    setDraft(
      (current) =>
        current && {
          ...current,
          lines: current.lines.map((line, lineIndex) =>
            lineIndex === index ? { ...line, ...patch } : line,
          ),
        },
    );
  }

  function addLine() {
    setDraft(
      (current) =>
        current && {
          ...current,
          lines: [
            ...current.lines,
            {
              id: 0,
              key: 0,
              temporaryId: crypto.randomUUID(),
              quantity: 1,
              unitPrice: 100,
              subtotal: 0,
            },
          ],
        },
    );
  }

  function reorderLines(fromIndex: number, toIndex: number) {
    setDraft((current) => {
      if (!current) return current;
      if (
        fromIndex < 0 ||
        fromIndex >= current.lines.length ||
        toIndex < 0 ||
        toIndex >= current.lines.length
      )
        return current;
      const lines = [...current.lines];
      const [line] = lines.splice(fromIndex, 1);
      lines.splice(toIndex, 0, line);
      return { ...current, lines };
    });
  }

  function removeLine(index: number) {
    setDraft(
      (current) =>
        current && {
          ...current,
          lines: current.lines.filter((_, lineIndex) => lineIndex !== index),
        },
    );
  }

  return (
    <Container size="lg" py="xl">
      <Stack gap="lg">
        <div>
          <Title order={1}>Order editor</Title>
          <Text c="dimmed" mt="xs">
            OpenAPI generates the client types. Edits are converted to a
            ChangePayload before submission.
          </Text>
        </div>
        <Paper withBorder p="md" radius="md">
          <Group align="end">
            <TextInput
              label="Order ID"
              value={orderId}
              onChange={(event) => setOrderId(event.currentTarget.value)}
              inputMode="numeric"
            />
            <Button onClick={load} loading={busy}>
              Load
            </Button>
            <Button onClick={save} loading={busy} disabled={!draft}>
              Save changes
            </Button>
          </Group>
        </Paper>
        {original && draft && (
          <OrderEditor
            order={draft}
            original={original}
            disabled={busy}
            onFieldChange={editField}
            onLineChange={updateLine}
            onAddLine={addLine}
            onReorderLines={reorderLines}
            onRemoveLine={removeLine}
          />
        )}
        <Alert color={statusColor} role="status">
          {status}
        </Alert>
        {original && draft && (
          <Accordion variant="separated">
            <Accordion.Item value="payload">
              <Accordion.Control>Pending ChangePayload</Accordion.Control>
              <Accordion.Panel>
                <JsonHighlight
                  code={JSON.stringify(
                    buildOrderDtoChangePayload(original, draft),
                    null,
                    2,
                  )}
                />
              </Accordion.Panel>
            </Accordion.Item>
          </Accordion>
        )}
      </Stack>
    </Container>
  );
}
