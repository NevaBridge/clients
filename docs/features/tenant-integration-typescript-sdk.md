# Tenant Integration TypeScript SDK

## Overview

`@nevabridge/sdk` is the official Node.js client for the supported NevaBridge Tenant Integration API.
Trusted tenant backends use it to run NevaBridge reporting conversations with typed requests and responses.
Consumers call a small handwritten client and never touch generated code or internal NevaBridge types.

[ADR 0001](../adr/0001_typescript_sdk_generation.md) records how the SDK is generated and packaged, and why.

## Operations

`NevaBridgeClient` has these operations:

- `startConversation`
- `listConversations`
- `getConversation`
- `appendMessage`
- `submitReport`
- `listAttachments`
- `requestAttachmentUpload`
- `confirmAttachmentUpload`
- `requestAttachmentDownloadUrl`
- `deleteAttachment`
- `startIntakeConversation`
- `startIntakeTurn`
- `getIntakeTurnEvents`
- `listIntakeConversations`
- `getFeatures`

These fifteen are the whole supported public API. Two functions build on them: `waitForIntakeTurn` follows a
Generic Intake turn to its end, and `intakeTurnRunningId` reads the running turn from a refused turn start.

## Authentication and endpoint

The constructor takes a token provider and an optional base URL.
The client calls the provider before every request, so a consumer can rotate credentials without creating a new client.
The base URL selects a non-production endpoint.

## Cancellation and errors

Each operation accepts an `AbortSignal`. Cancelling it throws the platform's `AbortError`.
When the API returns an error status, the operation throws `NevaBridgeApiError` with the HTTP status, the response headers, and the body as `detail`.
`detail` is parsed JSON when the response declares JSON and the body parses, and plain text otherwise.
A failed model call returns 408, 409, 422, 424 or 429 with a `ModelInvocationError` that names the failure category, the provider, the pipeline step and the model's catalog key.
Statuses 502 and 504 come from the API gateway with an `EdgeError` body, which has no `error` field.
The [SDK README](../../sdk/typescript/README.md#errors) explains each status.

## Audience roles

`startConversation` accepts an optional `actorRoles` list and sends it as the comma-separated `X-Actor-Roles` header.
The audience of a conversation cannot change after it starts, so later methods never send the header.
Callers send every role that applies, because each role is a separate grant.
An authenticated customer who may also use public knowledge sends `anonymous` and `customer`.
Roles only take effect for tenants with the knowledge base feature, where the assistant answers from the documents those roles may read.

## Writer model and response language

The first message may set `writerModelKey` to choose the model that writes the report.
The conversation keeps that model, and a later message that names a different one fails with status 400.
Summaries and details return the chosen `writerModelKey`. Conversations created before the model was fixed per conversation have none.
Each message may set `responseLanguage` to a BCP 47 tag. A valid tag overrides automatic language detection for that turn, and the API ignores an invalid one.

## Attachments

A reporter can attach images, videos and text files to a conversation for the engineer who handles the report. The assistant never reads them.
Attachments need the chat attachments feature, which NevaBridge enables per tenant. Without it, every attachment operation fails with status 403.

An upload takes three steps. `requestAttachmentUpload` admits the file by name and exact size and returns a presigned URL.
The consumer then sends the bytes to that URL with `PUT` and the header `If-None-Match: *`, because the SDK never handles file contents.
`confirmAttachmentUpload` then attaches the file.
A conversation holds at most 10 files of up to 100 MB each, the tenant's files share 5 GB, and the file name's extension decides whether the type is accepted.
Files can be added and deleted until the report is submitted, and they stay readable afterwards.
`listAttachments` lists confirmed files, `requestAttachmentDownloadUrl` returns a URL valid for 5 minutes, and `deleteAttachment` removes a file.
The [SDK README](../../sdk/typescript/README.md#attach-files) has a full upload example and every limit.

## Generic Intake

Generic Intake needs the Generic Intake feature, which NevaBridge enables per tenant; `getFeatures` reports it.
Its turns run in the background. `startIntakeConversation` and `startIntakeTurn` answer with a turn id, and the
turn's events (`status`, `text`, then exactly one `done` or `error`) are read with `getIntakeTurnEvents`, an
opaque cursor, a `wait` of 0 to 20 seconds and a `linger` of 0 to 3000 milliseconds.

The generator merges the members of the contract's event union into one interface, so the client converts each event
into a union of the generated member types and checks its fields, rejecting a known type whose fields break the
contract. An event of a type the SDK does not know arrives as `{type: "unknown", eventType, payload}`, with the wire type
in `eventType` and the event as sent in `payload`, so a `switch` on `type` still narrows the union. The contract asks
clients to ignore such events.

`waitForIntakeTurn` polls from the first event, passes each cursor on, and calls `onEvent` for every event in order.
It resolves with the new `revision` on `done` and throws `NevaBridgeApiError` on `error`, with the failure's
`statusCode` as `status` and its body as `detail`. It retries a poll that failed with a network error (including a response that breaks off mid-body) or a gateway
502 or 504 up to three times, 500 milliseconds apart, because reading again with the same cursor is safe. Any other
failure, and a fourth failed poll in a row, throws.

After `done` the consumer reads `getConversation`, which is authoritative and also returns `revision` and, while a
turn runs, `activeTurnId`. Each further turn sends that `revision` as `expectedRevision`. A turn start while another
turn runs fails with 409 `IntakeTurnRunning`; `intakeTurnRunningId` returns that turn's id.

## Types and compatibility

The request and response types match the OpenAPI contract.
When the API adds an enum value this SDK version does not know, the value arrives as its raw string and still type-checks, and editors still complete the known values.
The package runs on Node.js 22 or newer, has no runtime dependencies, and works from both ESM and CommonJS, with TypeScript declarations for each.

## Submit lifecycle

Consumers call `submitReport` when the user says they are finished.
They must not build an inactivity timer or an automatic submit that retries.
NevaBridge finalizes abandoned conversations itself.
Every successful turn restarts a 60-minute window, and a sweep runs every 10 minutes, so finalization usually happens 60 to 70 minutes after the last turn.
A manual submit cancels the window. An abandoned conversation goes through the same finalization and connector delivery as a submitted one, and its report records `submissionOrigin: "auto_abandoned"`.
The assistant can also submit a report during a turn when it judges it complete, which records `submissionOrigin: "auto_assistant"`.

The server currently finalizes only the first in-progress report this way, and never finalizes conversations authenticated as staff.

## Known limitations and missing features

- Publishing to npm and running sandbox smoke tests are explicit release steps. Building a branch never publishes.
- Consuming applications adopt the SDK in their own repositories.
- The .NET SDK design specification in the service repository covers the .NET SDK, NuGet publication, and compatibility with .NET consumers.

## Related

- `docs/adr/0001_typescript_sdk_generation.md` for generation, packaging and the checks that guard them
- `AGENTS.md` for the quality gate and its prerequisites
- `contracts/nevabridge-v1.yaml`
