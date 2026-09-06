import assert from "node:assert/strict";
import {createRequire} from "node:module";

const esm = await import("../dist/esm/index.js");
const require = createRequire(import.meta.url);
const commonjs = require("../dist/cjs/index.cjs");

for (const loadedPackage of [esm, commonjs]) {
  assert.equal(typeof loadedPackage.NevaBridgeClient, "function");
  assert.equal(typeof loadedPackage.NevaBridgeApiError, "function");
  assert.equal(loadedPackage.ConversationStatus.InProgress, "in_progress");
}
