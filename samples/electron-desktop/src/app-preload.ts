import {contextBridge, ipcRenderer} from "electron";

// The example's own windows (not the NevaBridge chat) talk to the main process through these.
contextBridge.exposeInMainWorld("example", {
  openChat: (): Promise<void> => ipcRenderer.invoke("example:open-chat"),
  openSettings: (): Promise<void> =>
    ipcRenderer.invoke("example:open-settings"),
  loadSettings: (): Promise<unknown> =>
    ipcRenderer.invoke("example:load-settings"),
  saveSettings: (form: unknown): Promise<void> =>
    ipcRenderer.invoke("example:save-settings", form),
  removeSettings: (): Promise<void> =>
    ipcRenderer.invoke("example:remove-settings"),
});
