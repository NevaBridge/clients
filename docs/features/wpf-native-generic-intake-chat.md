# Native Generic Intake chat for WPF

## Overview

The WPF package (`widgets/wpf`) has a native Generic Intake chat next to the classic native chat
and the hosted web chat. In Generic Intake the assistant works out what the reporter needs
instead of asking for a report type, and one conversation can file several reports. The chat
offers file attachments (attach, list, remove), dictation, and a hands-free conversation that reads
the replies aloud. The WPF sample (`samples/dotnet-wpf`) shows each chat behind its own entry and
switches the chats, attachments and both voice features on and off with a features file.

How to add the chat to an application is in the [package README](../../widgets/wpf/README.md)
(section "Native Generic Intake chat") and in the [sample guide](../../samples/dotnet-wpf/README.md)
(sections "Features file", "Adding Generic Intake to your own native chat" and "Adding voice to
your own native chat").

## How it works

### Package

- **`INevaBridgeIntakeConversations`** (`NevaBridge.Chat`) holds the operations the chat calls:
  start a conversation, start a turn, read a turn's events, read the conversation, attach, list
  and delete files, and read the tenant's features. It is separate from
  `INevaBridgeConversations`, so existing implementations keep compiling. A class implementing
  both shares one `AttachFileAsync` and one `GetFeaturesAsync`. Errors use
  `NevaBridgeApiException` (with the running `TurnId` where the API names one) and
  `NevaBridgeUploadException`.
- **`IntakeChatViewModel`** (`NevaBridge.Chat`) runs one conversation. It takes the interface, the
  product id, the reporter and `IntakeChatOptions` (application context, reporter contact, and a
  file picker; without a picker there are no attachments). Its rules for turns, lost answers,
  attachments and closing are listed in [`intake-chat-state.md`](../../widgets/wpf/docs/intake-chat-state.md).
- **`NevaBridgeIntakeChatControl`** (`NevaBridge.Wpf`) shows the transcript, the live reply,
  the composer, Finish, Check again, the attachments list with Attach and Remove, and after the
  end the list of filed reports. It supplies the Windows file picker (`PickFileAsync`).
- **Voice** is offered when the host passes an `IntakeChatVoice` (`IntakeChatOptions.Voice`) with
  an `INevaBridgeVoice`, a microphone and a speech player, and switches `Dictation` and
  `HandsFree`. `IntakeVoiceViewModel` (`chat.Voice`) runs dictation and the hands-free loop on the
  chat's own send path; its rules are rules 16 to 23 of `intake-chat-state.md`.
  `TranscribeListener` streams the microphone to Amazon Transcribe over the presigned sessions
  NevaBridge hands out, framed with the AWS event-stream codec, and rotates to a new session 3
  seconds before the paid 60 seconds run out. Replies are split into sentences with their
  Markdown removed and read through `synthesizeSpeech`. `NAudioMicrophone` (WASAPI, resampled to
  16 kHz mono 16-bit) and `NAudioSpeechPlayer` (MP3 from memory) are the Windows implementations;
  NAudio is the package's second runtime dependency. The language is English or Deutsch, chosen in
  the chat, starting from `IntakeChatOptions.VoiceLanguage` or the Windows display language.
- **Texts** are English and live in `IntakeChatText`.

### Conversation flow

1. The first message calls `startIntakeConversation` with the reporter, the application context
   and, when known, `reporterContact`, so the assistant does not ask for contact details.
2. The chat follows the turn with `getIntakeTurnEvents` (`wait=20`), showing the assistant's
   status and the reply as it is written, then reads the saved conversation on `done`.
3. Later messages call `startIntakeTurn` with the conversation's current revision.
4. Finish sends a fixed closing message with `finish: true`. A closed conversation takes no more
   messages and lists each filed report's title, category, status and status reason.

No `responseLanguage` or `writerModelKey` is sent: the assistant infers the reporter's language,
and the product's default writer applies.

### Sample

- **Features file** `nevabridge-features.json`, copied next to the exe, with the required boolean
  keys `genericIntakeChat`, `classicChat`, `webChat`, `attachments`, `dictation` and
  `handsFreeVoice`. A missing file, a missing
  or unknown key, or a non-boolean value stops the sample at start-up with a message naming the
  problem. The file holds no secrets.
- A feature is offered only when the file enables it and the tenant's feature gate allows it:
  `genericIntake` for the Generic Intake chat, `chatAttachments` for attachments, `voiceDictation`
  for dictation and `voiceConversation` for the hands-free conversation. The sample reads
  the tenant's features at start-up and again when a chat opens. The attachments switch covers the
  Generic Intake chat and the web chat; the classic native chat has no attachment UI.
- `NevaBridgeClient` implements `INevaBridgeIntakeConversations` and `INevaBridgeVoice`, and
  `CurrentReporter` carries an optional email and phone for `reporterContact`.

## Known limitations / not yet implemented

- Files can be attached only once the first turn is saved, because a first turn that fails
  deletes its conversation and its files.
- When the answer to the very first message's start is lost, the chat cannot find that
  conversation. Sending again starts a second one, and NevaBridge may still file the first after
  its idle timeout.
- Voice: speaking over the reply does not interrupt it, the language is not detected
  automatically, only English and German are offered, the sentence being read is not
  highlighted, and the language choice is not remembered between runs. A real microphone and
  speaker are exercised only on Windows by hand; the automated checks use prepared audio.
- No drafts while chatting, no resuming a conversation after the application restarts,
  no attachments in the classic native chat, and no choice of writer model or response language.
- Staff features of the NevaBridge web app (pins, planning details, archive) are not offered.
- Closing the window cancels polling; a running turn still completes on NevaBridge, and no local
  state is kept.

## Related

- [`widgets/wpf/README.md`](../../widgets/wpf/README.md), [`widgets/wpf/CHANGELOG.md`](../../widgets/wpf/CHANGELOG.md)
- [`widgets/wpf/docs/intake-chat-state.md`](../../widgets/wpf/docs/intake-chat-state.md)
- [`samples/dotnet-wpf/README.md`](../../samples/dotnet-wpf/README.md)
