import {join} from "node:path";
import {BrowserWindow, ipcMain} from "electron";
import {
  createSettings,
  PRODUCTION_API_BASE_URL,
  SANDBOX_API_BASE_URL,
} from "@nevabridge/electron";
import {savedSettingsStore} from "./settings-source.js";

interface SettingsForm {
  readonly apiKey: string;
  readonly productId: string;
  readonly environment: "sandbox" | "production";
  readonly webChatUrl: string;
}

/** Saves the NevaBridge settings once, encrypted with Electron's safeStorage. */
export function registerSettingsHandlers(): void {
  ipcMain.handle("example:load-settings", async () => {
    const store = savedSettingsStore();
    if (!(await store.exists())) {
      return null;
    }
    const saved = await store.getSettings();
    // The saved key is never sent back to a window.
    return {
      productId: saved.productId,
      environment:
        saved.apiBaseUrl === PRODUCTION_API_BASE_URL ? "production" : "sandbox",
      webChatUrl: saved.webChatUrl ?? "",
      hasKey: true,
    };
  });

  ipcMain.handle(
    "example:save-settings",
    async (_event, form: SettingsForm) => {
      const store = savedSettingsStore();
      const previousKey = (await store.exists())
        ? (await store.getSettings()).apiKey
        : "";
      await store.save(
        createSettings({
          apiKey: form.apiKey || previousKey,
          productId: form.productId,
          apiBaseUrl:
            form.environment === "production"
              ? PRODUCTION_API_BASE_URL
              : SANDBOX_API_BASE_URL,
          webChatUrl: form.webChatUrl,
        }),
      );
    },
  );

  ipcMain.handle("example:remove-settings", async () => {
    await savedSettingsStore().remove();
  });
}

export async function openSettingsWindow(parent: BrowserWindow): Promise<void> {
  const window = new BrowserWindow({
    parent,
    modal: true,
    width: 520,
    height: 470,
    title: "NevaBridge settings",
    resizable: false,
    webPreferences: {
      preload: join(__dirname, "app-preload.cjs"),
      contextIsolation: true,
      sandbox: true,
      nodeIntegration: false,
    },
  });
  window.removeMenu();
  await window.loadFile(join(__dirname, "renderer", "settings.html"));
}
