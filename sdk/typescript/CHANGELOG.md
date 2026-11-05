# Changelog

## Unreleased

### Added

- `fetch` in the client configuration sends every request through the given implementation.
- `client.json` offers every operation that answers with a body and resolves to the API's JSON, unchanged, for
  code that relays NevaBridge answers.

### Changed

- `X-Actor-Name` carries a display name of printable ASCII without `%` as it is. Other names stay percent-encoded.
  NevaBridge decodes both, so the stored name does not change.

## 0.1.0

The vendored contract moves to the revision that runs Generic Intake turns in the background.

### Breaking changes

- Generic Intake conversations run through `startIntakeConversation`, `startIntakeTurn` and the turn events. A turn
  answers with a turn id, not with the reply: follow it with `waitForIntakeTurn` or `getIntakeTurnEvents`, then read
  the conversation with `getConversation`. `appendMessage` fails with status 400 for a Generic Intake conversation.
- `WriterModelKey` is a plain string. The writer models come from the NevaBridge model catalog, which changes without
  a contract change, so the exported `WriterModelKey` constant and the `WriterModelKeyValue` type are removed. Pass
  the key as a string. A key the catalog does not hold, or that your product does not enable, fails with status 400.
- Starting or continuing a conversation of an archived product, or of one being deleted, fails with status 409 and
  the error `ProductArchived` or `ProductPurging`.

### Added

- `startIntakeConversation`, `startIntakeTurn`, `getIntakeTurnEvents` and `listIntakeConversations` for Generic
  Intake, with `waitForIntakeTurn`, which follows a turn to its end, and `intakeTurnRunningId`, which reads the
  running turn from a refused turn start.
- `getFeatures`, which reports the features NevaBridge has enabled for your tenant.
- `getConversation` returns `revision` and, while a Generic Intake turn runs, `activeTurnId`.
- New report fields and values from the contract, such as `statusReason` and the `intake_record` category.
