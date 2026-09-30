#!/usr/bin/env bun
/**
 * Proves the vendored OpenAPI contract has not changed since it was pinned. The
 * generator's own drift check only proves the committed client matches whatever
 * is vendored here, so without this a hand-edited contract would regenerate
 * cleanly and look correct.
 *
 * This script cannot prove that the recorded commit is the revision those bytes
 * came from. The checksum and the commit are recorded side by side, so a refresh
 * that updates both consistently passes whatever it claims. Only the service
 * repository holds the history to settle that, and it does. Its
 * `contract:consumers` check hashes the bytes at each consumer's recorded commit
 * and fails when they disagree with what the consumer recorded. That check and
 * the review of the refresh commit establish authenticity. This script
 * establishes tamper detection.
 */
import {createHash} from "node:crypto";
import {readFileSync} from "node:fs";
import {dirname, join} from "node:path";

interface ContractPin {
  commit: string;
  path: string;
  sha256: string;
}

const PIN_FIELDS: readonly (keyof ContractPin)[] = ["commit", "path", "sha256"];

function fail(message: string): never {
  console.error(message);
  process.exit(1);
}

function readPin(pinPath: string): ContractPin {
  let parsed: unknown;
  try {
    parsed = JSON.parse(readFileSync(pinPath, "utf8"));
  } catch (error) {
    fail(`Cannot read the contract pin at ${pinPath}: ${String(error)}`);
  }
  if (typeof parsed !== "object" || parsed === null) {
    fail(`The contract pin at ${pinPath} is not a JSON object.`);
  }
  const candidate = parsed as Record<string, unknown>;
  for (const field of PIN_FIELDS) {
    if (typeof candidate[field] !== "string" || candidate[field] === "") {
      fail(
        `The contract pin at ${pinPath} is missing a nonempty "${field}". ` +
          "Refusing to verify against an incomplete pin.",
      );
    }
  }
  return candidate as unknown as ContractPin;
}

function checksumOf(contractPath: string): string {
  let contents: Buffer;
  try {
    contents = readFileSync(contractPath);
  } catch (error) {
    fail(
      `Cannot read the vendored contract at ${contractPath}: ${String(error)}`,
    );
  }
  return createHash("sha256").update(contents).digest("hex");
}

const repositoryRoot = process.argv[2] ?? dirname(import.meta.dir);
const pinPath = join(repositoryRoot, "contracts", "pin.json");
const contractPath = join(repositoryRoot, "contracts", "nevabridge-v1.yaml");

const pin = readPin(pinPath);
const actualSha256 = checksumOf(contractPath);

if (actualSha256 !== pin.sha256) {
  fail(
    `The vendored contracts/nevabridge-v1.yaml does not match its recorded pin.\n` +
      `  upstream: ${pin.path} at ${pin.commit}\n` +
      `  expected: ${pin.sha256}\n` +
      `  actual:   ${actualSha256}\n` +
      "The vendored contract is generated input, not a source file. Refresh it from a " +
      "reviewed upstream commit and update contracts/pin.json in the same change.",
  );
}

console.log(
  "contract:verify: contracts/nevabridge-v1.yaml is unchanged since it was pinned at " +
    `${pin.commit.slice(0, 8)}.`,
);
