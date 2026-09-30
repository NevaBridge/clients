import {app, type BrowserWindow} from "electron";
import {createSdkConversations, openWebChatWindow} from "@nevabridge/electron";
import {currentReporter} from "./current-reporter.js";
import {createSettingsProvider} from "./settings-source.js";

/**
 * Step 3 in your application: open the NevaBridge web chat. The page comes from the settings' web
 * chat URL, or ships with @nevabridge/electron when none is set. Every API call runs in the main
 * process; the page never sees the key.
 */
export async function openNevaBridgeChat(
  parent: BrowserWindow,
  screen: string,
): Promise<void> {
  const settings = await createSettingsProvider().getSettings();
  await openWebChatWindow({
    conversations: createSdkConversations(settings),
    settings,
    reporter: currentReporter(),
    // Anything that helps the support team reproduce the problem.
    applicationContext: {
      application: "NevaBridge Electron example",
      version: app.getVersion(),
      screen,
      os: `${process.platform} ${process.getSystemVersion()}`,
    },
    parent,
  });
}
