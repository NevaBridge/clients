import {afterEach, beforeEach, describe, expect, test} from "bun:test";
import {mkdtemp, readdir, readFile, rm} from "node:fs/promises";
import {tmpdir} from "node:os";
import {join} from "node:path";
import {
  createSafeStorageSettingsStore,
  type SafeStorageLike,
} from "../src/safe-storage-store.js";
import {createSettings, SANDBOX_API_BASE_URL} from "../src/settings.js";

const API_KEY = "sk_test_not_a_real_key";

/** Stands in for Electron's safeStorage: reversible, but never stores the plain text. */
function fakeSafeStorage(available = true): SafeStorageLike {
  return {
    isEncryptionAvailable: () => available,
    encryptString: (text) =>
      Buffer.from(
        Buffer.from(text, "utf8")
          .toString("base64")
          .split("")
          .reverse()
          .join(""),
      ),
    decryptString: (data) =>
      Buffer.from(
        data.toString().split("").reverse().join(""),
        "base64",
      ).toString("utf8"),
  };
}

let directory: string;

beforeEach(async () => {
  directory = await mkdtemp(join(tmpdir(), "nevabridge-settings-"));
});

afterEach(async () => {
  await rm(directory, {recursive: true, force: true});
});

describe("safeStorage settings store", () => {
  test("saves all four settings encrypted and reads them back", async () => {
    // Given
    const filePath = join(directory, "nevabridge-settings.bin");
    const store = createSafeStorageSettingsStore({
      safeStorage: fakeSafeStorage(),
      filePath,
    });
    const settings = createSettings({
      apiBaseUrl: SANDBOX_API_BASE_URL,
      productId: "product-1",
      apiKey: API_KEY,
      webChatUrl: "https://chat.nevabridge.com/v1/index.html",
    });

    // When
    await store.save(settings);
    const restored = await store.getSettings();

    // Then
    expect(await readdir(directory)).toEqual(["nevabridge-settings.bin"]);
    expect(await store.exists()).toBe(true);
    expect((await readFile(filePath)).toString()).not.toContain(API_KEY);
    expect(restored.apiKey).toBe(API_KEY);
    expect(restored.webChatUrl).toBe(
      "https://chat.nevabridge.com/v1/index.html",
    );
  });

  test("explains that nothing is saved yet", async () => {
    // Given
    const store = createSafeStorageSettingsStore({
      safeStorage: fakeSafeStorage(),
      filePath: join(directory, "missing.bin"),
    });

    // Then
    expect(await store.exists()).toBe(false);
    await expect(store.getSettings()).rejects.toThrow(
      "No NevaBridge settings are saved",
    );
  });

  test("refuses to save when the operating system offers no encryption", async () => {
    // Given
    const store = createSafeStorageSettingsStore({
      safeStorage: fakeSafeStorage(false),
      filePath: join(directory, "nevabridge-settings.bin"),
    });
    const settings = createSettings({
      apiBaseUrl: SANDBOX_API_BASE_URL,
      productId: "p",
      apiKey: API_KEY,
    });

    // Then
    await expect(store.save(settings)).rejects.toThrow("encryption");
  });

  test("removes the saved settings", async () => {
    // Given
    const store = createSafeStorageSettingsStore({
      safeStorage: fakeSafeStorage(),
      filePath: join(directory, "nevabridge-settings.bin"),
    });
    await store.save(
      createSettings({
        apiBaseUrl: SANDBOX_API_BASE_URL,
        productId: "p",
        apiKey: API_KEY,
      }),
    );

    // When
    await store.remove();

    // Then
    expect(await store.exists()).toBe(false);
  });
});
