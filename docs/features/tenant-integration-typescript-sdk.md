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

These five are the whole supported public API.

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

## Writer model and response language

The first message may set `writerModelKey` to choose the model that writes the report.
The conversation keeps that model, and a later message that names a different one fails with status 400.
Summaries and details return the chosen `writerModelKey`. Conversations created before the model was fixed per conversation have none.
Each message may set `responseLanguage` to a BCP 47 tag. A valid tag overrides automatic language detection for that turn, and the API ignores an invalid one.

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

The server currently finalizes only the first in-progress report this way, and never finalizes conversations authenticated as staff.

## Known limitations and missing features

- Publishing to npm and running sandbox smoke tests are explicit release steps. Building a branch never publishes.
- Consuming applications adopt the SDK in their own repositories.
- The .NET SDK design specification in the service repository covers the .NET SDK, NuGet publication, and compatibility with .NET consumers.

## Related

- `docs/adr/0001_typescript_sdk_generation.md` for generation, packaging and the checks that guard them
- `AGENTS.md` for the quality gate and its prerequisites
- `contracts/nevabridge-v1.yaml`
