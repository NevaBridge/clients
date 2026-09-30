# webview-chat page (pinned build)

The NevaBridge chat page that desktop hosts show in a web view. Both host packages ship this folder as
the page they load when no web chat address is configured: `NevaBridge.Wpf` copies it to the
application's output folder, and `@nevabridge/electron` includes it in its `dist` folder.

This revision contains the plain HTML prototype, not a pinned copy of the deployed React page.
Use these files with the host bridge from the same tested revision. Both desktop bridges preserve
the deployed page's string request ids and this prototype's numeric ids.

## Bundle or host these files

No npm package or JavaScript build is needed for this copy. Take `index.html`, `chat.js`, `chat.css`
and `nevabridge-mark.svg` together; preserve their names and relative paths.

- **WPF bundle:** the `NevaBridge.Wpf` project copies the files into your application's output
  folder. Include the whole `webview-chat` folder in your installer and set `WebChatUrl` to null.
- **Your own server:** copy the files to an HTTPS directory, with normal HTML, JavaScript and CSS
  MIME types. Set `WebChatUrl` to the full `index.html` URL.
- **NevaBridge-hosted UI:** use
  `https://chat.app.nevabridge.com/v1/`, or `https://chat.app.sandbox.nevabridge.com/v1/` for sandbox.
  `/v1/` receives updates within protocol major 1; `/latest/` can move to a newer protocol major.

The [WPF guide](../wpf/README.md) gives the application and database setup for all three choices.
The page needs a desktop host bridge; serving it does not make it a standalone browser chat.
Never put an API key in these files. Bundling or self-hosting fixes the UI files until you update
them, but API compatibility, service availability and security updates still need attention.

## Protocol version 1

The page talks only to its host, through `window.chrome.webview` (WebView2) or `window.nevabridgeHost`
(the Electron preload). Every message is a JSON string: a request carries an `id` and a `type`, and the
answer echoes the `id` with `{"ok": true, "result": ...}` or
`{"ok": false, "error": {"name", "message", "status", "retryable"}}`.

| Request                                           | Meaning                                                                                     |
| ------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| `startConversation` (`text`, optional `category`) | Start a conversation with the first message.                                                |
| `appendMessage` (`conversationId`, `text`)        | Continue a conversation this page started.                                                  |
| `submitReport` (`conversationId`, `reportId`)     | Submit a report of a conversation this page started.                                        |
| `getFeatures`                                     | The tenant's effective features, such as `chatAttachments`.                                 |
| `attachFile` (`conversationId`)                   | The host shows its file picker and uploads the file; the result is the attachment, or null. |
