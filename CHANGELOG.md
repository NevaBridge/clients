# Changelog

What changed in this repository, across packages. Each package keeps a detailed changelog with
every addition and breaking change:

- [`widgets/wpf/CHANGELOG.md`](widgets/wpf/CHANGELOG.md) for `NevaBridge.Wpf` and the WPF example
- [`widgets/electron/CHANGELOG.md`](widgets/electron/CHANGELOG.md) for `@nevabridge/electron`
- [`sdk/typescript/CHANGELOG.md`](sdk/typescript/CHANGELOG.md) for `@nevabridge/sdk`

Entries are dated by the day they reached `main`. To upgrade, read every entry since the revision
you last took.

## 2026-11-16

### TypeScript SDK

- `ModelProvider` gains `azure`: a model failure can name the tenant's own Azure resource as the
  service that ran the model. The .NET models and both web chat bridges already pass provider
  values through as text, so they need no change.

### All packages

- **Reply ratings**: the user can give an assistant reply a thumbs up or down, with a comment on a
  thumbs down, when the tenant has `replyRatings`. Both web chat bridges answer the page's new
  `setReplyRating` and `clearReplyRating` requests as the reporter, and name the reporter on
  conversation reads and follow-ups, so NevaBridge marks their own replies as ratable. The TypeScript
  SDK gains `setReplyRating` and `clearReplyRating`.
- **The assistant knows whether the chat takes files**: a Generic Intake turn carries
  `chatControls`, saying whether the attach control is offered once the reply is shown. The native
  WPF chat sends it with every message, and both web chat bridges pass on what the page sends.
  Without it, the assistant asks for no files.
- The vendored contract and the webview chat bridge protocol move to the revision with both. An
  implementation of your own of `INevaBridgeWebChatApi` (WPF) needs the new operations and
  parameters; see the package changelogs.

## 2026-11-13

### TypeScript SDK

- **`applicationContext` now has a meaning**: Generic Intake files the `environment` key as the
  reporter's environment instead of asking, and reads the whole map as facts about where the
  reporter works. Documentation only; the wire types are unchanged.

## 2026-11-08

### WPF

- **Native Generic Intake chat** (`NevaBridgeIntakeChatControl`): the assistant works out what the
  user needs and one conversation can file several reports. Each reply runs in the background and
  the chat polls it, showing the assistant's status and the reply as it is written. Finish ends
  the conversation and lists the filed reports. This is now the recommended chat; the classic
  chat stays supported.
- **Attachments** in the Generic Intake chat: attach, list and remove files, with the tenant
  feature `chatAttachments`, also before the first message, as in the NevaBridge web app. A file
  that does not upload stays listed with Retry and Dismiss. Breaking for anyone who implemented
  `INevaBridgeIntakeConversations` themselves; see the package changelog.
- **Voice** in the Generic Intake chat: Dictate (`voiceDictation`) and a hands-free conversation
  that reads replies aloud (`voiceConversation`), in English or German. Adds the `NAudio`
  dependency.
- **WPF example:** a Support assistant entry for Generic Intake, and a features file that switches
  each chat, attachments, dictation and the hands-free conversation on or off. While the
  NevaBridge settings or the tenant's features cannot be read, every chat entry is hidden and the
  window says why.
- **Web chat bridge:** follows version 1 of the webview chat bridge protocol exactly, runs Generic
  Intake through the hosted page, and answers `deleteAttachment` so the page can remove a file.
  Breaking for anyone who implemented the bridge interfaces themselves; see the package changelog.
- **Docs:** the integration guide explains Generic Intake, polling and how to show activity while
  waiting, attachments, voice, and the choice between configuration switches and features fixed
  in code. `AGENTS.md` tells a coding agent how to upgrade an application built on the example.

### Electron

- The web chat bridge follows version 1 of the bridge protocol, runs Generic Intake through the
  hosted page and answers `deleteAttachment`.

### TypeScript SDK

- Generic Intake turns run in the background and are followed with `waitForIntakeTurn` or
  `getIntakeTurnEvents` (0.1.0).
- Unreleased: the voice operations `createTranscriptionSession` and `synthesizeSpeech`;
  `startHeldIntakeConversation`, which starts a Generic Intake conversation with files picked
  before the first message; a custom `fetch`; and `client.json` for relaying the API's JSON
  unchanged.
