import {openAsBlob} from "node:fs";
import {readFile} from "node:fs/promises";
import {extname, join, normalize, sep} from "node:path";
import {
  BrowserWindow,
  dialog,
  ipcMain,
  net,
  session,
  shell,
  type BrowserWindowConstructorOptions,
  type IpcMainEvent,
  type Session,
} from "electron";
import {createWebChatBridge} from "./bridge.js";
import type {
  NevaBridgeConversations,
  PickedFile,
  Reporter,
} from "./conversations.js";
import {originOf} from "./secure-address.js";
import type {NevaBridgeSettings} from "./settings.js";

/** The origin under which the page shipped with this package is served; nothing is fetched from it. */
export const BUNDLED_PAGE_ORIGIN = "https://webchat.nevabridge.example";

const CHANNEL = "nevabridge:webchat";
const SESSION_PARTITION = "persist:nevabridge-webchat";
const BUNDLED_PAGE_HOST = new URL(BUNDLED_PAGE_ORIGIN).host;
const ATTACHMENT_EXTENSIONS = [
  "png",
  "jpg",
  "jpeg",
  "gif",
  "webp",
  "mp4",
  "mov",
  "webm",
  "txt",
  "log",
  "md",
  "csv",
  "json",
  "xml",
  "yaml",
  "yml",
];
const CONTENT_TYPES: Readonly<Record<string, string>> = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".svg": "image/svg+xml",
  ".png": "image/png",
  ".woff2": "font/woff2",
};

export interface OpenWebChatOptions {
  readonly conversations: NevaBridgeConversations;
  readonly settings: NevaBridgeSettings;
  readonly reporter: Reporter;
  readonly applicationContext?: Readonly<Record<string, string>>;
  readonly parent?: BrowserWindow;
  /** Whether the page may offer file attachments (when the tenant has the feature). Default true. */
  readonly allowAttachments?: boolean;
  /** Size, title and similar; web preferences are fixed by this package. */
  readonly windowOptions?: Omit<
    BrowserWindowConstructorOptions,
    "webPreferences"
  >;
  /**
   * Where this package's `dist` files are. Only needed when your build bundles
   * `@nevabridge/electron` into your own main-process file; keep it external instead if you can.
   */
  readonly packageDirectory?: string;
}

/**
 * Opens the NevaBridge web chat in its own window. The page comes from `settings.webChatUrl`, or
 * ships with this package when that is not set. The window runs in an isolated session with
 * context isolation and the sandbox on and no Node integration; only this package's preload talks
 * to it, and every API call runs in the main process.
 */
export async function openWebChatWindow(
  options: OpenWebChatOptions,
): Promise<BrowserWindow> {
  const pageUrl =
    options.settings.webChatUrl ?? `${BUNDLED_PAGE_ORIGIN}/index.html`;
  const pageOrigin = originOf(pageUrl);
  const chatSession = session.fromPartition(SESSION_PARTITION);
  const packageDirectory = options.packageDirectory ?? __dirname;
  if (options.settings.webChatUrl === undefined) {
    serveBundledPage(chatSession, join(packageDirectory, "webview-chat"));
  }

  const window = new BrowserWindow({
    width: 460,
    height: 640,
    title: "NevaBridge",
    ...options.windowOptions,
    parent: options.parent,
    webPreferences: {
      session: chatSession,
      preload: join(packageDirectory, "preload.cjs"),
      contextIsolation: true,
      sandbox: true,
      nodeIntegration: false,
      webSecurity: true,
    },
  });

  window.removeMenu();

  const bridge = createWebChatBridge({
    conversations: options.conversations,
    productId: options.settings.productId,
    reporter: options.reporter,
    applicationContext: options.applicationContext,
    pageUrl,
    pickFile:
      options.allowAttachments === false ? undefined : () => pickFile(window),
  });

  // Only messages from this window's own page reach the bridge; the bridge checks the origin too.
  const relay = (event: IpcMainEvent, message: unknown): void => {
    if (
      event.sender !== window.webContents ||
      typeof message !== "string" ||
      !event.senderFrame
    ) {
      return;
    }
    void bridge.handle(event.senderFrame.url, message).then((response) => {
      if (response !== null && !window.isDestroyed()) {
        event.sender.send(CHANNEL, response);
      }
    });
  };
  ipcMain.on(CHANNEL, relay);
  window.on("closed", () => ipcMain.removeListener(CHANNEL, relay));

  const keepOnPage = (event: {preventDefault(): void}, url: string): void => {
    if (originOrNull(url) !== pageOrigin) {
      event.preventDefault();
    }
  };
  window.webContents.on("will-navigate", keepOnPage);
  window.webContents.on("will-redirect", keepOnPage);
  window.webContents.setWindowOpenHandler(({url}) => {
    // Links such as a delivered ticket open in the default browser, never inside the app.
    if (originOrNull(url)?.startsWith("https://")) {
      void shell.openExternal(url);
    }
    return {action: "deny"};
  });

  try {
    await window.loadURL(pageUrl);
  } catch (error: unknown) {
    const reason = error instanceof Error ? error.message : String(error);
    await window.loadURL(
      `data:text/html;charset=utf-8,${encodeURIComponent(
        `<p style="font-family:system-ui;color:#b8342a;padding:24px">The NevaBridge chat could not be loaded from ${escapeHtml(pageUrl)} (${escapeHtml(reason)}).</p>`,
      )}`,
    );
  }
  return window;
}

const servedSessions = new WeakSet<Session>();

function serveBundledPage(chatSession: Session, pageDirectory: string): void {
  if (servedSessions.has(chatSession)) {
    return;
  }
  servedSessions.add(chatSession);
  chatSession.protocol.handle("https", async (request) => {
    const url = new URL(request.url);
    if (url.host !== BUNDLED_PAGE_HOST) {
      return net.fetch(request, {bypassCustomProtocolHandlers: true});
    }
    const relative = decodeURIComponent(
      url.pathname === "/" ? "/index.html" : url.pathname,
    );
    const file = normalize(join(pageDirectory, relative));
    if (!file.startsWith(pageDirectory + sep)) {
      return new Response("Not found", {status: 404});
    }
    try {
      return new Response(await readFile(file), {
        headers: {
          "content-type":
            CONTENT_TYPES[extname(file)] ?? "application/octet-stream",
        },
      });
    } catch {
      return new Response("Not found", {status: 404});
    }
  });
}

async function pickFile(window: BrowserWindow): Promise<PickedFile | null> {
  const choice = await dialog.showOpenDialog(window, {
    title: "Attach a file",
    properties: ["openFile"],
    filters: [
      {name: "Screenshots, videos and logs", extensions: ATTACHMENT_EXTENSIONS},
    ],
  });
  const path = choice.filePaths[0];
  if (choice.canceled || path === undefined) {
    return null;
  }
  // One snapshot of the file: its size and its contents come from the same Blob, and reading
  // fails if the file changes on disk before the upload.
  const blob = await openAsBlob(path);
  const name = path.split(/[\\/]/).pop() ?? path;
  return {name, sizeBytes: blob.size, read: async () => blob};
}

function originOrNull(url: string): string | null {
  try {
    return originOf(url);
  } catch {
    return null;
  }
}

function escapeHtml(text: string): string {
  return text.replace(/[&<>"']/g, (c) => `&#${c.charCodeAt(0)};`);
}
