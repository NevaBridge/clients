# Changelog

## Unreleased

The web chat bridge now follows version 1 of the webview chat bridge protocol exactly and passes
every conformance case of the pinned protocol. A native Generic Intake chat joins the native
classic chat, which is unchanged.

### Added

- Dictation and a hands-free conversation in the native Generic Intake chat, offered through
  `IntakeChatOptions.Voice` (`IntakeChatVoice`) and run by `IntakeChatViewModel.Voice`. Dictate
  types what the reporter says into the message box; Talk sends after a pause, reads the reply
  aloud and listens again. English or Deutsch, chosen in the chat; `IntakeChatOptions.VoiceLanguage`
  sets the first choice.
- `INevaBridgeVoice` (`CreateTranscriptionSessionAsync`, `SynthesizeSpeechAsync`), separate from
  the other interfaces so existing implementations keep compiling. The sample's `NevaBridgeClient`
  implements it.
- `TranscribeListener`, which streams a microphone to Amazon Transcribe through NevaBridge's
  presigned sessions, with the `IMicrophone`, `ISpeechPlayer` and `ITranscribeSocket` seams, and
  `NAudioMicrophone` and `NAudioSpeechPlayer` on Windows audio.
- **New dependency:** `NAudio` 2.2.1 (MIT), next to WebView2.
- `NevaBridgeIntakeChatControl` with `IntakeChatViewModel`: a native Generic Intake chat. It follows
  each turn as it runs, offers Finish, lists the filed reports once the conversation has ended,
  and attaches, lists and removes files when given a file picker.
- `INevaBridgeIntakeConversations`, the operations the Generic Intake chat needs, separate from
  `INevaBridgeConversations` so existing implementations keep compiling. The sample's
  `NevaBridgeClient` implements it.
- `ReporterContact` on `StartIntakeConversationRequest`, and `Revision`, `ActiveTurnId` and
  `IsClosed` on `ConversationDetail`.

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
