import {describe, expect, it} from "bun:test";
import {mkdtempSync, mkdirSync, writeFileSync} from "node:fs";
import {tmpdir} from "node:os";
import {join} from "node:path";

const SCRIPT_PATH = join(
  import.meta.dir,
  "..",
  "scripts",
  "verify-contract-pin.ts",
);

const CONTRACT_BODY = "openapi: 3.1.0\n";
// SHA-256 of CONTRACT_BODY, computed independently of the script under test.
const CONTRACT_SHA256 =
  "f39db8e8ede3dc2457c613e2a304e6d478f6e5ec660e4746464f41e76ac77006";

type PinOverrides = Partial<{
  commit: string;
  path: string;
  sha256: string;
}>;

function createRoot(options: {
  contract?: string | null;
  pin?: PinOverrides | null;
}): string {
  const root = mkdtempSync(join(tmpdir(), "contract-pin-"));
  mkdirSync(join(root, "contracts"));
  if (options.contract !== null) {
    writeFileSync(
      join(root, "contracts", "nevabridge-v1.yaml"),
      options.contract ?? CONTRACT_BODY,
    );
  }
  if (options.pin !== null) {
    writeFileSync(
      join(root, "contracts", "pin.json"),
      JSON.stringify(
        {
          commit: "2bca4f8893d09920a210a4ad8a86d4c0b3bfc70c",
          path: "api/openapi/nevabridge-v1.yaml",
          sha256: CONTRACT_SHA256,
          ...options.pin,
        },
        null,
        2,
      ),
    );
  }
  return root;
}

async function runVerification(
  root: string,
): Promise<{exitCode: number; output: string}> {
  const process = Bun.spawn(["bun", SCRIPT_PATH, root], {
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

describe("contract pin verification", () => {
  it("accepts a vendored contract that matches its recorded checksum", async () => {
    const root = createRoot({});

    const result = await runVerification(root);

    expect(result.exitCode).toBe(0);
  });

  it("rejects a vendored contract that was edited after pinning", async () => {
    const root = createRoot({contract: "openapi: 3.1.0\ntampered: true\n"});

    const result = await runVerification(root);

    expect(result.exitCode).not.toBe(0);
    expect(result.output).toContain("nevabridge-v1.yaml");
  });

  it("names the upstream commit so a stale pin can be traced", async () => {
    const root = createRoot({contract: "openapi: 3.1.0\ntampered: true\n"});

    const result = await runVerification(root);

    expect(result.output).toContain("2bca4f8893d09920a210a4ad8a86d4c0b3bfc70c");
  });

  it("fails when the pin omits a required field rather than skipping the check", async () => {
    const root = createRoot({pin: {sha256: undefined}});

    const result = await runVerification(root);

    expect(result.exitCode).not.toBe(0);
    expect(result.output).toContain("sha256");
  });

  it("fails when the vendored contract is absent", async () => {
    const root = createRoot({contract: null});

    const result = await runVerification(root);

    expect(result.exitCode).not.toBe(0);
  });

  it("fails when the pin file is absent", async () => {
    const root = createRoot({pin: null});

    const result = await runVerification(root);

    expect(result.exitCode).not.toBe(0);
  });
});
