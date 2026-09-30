# NevaBridge Electron example

A small Electron application that embeds the NevaBridge web chat with the
[`@nevabridge/electron`](../../widgets/electron) package. Run it to see the chat working against your
NevaBridge sandbox, then read the four files listed under [Where to look](#where-to-look) to do the same
in your own application.

The example is a stand-in business app ("Contoso Billing"). The button with the NevaBridge mark opens
the chat; so does **Help > NevaBridge chat**.

## 1. Prerequisites

- macOS, Windows or Linux with [Bun](https://bun.sh) 1.3 or later.
- A NevaBridge **sandbox API key** and **product id**. Create a key in NevaBridge under Setup > API.

Install the dependencies once, from the root of the clients repository. This also downloads Electron:

```bash
bun install
```

## 2. Where the API key goes

The example reads its settings from one of two sources, chosen with the environment variable
`NEVABRIDGE_SETTINGS_SOURCE`. Start with the first.

### Try it out: environment variables (default)

macOS and Linux:

```bash
export NEVABRIDGE_API_KEY="<your sandbox API key>"
export NEVABRIDGE_PRODUCT_ID="<your product id>"
```

Windows (PowerShell):

```powershell
$env:NEVABRIDGE_API_KEY = "<your sandbox API key>"
$env:NEVABRIDGE_PRODUCT_ID = "<your product id>"
```

The sandbox is used unless you also set `NEVABRIDGE_API_BASE_URL`. `NEVABRIDGE_WEBCHAT_URL` is optional;
without it the page shipped in the package is used. When you are done trying, revoke the key in
NevaBridge.

### Recommended: Electron safeStorage

1. Start the example with this source (step 3):

   ```bash
   export NEVABRIDGE_SETTINGS_SOURCE=safe-storage
   ```

2. Open **File > NevaBridge settings**, enter the API key, the product id and the environment, and save.
   The settings are stored encrypted in the app's user data folder; the macOS Keychain, Windows DPAPI or
   the Linux secret service protects the encryption key.
3. Open the chat.

**Remove saved settings** in the same window deletes them again.

## 3. Build and run

In the folder of this README:

```bash
bun run start
```

This builds the SDK, the package and the example, then starts it. The Contoso Billing window opens.
Click the NevaBridge mark, describe a problem and press Enter: the assistant answers with a follow-up
question and a draft report appears under "Report so far".

## Where to look

Everything you do in your own application is in one file per step, in `src`:

| In your application you...       | Example file                                                     |
| -------------------------------- | ---------------------------------------------------------------- |
| Provide the NevaBridge settings  | `settings-source.ts` (the settings window: `settings-window.ts`) |
| Tell NevaBridge who is reporting | `current-reporter.ts`                                            |
| Open the chat                    | `open-chat.ts`                                                   |
| Explain why the chat cannot open | `chat-errors.ts`                                                 |

`main.ts` is the stand-in application. The [package guide](../../widgets/electron/README.md) explains the
page sources, feature gates and the security model.

## Troubleshooting

| Symptom                                                                            | Cause                                                                                                                                                                                                             |
| ---------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| "The environment variable NEVABRIDGE_API_KEY is not set."                          | Set the variables in the terminal you run `bun run start` from.                                                                                                                                                   |
| "... must use https ..."                                                           | An address in the settings uses plain `http`. Only `localhost` may.                                                                                                                                               |
| Linux: "The SUID sandbox helper binary was found, but is not configured correctly" | Your distribution restricts Chromium's sandbox. Either run `sudo chown root:root` and `sudo chmod 4755` on the `chrome-sandbox` file the message names, or, for local testing only, `bun run start --no-sandbox`. |
| "Electron failed to install correctly"                                             | Electron's download was skipped. In this folder, run `node node_modules/electron/install.js`.                                                                                                                     |
| The chat says NevaBridge refused the request (401 or 403)                          | The key was revoked, belongs to another environment, or the product id is wrong.                                                                                                                                  |
| No "Attach file" button                                                            | Attachments are not enabled for your organization; ask NevaBridge.                                                                                                                                                |

`bun run start` clears `ELECTRON_RUN_AS_NODE`, which terminals inside editors built on Electron
(such as VS Code) set and which would otherwise start Electron without its APIs.
