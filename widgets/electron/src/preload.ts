import {contextBridge, ipcRenderer} from "electron";

const CHANNEL = "nevabridge:webchat";

// The web chat page gets exactly two functions: send a protocol message, and receive answers.
contextBridge.exposeInMainWorld("nevabridgeHost", {
  postMessage(message: unknown): void {
    if (typeof message === "string") {
      ipcRenderer.send(CHANNEL, message);
    }
  },
  onMessage(handler: (message: string) => void): void {
    ipcRenderer.on(CHANNEL, (_event, message: unknown) => {
      if (typeof message === "string") {
        handler(message);
      }
    });
  },
});
