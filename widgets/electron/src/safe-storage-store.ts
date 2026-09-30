import {randomUUID} from "node:crypto";
import {mkdir, readFile, rename, rm, stat, writeFile} from "node:fs/promises";
import {dirname} from "node:path";
import {
  deserializeSettings,
  NevaBridgeSettingsError,
  serializeSettings,
  type NevaBridgeSettings,
  type NevaBridgeSettingsProvider,
} from "./settings.js";

/** The part of Electron's `safeStorage` this store uses. */
export interface SafeStorageLike {
  isEncryptionAvailable(): boolean;
  encryptString(plainText: string): Buffer;
  decryptString(encrypted: Buffer): string;
}

export interface SafeStorageSettingsStore extends NevaBridgeSettingsProvider {
  exists(): Promise<boolean>;
  save(settings: NevaBridgeSettings): Promise<void>;
  remove(): Promise<void>;
}

/**
 * Keeps the NevaBridge settings encrypted with Electron's `safeStorage`: the macOS Keychain,
 * Windows DPAPI or the Linux secret service protect the encryption key. All four settings are
 * stored together, so the web chat address travels with the key.
 *
 * Pass `safeStorage` from `electron` and a file in the app's user data folder, for example
 * `join(app.getPath("userData"), "nevabridge-settings.bin")`. Only the signed-in operating-system
 * user can decrypt it; any program they run as that user, including the app, can.
 */
export function createSafeStorageSettingsStore(options: {
  readonly safeStorage: SafeStorageLike;
  readonly filePath: string;
}): SafeStorageSettingsStore {
  const requireEncryption = (): void => {
    if (!options.safeStorage.isEncryptionAvailable()) {
      throw new NevaBridgeSettingsError(
        "This computer offers no encryption to Electron's safeStorage (on Linux: no secret service is running).",
      );
    }
  };

  return {
    async exists() {
      try {
        return (await stat(options.filePath)).isFile();
      } catch {
        return false;
      }
    },

    async getSettings() {
      let encrypted: Buffer;
      try {
        encrypted = await readFile(options.filePath);
      } catch (error: unknown) {
        throw new NevaBridgeSettingsError(
          `No NevaBridge settings are saved in ${options.filePath}.`,
          {
            cause: error,
          },
        );
      }
      requireEncryption();
      return deserializeSettings(options.safeStorage.decryptString(encrypted));
    },

    async save(settings) {
      requireEncryption();
      const encrypted = options.safeStorage.encryptString(
        serializeSettings(settings),
      );
      await mkdir(dirname(options.filePath), {recursive: true});
      // Replace the saved settings only once the new ones are fully written, so an interrupted
      // save never leaves a half-written file where the working key used to be.
      const temporary = `${options.filePath}.${randomUUID()}.tmp`;
      try {
        await writeFile(temporary, encrypted, {mode: 0o600, flag: "wx"});
        await rename(temporary, options.filePath);
      } catch (error: unknown) {
        await rm(temporary, {force: true});
        throw error;
      }
    },

    async remove() {
      await rm(options.filePath, {force: true});
    },
  };
}
