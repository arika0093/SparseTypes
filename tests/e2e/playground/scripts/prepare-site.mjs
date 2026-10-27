import { copyFile, readFile, writeFile } from "node:fs/promises";
import { resolve } from "node:path";

// Ensures the published Blazor index.html uses the root base href served by
// the custom domain sparsetypes.eclairs.cc, then adds the static files Pages
// needs (CNAME, 404.html fallback, .nojekyll). PLAYGROUND_SITE_DIR overrides
// the default publish output so the E2E site and the Pages deployment share
// this exact preparation instead of duplicating it in shell.
const siteDirectory = resolve(
  process.env.PLAYGROUND_SITE_DIR ?? resolve(import.meta.dirname, "../artifacts/wwwroot"),
);
const indexPath = resolve(siteDirectory, "index.html");
const customDomain = "sparsetypes.eclairs.cc";

const baseHref = '<base href="/" />';
const legacyBasePathHref = '<base href="/SparseTypes/" />';

let index;
try {
  index = await readFile(indexPath, "utf8");
} catch {
  console.error(
    `[prepare-site] cannot read ${indexPath}. The dotnet publish output is missing; run 'npm run build' first.`,
  );
  process.exit(1);
}

const rootBaseHrefCount = index.split(baseHref).length - 1;
const legacyBaseHrefCount = index.split(legacyBasePathHref).length - 1;

if (rootBaseHrefCount === 1 && legacyBaseHrefCount === 0) {
  console.log(`[prepare-site] base href is already / in ${indexPath}; nothing to rewrite`);
} else if (rootBaseHrefCount === 0 && legacyBaseHrefCount === 1) {
  await writeFile(indexPath, index.replace(legacyBasePathHref, baseHref));
  console.log(`[prepare-site] rewrote legacy /SparseTypes/ base href to / in ${indexPath}`);
} else {
  console.error(
    `[prepare-site] expected exactly one ${baseHref} or one ${legacyBasePathHref} in ${indexPath}, ` +
      `found ${rootBaseHrefCount} root and ${legacyBaseHrefCount} legacy occurrences. ` +
      `The published index.html layout changed; update prepare-site.mjs to match.`,
  );
  process.exit(1);
}

await copyFile(indexPath, resolve(siteDirectory, "404.html"));
await writeFile(resolve(siteDirectory, ".nojekyll"), "");
await writeFile(resolve(siteDirectory, "CNAME"), `${customDomain}\n`);
console.log(`[prepare-site] wrote 404.html, .nojekyll, and CNAME (${customDomain}) in ${siteDirectory}`);
