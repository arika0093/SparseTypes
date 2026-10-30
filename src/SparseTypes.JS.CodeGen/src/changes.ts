import {
  dereferenceObject,
  metadataOf,
  refName,
  schemaRef,
  unwrapSchema,
} from "./openapi.js";
import type { OpenApiDocument } from "./openapi.js";

export interface ChangeMember extends Record<string, any> {
  name: string;
  shape: string;
}

export interface ChangeModel {
  model: string;
  dto: string;
  membersComponent: string;
  members: ChangeMember[];
  payload: string;
  schemaIdentity: string;
  typeName: string;
}

// Validation-critical keywords that parseT cannot enforce. Generation fails
// when a DTO schema uses them so invalid values are never silently accepted.
const UNSUPPORTED_VALIDATION_KEYWORDS = [
  "contains",
  "minContains",
  "maxContains",
  "prefixItems",
  "patternProperties",
  "propertyNames",
  "dependentRequired",
  "dependentSchemas",
  "dependencies",
  "unevaluatedProperties",
  "unevaluatedItems",
  "additionalItems",
];

export function assertSupportedValidationSchemas(
  document: OpenApiDocument,
  models: ChangeModel[],
): void {
  for (const model of models) {
    const root = document.components.schemas[model.dto];
    if (!root) continue;
    checkValidationSchema(
      root,
      `#/components/schemas/${model.dto}`,
      new Set(),
      new WeakSet(),
      document,
      model,
    );
  }
}

function checkValidationSchema(
  schema: Record<string, any>,
  path: string,
  resolving: Set<string>,
  seen: WeakSet<object>,
  document: OpenApiDocument,
  model: ChangeModel,
): void {
  if (!schema || typeof schema !== "object" || seen.has(schema)) return;
  seen.add(schema);
  if (typeof schema.$ref === "string") {
    const name = refName(schema);
    if (!name)
      throw new Error(
        `Model '${model.model}' uses an external schema reference '${schema.$ref}' at '${path}', which parseT cannot resolve.`,
      );
    const target = document.components.schemas[name];
    if (!target)
      throw new Error(
        `Model '${model.model}' references unknown OpenAPI schema '${name}' at '${path}'.`,
      );
    if (!resolving.has(name)) {
      resolving.add(name);
      try {
        checkValidationSchema(
          target,
          `#/components/schemas/${name}`,
          resolving,
          seen,
          document,
          model,
        );
      } finally {
        resolving.delete(name);
      }
    }
  }
  for (const keyword of UNSUPPORTED_VALIDATION_KEYWORDS) {
    if (schema[keyword] !== undefined)
      throw new Error(
        `Model '${model.model}' uses unsupported validation keyword '${keyword}' in OpenAPI schema '${path}'. parseT cannot enforce it; remodel the constraint with a supported keyword.`,
      );
  }
  if (Array.isArray(schema.items))
    throw new Error(
      `Model '${model.model}' uses tuple-form 'items' in OpenAPI schema '${path}'. parseT only supports single-schema 'items'.`,
    );
  const children: Array<[string, unknown]> = [];
  for (const [name, child] of Object.entries(schema.properties ?? {}))
    children.push([`${path}/properties/${name}`, child]);
  if (schema.items) children.push([`${path}/items`, schema.items]);
  if (schema.additionalProperties && typeof schema.additionalProperties === "object")
    children.push([`${path}/additionalProperties`, schema.additionalProperties]);
  for (const keyword of ["allOf", "anyOf", "oneOf"] as const)
    for (const [index, child] of (schema[keyword] ?? []).entries())
      children.push([`${path}/${keyword}/${index}`, child]);
  if (schema.not) children.push([`${path}/not`, schema.not]);
  for (const [childPath, child] of children)
    if (child && typeof child === "object")
      checkValidationSchema(
        child as Record<string, any>,
        childPath,
        resolving,
        seen,
        document,
        model,
      );
}

export function collectChangeModels(document: OpenApiDocument): ChangeModel[] {
  const schemas = Object.entries(document.components.schemas);
  const membersByModel = new Map<
    string,
    { component: string; metadata: Record<string, any> }
  >();
  const payloadByComponent = new Map<
    string,
    { component: string; metadata: Record<string, any> }
  >();
  const payloadByModel = new Map<
    string,
    { component: string; metadata: Record<string, any> }
  >();
  for (const [component, schema] of schemas) {
    const metadata = metadataOf(schema);
    if (metadata?.role === "members")
      membersByModel.set(metadata.model, { component, metadata });
    if (metadata?.role === "payload") {
      const payload = { component, metadata };
      payloadByComponent.set(component, payload);
      if (!payloadByModel.has(metadata.model))
        payloadByModel.set(metadata.model, payload);
    }
  }
  if (membersByModel.size === 0 || payloadByModel.size === 0) {
    throw new Error(
      "OpenAPI document has no x-sparsetypes payload/member metadata. Rebuild the server with SparseTypes TypeScript metadata enabled.",
    );
  }

  const dtoByModel = new Map<string, string>();
  for (const [model, payload] of payloadByModel) {
    const explicit = explicitDtoComponent(document, model, payload);
    const inferred = inferredDtoComponents(
      document,
      payloadByComponent,
      payload,
    );
    if (explicit) {
      if (inferred.size > 0 && !inferred.has(explicit)) {
        const genuine = [...inferred]
          .filter((candidate) =>
            responseMatchesMembers(document, membersByModel, model, candidate),
          )
          .sort();
        if (genuine.length > 0)
          throw new Error(
            `Model '${model}' names DTO component '${explicit}' in payload schema '${payload.component}', but operations carrying its payload return ${genuine.join(", ")}. Fix the payload 'modelSchema' metadata or the operation responses.`,
          );
      }
      dtoByModel.set(model, explicit);
      continue;
    }
    if (inferred.size > 1)
      throw new Error(
        `Model '${model}' matches multiple response components (${[...inferred].sort().join(", ")}). Name the DTO explicitly with 'modelSchema' in the payload 'x-sparsetypes' metadata.`,
      );
    const [only] = inferred;
    if (!only) continue;
    assertResponseMatchesMembers(document, membersByModel, model, only);
    dtoByModel.set(model, only);
  }

  let changed = true;
  while (changed) {
    changed = false;
    for (const [model, entry] of membersByModel) {
      const parentSchemaName = dtoByModel.get(model);
      if (!parentSchemaName) continue;
      const parentSchema = document.components.schemas[parentSchemaName];
      for (const member of entry.metadata.members ?? []) {
        const child = member.childModel ?? member.itemModel;
        if (!child || dtoByModel.has(child)) continue;
        const property = parentSchema?.properties?.[member.name];
        const propertyShape = unwrapSchema(property);
        const childSchema =
          member.shape === "keyed"
            ? unwrapSchema(propertyShape?.items)
            : member.shape === "dictionary"
              ? unwrapSchema(propertyShape?.additionalProperties)
              : propertyShape;
        const childComponent = schemaRef(childSchema);
        if (childComponent) {
          dtoByModel.set(child, childComponent);
          changed = true;
        }
      }
    }
  }

  const missing = [...membersByModel.keys()].filter(
    (model) => !dtoByModel.has(model),
  );
  if (missing.length > 0) {
    throw new Error(
      `Could not associate SparseTypes models with ordinary OpenAPI schemas: ${missing.join(", ")}. Ensure payload models occur in a response schema, or name the DTO with 'modelSchema' in the payload 'x-sparsetypes' metadata.`,
    );
  }
  for (const [model, entry] of membersByModel) {
    const unsupported = (entry.metadata.members ?? []).find(
      (member: ChangeMember) => member.comparison === "custom",
    );
    if (unsupported)
      throw new Error(
        `Member '${model}.${unsupported.name}' uses custom scalar comparison, which JavaScript codegen cannot reproduce.`,
      );
  }

  const models = [...membersByModel].map(([model, members]) => {
    const payload = payloadByModel.get(model);
    if (!payload)
      throw new Error(
        `Model '${model}' has no generated ChangePayload schema.`,
      );
    const dto = dtoByModel.get(model);
    if (!dto)
      throw new Error(
        `Could not associate SparseTypes model '${model}' with an ordinary OpenAPI schema.`,
      );
    return {
      model,
      dto,
      membersComponent: members.component,
      members: (members.metadata.members ?? []).map((member: ChangeMember) => {
        const dtoSchema = document.components.schemas[dto];
        const property = unwrapSchema(dtoSchema?.properties?.[member.name]);
        const valueSchema =
          member.shape === "sequence"
            ? unwrapSchema(property?.items)
            : member.shape === "dictionary"
              ? unwrapSchema(property?.additionalProperties)
              : property;
        return {
          ...member,
          snapshotType: valueSchema?.type,
          snapshotFormat: valueSchema?.format,
          guidKeyProperties: keyedGuidProperties(document, dtoByModel, member),
        };
      }),
      payload: payload.component,
      schemaIdentity: payload.metadata.schemaIdentity,
    };
  });
  const duplicateNames = new Set(
    [
      ...models.reduce(
        (counts, model) =>
          counts.set(
            modelTypeName(model.model),
            (counts.get(modelTypeName(model.model)) ?? 0) + 1,
          ),
        new Map(),
      ),
    ]
      .filter(([, count]) => count > 1)
      .map(([name]) => name),
  );
  return models.map((model) => ({
    ...model,
    typeName: duplicateNames.has(modelTypeName(model.model))
      ? model.model
          .replace(/^global::/, "")
          .split(/[.+]/)
          .map((part) => part.replace(/`[0-9]+$/, ""))
          .join("_")
      : modelTypeName(model.model),
  }));
}

function explicitDtoComponent(
  document: OpenApiDocument,
  model: string,
  payload: { component: string; metadata: Record<string, any> },
): string | undefined {
  const explicit = payload.metadata.modelSchema ?? payload.metadata.dto;
  if (explicit === undefined) return undefined;
  if (
    typeof explicit !== "string" ||
    !document.components.schemas[explicit]
  )
    throw new Error(
      `Model '${model}' names unknown DTO component '${String(explicit)}' in payload schema '${payload.component}'. Declare 'modelSchema' with an existing '#/components/schemas/<name>'.`,
    );
  return explicit;
}

function inferredDtoComponents(
  document: OpenApiDocument,
  payloadByComponent: Map<string, { component: string; metadata: Record<string, any> }>,
  payload: { component: string; metadata: Record<string, any> },
): Set<string> {
  const inferred = new Set<string>();
  for (const pathItem of Object.values(document.paths) as Record<string, any>[]) {
    for (const operation of Object.values(pathItem ?? {}) as Record<string, any>[]) {
      // Resolve local requestBody/response refs before reading content so
      // referenced payloads and DTOs participate in inference (issue 391).
      const requestBody = dereferenceObject(document, operation?.requestBody);
      const request = (
        Object.values(requestBody?.content ?? {}) as Record<string, any>[]
      ).find((entry) => entry.schema);
      if (payloadByComponent.get(schemaRef(request?.schema) ?? "") !== payload)
        continue;
      for (const [status, rawResponse] of Object.entries(
        operation.responses ?? {},
      ) as [string, Record<string, any>][]) {
        if (!/^[23]/.test(status)) continue;
        const response = dereferenceObject(document, rawResponse) ?? rawResponse;
        for (const entry of Object.values(response?.content ?? {}) as Record<string, any>[]) {
          const component = schemaRef(entry?.schema);
          if (component) inferred.add(component);
        }
      }
    }
  }
  return inferred;
}

function assertResponseMatchesMembers(
  document: OpenApiDocument,
  membersByModel: Map<string, { component: string; metadata: Record<string, any> }>,
  model: string,
  component: string,
): void {
  const memberNames = memberNamesOf(membersByModel, model);
  if (!responseMatchesMembers(document, membersByModel, model, component))
    throw new Error(
      `Response schema '${component}' for model '${model}' shares no properties with the model's members (${memberNames.join(", ")}); it looks like a wrapper/result type. Name the DTO explicitly with 'modelSchema' in the payload 'x-sparsetypes' metadata.`,
    );
}

function memberNamesOf(
  membersByModel: Map<string, { component: string; metadata: Record<string, any> }>,
  model: string,
): string[] {
  return (
    (membersByModel.get(model)?.metadata.members ?? []) as ChangeMember[]
  ).map((member) => member.name);
}

function responseMatchesMembers(
  document: OpenApiDocument,
  membersByModel: Map<string, { component: string; metadata: Record<string, any> }>,
  model: string,
  component: string,
): boolean {
  const memberNames = memberNamesOf(membersByModel, model);
  if (memberNames.length === 0) return true;
  const defined = new Set<string>();
  collectDefinedProperties(document, component, new Set(), defined);
  return memberNames.some((name) => defined.has(name));
}

function collectDefinedProperties(
  document: OpenApiDocument,
  component: string,
  seen: Set<string>,
  defined: Set<string>,
): void {
  if (seen.has(component)) return;
  seen.add(component);
  const schema = document.components.schemas[component];
  for (const name of Object.keys(schema?.properties ?? {})) defined.add(name);
  for (const variant of [
    ...(schema?.allOf ?? []),
    ...(schema?.anyOf ?? []),
    ...(schema?.oneOf ?? []),
  ] as Record<string, any>[]) {
    if (!variant || typeof variant !== "object") continue;
    if (typeof variant.$ref === "string") {
      const name = refName(variant);
      if (name && document.components.schemas[name])
        collectDefinedProperties(document, name, seen, defined);
    } else {
      for (const name of Object.keys(variant.properties ?? {})) defined.add(name);
    }
  }
}

function modelTypeName(model: string): string {
  const name = model.split(/[.+]/).at(-1) ?? model;
  return name.replace(/`[0-9]+$/, "");
}

/// Names of key properties whose DTO schema is a UUID, used to normalize
/// GUID key components case-insensitively like C# Guid equality.
function keyedGuidProperties(
  document: OpenApiDocument,
  dtoByModel: Map<string, string>,
  member: ChangeMember,
): string[] {
  if (member.shape !== "keyed" || !member.itemModel) return [];
  const childDto = dtoByModel.get(member.childModel ?? member.itemModel);
  const schema = childDto ? document.components.schemas[childDto] : undefined;
  const names = Array.isArray(member.keyProperties) ? member.keyProperties : [];
  return names.filter((name: string) => {
    const property = unwrapSchema(schema?.properties?.[name]);
    return property?.format === "uuid";
  });
}


// Precision-sensitive members (decimal/int64/uint64 and 64-bit enums) travel
// in ChangePayload endpoints as strings, but an ordinary DTO may still carry
// them as JSON numbers. JSON.parse rounds those before parseT or the payload
// builders ever see them, so a numeric DTO representation is inherently lossy
// beyond 2^53 integers / high-scale decimals. With
// `requireLosslessPrecision`, generation fails for such members instead of
// silently producing a lossy client.
export function assertLosslessPrecision(models: ChangeModel[]): void {
  for (const model of models) {
    for (const member of model.members) {
      const wireType =
        member.shape === "sequence"
          ? member.elementWireType
          : member.shape === "dictionary"
            ? member.valueWireType
            : member.wireType;
      if (!isPrecisionStringType(wireType)) continue;
      if (member.childModel ?? member.itemModel) continue;
      const snapshotTypes = (
        Array.isArray(member.snapshotType)
          ? member.snapshotType
          : member.snapshotType
            ? [member.snapshotType]
            : []
      ) as string[];
      if (
        snapshotTypes.length > 0 &&
        !snapshotTypes.some((type) => type === "number" || type === "integer")
      )
        continue;
      throw new Error(
        `Model '${model.model}' member '${member.name}' uses precision-sensitive wire type '${wireType}' but the DTO carries it as ${snapshotTypes.length > 0 ? `JSON ${snapshotTypes.join("/")} (already rounded by JSON.parse beyond 2^53 / high-scale decimals)` : "a schema without a JSON type"}. Use a string-based DTO contract for this member, or drop the 'requireLosslessPrecision' option.`,
      );
    }
  }
}

function jsString(value: unknown): string {
  return JSON.stringify(value);
}

export function emitChangesJs(models: ChangeModel[]): string {
  const definitions = Object.fromEntries(
    models.map((model) => [
      model.model,
      {
        payload: model.payload,
        schema: model.schemaIdentity,
        members: model.members,
      },
    ]),
  );
  return `const models = ${JSON.stringify(definitions, null, 2)};\n\n${CHANGE_RUNTIME}\n\n${models
    .map((model) => {
      const functionName = `build${model.typeName}ChangePayload`;
      return `export function ${functionName}(before, after) { return buildPayload(${jsString(model.model)}, before, after); }\n`;
    })
    .join("")}`;
}

export function emitChangesDts(models: ChangeModel[]): string {
  const functions = models
    .map((model) => {
      const name = `build${model.typeName}ChangePayload`;
      const alias = `Optional${model.typeName}`;
      return `export declare function ${name}(before: DeepReadonly<${alias}> | null | undefined, after: DeepReadonly<${alias}> | null | undefined): components["schemas"][${jsString(model.payload)}] | undefined;`;
    })
    .join("\n");
  const imports = models.map((model) => `Optional${model.typeName}`).join(", ");
  return `import type { components } from "./api.js";\nimport type { DeepReadonly, ${imports} } from "./models.js";\n\n${functions}\n`;
}

export function emitModelsDts(
  document: OpenApiDocument,
  models: ChangeModel[],
): string {
  const modelByIdentity = new Map(models.map((model) => [model.model, model]));
  // Keyed-element editable types keep stable identity members statically
  // required. OptionalT stays fully deep-optional; parents of keyed
  // collections reference the TKeyedItem union instead.
  const keyedItemOf = new Map<string, string>();
  const keyedItems = models.flatMap((model) => {
    const usages = model.members.filter(
      (member) => member.shape === "keyed" && member.itemModel,
    );
    return usages.flatMap((member) => {
      const child = modelByIdentity.get(member.itemModel);
      if (!child || keyedItemOf.has(child.model)) return [];
      const keys = ((member.keyProperties ?? []) as unknown[]).filter(
        (key): key is string => typeof key === "string",
      );
      const temporaryKey =
        typeof member.temporaryKey === "string" ? member.temporaryKey : undefined;
      const unassignedKey = Object.prototype.hasOwnProperty.call(member, "unassignedKey")
        ? member.unassignedKey
        : undefined;
      keyedItemOf.set(child.model, `${child.typeName}KeyedItem`);
      if (keys.length === 0) return [];
      const required = `Required<Pick<${child.typeName}, ${keys.map((key) => JSON.stringify(key)).join(" | ")}>>`;
      // A second keying of the same item with different identity rules, or a
      // composite key, falls back to required keys only rather than guessing
      // an unassigned-key union.
      const singleKeying = !usages.some(
        (other) =>
          other !== member &&
          other.itemModel === member.itemModel &&
          (JSON.stringify(other.keyProperties ?? []) !==
            JSON.stringify(member.keyProperties ?? []) ||
            other.temporaryKey !== member.temporaryKey),
      );
      if (
        temporaryKey === undefined ||
        unassignedKey === undefined ||
        keys.length !== 1 ||
        !singleKeying
      )
        return [
          `export type ${child.typeName}KeyedItem = Optional${child.typeName} & ${required};`,
        ];
      return [
        `export type ${child.typeName}AssignedItem = Optional${child.typeName} & ${required};`,
        `export type ${child.typeName}UnassignedItem = Optional${child.typeName} & { ${JSON.stringify(keys[0])}: ${JSON.stringify(unassignedKey)}; ${JSON.stringify(temporaryKey)}: string };`,
        `export type ${child.typeName}KeyedItem = ${child.typeName}AssignedItem | ${child.typeName}UnassignedItem;`,
      ];
    });
  });
  const aliases = models
    .map(
      (model) =>
        `export type ${model.typeName} = components["schemas"][${jsString(model.dto)}];`,
    )
    .join("\n");
  const states = models
    .map((model) => {
      const rewritten = model.members.filter(
        (member) =>
          member.shape === "nested" ||
          (member.shape === "keyed" && member.itemModel) ||
          (member.shape === "dictionary" && member.itemModel) ||
          (member.shape === "sequence" &&
            member.elementWireType &&
            isPrecisionStringType(member.elementWireType)) ||
          (member.shape === "dictionary" &&
            member.valueWireType &&
            isPrecisionStringType(member.valueWireType)) ||
          (member.shape === "value" &&
            member.wireType &&
            isPrecisionStringType(member.wireType)),
      );
      if (rewritten.length === 0)
        return `export type Optional${model.typeName} = DeepOptional<${model.typeName}>;`;
      const omitted = rewritten
        .map((member) => JSON.stringify(member.name))
        .join(" | ");
      const fields = rewritten
        .map((member) => {
          const property = `StateProperty<${model.typeName}, ${JSON.stringify(member.name)}>`;
          const child = modelByIdentity.get(
            member.childModel ?? member.itemModel,
          );
          let value;
          if (member.shape === "nested" && child) {
            value = `Optional${child.typeName}${member.nullable ? " | null" : ""}`;
          } else if (member.shape === "keyed" && child) {
            value = `readonly (${keyedItemOf.get(child.model) ?? `Optional${child.typeName}`}${member.elementNullable ? " | null" : ""})[]`;
          } else if (member.shape === "dictionary" && child) {
            value = `Record<string, Optional${child.typeName}${member.valueNullable ? " | null" : ""}>`;
          } else if (member.shape === "sequence") {
            value = `StringElements<DeepOptional<${property}>>`;
          } else if (
            member.shape === "dictionary" &&
            member.valueWireType &&
            isPrecisionStringType(member.valueWireType)
          ) {
            value = `StringRecordValues<DeepOptional<${property}>>`;
          } else if (
            member.wireType &&
            isPrecisionStringType(member.wireType)
          ) {
            value = `DeepOptional<${property}> | string`;
          } else {
            value = `DeepOptional<${property}>`;
          }
          if (
            member.shape !== "nested" &&
            member.shape !== "keyed" &&
            member.shape !== "dictionary" &&
            member.nullable
          )
            value += " | null";
          if (
            (member.shape === "keyed" ||
              member.shape === "sequence" ||
              member.shape === "dictionary") &&
            member.nullable
          )
            value += " | null";
          return `  ${JSON.stringify(member.name)}?: ${value} | undefined;`;
        })
        .join("\n");
      return `export type Optional${model.typeName} = DeepOptional<Omit<${model.typeName}, ${omitted}>> & {\n${fields}\n};`;
    })
    .join("\n");
  return `import type { components } from "./api.js";\n\nexport type DeepReadonly<T> = T extends (...args: never[]) => unknown ? T : T extends readonly (infer U)[] ? readonly DeepReadonly<U>[] : T extends object ? { readonly [K in keyof T]: DeepReadonly<T[K]> } : T;\nexport type DeepOptional<T> = T extends readonly (infer U)[] ? readonly DeepOptional<U>[] : T extends object ? { [K in keyof T]?: DeepOptional<T[K]> | undefined } : T;\ntype StateProperty<T, K extends keyof T> = Exclude<T[K], undefined>;\ntype StringElements<T> = T extends readonly (infer U)[] ? readonly (U | string)[] : T;\n  type StringRecordValues<T> = T extends Record<string, infer U> ? { [K in keyof T]: U | string } : T;\n\n${aliases}\n\n${states}${keyedBlock}\n\n${models}.map((model) => `export declare function parse${model.typeName}(value: unknown): ${model.typeName};`).join("\n")}\n`;}

export function emitModelsJs(
  document: OpenApiDocument,
  models: ChangeModel[],
): string {
  return `const schemas = ${JSON.stringify(document.components.schemas)};\n\n${MODEL_RUNTIME}\n\n${models
    .map(
      (model) =>
        `export function parse${model.typeName}(value) { validateSchema(${jsString(model.dto)}, value, "$", new WeakSet()); return value; }\n`,
    )
    .join("")}`;
}

function isPrecisionStringType(type: string): boolean {
  return ["decimal-string", "int64-string", "uint64-string"].includes(type);
}

const MODEL_RUNTIME = String.raw`
function fail(path, reason) { throw new TypeError(path + ": " + reason); }
function referenceName(ref) {
  const prefix = "#/components/schemas/";
  return typeof ref === "string" && ref.startsWith(prefix)
    ? decodeURIComponent(ref.slice(prefix.length)).replace(/~1/g, "/").replace(/~0/g, "~")
    : undefined;
}
function matches(schema, value, path) {
  try { validate(schema, value, path, new WeakSet()); return true; }
  catch (error) { if (error instanceof TypeError) return false; throw error; }
}
function validateSchema(name, value, path, active) {
  const schema = schemas[name];
  if (!schema) fail(path, "missing OpenAPI schema '" + name + "'");
  validate(schema, value, path, active);
}
function validate(schema, value, path, active) {
  if (!schema || typeof schema !== "object") return;
  if (schema.$ref) {
    const name = referenceName(schema.$ref);
    if (!name) fail(path, "external schema references are not supported");
    validateSchema(name, value, path, active);
  }
  if (value === null && (schema.nullable === true || (Array.isArray(schema.type) && schema.type.includes("null")))) return;
  if (schema.const !== undefined && value !== schema.const) fail(path, "does not match its constant");
  if (Array.isArray(schema.enum) && !schema.enum.some((item) => Object.is(item, value))) fail(path, "is not an allowed enum value");
  if (schema.not && typeof schema.not === "object" && matches(schema.not, value, path)) fail(path, "matches a forbidden schema");
  if (Array.isArray(schema.allOf)) for (const child of schema.allOf) validate(child, value, path, active);
  if (Array.isArray(schema.anyOf) && !schema.anyOf.some((child) => matches(child, value, path))) fail(path, "does not match any allowed schema");
  if (Array.isArray(schema.oneOf)) {
    const matchesCount = schema.oneOf.filter((child) => matches(child, value, path)).length;
    if (matchesCount !== 1) fail(path, "must match exactly one schema");
  }
  const rawTypes = Array.isArray(schema.type) ? schema.type : schema.type ? [schema.type] : [];
  const types = rawTypes.filter((type) => type !== "null");
  if (types.length) {
    if (!types.some((type) => matchesType(type, value))) fail(path, "has an invalid JSON type");
  } else if (rawTypes.length && value !== null) {
    // A null-only schema (type "null" or ["null"]) accepts null and nothing else.
    // Without this branch the filtered list is empty and non-null values skip validation.
    fail(path, "has an invalid JSON type");
  }
  if (typeof value === "string") {
    if (schema.minLength !== undefined && value.length < schema.minLength) fail(path, "is shorter than minLength");
    if (schema.maxLength !== undefined && value.length > schema.maxLength) fail(path, "is longer than maxLength");
    if (schema.pattern && !new RegExp(schema.pattern).test(value)) fail(path, "does not match its pattern");
    if (schema.format === "uuid" && !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)) fail(path, "is not a UUID");
    if (schema.format === "email" && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)) fail(path, "is not an email address");
    if (schema.format === "date" && !isIsoDate(value)) fail(path, "is not a date");
    if (schema.format === "date-time" && !isIsoDateTime(value)) fail(path, "is not a date-time");
  }
  if (typeof value === "number") {
    if (!Number.isFinite(value)) fail(path, "must be finite");
    if (schema.type === "integer" && !Number.isInteger(value)) fail(path, "must be an integer");
    if (schema.minimum !== undefined && value < schema.minimum) fail(path, "is below minimum");
    if (schema.maximum !== undefined && value > schema.maximum) fail(path, "is above maximum");
    if (typeof schema.exclusiveMinimum === "number" && value <= schema.exclusiveMinimum) fail(path, "is below the exclusive minimum");
    else if (schema.exclusiveMinimum === true && schema.minimum !== undefined && value <= schema.minimum) fail(path, "is below the exclusive minimum");
    if (typeof schema.exclusiveMaximum === "number" && value >= schema.exclusiveMaximum) fail(path, "is above the exclusive maximum");
    else if (schema.exclusiveMaximum === true && schema.maximum !== undefined && value >= schema.maximum) fail(path, "is above the exclusive maximum");
    if (schema.multipleOf !== undefined && !isMultipleOf(value, schema.multipleOf)) fail(path, "is not a multiple of the expected divisor");
  }
  if (Array.isArray(value)) {
    enter(value, active, path);
    try {
      if (schema.minItems !== undefined && value.length < schema.minItems) fail(path, "has fewer than minItems");
      if (schema.maxItems !== undefined && value.length > schema.maxItems) fail(path, "has more than maxItems");
      if (schema.uniqueItems === true && !hasUniqueItems(value)) fail(path, "has duplicate items");
      if (schema.items) value.forEach((item, index) => validate(schema.items, item, path + "[" + index + "]", active));
    } finally { active.delete(value); }
  } else if (value !== null && typeof value === "object") {
    enter(value, active, path);
    try {
      const properties = schema.properties ?? {};
      const propertyNames = Object.keys(value);
      if (schema.minProperties !== undefined && propertyNames.length < schema.minProperties) fail(path, "has fewer than minProperties");
      if (schema.maxProperties !== undefined && propertyNames.length > schema.maxProperties) fail(path, "has more than maxProperties");
      for (const name of schema.required ?? []) if (!Object.prototype.hasOwnProperty.call(value, name)) fail(path + "." + name, "is required");
      for (const [name, child] of Object.entries(properties)) {
        if (Object.prototype.hasOwnProperty.call(value, name)) validate(child, value[name], path + "." + name, active);
      }
      for (const [name, item] of Object.entries(value)) {
        if (Object.prototype.hasOwnProperty.call(properties, name)) continue;
        if (schema.additionalProperties === false) fail(path + "." + name, "is not allowed");
        if (schema.additionalProperties && typeof schema.additionalProperties === "object") {
          validate(schema.additionalProperties, item, path + "." + name, active);
        }
      }
    } finally { active.delete(value); }
  }
}
function isIsoDate(value) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const time = Date.parse(value + "T00:00:00Z");
  if (Number.isNaN(time)) return false;
  const date = new Date(time);
  return date.getUTCFullYear() === Number(value.slice(0, 4)) && date.getUTCMonth() + 1 === Number(value.slice(5, 7)) && date.getUTCDate() === Number(value.slice(8, 10));
}
function isIsoDateTime(value) {
  if (!/^\d{4}-\d{2}-\d{2}[Tt]\d{2}:\d{2}:\d{2}(\.\d+)?([Zz]|[+-]\d{2}:?\d{2})?$/.test(value)) return false;
  return !Number.isNaN(Date.parse(value));
}
function isMultipleOf(value, divisor) {
  if (!(divisor > 0)) return false;
  const quotient = value / divisor;
  const nearest = Math.round(quotient);
  return Math.abs(quotient - nearest) <= 1e-9 * Math.max(1, Math.abs(quotient));
}
function deepEqualValue(left, right) {
  if (Object.is(left, right)) return true;
  if (Array.isArray(left) || Array.isArray(right)) {
    return Array.isArray(left) && Array.isArray(right) && left.length === right.length
      && left.every((item, index) => deepEqualValue(item, right[index]));
  }
  if (left !== null && right !== null && typeof left === "object" && typeof right === "object") {
    const leftKeys = Object.keys(left);
    const rightKeys = Object.keys(right);
    return leftKeys.length === rightKeys.length
      && leftKeys.every((key) => Object.prototype.hasOwnProperty.call(right, key) && deepEqualValue(left[key], right[key]));
  }
  return false;
}
function hasUniqueItems(value) {
  for (let index = 0; index < value.length; index++) {
    for (let other = index + 1; other < value.length; other++) {
      if (deepEqualValue(value[index], value[other])) return false;
    }
  }
  return true;
}
function enter(value, active, path) {
  if (active.has(value)) fail(path, "contains a cyclic object");
  active.add(value);
}
function matchesType(type, value) {
  if (type === "array") return Array.isArray(value);
  if (type === "object") return value !== null && typeof value === "object" && !Array.isArray(value);
  if (type === "integer") return typeof value === "number" && Number.isInteger(value);
  if (type === "number") return typeof value === "number" && Number.isFinite(value);
  if (type === "null") return value === null;
  return typeof value === type;
}
`;

const CHANGE_RUNTIME = String.raw`
function own(object, key) { return object !== null && typeof object === "object" && Object.prototype.hasOwnProperty.call(object, key); }
function state(object, key, wireType, elementWireType) {
  if (!own(object, key) || object[key] === undefined) return { state: "missing" };
  if (object[key] === null) return { state: "null" };
  const value = object[key];
  const encoded = Array.isArray(value) && elementWireType ? value.map((item) => encodeWireValue(item, elementWireType)) : encodeWireValue(value, wireType);
  return { state: "value", value: clone(encoded) };
}
function endpoint(value, redact = false, wireType, elementWireType) {
  if (redact) return { state: "redacted" };
  if (value === undefined) return { state: "missing" };
  if (value === null) return { state: "null" };
  const encoded = Array.isArray(value) && elementWireType ? value.map((item) => encodeWireValue(item, elementWireType)) : encodeWireValue(value, wireType);
  return { state: "value", value: clone(encoded) };
}
function encodeWireValue(value, wireType) {
  if (value === null || value === undefined || !["decimal-string", "int64-string", "uint64-string"].includes(wireType)) return value;
  if (Array.isArray(value)) return value.map((item) => encodeWireValue(item, wireType));
  if (typeof value === "object") {
    const result = {};
    for (const key of Object.keys(value)) {
      Object.defineProperty(result, key, {
        value: encodeWireValue(value[key], wireType), enumerable: true, writable: true, configurable: true,
      });
    }
    return result;
  }
  if (typeof value === "number") {
    if (!Number.isFinite(value)) {
      throw new TypeError("Precision-sensitive SparseTypes values must be finite numbers or canonical strings.");
    }
    if (wireType === "decimal-string") return canonicalDecimal(String(value));
    if (!Number.isSafeInteger(value)) {
      throw new TypeError("Precision-sensitive SparseTypes values must be supplied as strings.");
    }
    return canonicalInteger(String(value), wireType);
  }
  if (typeof value !== "string") throw new TypeError("Precision-sensitive SparseTypes values must be numbers or strings.");
  if (wireType === "decimal-string") return canonicalDecimal(value);
  return canonicalInteger(value, wireType);
}
function canonicalDecimal(text) {
  const match = /^(-)?(0|[1-9][0-9]*)(\.([0-9]+))?$/.exec(text);
  if (!match) {
    throw new TypeError("A decimal value '" + text + "' is not canonical: use [-]digits[.fraction] without exponent, plus sign, whitespace, or leading zeros.");
  }
  const fraction = match[4] ?? "";
  if (fraction.length > 28) throw new TypeError("A decimal value exceeds the maximum scale of 28.");
  const significant = (match[2] + fraction).replace(/^0+/, "") || "0";
  if (significant.length > 29) throw new TypeError("A decimal value exceeds 29 significant digits.");
  if (BigInt(significant) > (1n << 96n) - 1n) throw new TypeError("A decimal value is out of range.");
  if (significant === "0") return fraction.length ? "0." + fraction : "0";
  return (match[1] ?? "") + match[2] + (fraction.length ? "." + fraction : "");
}
function canonicalInteger(text, wireType) {
  const pattern = wireType === "uint64-string" ? /^(0|[1-9][0-9]*)$/ : /^(-)?(0|[1-9][0-9]*)$/;
  if (!pattern.test(text)) {
    throw new TypeError("A precision-sensitive integer value '" + text + "' is not canonical: use [-]digits without plus sign, whitespace, exponent, or leading zeros.");
  }
  const normalized = text === "-0" ? "0" : text;
  const integer = BigInt(normalized);
  const minimum = wireType === "int64-string" ? -(1n << 63n) : 0n;
  const maximum = wireType === "int64-string" ? (1n << 63n) - 1n : (1n << 64n) - 1n;
  if (integer < minimum || integer > maximum) throw new TypeError("A precision-sensitive integer value is out of range.");
  return normalized;
}
function normalizeDecimal(value) {
  const match = /^([+-]?)(\d*)(?:\.(\d*))?(?:[eE]([+-]?\d+))?$/.exec(String(value));
  if (!match || (!match[2] && !match[3])) throw new TypeError("A decimal value is invalid.");
  const exponent = Number(match[4] ?? 0);
  if (!Number.isSafeInteger(exponent) || Math.abs(exponent) > 1000) throw new TypeError("A decimal exponent is out of range.");
  const fraction = match[3] ?? "";
  let digits = (match[2] + fraction).replace(/^0+/, "") || "0";
  let scale = fraction.length - exponent;
  while (digits.length > 1 && digits.endsWith("0") && scale > 0) { digits = digits.slice(0, -1); scale--; }
  if (scale < 0) { digits += "0".repeat(-scale); scale = 0; }
  const sign = match[1] === "-" && digits !== "0" ? "-" : "";
  return sign + digits + ":" + scale;
}
function clone(value, seen = new WeakSet()) {
  if (value === null || typeof value === "string" || typeof value === "boolean") return value;
  if (typeof value === "number") { if (!Number.isFinite(value)) throw new TypeError("ChangePayload values must contain finite JSON numbers."); return value; }
  if (typeof value !== "object") throw new TypeError("ChangePayload snapshots must contain JSON values.");
  if (seen.has(value)) throw new TypeError("ChangePayload snapshots cannot contain cyclic values.");
  seen.add(value);
  let result;
  if (Array.isArray(value)) result = value.map((item) => {
    if (item === undefined) throw new TypeError("ChangePayload arrays cannot contain undefined values.");
    return clone(item, seen);
  });
  else {
    const prototype = Object.getPrototypeOf(value);
    if (prototype !== Object.prototype && prototype !== null) throw new TypeError("ChangePayload snapshots must contain plain JSON objects.");
    result = {};
    for (const key of Object.keys(value)) if (value[key] !== undefined) Object.defineProperty(result, key, { value: clone(value[key], seen), enumerable: true, writable: true, configurable: true });
  }
  seen.delete(value);
  return result;
}
function equal(left, right) {
  if (Object.is(left, right)) return true;
  if (left === null || right === null || typeof left !== "object" || typeof right !== "object") return false;
  if (Array.isArray(left) !== Array.isArray(right)) return false;
  const leftKeys = Object.keys(left).sort();
  const rightKeys = Object.keys(right).sort();
  return leftKeys.length === rightKeys.length && leftKeys.every((key, index) => key === rightKeys[index] && equal(left[key], right[key]));
}
function sameEndpoint(before, after, wireType) {
  if (before.state !== after.state) return false;
  if (before.state !== "value") return true;
  return equalWireValue(before.value, after.value, wireType);
}
function equalWireValue(before, after, wireType) {
  if (wireType === "guid-string" && typeof before === "string" && typeof after === "string") {
    // C# Guid equality is case-independent; compare canonically so a
    // case-only spelling change is not a change. Other values stay strict.
    if (GUID_PATTERN.test(before) && GUID_PATTERN.test(after)) {
      return before.toLowerCase() === after.toLowerCase();
    }
  }
  // Precision checks stay below the Array/object/null handling so null
  // elements and nested values compare without throwing (issues 380/386).
  if (Array.isArray(before) || Array.isArray(after)) {
    return Array.isArray(before) && Array.isArray(after) && before.length === after.length
      && before.every((value, index) => equalWireValue(value, after[index], wireType));
  }
  if (before && after && typeof before === "object" && typeof after === "object") {
    const beforeKeys = Object.keys(before).sort();
    const afterKeys = Object.keys(after).sort();
    return beforeKeys.length === afterKeys.length && beforeKeys.every((key, index) =>
      key === afterKeys[index] && equalWireValue(before[key], after[key], wireType));
  }
  if (before === null || after === null || before === undefined || after === undefined) return Object.is(before, after);
  if (wireType === "decimal-string") return normalizeDecimal(before) === normalizeDecimal(after);
  if (wireType === "int64-string" || wireType === "uint64-string") return BigInt(before) === BigInt(after);
  return equal(before, after);
}
function hasRedactedMember(modelName) {
  return models[modelName].members.some((member) => member.redactBefore);
}
function snapshot(modelName, value) {
  if (value === null || value === undefined) return value;
  const result = {};
  for (const member of models[modelName].members) {
    const present = own(value, member.name) && value[member.name] !== undefined;
    const item = present ? value[member.name] : undefined;
    const output = member.shape === "nested" && present && item !== null ? snapshot(member.childModel, item) : item;
    Object.defineProperty(result, member.name, { value: endpoint(output, false, member.wireType, member.elementWireType), enumerable: true, writable: true, configurable: true });
  }
  return result;
}
function snapshotDto(modelName, value) {
  const result = clone(value);
  for (const member of models[modelName].members) {
    if (!own(result, member.name) || result[member.name] === undefined || result[member.name] === null) continue;
    const current = result[member.name];
    if (member.shape === "nested" && member.childModel) {
      result[member.name] = snapshotDto(member.childModel, current);
    } else if (member.shape === "keyed" && member.itemModel && Array.isArray(current)) {
      result[member.name] = current.map((item) => item === null ? null : snapshotDto(member.itemModel, item));
    } else if (member.shape === "dictionary" && member.itemModel && current && typeof current === "object" && !Array.isArray(current)) {
      const entries = {};
      for (const key of Object.keys(current)) {
        Object.defineProperty(entries, key, {
          value: current[key] === null ? null : snapshotDto(member.itemModel, current[key]),
          enumerable: true, writable: true, configurable: true,
        });
      }
      result[member.name] = entries;
    } else if (member.shape === "value") {
      result[member.name] = snapshotScalar(current, member.wireType, member.snapshotType);
    } else if (member.shape === "sequence" && Array.isArray(current) && isNumericSnapshotType(member.snapshotType)) {
      result[member.name] = current.map((item) => snapshotScalar(item, member.elementWireType, member.snapshotType));
    } else if (member.shape === "dictionary" && current && typeof current === "object" && !Array.isArray(current) && isNumericSnapshotType(member.snapshotType)) {
      const entries = {};
      for (const key of Object.keys(current)) {
        Object.defineProperty(entries, key, {
          value: snapshotScalar(current[key], member.valueWireType, member.snapshotType),
          enumerable: true, writable: true, configurable: true,
        });
      }
      result[member.name] = entries;
    }
  }
  return result;
}
function isNumericSnapshotType(snapshotType) {
  if (snapshotType === "number" || snapshotType === "integer") return true;
  if (Array.isArray(snapshotType)) return snapshotType.some((type) => type === "number" || type === "integer");
  return false;
}
function snapshotScalar(value, wireType, snapshotType) {
  if (!isNumericSnapshotType(snapshotType) || value === null || value === undefined) return value;
  if (wireType === "decimal-string" && typeof value === "string") {
    const number = Number(value);
    if (!Number.isFinite(number) || normalizeDecimal(String(number)) !== normalizeDecimal(value)) {
      throw new TypeError("A keyed DTO snapshot cannot preserve this decimal as an OpenAPI number.");
    }
    return number;
  }
  if ((wireType === "int64-string" || wireType === "uint64-string") && typeof value === "string") {
    let integer;
    try {
      integer = BigInt(value);
    } catch {
      throw new TypeError("A keyed DTO snapshot cannot preserve this integer as an OpenAPI number.");
    }
    const number = Number(value);
    if (!Number.isSafeInteger(number) || BigInt(String(number)) !== integer) {
      throw new TypeError("A keyed DTO snapshot cannot preserve this integer as an OpenAPI number.");
    }
    return number;
  }
  return value;
}
function identity(model, item, member, index) {
  if (item === null || typeof item !== "object") throw new TypeError("Keyed collection elements must be objects.");
  if (!Array.isArray(member.keyProperties) || member.keyProperties.length === 0) {
    throw new TypeError("A keyed collection member needs key properties.");
  }
  const names = member.keyProperties;
  const keyParts = names.map((name) => {
    if (!own(item, name) || item[name] === undefined) throw new TypeError("A keyed collection item is missing key property '" + name + "'.");
    return item[name];
  });
  const key = keyParts.length === 1 ? canonicalKeyPart(keyParts[0], isGuidKey(member, names[0])) : keyParts.map((part, position) => canonicalKeyPart(part, isGuidKey(member, names[position])));
  const unassigned = Object.prototype.hasOwnProperty.call(member, "unassignedKey") && equalKey(key, member.unassignedKey, member);
  if (unassigned && member.temporaryKey) {
    const temporary = item[member.temporaryKey];
    if (typeof temporary !== "string" || !GUID_PATTERN.test(temporary) ||
        EMPTY_GUID_PATTERN.test(temporary)) {
      throw new TypeError("An unassigned keyed item needs a valid, non-empty temporary key.");
    }
    const canonicalTemporary = temporary.toLowerCase();
    return { key, id: "temporary:" + canonicalTemporary, temporary: canonicalTemporary, unassigned };
  }
  return { key, id: unassigned ? "unassigned:" + index : "key:" + JSON.stringify(key), temporary: undefined, unassigned };
}
const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const EMPTY_GUID_PATTERN = /^0{8}-0{4}-0{4}-0{4}-0{12}$/i;
function isGuidKey(member, name) {
  if (member.keyType === "guid-string" && (member.keyProperties ?? []).length <= 1) return true;
  return (member.guidKeyProperties ?? []).includes(name);
}
function canonicalKeyPart(value, isGuid) {
  if (!isGuid || value === null || value === undefined) return value;
  if (typeof value !== "string" || !GUID_PATTERN.test(value)) {
    throw new TypeError("A GUID key component is not a valid UUID: " + JSON.stringify(value) + ".");
  }
  return value.toLowerCase();
}
function canonicalKeyValue(value, isGuid) {
  return isGuid && typeof value === "string" && GUID_PATTERN.test(value) ? value.toLowerCase() : value;
}
function canonicalKey(key, member) {
  const names = Array.isArray(member.keyProperties) ? member.keyProperties : [];
  if (Array.isArray(key)) return key.map((part, position) => canonicalKeyValue(part, isGuidKey(member, names[position])));
  return canonicalKeyValue(key, isGuidKey(member, names[0]));
}
function equalKey(left, right, member) {
  return equal(canonicalKey(left, member), canonicalKey(right, member));
}
function validateUnique(entries) {
  const seen = new Set();
  for (const entry of entries) { if (seen.has(entry.id)) throw new TypeError("Duplicate keyed collection identity: " + entry.id); seen.add(entry.id); }
}
function orderedRanks(before, after) {
  const afterRank = new Map();
  let rank = 0;
  for (const item of after) if (before.some((candidate) => candidate.id === item.id)) afterRank.set(item.id, rank++);
  const beforeRank = new Map();
  rank = 0;
  for (const item of before) if (afterRank.has(item.id)) beforeRank.set(item.id, rank++);
  return new Set([...beforeRank].filter(([id, value]) => afterRank.get(id) !== value).map(([id]) => id));
}
function diffKeyed(model, member, before, after, redact) {
  const secret = member.redactBefore || redact;
  const b = state(before, member.name, member.wireType); const a = state(after, member.name, member.wireType);
  if ((b.state === "value" && !Array.isArray(b.value)) || (a.state === "value" && !Array.isArray(a.value))) {
    throw new TypeError("Keyed collection values must be arrays, null, or missing.");
  }
  if (b.state !== "value" || a.state !== "value") {
    if (sameEndpoint(b, a, member.wireType)) return undefined;
    if (secret && b.state !== "missing" && a.state === "missing") throw new TypeError("A redacted '" + member.name + "' change requires an observable after-state; blind whole-collection removal has no patch projection.");
    return { kind: "replace", before: secret && b.state !== "missing" ? { state: "redacted" } : b, after: a };
  }
  const beforeEntries = b.value.map((item, index) => ({ item, index, ...identity(model, item, member, index) }));
  const afterEntries = a.value.map((item, index) => ({ item, index, ...identity(model, item, member, index) }));
  if (beforeEntries.some((entry) => entry.unassigned && !member.temporaryKey)) throw new TypeError("An unassigned key cannot appear in the baseline without a temporary key.");
  validateUnique(beforeEntries); validateUnique(afterEntries);
  const beforeById = new Map(beforeEntries.map((entry) => [entry.id, entry]));
  const afterById = new Map(afterEntries.map((entry) => [entry.id, entry]));
  const reordered = orderedRanks(beforeEntries, afterEntries);
  const items = [];
  for (const entry of afterEntries) {
    const previous = beforeById.get(entry.id);
    if (!previous) {
      items.push({ key: entry.key, kind: "add", afterIndex: entry.index, ...(entry.temporary ? { temporaryKey: entry.temporary } : {}), after: endpoint(snapshotDto(member.itemModel, entry.item)) });
      continue;
    }
    const members = diffMembers(member.itemModel, previous.item, entry.item, secret);
    const isReordered = reordered.has(entry.id);
    if (members && Object.keys(members).length || isReordered) {
      items.push({ key: entry.key, kind: "update", beforeIndex: previous.index, afterIndex: entry.index, ...(entry.temporary ? { temporaryKey: entry.temporary } : {}), ...(isReordered ? { isReordered: true } : {}), ...(members && Object.keys(members).length ? { members } : {}) });
    }
  }
  for (const entry of beforeEntries) {
    if (!afterById.has(entry.id)) items.push({ key: entry.key, kind: "remove", beforeIndex: entry.index, ...(entry.temporary ? { temporaryKey: entry.temporary } : {}), before: endpoint(snapshotDto(member.itemModel, entry.item), secret) });
  }
  if (!items.length) return undefined;
  return {
    kind: "update",
    items,
    beforeOrder: beforeEntries.map((entry) => entry.key),
    afterOrder: afterEntries.map((entry) => entry.key),
    ...(member.temporaryKey ? {
      tempBeforeOrder: beforeEntries.flatMap((entry) => entry.temporary ? [entry.temporary] : []),
      tempAfterOrder: afterEntries.flatMap((entry) => entry.temporary ? [entry.temporary] : []),
    } : {}),
  };
}
function isNestedObject(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}
function dictionaryItemEndpoint(modelName, item) {
  if (item === undefined) return { state: "missing" };
  if (item === null) return { state: "null" };
  if (!isNestedObject(item)) throw new TypeError("Dictionary model values must be objects, null, or missing.");
  return endpoint(snapshotDto(modelName, item), false);
}
function diffDictionary(model, member, before, after, redact) {
  const secret = member.redactBefore || redact;
  const b = state(before, member.name, member.valueWireType); const a = state(after, member.name, member.valueWireType);
  if ((b.state === "value" && (!b.value || typeof b.value !== "object" || Array.isArray(b.value))) ||
      (a.state === "value" && (!a.value || typeof a.value !== "object" || Array.isArray(a.value)))) {
    throw new TypeError("Dictionary values must be objects, null, or missing.");
  }
  if (b.state !== "value" || a.state !== "value") {
    if (sameEndpoint(b, a, member.valueWireType)) return undefined;
    if (secret && b.state !== "missing" && a.state === "missing") throw new TypeError("A redacted '" + member.name + "' change requires an observable after-state; blind whole-collection removal has no patch projection.");
    return { kind: "replace", before: secret && b.state !== "missing" ? { state: "redacted" } : b, after: a };
  }
  const beforeKeys = Object.keys(b.value).filter((key) => b.value[key] !== undefined);
  const afterKeys = Object.keys(a.value).filter((key) => a.value[key] !== undefined);
  const items = [];
  for (const key of afterKeys) {
    if (!own(b.value, key)) {
      const value = member.itemModel ? snapshotDto(member.itemModel, a.value[key]) : a.value[key];
      items.push({ key, kind: "add", after: endpoint(value, false, member.itemModel ? undefined : member.valueWireType) });
      continue;
    }
    const previous = b.value[key]; const current = a.value[key];
    if (member.itemModel) {
      if (isNestedObject(previous) && isNestedObject(current)) {
        const nested = diffMembers(member.itemModel, previous, current, secret);
        if (nested && Object.keys(nested).length) items.push({ key, kind: "update", members: nested });
      } else {
        // Whole-value presence changes (null/missing <-> model) are a
        // replace: diffing a missing model as a nested update would emit
        // member changes the server cannot apply.
        const itemBefore = dictionaryItemEndpoint(member.itemModel, previous);
        const itemAfter = dictionaryItemEndpoint(member.itemModel, current);
        if (!sameEndpoint(itemBefore, itemAfter)) items.push({ key, kind: "replace", before: secret ? { state: "redacted" } : itemBefore, after: itemAfter });
      }
    } else if (!sameEndpoint(endpoint(previous, false, member.valueWireType), endpoint(current, false, member.valueWireType), member.valueWireType)) {
      items.push({ key, kind: "replace", before: endpoint(previous, secret, member.valueWireType), after: endpoint(current, false, member.valueWireType) });
    }
  }
  for (const key of beforeKeys) {
    if (!own(a.value, key)) {
      const previous = b.value[key];
      const before = member.itemModel ? snapshotDto(member.itemModel, previous) : previous;
      items.push({
        key,
        kind: "remove",
        before: endpoint(before, secret, member.itemModel ? undefined : member.valueWireType),
      });
    }
  }
  if (!items.length) return undefined;
  return { kind: "update", items };
}
function diffMembers(modelName, before, after, redact) {
  const model = models[modelName];
  if (!model) throw new TypeError("Missing SparseTypes metadata for model '" + modelName + "'.");
  const members = {};
  for (const member of model.members) {
    const secret = member.redactBefore || redact;
    if (member.shape === "nested") {
      const nested = diffModel(member.childModel, own(before, member.name) ? before[member.name] : undefined, own(after, member.name) ? after[member.name] : undefined, secret);
      if (nested) {
        Object.defineProperty(members, member.name, { value: nested, enumerable: true, writable: true, configurable: true });
      }
      continue;
    }
    if (member.shape === "keyed") {
      const change = diffKeyed(modelName, member, before, after, secret);
      if (change) Object.defineProperty(members, member.name, { value: change, enumerable: true, writable: true, configurable: true });
      continue;
    }
    if (member.shape === "dictionary") {
      const change = diffDictionary(modelName, member, before, after, secret);
      if (change) Object.defineProperty(members, member.name, { value: change, enumerable: true, writable: true, configurable: true });
      continue;
    }
    const b = state(before, member.name, member.wireType, member.elementWireType); const a = state(after, member.name, member.wireType, member.elementWireType);
    const wireType = member.wireType ?? member.elementWireType;
    if (!sameEndpoint(b, a, wireType)) Object.defineProperty(members, member.name, { value: { kind: "replace", before: secret && (b.state === "value" || b.state === "null") ? { state: "redacted" } : b, after: a }, enumerable: true, writable: true, configurable: true });
  }
  return members;
}
function diffModel(modelName, before, after, redact) {
  for (const value of [before, after]) {
    if (value !== undefined && value !== null && (typeof value !== "object" || Array.isArray(value))) {
      throw new TypeError("SparseTypes model states must be objects, null, or missing.");
    }
  }
  if (before === after) return undefined;
  const model = models[modelName];
  if (before === undefined || after === undefined || before === null || after === null || typeof before !== "object" || typeof after !== "object") {
    const redactRoot = redact || hasRedactedMember(modelName);
    const b = before === undefined ? { state: "missing" } : before === null ? { state: "null" } : redactRoot ? { state: "redacted" } : { state: "value", value: snapshot(modelName, before) };
    const a = after === undefined ? { state: "missing" } : after === null ? { state: "null" } : { state: "value", value: snapshot(modelName, after) };
    return sameEndpoint(b, a) ? undefined : { kind: "replace", before: b, after: a };
  }
  const members = diffMembers(modelName, before, after, redact);
  return Object.keys(members).length ? { kind: "update", members } : undefined;
}
function buildPayload(modelName, before, after) {
  validateState(before);
  validateState(after);
  const change = diffModel(modelName, before, after, false);
  if (!change) return undefined;
  const model = models[modelName];
  return { version: "0.1", schema: model.schema, change };
}
function validateState(value, active = new WeakSet()) {
  if (value === null || value === undefined || typeof value === "string" || typeof value === "boolean") return;
  if (typeof value === "number") {
    if (!Number.isFinite(value)) throw new TypeError("ChangePayload values must contain finite JSON numbers.");
    return;
  }
  if (typeof value !== "object") throw new TypeError("ChangePayload states must contain JSON values.");
  const prototype = Object.getPrototypeOf(value);
  if (!Array.isArray(value) && prototype !== Object.prototype && prototype !== null) {
    throw new TypeError("ChangePayload states must contain plain JSON objects.");
  }
  if (active.has(value)) throw new TypeError("ChangePayload states cannot contain cyclic values.");
  active.add(value);
  for (const key of Object.keys(value)) {
    if (Array.isArray(value) && value[key] === undefined) throw new TypeError("ChangePayload arrays cannot contain undefined values.");
    validateState(value[key], active);
  }
  active.delete(value);
}
`;
