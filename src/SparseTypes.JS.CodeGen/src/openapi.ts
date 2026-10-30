import openapiTS, { astToString } from "openapi-typescript";
import { readFile } from "node:fs/promises";
import path from "node:path";

export interface OpenApiDocument extends Record<string, any> {
  openapi?: string;
  paths: Record<string, Record<string, any>>;
  components: {
    schemas: Record<string, OpenApiSchema>;
    parameters?: Record<string, OpenApiSchema>;
    requestBodies?: Record<string, OpenApiSchema>;
    responses?: Record<string, OpenApiSchema>;
  };
}
export type OpenApiSchema = Record<string, any>;

export async function loadDocument(
  input: string | URL | OpenApiDocument,
): Promise<OpenApiDocument> {
  if (typeof input === "object" && !(input instanceof URL)) return input;
  const text = await readFile(
    input instanceof URL ? input : path.resolve(input),
    "utf8",
  );
  let document: OpenApiDocument;
  try {
    document = JSON.parse(text) as OpenApiDocument;
  } catch (error) {
    throw new Error(
      `OpenAPI input must be JSON: ${error instanceof Error ? error.message : String(error)}`,
    );
  }
  if (
    !["3.0", "3.1"].some((version) => document.openapi?.startsWith(version))
  ) {
    throw new Error(
      `Unsupported OpenAPI version '${document.openapi ?? "(missing)"}'; expected 3.0 or 3.1.`,
    );
  }
  if (!document.paths || !document.components?.schemas) {
    throw new Error(
      "OpenAPI document must define paths and components.schemas.",
    );
  }
  return document;
}

export async function makeTypes(document: OpenApiDocument): Promise<string> {
  const ast = await openapiTS(document as any, { alphabetize: true });
  return astToString(ast);
}

export function parameterRefName(
  parameter: OpenApiSchema | undefined,
): string | undefined {
  const ref = parameter?.$ref;
  const prefix = "#/components/parameters/";
  if (typeof ref !== "string" || !ref.startsWith(prefix)) return undefined;
  return decodeURIComponent(ref.slice(prefix.length))
    .replace(/~1/g, "/")
    .replace(/~0/g, "~");
}

export function refName(schema: OpenApiSchema | undefined): string | undefined {
  const ref = schema?.$ref;
  if (typeof ref !== "string" || !ref.startsWith("#/components/schemas/"))
    return undefined;
  return decodeURIComponent(ref.slice("#/components/schemas/".length))
    .replace(/~1/g, "/")
    .replace(/~0/g, "~");
}

/// Resolves a local `#/...` JSON-pointer reference against the document,
/// following chained `$ref` values. Shared by parameter, requestBody and
/// response resolution so every consumer observes the same semantics.
/// Throws an actionable error for external, missing or cyclic references.
export function resolveDocumentRef(
  document: OpenApiDocument,
  ref: string,
  seen: string[] = [],
): Record<string, any> {
  if (!ref.startsWith("#/")) {
    throw new Error(
      `OpenAPI reference '${ref}' cannot be resolved: only local '#/...' references are supported.`,
    );
  }
  if (seen.includes(ref)) {
    throw new Error(
      `OpenAPI reference '${ref}' is cyclic: ${[...seen, ref].join(" -> ")}.`,
    );
  }
  const segments = ref
    .slice(2)
    .split("/")
    .map((segment) =>
      decodeURIComponent(segment).replace(/~1/g, "/").replace(/~0/g, "~"),
    );
  let current: unknown = document;
  for (let index = 0; index < segments.length; index++) {
    const segment = segments[index]!;
    if (
      current === null ||
      typeof current !== "object" ||
      !Object.prototype.hasOwnProperty.call(current, segment)
    ) {
      throw new Error(
        `OpenAPI reference '${ref}' does not resolve: '${segments.slice(index).join("/")}' is not defined.`,
      );
    }
    current = (current as Record<string, unknown>)[segment];
  }
  if (
    current === null ||
    typeof current !== "object" ||
    Array.isArray(current)
  ) {
    throw new Error(
      `OpenAPI reference '${ref}' does not resolve to an object.`,
    );
  }
  const target = current as Record<string, any>;
  if (typeof target.$ref === "string" && target.$ref !== ref) {
    return resolveDocumentRef(document, target.$ref, [...seen, ref]);
  }
  return target;
}

/// Follows an inline object's own `$ref` (if any) via [resolveDocumentRef].
/// Values without `$ref` are returned unchanged.
export function dereferenceObject<T extends Record<string, any>>(
  document: OpenApiDocument,
  value: T | undefined,
): T | undefined {
  if (!value || typeof value.$ref !== "string") return value;
  return resolveDocumentRef(document, value.$ref) as T;
}

export function schemaRef(
  schema: OpenApiSchema | undefined,
): string | undefined {
  if (!schema) return undefined;
  const direct = refName(schema);
  if (direct) return direct;
  for (const variant of [
    ...(schema.oneOf ?? []),
    ...(schema.anyOf ?? []),
    ...(schema.allOf ?? []),
  ]) {
    const name = schemaRef(variant);
    if (name) return name;
  }
  return undefined;
}

export function unwrapSchema(
  schema: OpenApiSchema | undefined,
): OpenApiSchema | undefined {
  if (!schema) return undefined;
  if (schema.$ref) return schema;
  const variants = [
    ...(schema.oneOf ?? []),
    ...(schema.anyOf ?? []),
    ...(schema.allOf ?? []),
  ];
  return (
    variants.find(
      (variant) =>
        !(variant.type === "null" || variant.type?.includes?.("null")),
    ) ?? schema
  );
}

export function metadataOf(
  schema: OpenApiSchema | undefined,
): Record<string, any> | undefined {
  const metadata = schema?.["x-sparsetypes"];
  return metadata && metadata.version === 1 ? metadata : undefined;
}

export function propertySchema(
  document: OpenApiDocument,
  modelRef: string,
  name: string,
): OpenApiSchema | undefined {
  const component = document.components.schemas[modelRef];
  return component?.properties?.[name];
}

export function findResponseModel(
  document: OpenApiDocument,
  payloadRef: string,
  payloadMetadata: Record<string, any>,
):
  | {
      model: string;
      operation?: Record<string, any>;
      method?: string;
      path?: string;
    }
  | undefined {
  for (const [path, pathItem] of Object.entries(document.paths) as [
    string,
    Record<string, any>,
  ][]) {
    for (const [method, operation] of Object.entries(pathItem) as [
      string,
      Record<string, any>,
    ][]) {
      if (!["get", "post", "put", "patch", "delete"].includes(method)) continue;
      const requestSchema = bodySchema(operation);
      if (schemaRef(requestSchema) !== payloadRef) continue;
      for (const [status, response] of Object.entries(
        operation.responses ?? {},
      ) as [string, Record<string, any>][]) {
        if (!/^[23]/.test(status)) continue;
        const content = response?.content ?? {};
        for (const entry of Object.values(content) as Record<string, any>[]) {
          const model = schemaRef(entry?.schema);
          if (model) return { model, operation, method, path };
        }
      }
    }
  }
  if (
    payloadMetadata.modelSchema &&
    document.components.schemas[payloadMetadata.modelSchema]
  ) {
    return { model: payloadMetadata.modelSchema };
  }
  return undefined;
}

export function bodySchema(
  operation: Record<string, any>,
): OpenApiSchema | undefined {
  const content = operation?.requestBody?.content ?? {};
  return (
    (Object.values(content) as Record<string, any>[])
      .map((entry) => entry.schema)
      .find((schema) => schema) ?? undefined
  );
}

export function collectOperations(
  document: OpenApiDocument,
): Array<Record<string, any>> {
  const output: Array<Record<string, any>> = [];
  for (const [path, pathItem] of Object.entries(document.paths) as [
    string,
    Record<string, any>,
  ][]) {
    for (const [method, operation] of Object.entries(pathItem) as [
      string,
      Record<string, any>,
    ][]) {
      if (
        !["get", "post", "put", "patch", "delete", "options", "head"].includes(
          method,
        ) ||
        !operation
      )
        continue;
      output.push({
        path,
        method,
        operation,
        bodySchema: bodySchema(operation),
      });
    }
  }
  return output;
}
