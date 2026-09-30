# @nevabridge/electron

Embed the NevaBridge web chat in an Electron application. Your users ask a question, suggest an idea
or describe a problem; the NevaBridge assistant asks a few follow-up questions and writes a structured
report for your team.

A runnable example that uses this package lives in
[`samples/electron-desktop`](../../samples/electron-desktop) of the NevaBridge clients repository.

## Requirements

- Electron 32 or later, Node.js 22.12 or later for the main process.
- A NevaBridge API key and product id. Create a key in NevaBridge under Setup > API.

## Install

Build and pack locally from the repository root. These commands do not publish to npm:

```bash
bun install --frozen-lockfile
bun run --filter @nevabridge/sdk build
bun run --filter @nevabridge/electron build
mkdir -p out
cd sdk/typescript
bun pm pack --destination ../../out
cd ../../widgets/electron
bun pm pack --destination ../../out
cd ../..
```

From your Electron application's directory, install both resulting tarballs together. Replace
`<clients-checkout>` with the location of the repository you built:

```bash
npm install <clients-checkout>/out/nevabridge-sdk-0.0.1.tgz <clients-checkout>/out/nevabridge-electron-0.1.0.tgz
```

Pin the repository revision and keep the tarballs in your build inputs or your own package feed.
Installing the SDK tarball alongside the host avoids depending on a published NevaBridge SDK.

## 1. Provide the settings

The chat needs four settings, supplied through a `NevaBridgeSettingsProvider`:

| Setting      | Meaning                                                                                  |
| ------------ | ---------------------------------------------------------------------------------------- |
| `apiBaseUrl` | The NevaBridge API: `SANDBOX_API_BASE_URL` or `PRODUCTION_API_BASE_URL`.                 |
| `productId`  | The NevaBridge product the reports belong to.                                            |
| `apiKey`     | The tenant's API key. Not enumerable, so logging the settings object does not reveal it. |
| `webChatUrl` | Where the web chat page is loaded from. Absent uses the page shipped in this package.    |

Addresses must use `https`; plain `http` is accepted only for `localhost`, for local testing. Build
settings with `createSettings(...)`, which checks them, or use a ready-made provider:

| Provider                                                  | Use it for                                                                                                                                                                                        |
| --------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `environmentSettingsProvider()`                           | Trying the chat: `NEVABRIDGE_API_KEY` and `NEVABRIDGE_PRODUCT_ID`, optional `NEVABRIDGE_API_BASE_URL` (default: sandbox) and `NEVABRIDGE_WEBCHAT_URL`.                                            |
| `createSafeStorageSettingsStore({safeStorage, filePath})` | The recommended store: all four settings encrypted with Electron's `safeStorage` (macOS Keychain, Windows DPAPI, Linux secret service). Save once with `save(settings)`; `remove()` deletes them. |
| Your own implementation                                   | Any object with `getSettings(): Promise<NevaBridgeSettings>`.                                                                                                                                     |

```ts
import {app, safeStorage} from "electron";
import {join} from "node:path";
import {createSafeStorageSettingsStore} from "@nevabridge/electron";

const store = createSafeStorageSettingsStore({
  safeStorage,
  filePath: join(app.getPath("userData"), "nevabridge-settings.bin"),
});
const settings = await store.getSettings();
```

Missing or unusable settings raise `NevaBridgeSettingsError`, whose message names the setting.

## 2. Tell NevaBridge who is reporting

```ts
const reporter = {id: currentUser.id, displayName: currentUser.name};
```

The id becomes the report's reporterId, so keep it stable. Optional `roles` (`anonymous`, `customer`,
`tenant`) decide which knowledge-base documents the assistant may draw on.

## 3. Open the chat

In the main process:

```ts
import {createSdkConversations, openWebChatWindow} from "@nevabridge/electron";

await openWebChatWindow({
  conversations: createSdkConversations(settings),
  settings,
  reporter,
  applicationContext: {screen: "Invoices", version: app.getVersion()},
  parent: mainWindow,
});
```

The chat opens in its own window with context isolation and the sandbox on and no Node integration,
in an isolated session. Only this package's preload talks to the page; every API call runs in the main
process through `@nevabridge/sdk`.

**If your build bundles the main process** (esbuild, webpack, electron-vite), mark
`@nevabridge/electron` as external so it finds its preload and page files. If you cannot, pass
`packageDirectory` with the location of this package's `dist` folder.

## Where the web chat page comes from

`webChatUrl` decides:

- **Absent:** the copy shipped in this package, served from disk under
  `https://webchat.nevabridge.example`. Nothing is loaded over the network.
- **NevaBridge's server, `/v1/`:** the newest page that speaks protocol version 1; updates reach your
  users without a release of your application.
- **NevaBridge's server, `/latest/`:** always the newest page, for example for a staging environment.
- **Your own server:** host the page yourself.

The page never receives the API key. The bridge treats it as untrusted: it answers only the page's own
origin, offers only the chat operations, sets the product, reporter and context itself, and continues
only conversations the page started. Links such as a delivered ticket open in the default browser.

## Feature gates and attachments

The chat reads your tenant's features when it opens; the page shows "Attach file" only when file
attachments (`chatAttachments`) are enabled. The user picks a file in the operating system's dialog and
the main process uploads it. Pass `allowAttachments: false` to never offer attachments.

## Security model

Every settings source puts the API key on the user's computer, because the application calls
NevaBridge itself. The key is organization-wide: whoever obtains it can read every conversation of
every user and act as any reporter. That is acceptable when every user of your application may see
everything your organization reports. Otherwise, move the NevaBridge calls into a small service your
users cannot inspect, and pass `openWebChatWindow` a `conversations` object that calls that service.
