# Changelog

What changed in this repository, across packages. Each package keeps a detailed changelog with
every addition and breaking change:

- [`widgets/wpf/CHANGELOG.md`](widgets/wpf/CHANGELOG.md) for `NevaBridge.Wpf` and the WPF example
- [`widgets/electron/CHANGELOG.md`](widgets/electron/CHANGELOG.md) for `@nevabridge/electron`
- [`sdk/typescript/CHANGELOG.md`](sdk/typescript/CHANGELOG.md) for `@nevabridge/sdk`

Entries are dated by the day they reached `main`. To upgrade, read every entry since the revision
you last took.

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
