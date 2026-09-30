import {join} from "node:path";
import {app, BrowserWindow, ipcMain, Menu} from "electron";
import {showChatProblem} from "./chat-errors.js";
import {openNevaBridgeChat} from "./open-chat.js";
import {
  openSettingsWindow,
  registerSettingsHandlers,
} from "./settings-window.js";

// A stand-in for your application's main window.
async function createMainWindow(): Promise<BrowserWindow> {
  const window = new BrowserWindow({
    width: 1000,
    height: 640,
    title: "Contoso Billing (NevaBridge Electron example)",
    webPreferences: {
      preload: join(__dirname, "app-preload.cjs"),
      contextIsolation: true,
      sandbox: true,
      nodeIntegration: false,
    },
  });
  await window.loadFile(join(__dirname, "renderer", "index.html"));
  return window;
}

async function openChat(window: BrowserWindow): Promise<void> {
  try {
    await openNevaBridgeChat(window, "Invoices");
  } catch (error: unknown) {
    await showChatProblem(window, error);
  }
}

app.whenReady().then(async () => {
  registerSettingsHandlers();
  const mainWindow = await createMainWindow();
  ipcMain.handle("example:open-chat", () => openChat(mainWindow));
  ipcMain.handle("example:open-settings", () => openSettingsWindow(mainWindow));

  Menu.setApplicationMenu(
    Menu.buildFromTemplate([
      ...(process.platform === "darwin" ? [{role: "appMenu" as const}] : []),
      {
        label: "File",
        submenu: [
          {
            label: "NevaBridge settings...",
            click: () => void openSettingsWindow(mainWindow),
          },
          {type: "separator"},
          {role: process.platform === "darwin" ? "close" : "quit"},
        ],
      },
      {
        label: "Help",
        submenu: [
          {label: "NevaBridge chat...", click: () => void openChat(mainWindow)},
        ],
      },
    ]),
  );
});

app.on("window-all-closed", () => app.quit());
