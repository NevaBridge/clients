import {join} from "node:path";
import {app, safeStorage} from "electron";
import {
  createSafeStorageSettingsStore,
  environmentSettingsProvider,
  NevaBridgeSettingsError,
  type NevaBridgeSettingsProvider,
  type SafeStorageSettingsStore,
} from "@nevabridge/electron";

/**
 * Step 1 in your application: provide the NevaBridge settings (API key, product id, API base URL,
 * web chat URL). Pick the one source that matches how your application keeps configuration.
 *
 * This example lets you choose with NEVABRIDGE_SETTINGS_SOURCE:
 * - `environment` (default): NEVABRIDGE_API_KEY and NEVABRIDGE_PRODUCT_ID, to try the chat.
 * - `safe-storage`: encrypted with Electron's safeStorage, saved once with the NevaBridge settings window.
 */
export const SETTINGS_SOURCE_VARIABLE = "NEVABRIDGE_SETTINGS_SOURCE";

export function settingsSourceName(): string {
  return (
    process.env[SETTINGS_SOURCE_VARIABLE]?.trim().toLowerCase() || "environment"
  );
}

export function savedSettingsStore(): SafeStorageSettingsStore {
  return createSafeStorageSettingsStore({
    safeStorage,
    filePath: join(app.getPath("userData"), "nevabridge-settings.bin"),
  });
}

export function createSettingsProvider(): NevaBridgeSettingsProvider {
  switch (settingsSourceName()) {
    case "environment":
      return environmentSettingsProvider();
    case "safe-storage":
      return savedSettingsStore();
    default:
      throw new NevaBridgeSettingsError(
        `${SETTINGS_SOURCE_VARIABLE} is '${settingsSourceName()}'. Use environment or safe-storage.`,
      );
  }
}
