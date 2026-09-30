import {dialog, type BrowserWindow} from "electron";
import {NevaBridgeSettingsError} from "@nevabridge/electron";
import {settingsSourceName} from "./settings-source.js";

/**
 * Step 4 in your application: explain why the chat cannot open. Problems during a conversation
 * (no credits, NevaBridge busy, no network) are shown inside the chat itself.
 */
export async function showChatProblem(
  parent: BrowserWindow,
  error: unknown,
): Promise<void> {
  const detail =
    error instanceof NevaBridgeSettingsError
      ? `The NevaBridge settings could not be read (source: ${settingsSourceName()}).\n\n${error.message}`
      : `The NevaBridge chat could not be opened.\n\n${error instanceof Error ? error.message : String(error)}`;
  await dialog.showMessageBox(parent, {
    type: "warning",
    title: "NevaBridge",
    message: "NevaBridge",
    detail,
  });
}
