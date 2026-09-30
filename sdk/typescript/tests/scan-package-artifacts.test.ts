import {describe, expect, it} from "bun:test";
import {mkdtempSync, mkdirSync, writeFileSync} from "node:fs";
import {tmpdir} from "node:os";
import {join} from "node:path";

const SCRIPT_PATH = join(
  import.meta.dir,
  "..",
  "scripts",
  "scan-package-artifacts.sh",
);

// Shaped to match the scanner's Bearer-token rule without being a real credential.
const CANARY = "Bearer AAAAAAAAAAAAAAAAAAAAAAAA";
// Shaped to match the scanner's WorkOS-key rule, used as a file name rather than content.
const FILENAME_CANARY = "sk-workos-abcdefghij";

function createPackage(options: {leak: boolean; leakInName?: boolean}): string {
  const root = mkdtempSync(join(tmpdir(), "artifact-scan-"));
  mkdirSync(join(root, "src"));
  writeFileSync(
    join(root, "package.json"),
    JSON.stringify(
      {name: "scan-fixture", version: "0.0.1", files: ["src"]},
      null,
      2,
    ),
  );
  const name =
    options.leakInName === true ? `${FILENAME_CANARY}.js` : "index.js";
  writeFileSync(
    join(root, "src", name),
    options.leak
      ? `const authorization = "${CANARY}";\nmodule.exports = {authorization};\n`
      : "module.exports = {};\n",
  );
  return root;
}

async function scan(root: string): Promise<{exitCode: number; output: string}> {
  const process = Bun.spawn(["bash", SCRIPT_PATH, root], {
    stdout: "pipe",
    stderr: "pipe",
  });
  const [stdout, stderr, exitCode] = await Promise.all([
    new Response(process.stdout).text(),
    new Response(process.stderr).text(),
    process.exited,
  ]);
  return {exitCode, output: stdout + stderr};
}

describe("packaged artifact scan", () => {
  it("accepts a package with no forbidden content", async () => {
    const root = createPackage({leak: false});

    const result = await scan(root);

    expect(result.exitCode).toBe(0);
  });

  it("rejects a package that embeds a credential", async () => {
    const root = createPackage({leak: true});

    const result = await scan(root);

    expect(result.exitCode).not.toBe(0);
  });

  it("names the offending file so the leak can be found", async () => {
    const root = createPackage({leak: true});

    const result = await scan(root);

    expect(result.output).toContain("index.js");
  });

  it("never reproduces the matched credential in its own output", async () => {
    const root = createPackage({leak: true});

    const result = await scan(root);

    expect(result.output).not.toContain(CANARY);
  });

  it("never reproduces a credential carried by the file name itself", async () => {
    const root = createPackage({leak: true, leakInName: true});

    const result = await scan(root);

    expect(result.exitCode).not.toBe(0);
    expect(result.output).not.toContain(FILENAME_CANARY);
  });
});
