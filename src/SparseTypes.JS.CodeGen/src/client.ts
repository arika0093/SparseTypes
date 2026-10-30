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
  requestKind: string | undefined;
  requestMediaType: string | undefined;
  argsRequired: boolean;
}

export const SUPPORTED_MEDIA_DOC =
  "application/json (or any +json suffix), text/*, application/octet-stream, multipart/form-data, application/x-www-form-urlencoded";

function mediaKind(type: string): string | undefined {
  const mime = type.split(";")[0]?.trim().toLowerCase() ?? "";
  if (mime === "application/json" || mime.endsWith("+json")) return "json";
  if (mime.startsWith("text/")) return "text";
  if (mime === "application/octet-stream") return "octet-stream";
  if (mime === "multipart/form-data") return "multipart";
  if (mime === "application/x-www-form-urlencoded") return "urlencoded";
  return undefined;
}

function describeOperation(name: string, method: string, path: string): string {
  return `operation '${name}' (${method.toUpperCase()} ${path})`;
}

function requestSerialization(
  name: string,
  method: string,
  path: string,
  requestBody: Record<string, any> | undefined,
): { kind: string; mediaType: string } | undefined {
  const media = Object.keys(requestBody?.content ?? {});
  if (media.length === 0) return undefined;
  const unsupported = media.filter((type) => !mediaKind(type));
  if (unsupported.length > 0) {
    throw new Error(
      `OpenAPI ${describeOperation(name, method, path)} declares unsupported request media type(s) ${unsupported.map((type) => `'${type}'`).join(", ")}. Supported request media: ${SUPPORTED_MEDIA_DOC}.`,
    );
  }
  const kinds = [...new Set(media.map((type) => mediaKind(type)))];
  if (kinds.length > 1) {
    throw new Error(
      `OpenAPI ${describeOperation(name, method, path)} declares multiple request serialization kinds (${kinds.join(", ")}). Narrow requestBody.content to a single supported media type.`,
    );
  }
  return { kind: kinds[0]!, mediaType: media[0]! };
}

function validateSuccessMedia(
  name: string,
  method: string,
  path: string,
  responses: Record<string, any> = {},
): void {
  const unsupported = new Set<string>();
  for (const [status, response] of Object.entries(responses ?? {})) {
    if (!/^[23]/.test(status)) continue;
    for (const type of Object.keys(response?.content ?? {})) {
      if (!mediaKind(type)) unsupported.add(type);
    }
  }
  if (unsupported.size > 0) {
    throw new Error(
      `OpenAPI ${describeOperation(name, method, path)} declares unsupported success media type(s) ${[...unsupported].map((type) => `'${type}'`).join(", ")}. Supported response media: application/json (or any +json suffix), text/*, application/octet-stream, or no content.`,
    );
  }
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

// Operation names share one namespace with their `name + "Raw"` variants,
// which createClient() also installs. Reserve both spellings so an explicit
// `fooRaw` operationId (or a later duplicate) is renamed instead of clobbered.
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
  while (used.has(name) || used.has(`${name}Raw`)) name = `${base}${suffix++}`;
  used.add(name);
  used.add(`${name}Raw`);
  return name;
}

function collectOperations(document: OpenApiDocument): Operation[] {
  const operations: Operation[] = [];
  const used = new Set<string>();
  for (const [path, pathItem] of Object.entries(document.paths)) {
    const sharedParameters = pathItem.parameters ?? [];
    for (const [method, operation] of Object.entries(pathItem)) {
      if (!METHODS.has(method) || !operation) continue;
      // Per OpenAPI, an operation parameter with the same (name, in) identity
      // overrides the PathItem parameter instead of duplicating it. Resolve
      // $refs before comparing so equivalent identities are recognized.
      const merged = new Map<string, Parameter>();
      for (const parameter of sharedParameters) {
        const resolved = dereference(document, parameter);
        merged.set(`${resolved.name}\0${resolved.in}`, resolved);
      }
      for (const parameter of operation.parameters ?? []) {
        const resolved = dereference(document, parameter);
        // Map.set on an existing key replaces the definition in place, so
        // overrides keep the PathItem order while new parameters append.
        merged.set(`${resolved.name}\0${resolved.in}`, resolved);
      }
      const parameters = [...merged.values()];
      for (const parameter of parameters) validateParameter(parameter);
      const operationNameValue = operationName(operation, method, path, used);
      const serialization = requestSerialization(
        operationNameValue,
        method,
        path,
        operation.requestBody,
      );
      validateSuccessMedia(
        operationNameValue,
        method,
        path,
        operation.responses,
      );
      operations.push({
        path,
        method,
        operation,
        name: operationNameValue,
        parameters,
        requestBody: operation.requestBody,
        requestKind: serialization?.kind,
        requestMediaType: serialization?.mediaType,
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
  return `import type { paths } from "./api.js";\n\n${argumentTypes}\n\n${types}\nexport interface ClientOptions { baseUrl?: string; fetch?: typeof globalThis.fetch; }\nexport declare class ApiError<TBody = unknown> extends Error { readonly status: number; readonly body: TBody; readonly response: Response; }\nexport interface ApiClient {\n${functions}\n}\nexport declare function createClient(options?: ClientOptions): ApiClient;\n`;
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
    requestKind: operation.requestKind,
    requestMediaType: operation.requestMediaType,
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
function isJsonMime(mime) { return mime === "application/json" || mime.endsWith("+json"); }
async function responseBody(response) {
  if (response.status === 204 || response.status === 205 || response.status === 304) return undefined;
  const contentType = response.headers.get("content-type") ?? "";
  const mime = contentType.split(";")[0].trim().toLowerCase();
  if (!mime) {
    const text = await response.text();
    return text ? text : undefined;
  }
  if (!isJsonMime(mime) && mime !== "application/octet-stream" && !mime.startsWith("text/")) return response.blob();
  if (mime === "application/octet-stream") return response.blob();
  const text = await response.text();
  if (!text) return undefined;
  if (isJsonMime(mime)) return JSON.parse(text);
  return text;
}
function appendFormValue(form, name, value) {
  if (value === undefined || value === null) return;
  if (typeof Blob !== "undefined" && value instanceof Blob) { form.append(name, value); return; }
  if (typeof value === "object") { form.append(name, JSON.stringify(value)); return; }
  form.append(name, String(value));
}
function serializeBody(kind, mediaType, body) {
  if (kind === "json") return { body: JSON.stringify(body), contentType: mediaType, boundary: false };
  if (kind === "text") {
    if (typeof body !== "string") throw new TypeError("A text/* request body must be a string; received " + typeof body + ".");
    return { body, contentType: mediaType, boundary: false };
  }
  if (kind === "octet-stream") {
    const binary = typeof Blob !== "undefined" && body instanceof Blob;
    const buffer = typeof ArrayBuffer !== "undefined" && (body instanceof ArrayBuffer || ArrayBuffer.isView(body));
    if (!binary && !buffer && typeof body !== "string") throw new TypeError("An application/octet-stream request body must be a Blob, ArrayBuffer, ArrayBuffer view, or string.");
    return { body, contentType: mediaType, boundary: false };
  }
  if (kind === "urlencoded") {
    if (typeof URLSearchParams !== "undefined" && body instanceof URLSearchParams) return { body: body.toString(), contentType: mediaType, boundary: false };
    if (!body || typeof body !== "object" || Array.isArray(body)) throw new TypeError("An application/x-www-form-urlencoded request body must be a plain object or URLSearchParams.");
    const params = new URLSearchParams();
    for (const [name, value] of Object.entries(body)) {
      if (value === undefined || value === null) continue;
      if (Array.isArray(value)) for (const item of value) params.append(name, String(item));
      else params.append(name, String(value));
    }
    return { body: params.toString(), contentType: mediaType, boundary: false };
  }
  if (typeof FormData !== "undefined" && body instanceof FormData) return { body, contentType: undefined, boundary: true };
  if (!body || typeof body !== "object" || Array.isArray(body)) throw new TypeError("A multipart/form-data request body must be FormData or a plain object.");
  const form = new FormData();
  for (const [name, value] of Object.entries(body)) {
    if (Array.isArray(value)) for (const item of value) appendFormValue(form, name, item);
    else appendFormValue(form, name, value);
  }
  return { body: form, contentType: undefined, boundary: true };
}
async function invoke(fetcher, baseUrl, operation, args, overrides, raw) {
  const url = buildUrl(baseUrl, operation, args);
  const headers = new Headers(args.headers);
  const requestedBody = Object.prototype.hasOwnProperty.call(args, "body") && args.body !== undefined;
  let generatedBody;
  let boundary = false;
  if (requestedBody) {
    if (!operation.requestKind) throw new TypeError("Operation '" + operation.name + "' declares no request media type, so args.body cannot be serialized.");
    const serialized = serializeBody(operation.requestKind, operation.requestMediaType, args.body);
    generatedBody = serialized.body;
    boundary = serialized.boundary;
    if (serialized.contentType && !headers.has("content-type")) headers.set("content-type", serialized.contentType);
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
  if (boundary && !overrideHeaders.has("content-type") && !new Headers(args.headers).has("content-type")) headers.delete("content-type");
  const init = { ...overrides, method: overrides.method ?? operation.method, headers };
  if (body === undefined) delete init.body; else init.body = body;
  const response = await fetcher(url, init);
  if (raw) return response;
  if (!response.ok) throw new ApiError(response, await responseBody(response));
  return responseBody(response);
}
`;
