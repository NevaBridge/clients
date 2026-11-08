# Changelog

## Unreleased

The bridge now follows version 1 of the webview chat bridge protocol exactly and passes every
conformance case of the pinned protocol.

### Added

- The optional request `deleteAttachment`, which removes a file the page attached, for a
  conversation the page started; the hosted page offers removal once a host answers it. A
  `NevaBridgeConversations` implementation of your own needs `deleteAttachment` too;
  `createSdkConversations` has it.
- The Generic Intake requests `startIntakeConversation`, `startIntakeTurn`, `getIntakeTurnEvents`
  and `getConversation`. A page that speaks them runs a Generic Intake conversation through the
  bridge, polling each turn. Upgrading needs no code change unless you replaced
  `NevaBridgeConversations` with your own implementation, which then needs these four operations.
- `UploadFailedError`, which a `NevaBridgeConversations` implementation throws when the upload of an
  attachment fails.

### Breaking changes

- Every `NevaBridgeConversations` operation resolves to the API's JSON for the operation, unchanged,
  instead of an SDK model, and the bridge passes it to the page as it is. Dates stay strings, and
  fields the SDK does not know yet reach the page.
- Answers to the page use the protocol's names. Only the web chat page reads them:
  - a request the bridge does not know, or one that is not valid, gets `UnsupportedRequest` instead
    of `UnsupportedOperation` or `InvalidRequest`;
  - an answer from the API gateway without an error name gets `EdgeFailure` instead of
    `GatewayError`, and a missing answer `NetworkFailure` instead of `NetworkError`;
  - a failed upload gets `UploadFailed` with the upload's status, and the attachment is not
    confirmed;
  - a closed file picker gets the error `Cancelled` instead of a success with `null`;
  - an API error is retryable for status 408 or 429 and for the model failures the protocol names,
    and no longer for 409, 424, 502 or 504 with an error name;
  - a 409 `IntakeTurnRunning` carries the running turn's `turnId`;
  - a message without a request id is ignored instead of answered.
