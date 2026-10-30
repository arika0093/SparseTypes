import assert from "node:assert/strict";
import test from "node:test";
import { parseCliArgs } from "../src/args.js";

test("parses input and output regardless of option order", () => {
  assert.deepEqual(parseCliArgs(["./openapi.json", "--output", "./generated"]), {
    input: "./openapi.json",
    output: "./generated",
  });
  assert.deepEqual(parseCliArgs(["--output", "./generated", "./openapi.json"]), {
    input: "./openapi.json",
    output: "./generated",
  });
  assert.deepEqual(parseCliArgs(["--output=./generated", "./openapi.json"]), {
    input: "./openapi.json",
    output: "./generated",
  });
});

test("handles dashes, missing values, unknown flags, and help", () => {
  assert.equal(parseCliArgs(["--", "-leading.json"]).input, "-leading.json");
  assert.deepEqual(parseCliArgs(["--output"]).exitCode, 2);
  assert.deepEqual(parseCliArgs(["--output", "--", "in.json"]).exitCode, 2);
  assert.match(parseCliArgs(["--output"])!.error!, /Missing value/);
  assert.match(parseCliArgs(["--unknown", "in.json"])!.error!, /Unknown option/);
  assert.deepEqual(parseCliArgs(["--unknown", "in.json"]).exitCode, 2);
  assert.match(parseCliArgs(["a.json", "b.json"])!.error!, /Multiple input/);
  assert.deepEqual(parseCliArgs(["a.json", "b.json"]).exitCode, 2);
  assert.deepEqual(parseCliArgs([]).exitCode, 2);
  assert.equal(parseCliArgs(["--help"]).help, true);
  assert.equal(parseCliArgs(["-h", "in.json"]).help, true);
  assert.equal(parseCliArgs(["in.json"]).input, "in.json");
});
