# webview-chat bridge protocol

The webview-chat page never calls the NevaBridge API. It sends every request as a message to the
application hosting it, and the host's bridge makes the API calls with the tenant's API key. This
document is the contract between the page and every host bridge. The clients repository pins it,
together with the schemas and conformance cases next to it, the way it pins the API contract.

| File                                                                 | What it is                                                                                          |
| -------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------- |
| [`protocol.json`](protocol.json)                                     | `currentMajor`: the major the page speaks and the deployment serves at `/v{major}/` and `/latest/`. |
| [`v1/messages.schema.json`](v1/messages.schema.json)                 | JSON Schema of every request and response in version 1.                                             |
| [`v1/conformance/*.json`](v1/conformance/)                           | Conformance cases: request in, expected API calls and response out.                                 |
| [`v1/conformance-case.schema.json`](v1/conformance-case.schema.json) | JSON Schema of one conformance case.                                                                |

## Versions and addresses

A major version is this protocol. The page for major 1 is served at `/v1/`. `/latest/` serves the
newest major. Within a major every change is backwards compatible for hosts: a deployment may add
optional request fields the host can ignore, add result fields the page already tolerates, and add
**optional request types**, but never removes a field or changes a meaning. A host that predates an
optional request type answers it with `UnsupportedRequest`, as for any request it does not know,
and the page then falls back to what it did before that type existed. The messages schema marks
optional request types with `x-optional`. A change a conforming major-1 host would get wrong needs
major 2.

The page learns nothing about the host's version. A host that points at `/latest/` accepts that a
new major can arrive before the host supports it.

## Transport

Every message in both directions is one JSON **string** (not a structured object), so the same
serialization works in every web view.

| Host                                             | Page to host                                 | Host to page                                                                                                                       |
| ------------------------------------------------ | -------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| WebView2 (WPF, WinForms, WinUI, MAUI on Windows) | `window.chrome.webview.postMessage(message)` | `CoreWebView2.PostWebMessageAsString(message)`, received by the page as the `data` of a `message` event on `window.chrome.webview` |
| Electron                                         | `window.nevabridgeHost.postMessage(message)` | the listener the page passes to `window.nevabridgeHost.onMessage(listener)`                                                        |

The Electron preload exposes exactly this object through `contextBridge.exposeInMainWorld`:

```ts
interface NevaBridgeHost {
  /** Delivers one message from the page to the host. */
  postMessage(message: string): void;
  /** Registers a listener for messages from the host; returns a function that removes it. */
  onMessage(listener: (message: string) => void): () => void;
}
```

The page checks for `window.chrome.webview` first, then `window.nevabridgeHost`. When neither
exists, for example in a plain browser, the page says it runs inside an application and accepts no
input.

## Messages

A **request** carries an `id`, unique within the page session, and a `type`:

| `type`              | Fields                                                                           | Result (API schema)  | API calls                                                       |
| ------------------- | -------------------------------------------------------------------------------- | -------------------- | --------------------------------------------------------------- |
| `startConversation` | `text`, optional `category` (`bug_report`, `feature_request`, `support_request`) | `ConversationTurn`   | `startConversation`                                             |
| `appendMessage`     | `conversationId`, `text`                                                         | `ConversationTurn`   | `appendMessage`                                                 |
| `submitReport`      | `conversationId`, `reportId`                                                     | `SubmitReportResult` | `submitReport`                                                  |
| `getFeatures`       | none                                                                             | `Features`           | `getFeatures`                                                   |
| `attachFile`        | `conversationId`                                                                 | `Attachment`         | `requestAttachmentUpload`, the `PUT`, `confirmAttachmentUpload` |

Optional request types, for Generic Intake conversations (ADR 0054). A turn runs in the background,
and the page reads its events with one `getIntakeTurnEvents` request per poll:

| `type`                    | Fields                                                  | Result (API schema)         | API calls                 |
| ------------------------- | ------------------------------------------------------- | --------------------------- | ------------------------- |
| `startIntakeConversation` | `text`                                                  | `IntakeConversationStarted` | `startIntakeConversation` |
| `startIntakeTurn`         | `conversationId`, `text`, `expectedRevision`, `finish?` | `IntakeTurnStarted`         | `startIntakeTurn`         |
| `getIntakeTurnEvents`     | `turnId`, `after?`                                      | `IntakeTurnEvents`          | `getIntakeTurnEvents`     |
| `getConversation`         | `conversationId`                                        | `ConversationDetail`        | `getConversation`         |

`startIntakeTurn` sends `text` as the API's `content`, with `expectedRevision` and `finish`
unchanged. `getIntakeTurnEvents` sends `after` unchanged when the page gives one.

The host answers every request with exactly one **response** carrying the same `id`:

```json
{"id": "r1", "ok": true, "result": {}}
{"id": "r1", "ok": false, "error": {"name": "Forbidden", "message": "...", "status": 403, "retryable": false}}
```

A result is the API's JSON for the operation, unchanged. The schema names are components of
`api/openapi/nevabridge-v1.yaml`. The page ignores fields it does not know, and so must the host.

### What the host supplies

The page cannot set the product, the reporter or the application context. The host adds them:

- `startConversation` and `startIntakeConversation` go to the host's product, with `X-Actor-Id`
  (required), `X-Actor-Name` and `X-Actor-Roles` when the host has them, and the host's
  `applicationContext` in the body.
- `attachFile` sends `X-Actor-Id` to `requestAttachmentUpload`, which records the uploader.
- No other call carries an `X-Actor-*` header.

### Host rules

- Serve the page over https only, except plain http on a loopback address during development.
- Accept messages only from the page's own origin, and ignore any other.
- Accept `appendMessage`, `submitReport`, `attachFile`, `startIntakeTurn` and `getConversation`
  only for a conversation the page started in this session (the `conversationId` of a
  `startConversation` or `startIntakeConversation` result). Answer anything else with
  `UnknownConversation` and make no API call.
- Accept `getIntakeTurnEvents` only for a turn the host returned in this session: the `turnId` of a
  `startIntakeConversation` or `startIntakeTurn` result or of an `IntakeTurnRunning` error, or the
  `activeTurnId` of a `getConversation` result. Answer anything else with `UnknownTurn` and make no
  API call.
- Send `getIntakeTurnEvents` with the query `after` (when the page gave one) and then `wait=20`, and
  no `linger`. The page never chooses either.
- For `attachFile`, show the operating system's file picker. The file's bytes never pass through
  the page. Then run the three-step upload the API describes: request the upload, `PUT` exactly
  `sizeBytes` bytes to `uploadUrl` with `If-None-Match: *`, and confirm.
- Read `getFeatures` from `GET /v1/features` when the page asks. The page asks when the chat opens.

### Errors

When the API answered with an error body that has an `error` name, the response error carries that
name, its `message` verbatim and the HTTP `status`. Otherwise it carries one of the bridge names:

| `name`                | When                                                                                                     | `status`                    | `retryable` |
| --------------------- | -------------------------------------------------------------------------------------------------------- | --------------------------- | ----------- |
| `EdgeFailure`         | The API gateway answered without an error name (502 or 504). `message` is its `message` when it has one. | the HTTP status             | `true`      |
| `NetworkFailure`      | No HTTP response arrived.                                                                                | `null`                      | `true`      |
| `UploadFailed`        | The `PUT` to the presigned URL failed. The host does not confirm.                                        | the `PUT` status, or `null` | `true`      |
| `Cancelled`           | The user closed the file picker without choosing a file.                                                 | `null`                      | `false`     |
| `UnknownConversation` | The conversation was not started by the page in this session.                                            | `null`                      | `false`     |
| `UnknownTurn`         | The turn was not returned to the page in this session.                                                   | `null`                      | `false`     |
| `UnsupportedRequest`  | The message has an `id` but is not a valid request of this major.                                        | `null`                      | `false`     |

`retryable` for an API error is `true` for `ModelInvocationFailed` with `category` `throttled`,
`transient-unavailable`, `timed-out`, `output-unparseable` or `unknown`, and for status 408 or 429.
It is `false` for every other API error. The page may resend a retryable request with a new `id`.

When the API answers 409 `IntakeTurnRunning`, the error also carries the running turn's `turnId`, so
the page can read that turn instead of sending again. No other error carries `turnId`.

A host that cannot find an `id` in a message ignores the message.

The page stops waiting for a response after 2 minutes, or 30 minutes for `attachFile`, which
includes the user's file choice and the upload. It then treats the request as failed with the
page-side name `HostTimeout` (retryable) and ignores a response that arrives later. A host must
therefore answer within those times, and must not assume a late result reached the user.

## Feature gates

The page shows a gated control only when its feature is on in the `getFeatures` result:
`chatAttachments` shows the attach control. Staff can revoke a feature while a chat is open, so the
page also treats a `Forbidden` answer to a gated request as the feature being off: it hides the
control and says so. The page ignores feature keys it does not know.

## Conformance cases

Each file in `v1/conformance/` is one case: the host context, the conversations the page started
earlier, the turns the host returned earlier, the user's file choice for `attachFile`, the request, every API call a conforming host
makes (method, path, the listed headers with their exact values, and the exact JSON body) with
the answer the API gives, and the response the host must send. A host bridge passes a case when,
fed the request and the listed API answers, it makes exactly those calls in that order and sends
a response equal to the expected one. The `message` of a bridge-named error is free text and is
not compared; every other field is.

The NevaBridge build checks that every case validates against these schemas, that each API call
and answer matches the published API contract, and that each result validates against its API
schema. The page's own tests check that every request it sends validates against the messages
schema. A deployment serves only the major in `protocol.json`.
