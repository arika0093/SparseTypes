#!/usr/bin/env node
import { generate } from "./generate.js";

const args = process.argv.slice(2);
const input = args.find((value) => !value.startsWith("-"));
const outputFlag = args.indexOf("--output");
const output = outputFlag >= 0 ? args[outputFlag + 1] : undefined;

if (!input || (outputFlag >= 0 && (!output || output.startsWith("--")))) {
  console.error(
    "Usage: sparsetypes-codegen <openapi.json> [--output <directory>] (default: node_modules/@sparsetypes/generated)",
  );
  process.exitCode = 2;
} else {
  try {
    await generate({ input, ...(output ? { output } : {}) });
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}
