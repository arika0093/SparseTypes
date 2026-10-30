import { parameterRefName } from "./openapi.js";
import type { OpenApiDocument } from "./openapi.js";

const METHODS = new Set([
  "get",
  "post",
  "put",
  "patch",
  "delete",
  "options",
  "head",
]);

type Parameter = Record<string, any>;
interface Operation {
  path: string;
  method: string;
  operation: Record<string, any>;
  name: string;
  parameters: Parameter[];
  requestBody: Record<string, any> | undefined;
  argsRequired: boolean;
}

function dereference(
  document: OpenApiDocument,
  value: Parameter,
  seen: Set<string> = new Set(),
): Parameter {
  if (!value?.$ref) return value;
  const ref = value.$ref;
  if (typeof ref !== "string") {
    throw new Error(
      `OpenAPI parameter reference must be a string, but received ${typeof ref}.`,
    );
  }
  const name = parameterRefName(value);
  if (name === undefined) {
    if (ref.startsWith("#/")) {
      throw new Error(
        `Unsupported OpenAPI parameter reference '${ref}'; only local '#/components/parameters/...' references are supported.`,
      );
    }
    throw new Error(
      `Unsupported external OpenAPI parameter reference '${ref}'; only local '#/components/parameters/...' references are supported.`,
    );
  }
  if (seen.has(name)) {
    throw new Error(
      `Circular OpenAPI parameter reference '#/components/parameters/${name}' detected.`,
    );
  }
  const resolved = document.components?.parameters?.[name];
  if (!resolved) {
    throw new Error(
      `Unresolved OpenAPI parameter reference '${ref}'; no such entry exists in components.parameters.`,
    );
  }
  seen.add(name);
  return dereference(document, resolved, seen);
}

function operationName(
  operation: Record<string, any>,
  method: string,
  path: string,
  used: Set<string>,
): string {
  const raw =
    operation.operationId ??
    `${method.toLowerCase()}${path
      .split("/")
      .filter(Boolean)
      .map((segment) => {
        const parameter = /^\{([^}]+)\}$/.exec(segment);
        const value = parameter
          ? `By${parameter[1][0].toUpperCase()}${parameter[1].slice(1)}`
          : segment;
        return value[0].toUpperCase() + value.slice(1);
      })
      .join("")}`;
  let name = raw.replace(
    /[^A-Za-z0-9_$]+(.)?/g,
    (_: string, next: string | undefined) => (next ? next.toUpperCase() : ""),
  );
  if (!/^[A-Za-z_$]/.test(name)) name = `request${name}`;
  name = name[0].toLowerCase() + name.slice(1);
  const base = name;
  let suffix = 2;
  while (used.has(name)) name = `${base}${suffix++}`;
  used.add(name);
  return name;
}

function collectOperations(document: OpenApiDocument): Operation[] {
  const operations: Operation[] = [];
  const used = new Set<string>();
  for (const [path, pathItem] of Object.entries(document.paths)) {
    const sharedParameters = pathItem.parameters ?? [];
    for (const [method, operation] of Object.entries(pathItem)) {
      if (!METHODS.has(method) || !operation) continue;
      const parameters = [
        ...sharedParameters,
        ...(operation.parameters ?? []),
      ].map((parameter) => dereference(document, parameter));
      for (const parameter of parameters) validateParameter(parameter);
      operations.push({
        path,
        method,
        operation,
        name: operationName(operation, method, path, used),
        parameters,
        requestBody: operation.requestBody,
        argsRequired:
          parameters.some((parameter) => parameter.required) ||
          operation.requestBody?.required === true,
      });
    }
  }
  return operations;
}

function validateParameter(parameter: Parameter): void {
  if (parameter.content)
    throw new Error(
      `OpenAPI parameter '${parameter.name}' uses content serialization, which codegen does not support.`,
    );
  const styles: Record<string, string[]> = {
    path: ["simple", "label", "matrix"],
    query: ["form", "spaceDelimited", "pipeDelimited", "deepObject"],
    header: ["simple"],
  };
  if (
    !styles[parameter.in]?.includes(
      parameter.style ?? (parameter.in === "query" ? "form" : "simple"),
    )
  ) {
    throw new Error(
      `OpenAPI parameter '${parameter.name}' uses unsupported location or style '${parameter.in}:${parameter.style}'.`,
    );
  }
  if (parameter.in === "query" && parameter.allowReserved) {
    throw new Error(
      `OpenAPI query parameter '${parameter.name}' uses allowReserved, which codegen does not support.`,
    );
  }
}

function typesForOperation(operation: Operation): string {
  const safe = operation.name[0].toUpperCase() + operation.name.slice(1);
  return `type ${safe}Operation = NonNullable<paths[${JSON.stringify(operation.path)}][${JSON.stringify(operation.method)}]>;\nexport type ${safe}Args = OperationArgs<${safe}Operation>;\n`;
}

export function emitClientDts(document: OpenApiDocument): string {
  const operations = collectOperations(document);
  const types = operations.map(typesForOperation).join("\n");
  const functions = operations
    .map(({ name, argsRequired }) => {
      const safe = name[0].toUpperCase() + name.slice(1);
      const args = argsRequired ? `args: ${safe}Args` : `args?: ${safe}Args`;
      return `  ${name}(${args}, init?: RequestInit): Promise<OperationSuccess<${safe}Operation>>;\n  ${name}Raw(${args}, init?: RequestInit): Promise<Response>;`;
    })
    .join("\n");
  const argumentTypes = `type OperationParameters<T> = T extends { parameters: infer P } ? P : never;
type PathParameters<T> = OperationParameters<T> extends { path: infer P } ? Exclude<P, undefined> : never;
type QueryParameters<T> = OperationParameters<T> extends { query: infer P } ? Exclude<P, undefined> : never;
type HeaderParameters<T> = OperationParameters<T> extends { header?: infer P } ? Exclude<P, undefined> : never;
type RequestBody<T> = T extends { requestBody: { content: infer C } } ? C[keyof C] : never;
type BodyArgument<T> = T extends { requestBody: { content: infer C } } ? { body: C[keyof C] } : T extends { requestBody?: { content: infer C } } ? { body?: C[keyof C] } : {};
type RequiredKeys<T> = { [K in keyof T]-?: {} extends Pick<T, K> ? never : K }[keyof T];
type QueryArguments<T> = [QueryParameters<T>] extends [never] ? {} : [RequiredKeys<QueryParameters<T>>] extends [never] ? { query?: QueryParameters<T> } : { query: QueryParameters<T> };
type ParameterArguments<T> = [PathParameters<T>] extends [never] ? ([QueryParameters<T>] extends [never] ? {} : [RequiredKeys<QueryParameters<T>>] extends [never] ? { params?: { query?: QueryParameters<T> } } : { params: { query: QueryParameters<T> } }) : { params: { path: PathParameters<T> } & QueryArguments<T> };
type HeaderArgument<T> = [HeaderParameters<T>] extends [never] ? {} : [RequiredKeys<HeaderParameters<T>>] extends [never] ? { headers?: HeaderParameters<T> } : { headers: HeaderParameters<T> };
type OperationArgs<T> = ParameterArguments<T> & HeaderArgument<T> & BodyArgument<T>;
type SuccessResponses<T> = T extends { responses: infer R } ? { [K in keyof R]: \`\${K extends number ? K : K extends string ? K : never}\` extends \`2\${string}\` ? R[K] : never }[keyof R] : never;
type Content<T> = T extends { content: infer C } ? C[keyof C] : never;
type OperationSuccess<T> = Content<SuccessResponses<T>>;`;
  return `import type { paths } from "./api";\n\n${argumentTypes}\n\n${types}\nexport interface ClientOptions { baseUrl?: string; fetch?: typeof globalThis.fetch; }\nexport declare class ApiError<TBody = unknown> extends Error { readonly status: number; readonly body: TBody; readonly response: Response; }\nexport interface ApiClient {\n${functions}\n}\nexport declare function createClient(options?: ClientOptions): ApiClient;\n`;
}

export function emitClientJs(document: OpenApiDocument): string {
  const operations = collectOperations(document);
  const manifests = operations.map((operation) => ({
    name: operation.name,
    method: operation.method.toUpperCase(),
    path: operation.path,
    parameters: operation.parameters.map(
      ({ name, in: location, style, explode, required }) => ({
        name,
        in: location,
        style: style ?? (location === "query" ? "form" : "simple"),
        explode: explode ?? location === "query",
        required: required === true,
      }),
    ),
    argsRequired: operation.argsRequired,
    bodyRequired: operation.operation.requestBody?.required === true,
    requestMedia: Object.keys(operation.requestBody?.content ?? {}),
    successMedia: successfulMedia(operation.operation.responses),
  }));
  return `const operations = ${JSON.stringify(manifests, null, 2)};\n\n${CLIENT_RUNTIME}\n\nexport function createClient(options = {}) {\n  const baseUrl = options.baseUrl ?? ${JSON.stringify(document.servers?.[0]?.url ?? "")};\n  const fetcher = options.fetch ?? globalThis.fetch;\n  if (typeof fetcher !== "function") throw new TypeError("A fetch implementation is required.");\n  const client = {};\n  for (const operation of operations) {\n    client[operation.name] = (args = {}, init = {}) => invoke(fetcher, baseUrl, operation, args, init, false);\n    client[operation.name + "Raw"] = (args = {}, init = {}) => invoke(fetcher, baseUrl, operation, args, init, true);\n  }\n  return client;\n}\n`;
}

function successfulMedia(responses: Record<string, any> = {}): string[] {
  const media = new Set<string>();
  for (const [status, response] of Object.entries(responses ?? {})) {
    if (!/^[23]/.test(status)) continue;
    for (const type of Object.keys(response?.content ?? {})) media.add(type);
  }
  return [...media];
}

const CLIENT_RUNTIME = String.raw`
export class ApiError extends Error {
  constructor(response, body) {
    super("HTTP " + response.status + " " + response.statusText);
    this.name = "ApiError";
    this.status = response.status;
    this.body = body;
    this.response = response;
  }
}
function encode(value) { return encodeURIComponent(String(value)); }
function serializeSimple(value, explode) {
  if (Array.isArray(value)) return value.map(encode).join(",");
  if (value && typeof value === "object") return Object.entries(value).map(([key, item]) => explode ? encode(key) + "=" + encode(item) : encode(key) + "," + encode(item)).join(",");
  return encode(value);
}
function serializeQuery(pairs, parameter, value) {
  if (value === undefined) return;
  const name = parameter.name;
  if (parameter.style === "deepObject" && value && typeof value === "object" && !Array.isArray(value)) {
    for (const [key, item] of Object.entries(value)) pairs.push(encode(name + "[" + key + "]") + "=" + encode(item));
    return;
  }
  if (Array.isArray(value) && parameter.style === "spaceDelimited") { pairs.push(encode(name) + "=" + value.map(encode).join("%20")); return; }
  if (Array.isArray(value) && parameter.style === "pipeDelimited") { pairs.push(encode(name) + "=" + value.map(encode).join("|")); return; }
  if (parameter.style === "form" && parameter.explode && value && typeof value === "object" && !Array.isArray(value)) {
    for (const [key, item] of Object.entries(value)) pairs.push(encode(key) + "=" + encode(item));
    return;
  }
  if (parameter.style === "form" && parameter.explode && Array.isArray(value)) {
    for (const item of value) pairs.push(encode(name) + "=" + encode(item));
    return;
  }
  pairs.push(encode(name) + "=" + serializeSimple(value, parameter.explode));
}
function buildUrl(baseUrl, operation, args) {
  let path = operation.path;
  for (const parameter of operation.parameters) {
    if (parameter.in !== "path") continue;
    const value = args.params?.path?.[parameter.name];
    if (value === undefined || value === null) {
      if (parameter.required) throw new TypeError("Missing path parameter '" + parameter.name + "'.");
      continue;
    }
    const formatted = parameter.style === "label" ? "." + serializeSimple(value, parameter.explode)
      : parameter.style === "matrix" ? ";" + parameter.name + "=" + serializeSimple(value, parameter.explode)
        : serializeSimple(value, parameter.explode);
    path = path.replace("{" + parameter.name + "}", formatted);
  }
  const pairs = [];
  for (const parameter of operation.parameters) {
    if (parameter.in !== "query") continue;
    const value = args.params?.query?.[parameter.name];
    if (parameter.required && value === undefined) throw new TypeError("Missing query parameter '" + parameter.name + "'.");
    serializeQuery(pairs, parameter, value);
  }
  const prefix = baseUrl.endsWith("/") && path.startsWith("/") ? baseUrl.slice(0, -1) : baseUrl;
  const url = prefix + path;
  const suffix = pairs.join("&");
  return suffix ? url + (url.includes("?") ? "&" : "?") + suffix : url;
}
async function responseBody(response) {
  const text = await response.text();
  if (!text) return undefined;
  const contentType = response.headers.get("content-type") ?? "";
  if (/[/+]json(?:\s*;|$)/i.test(contentType) || /application\/json/i.test(contentType)) return JSON.parse(text);
  return text;
}
async function invoke(fetcher, baseUrl, operation, args, overrides, raw) {
  const url = buildUrl(baseUrl, operation, args);
  const headers = new Headers(args.headers);
  const requestedBody = Object.prototype.hasOwnProperty.call(args, "body") && args.body !== undefined;
  const generatedBody = requestedBody ? JSON.stringify(args.body) : undefined;
  if (generatedBody !== undefined && !headers.has("content-type")) {
    const media = operation.requestMedia.find((type) => type.includes("json"));
    if (media) headers.set("content-type", media);
  }
  const overrideHeaders = new Headers(overrides.headers);
  overrideHeaders.forEach((value, name) => headers.set(name, value));
  const hasOverrideBody = Object.prototype.hasOwnProperty.call(overrides, "body") && overrides.body !== undefined;
  for (const parameter of operation.parameters) {
    if (parameter.in === "header" && parameter.required && !headers.has(parameter.name)) {
      throw new TypeError("Missing header parameter '" + parameter.name + "'.");
    }
  }
  if (operation.bodyRequired && !requestedBody && !hasOverrideBody) throw new TypeError("Missing required request body.");
  const body = hasOverrideBody ? overrides.body : generatedBody;
  if (hasOverrideBody && !overrideHeaders.has("content-type") && !new Headers(args.headers).has("content-type")) headers.delete("content-type");
  const init = { ...overrides, method: overrides.method ?? operation.method, headers };
  if (body === undefined) delete init.body; else init.body = body;
  const response = await fetcher(url, init);
  if (raw) return response;
  if (!response.ok) throw new ApiError(response, await responseBody(response));
  return responseBody(response);
}
`;
