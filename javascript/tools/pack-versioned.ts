import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const packageRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "../..",
);
const nbgvScript = path.join(
  packageRoot,
  "node_modules/nerdbank-gitversioning/npmVersion.js",
);
const npmCli = process.env.npm_execpath;

if (!npmCli) {
  throw new Error(
    "Run this command through npm to create a versioned package.",
  );
}

function run(command: string, args: string[]): void {
  const result = spawnSync(command, args, {
    cwd: packageRoot,
    stdio: "inherit",
  });
  if (result.error) throw result.error;
  if (result.status !== 0) {
    throw new Error(
      `${path.basename(command)} exited with status ${result.status ?? "unknown"}.`,
    );
  }
}

try {
  run("dotnet", [
    "tool",
    "restore",
    "--tool-manifest",
    "../.config/dotnet-tools.json",
  ]);
  run(process.execPath, [nbgvScript]);
  run(process.execPath, [
    npmCli,
    "pack",
    "--ignore-scripts",
    ...process.argv.slice(2),
  ]);
} finally {
  run(process.execPath, [nbgvScript, "--reset"]);
}
