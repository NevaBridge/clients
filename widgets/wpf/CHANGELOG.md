# Changelog

## Unreleased

The web chat bridge now follows version 1 of the webview chat bridge protocol exactly and passes
every conformance case of the pinned protocol. The native WPF chat is unchanged.

### Added

- The Generic Intake requests `startIntakeConversation`, `startIntakeTurn`, `getIntakeTurnEvents`
  and `getConversation` in `WebChatBridge`. A page that speaks them, such as the hosted `/v1/`
  page, runs a Generic Intake conversation through the bridge, polling each turn.
- `INevaBridgeWebChatApi`, the operations the web chat bridge relays, each returning the API's JSON
  unchanged. The sample's `NevaBridgeClient` implements it next to `INevaBridgeConversations`.
- `StartIntakeConversationRequest`, `StartIntakeTurnRequest`, `NevaBridgeApiException.TurnId` and
  `NevaBridgeUploadException`.

### Breaking changes

- `WebChatBridge` and `NevaBridgeWebChatControl.InitializeAsync` take an `INevaBridgeWebChatApi`
  instead of an `INevaBridgeConversations`. With the sample's `NevaBridgeClient`, pass the same
  object as before. If you replaced it with your own implementation, implement
  `INevaBridgeWebChatApi` as well; it returns the API's JSON, so dates stay as the API wrote them
  and fields this package does not know yet reach the page.
- The sample's `NevaBridgeClient` throws `NevaBridgeUploadException` when the upload of an
  attachment fails, instead of a `NevaBridgeApiException` named `UploadFailed`.
- Answers to the page use the protocol's names. Only the web chat page reads them:
  - a request the bridge does not know, or one that is not valid, gets `UnsupportedRequest` instead
    of `UnsupportedOperation` or `InvalidRequest`;
  - an answer from the API gateway without an error name gets `EdgeFailure` instead of
    `GatewayError`, and a missing answer or a timeout `NetworkFailure` instead of `NetworkError` or
    `Timeout`;
  - a failed upload gets `UploadFailed` with the upload's status, retryable;
  - a closed file picker gets the error `Cancelled` instead of a success with `null`;
  - an API error is retryable for status 408 or 429 and for the model failures the protocol names,
    and no longer for 409, 424, 502 or 504 with an error name;
  - a 409 `IntakeTurnRunning` carries the running turn's `turnId`;
  - a message without a request id is ignored instead of answered.
- The sample's `NevaBridgeClient` sends an `X-Actor-Name` of printable ASCII without `%` as it is,
  and percent-encodes other names. NevaBridge decodes both, so the stored name does not change.
