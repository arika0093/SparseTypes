export interface ParsedCliArgs {
  input?: string;
  output?: string;
  help?: boolean;
  error?: string;
  exitCode?: number;
}

export const CLI_USAGE =
  "Usage: sparsetypes-codegen <openapi.json> [--output <directory>] (default: node_modules/@sparsetypes/generated)";

export function parseCliArgs(argv: string[]): ParsedCliArgs {
  let input: string | undefined;
  let output: string | undefined;
  let help = false;
  let positionalCount = 0;
  let endOfOptions = false;

  for (let index = 0; index < argv.length; index++) {
    const arg = argv[index]!;
    if (endOfOptions) {
      positionalCount++;
      if (positionalCount > 1) {
        return {
          error: `Multiple input files are not supported.\n${CLI_USAGE}`,
          exitCode: 2,
        };
      }
      input = arg;
      continue;
    }
    if (arg === "--") {
      endOfOptions = true;
      continue;
    }
    if (arg === "--help" || arg === "-h") {
      help = true;
      continue;
    }
    if (arg === "--output") {
      const value = argv[index + 1];
      if (value === undefined || value === "--" || value.startsWith("-")) {
        return {
          error: `Missing value for --output.\n${CLI_USAGE}`,
          exitCode: 2,
        };
      }
      output = value;
      index++;
      continue;
    }
    if (arg.startsWith("--output=")) {
      const value = arg.slice("--output=".length);
      if (!value) {
        return {
          error: `Missing value for --output.\n${CLI_USAGE}`,
          exitCode: 2,
        };
      }
      output = value;
      continue;
    }
    if (arg.startsWith("-")) {
      return { error: `Unknown option '${arg}'.\n${CLI_USAGE}`, exitCode: 2 };
    }
    positionalCount++;
    if (positionalCount > 1) {
      return {
        error: `Multiple input files are not supported.\n${CLI_USAGE}`,
        exitCode: 2,
      };
    }
    input = arg;
  }

  if (help) return { help: true, input, output };
  if (!input) return { error: CLI_USAGE, exitCode: 2 };
  return { input, output };
}
