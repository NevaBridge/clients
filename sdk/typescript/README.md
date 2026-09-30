# NevaBridge TypeScript SDK

`@nevabridge/sdk` is the official Node.js client for the supported NevaBridge Tenant Integration API. It requires Node.js 22 or newer and has ESM and CommonJS entry points.

## Install

```bash
bun add @nevabridge/sdk
```

## Get an API key

Create an API key in your NevaBridge account under [Setup > API](https://app.nevabridge.com/setup?section=api). The key grants access to your whole tenant, so keep it on your server in a secret store or an environment variable. Never commit it.

## Create a client

```typescript
import {NevaBridgeClient} from "@nevabridge/sdk";

const client = new NevaBridgeClient({
  tokenProvider: () => process.env.NEVABRIDGE_API_KEY ?? "",
});
```

The token provider returns the API key. The client calls it before every operation, so your application can return a refreshed credential without creating a new client. Set `baseUrl` to use a non-production endpoint. Never expose the API key to browser code.

Pass the user's display name as `actorName`, unencoded. The SDK percent-encodes it for the `X-Actor-Name` header, which handles non-ASCII names.

Pass every knowledge audience that applies in `actorRoles`. Each role is a separate grant, so an authenticated customer who may also use public knowledge sends both `"anonymous"` and `"customer"`. The SDK sends the `X-Actor-Roles` header only when it starts a conversation.

## Start and continue a conversation

```typescript
const turn = await client.startConversation({
  productId: "product-2f6a1c58-51b7-4a0e-9d0e-63a8b0e5c111",
  actorId: "user-4821",
  actorName: "Dana",
  actorRoles: ["anonymous", "customer"],
  request: {
    userMessage: {content: "The checkout page is blank after I click Pay."},
    applicationContext: {environment: "production"},
  },
});

await client.appendMessage({
  conversationId: turn.userMessage.conversationId,
  request: {content: "It happens in Chrome and Safari."},
});
```

Set `writerModelKey` on the first message to choose the model that writes the report. The conversation keeps that model for its whole life. A later message may repeat the same key, but a different key fails with status 400. Summaries and details return the key as `writerModelKey`. Conversations created before the model was fixed per conversation have no key.

Set `responseLanguage` to a BCP 47 tag, such as `de-DE`, to choose the language of the reply for one turn. A valid tag overrides automatic language detection, and the API ignores an invalid one.

Every method accepts `signal: AbortSignal` for cancellation.

## Errors

When the API returns an error status, the method throws `NevaBridgeApiError`. The error has `status`, `headers`, and `detail`. `detail` is the parsed body when the response declares JSON and the body parses. Otherwise it is the body text, or `null` when the body is empty.

NevaBridge error bodies have an `error` field that names the error. When a model fails, the body is a `ModelInvocationError`. Its `category`, `provider`, `role`, and `catalogKey` say which model failed, at which step, and why. These statuses report model and dependency failures:

| Status | Meaning                                                                                                                                                                                                                       |
| ------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 408    | The model timed out.                                                                                                                                                                                                          |
| 409    | The model is permanently unavailable (`access-denied` or `model-retired`), or another caller is submitting the report.                                                                                                        |
| 422    | The model provider rejected the request (`request-rejected`), or NevaBridge found a configuration fault it cannot resolve (`ConfigurationError`).                                                                             |
| 424    | A service NevaBridge depends on failed. For a model, the category is `transient-unavailable`, `output-unparseable`, or `unknown`, and the request can be retried. Any other dependency failure is a `FailedDependency` error. |
| 429    | The model provider throttled the request.                                                                                                                                                                                     |

Statuses 502 and 504 come from the API gateway, not from NevaBridge. Their body is an `EdgeError`, which has a `message` and no `error` field. A 502 usually means the request ran past the gateway's 29-second limit.

## Submit behavior

Call `submitReport` when the user says they are finished. Do not build an inactivity timer or an automatic submit that retries. NevaBridge finalizes an in-progress machine-to-machine conversation itself after 60 minutes without a turn. The server checks for these every 10 minutes, so finalization usually happens 60 to 70 minutes after the last turn.

Every successful turn restarts the 60-minute window. A manual submit cancels it. An abandoned conversation goes through the same finalization and connector delivery as a submitted one. When you read an automatically finalized report, it has `submissionOrigin: "auto_abandoned"`. The server currently finalizes only the first in-progress report this way. Conversations authenticated as staff are never finalized automatically.

```typescript
for (const report of turn.reports) {
  if (report.status === "reporting_in_progress") {
    await client.submitReport({
      conversationId: report.conversationId,
      reportId: report.id,
    });
  }
}
```

The SDK has no inactivity setting, no timer, and no automatic submit function.

## Supported operations

- `startConversation`
- `listConversations`
- `getConversation`
- `appendMessage`
- `submitReport`

The supported public contract includes only these operations.
