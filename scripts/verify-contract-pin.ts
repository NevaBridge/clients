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
 *
 * The webview chat bridge protocol is pinned the same way, as a directory: its
 * pin lists every file with its SHA-256, so an edited, added or removed file
 * fails as well.
 */
import {createHash} from "node:crypto";
import {readdirSync, readFileSync} from "node:fs";
import {dirname, join, relative} from "node:path";

interface ContractPin {
  commit: string;
  path: string;
  sha256: string;
}

interface DirectoryPin {
  commit: string;
  path: string;
  files: Record<string, string>;
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

function readDirectoryPin(pinPath: string): DirectoryPin {
  let parsed: unknown;
  try {
    parsed = JSON.parse(readFileSync(pinPath, "utf8"));
  } catch (error) {
    fail(`Cannot read the protocol pin at ${pinPath}: ${String(error)}`);
  }
  if (typeof parsed !== "object" || parsed === null) {
    fail(`The protocol pin at ${pinPath} is not a JSON object.`);
  }
  const candidate = parsed as Record<string, unknown>;
  const commit = candidate["commit"];
  const path = candidate["path"];
  const files = candidate["files"];
  if (
    typeof commit !== "string" ||
    commit === "" ||
    typeof path !== "string" ||
    path === "" ||
    typeof files !== "object" ||
    files === null ||
    Array.isArray(files)
  ) {
    fail(
      `The protocol pin at ${pinPath} needs a nonempty "commit", "path" and "files". ` +
        "Refusing to verify against an incomplete pin.",
    );
  }
  const checksums: Record<string, string> = {};
  for (const [file, sha256] of Object.entries(files)) {
    if (typeof sha256 !== "string" || sha256 === "") {
      fail(`The protocol pin at ${pinPath} has no checksum for ${file}.`);
    }
    checksums[file] = sha256;
  }
  if (Object.keys(checksums).length === 0) {
    fail(`The protocol pin at ${pinPath} lists no files.`);
  }
  return {commit, path, files: checksums};
}

function filesBelow(directory: string): string[] {
  try {
    return readdirSync(directory, {recursive: true, withFileTypes: true})
      .filter((entry) => entry.isFile())
      .map((entry) =>
        relative(directory, join(entry.parentPath, entry.name))
          .split("\\")
          .join("/"),
      )
      .sort();
  } catch (error) {
    fail(`Cannot read the vendored protocol at ${directory}: ${String(error)}`);
  }
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

const protocolPin = readDirectoryPin(
  join(repositoryRoot, "contracts", "webview-chat-protocol.pin.json"),
);
const protocolDirectory = join(
  repositoryRoot,
  "contracts",
  "webview-chat-protocol",
);
const pinnedFiles = Object.keys(protocolPin.files).sort();
const vendoredFiles = filesBelow(protocolDirectory);
const problems: string[] = [];
for (const file of vendoredFiles) {
  if (!(file in protocolPin.files)) {
    problems.push(`  not in the pin: ${file}`);
  }
}
for (const file of pinnedFiles) {
  if (!vendoredFiles.includes(file)) {
    problems.push(`  missing:        ${file}`);
  } else if (
    checksumOf(join(protocolDirectory, file)) !== protocolPin.files[file]
  ) {
    problems.push(`  changed:        ${file}`);
  }
}
if (problems.length > 0) {
  fail(
    "The vendored contracts/webview-chat-protocol/ does not match its recorded pin.\n" +
      `  upstream: ${protocolPin.path} at ${protocolPin.commit}\n` +
      problems.join("\n") +
      "\nThe vendored protocol is upstream input, not a source file. Refresh it from a " +
      "reviewed upstream commit and update contracts/webview-chat-protocol.pin.json in the same change.",
  );
}

console.log(
  "contract:verify: contracts/nevabridge-v1.yaml is unchanged since it was pinned at " +
    `${pin.commit.slice(0, 8)}, and contracts/webview-chat-protocol/ since ` +
    `${protocolPin.commit.slice(0, 8)}.`,
);
