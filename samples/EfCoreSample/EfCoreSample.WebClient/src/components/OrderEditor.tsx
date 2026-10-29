import {
  DndContext,
  KeyboardSensor,
  PointerSensor,
  closestCenter,
  useSensor,
  useSensors,
  type DragEndEvent,
} from "@dnd-kit/core";
import {
  SortableContext,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import {
  ActionIcon,
  Button,
  Group,
  NumberInput,
  Paper,
  SimpleGrid,
  Stack,
  Table,
  Text,
  TextInput,
  Textarea,
  Tooltip,
} from "@mantine/core";
import type { CSSProperties } from "react";
import type { EditableLine, EditableOrder } from "../logics/orderState";

type EditableField = "name" | "notes" | "code" | "shipCity";

interface OrderEditorProps {
  order: EditableOrder;
  disabled: boolean;
  onFieldChange: (field: EditableField, value: string) => void;
  onLineChange: (
    index: number,
    patch: Partial<Pick<EditableLine, "quantity" | "unitPrice">>,
  ) => void;
  onAddLine: () => void;
  onReorderLines: (fromIndex: number, toIndex: number) => void;
  onRemoveLine: (index: number) => void;
}

interface SortableLineRowProps {
  line: EditableLine;
  index: number;
  disabled: boolean;
  onLineChange: OrderEditorProps["onLineChange"];
  onRemoveLine: OrderEditorProps["onRemoveLine"];
}

function sortableLineId(line: EditableLine): string {
  if (line.id > 0) return `line-${line.id}`;
  if (!line.temporaryId)
    throw new Error("A new line is missing its temporary ID.");
  return `new-${line.temporaryId}`;
}

function SortableLineRow({
  line,
  index,
  disabled,
  onLineChange,
  onRemoveLine,
}: SortableLineRowProps) {
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({
    id: sortableLineId(line),
    disabled,
  });
  const style: CSSProperties = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.5 : undefined,
  };

  return (
    <Table.Tr ref={setNodeRef} style={style}>
      <Table.Td>{line.id > 0 ? `#${line.id}` : "New"}</Table.Td>
      <Table.Td>
        <NumberInput
          aria-label={`Line ${index + 1} quantity`}
          value={line.quantity}
          onChange={(value) => {
            const parsed = Number(value);
            if (Number.isFinite(parsed))
              onLineChange(index, { quantity: parsed });
          }}
          min={1}
          step={1}
          decimalScale={0}
          disabled={disabled}
        />
      </Table.Td>
      <Table.Td>
        <NumberInput
          aria-label={`Line ${index + 1} unit price`}
          value={line.unitPrice}
          onChange={(value) => {
            const parsed = Number(value);
            if (Number.isFinite(parsed))
              onLineChange(index, { unitPrice: parsed });
          }}
          min={0}
          decimalScale={2}
          disabled={disabled}
        />
      </Table.Td>
      <Table.Td>{line.subtotal}</Table.Td>
      <Table.Td>
        <Group gap="xs" wrap="nowrap">
          <Tooltip label="Drag to reorder">
            <ActionIcon
              {...attributes}
              {...listeners}
              variant="subtle"
              aria-label="Reorder line"
              disabled={disabled}
              style={{ touchAction: "none" }}
            >
              <span aria-hidden="true">⠿</span>
            </ActionIcon>
          </Tooltip>
          <ActionIcon
            color="red"
            variant="light"
            aria-label="Remove line"
            onClick={() => onRemoveLine(index)}
            disabled={disabled}
          >
            ×
          </ActionIcon>
        </Group>
      </Table.Td>
    </Table.Tr>
  );
}

export default function OrderEditor({
  order,
  disabled,
  onFieldChange,
  onLineChange,
  onAddLine,
  onReorderLines,
  onRemoveLine,
}: OrderEditorProps) {
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    }),
  );

  function handleDragEnd({ active, over }: DragEndEvent) {
    if (!over || active.id === over.id) return;
    const fromIndex = order.lines.findIndex(
      (line) => sortableLineId(line) === active.id,
    );
    const toIndex = order.lines.findIndex(
      (line) => sortableLineId(line) === over.id,
    );
    if (fromIndex >= 0 && toIndex >= 0) onReorderLines(fromIndex, toIndex);
  }

  return (
    <Stack gap="lg">
      <Paper withBorder p="md" radius="md">
        <SimpleGrid cols={{ base: 1, sm: 2 }}>
          <TextInput
            label="Customer"
            value={order.name}
            onChange={(event) =>
              onFieldChange("name", event.currentTarget.value)
            }
            disabled={disabled}
          />
          <TextInput
            label="Code"
            value={order.code}
            onChange={(event) =>
              onFieldChange("code", event.currentTarget.value)
            }
            disabled={disabled}
          />
          <TextInput
            label="Shipping city"
            value={order.shipCity}
            onChange={(event) =>
              onFieldChange("shipCity", event.currentTarget.value)
            }
            disabled={disabled}
          />
          <Textarea
            label="Notes"
            value={order.notes ?? ""}
            onChange={(event) =>
              onFieldChange("notes", event.currentTarget.value)
            }
            disabled={disabled}
          />
        </SimpleGrid>
      </Paper>
      <Paper withBorder p="md" radius="md">
        <Stack gap="md">
          <Group justify="space-between">
            <div>
              <Text fw={600}>Order lines</Text>
              <Text size="sm" c="dimmed">
                {order.lines.length} items
              </Text>
            </div>
            <Button onClick={onAddLine} disabled={disabled}>
              Add line
            </Button>
          </Group>
          <Table.ScrollContainer minWidth={640}>
            <DndContext
              sensors={sensors}
              collisionDetection={closestCenter}
              onDragEnd={handleDragEnd}
            >
              <SortableContext
                items={order.lines.map(sortableLineId)}
                strategy={verticalListSortingStrategy}
              >
                <Table verticalSpacing="sm">
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th>Line</Table.Th>
                      <Table.Th>Quantity</Table.Th>
                      <Table.Th>Unit price</Table.Th>
                      <Table.Th>Subtotal</Table.Th>
                      <Table.Th>Actions</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {order.lines.map((line, index) => (
                      <SortableLineRow
                        key={sortableLineId(line)}
                        line={line}
                        index={index}
                        disabled={disabled}
                        onLineChange={onLineChange}
                        onRemoveLine={onRemoveLine}
                      />
                    ))}
                  </Table.Tbody>
                </Table>
              </SortableContext>
            </DndContext>
          </Table.ScrollContainer>
        </Stack>
      </Paper>
    </Stack>
  );
}
