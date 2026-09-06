# NevaBridge TypeScript SDK

`@nevabridge/sdk` is the official Node.js client for the supported NevaBridge Tenant Integration API. It targets Node.js 22 or newer and provides ESM and CommonJS entry points.

## Install

```bash
bun add @nevabridge/sdk
```

## Create a client

```typescript
import {NevaBridgeClient} from "@nevabridge/sdk";

const client = new NevaBridgeClient({
  tokenProvider: () => process.env.NEVABRIDGE_API_KEY ?? "",
});
```

The token provider runs for every operation, so the application can return a refreshed credential without recreating the client. Use `baseUrl` to select a non-production endpoint. Never expose the API key to browser code.

Pass `actorName` as the raw human-readable display name. The SDK percent-encodes it for the `X-Actor-Name` header, including non-ASCII names.

Pass every knowledge audience that applies through `actorRoles`. Roles are independent, so an authenticated customer who may also use public knowledge sends both `"anonymous"` and `"customer"`. The SDK sends the resulting `X-Actor-Roles` header only when starting the conversation.

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

Every method also accepts `signal: AbortSignal` for cancellation. Non-successful responses throw `NevaBridgeApiError`, which exposes `status`, `headers`, and safely parsed `detail`.

## Submit behavior

Call `submitReport` when the user explicitly finishes. Do not implement an inactivity timer or retry-based auto-submit. NevaBridge automatically finalizes an in-progress M2M conversation after 60 minutes without a turn, normally within 60-70 minutes after the last turn because the server sweep runs every 10 minutes.

Every successful turn resets the server inactivity window. Manual submit disarms the window. Abandonment uses normal finalization and connector delivery. Auto-finalized reports expose `submissionOrigin: "auto_abandoned"` when read. The fallback currently targets the first in-progress report. Staff-authenticated conversations do not receive this fallback.

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

The SDK exposes no inactivity setting, timer, or automatic submit function.

## Supported operations

- `startConversation`
- `listConversations`
- `getConversation`
- `appendMessage`
- `submitReport`

Only these operations are part of the supported public contract.
