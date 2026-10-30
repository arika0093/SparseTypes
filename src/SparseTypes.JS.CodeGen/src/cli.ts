#!/usr/bin/env node
import { parseCliArgs } from "./args.js";
import { generate } from "./generate.js";

const parsed = parseCliArgs(process.argv.slice(2));
if (parsed.help) {
  console.log(
    "Usage: sparsetypes-codegen <openapi.json> [--output <directory>] (default: node_modules/@sparsetypes/generated)",
  );
} else if (parsed.error) {
  console.error(parsed.error);
  process.exitCode = parsed.exitCode ?? 2;
} else {
  try {
    await generate({
      input: parsed.input!,
      ...(parsed.output !== undefined ? { output: parsed.output } : {}),
    });
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}
