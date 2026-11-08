# Agent instructions for NevaBridge clients

This repository holds the official client libraries for the NevaBridge Tenant Integration API.
It is public. Everything committed here is published, including commit messages and author
identities.

This file serves two kinds of agents:

- An agent **maintaining its own application** that integrates NevaBridge, using this repository
  as the reference: read [Maintaining your own application](#maintaining-your-own-application-built-on-this-example).
- An agent **changing this repository**: read everything from [Boundaries](#boundaries) on.

## Maintaining your own application built on this example

You are working in another codebase, typically a WPF desktop application that embeds the NevaBridge
chat. This repository is your reference; you do not commit to it.

### Read first

1. [`widgets/wpf/README.md`](widgets/wpf/README.md): the integration guide. Its section "What the
   package offers" holds the product decisions below; the rest shows the setup steps and code.
2. [`samples/dotnet-wpf/README.md`](samples/dotnet-wpf/README.md): the runnable example, the table
   "Where to look" (one file per integration step), and the upgrade recipes "Adding Generic Intake
   to your own native chat" and "Adding voice to your own native chat".
3. [`widgets/wpf/CHANGELOG.md`](widgets/wpf/CHANGELOG.md): what changed, including breaking
   changes for anyone who implemented an interface themselves. The root [`CHANGELOG.md`](CHANGELOG.md)
   summarizes each release across packages.
4. [`docs/features/wpf-native-generic-intake-chat.md`](docs/features/wpf-native-generic-intake-chat.md)
   and [`widgets/wpf/docs/intake-chat-state.md`](widgets/wpf/docs/intake-chat-state.md): how the
   Generic Intake chat works, its numbered behavior rules, and its known limitations.

### Product decisions to carry into the application

- **Use Generic Intake.** Offer `NevaBridgeIntakeChatControl` (or the hosted web chat) as the chat.
  Hide the classic `NevaBridgeChatControl` from users. If the application needs an emergency path
  back, keep the classic chat behind a feature toggle that is off by default.
- **Design for polling.** A reply is produced by a background turn that the chat polls. Updates
  arrive in steps, a few seconds apart at first. Every waiting state needs visible activity: an
  animation plus the status text from `IntakeChatViewModel.ProgressText`. Never block the UI
  thread while waiting.
- **Gate every feature twice.** Offer a chat, attachments, dictation or the hands-free conversation
  only when the application wants it and the tenant's gate allows it: `genericIntake`,
  `chatAttachments`, `voiceDictation`, `voiceConversation` from `GetFeaturesAsync`. Whether the
  application's own decision lives in a configuration file (as in the sample's
  `nevabridge-features.json` with `SampleFeatures.cs` and `NevaBridgeFeatureSwitches.cs`) or is
  fixed in code is the application owner's choice; ask if it is not stated.
- **Keep the API key out of logs and source control.** It grants access to the whole tenant. The
  same holds for a transcription session's address, which is a credential.

### How to upgrade the application

1. Find the revision of this repository the application was built from (a pinned checkout, a
   recorded commit, or the copied files). Compare it with the revision you upgrade to:
   `git diff <old>..<new> -- widgets/wpf samples/dotnet-wpf`, and read the changelog entries in
   between.
2. Prefer project references to `widgets/wpf/src/NevaBridge.Chat` and `widgets/wpf/src/NevaBridge.Wpf`
   from a checkout pinned to a tested revision, as the integration guide's "Build the components
   into your application" shows. If the application copied these projects instead, replace the
   copies wholesale from the new revision; do not patch them file by file.
3. The application's own code mirrors the sample's `src/NevaBridge.Sample.DesktopApp` files and the
   sample client `src/NevaBridge.Sample.Client/NevaBridgeClient.cs`. Port changes in those files
   into the application's equivalents, keeping its own settings source, user mapping and styling.
4. If the application implements `INevaBridgeConversations`, `INevaBridgeIntakeConversations`,
   `INevaBridgeVoice` or `INevaBridgeWebChatApi` itself (for example to call its own backend),
   add every operation the new revision declares. The upgrade recipes in the sample guide list
   each operation with its HTTP request.
5. Verify against a NevaBridge sandbox product, never production: send messages and watch the
   status while the reply grows, attach and remove a file, dictate, run a hands-free exchange,
   press Finish and check the filed reports in NevaBridge. Voice needs a real microphone on
   Windows.
6. Report the known limitations from the feature doc to the application owner rather than working
   around them in the application.

## Boundaries

- **The contract is not owned here.** `contracts/nevabridge-v1.yaml` is a vendored copy from the
  NevaBridge service repository, pinned in `contracts/pin.json`. Never edit it by hand. See
  [`contracts/README.md`](contracts/README.md) for how to refresh it.
- **Never edit generated code by hand.** Every generation run replaces
  `sdk/typescript/src/generated/` wholesale. Fix the generation configuration or the step that
  runs after generation instead.
- **Never depend on NevaBridge-internal packages.** The published packages have no runtime
  dependencies and must never import internal service types. The artifact scan enforces this.
- **Never commit anything internal, private, or credential-bearing.** That means no internal
  hostnames, no service repository paths in published files, no tenant data, and no keys.
- **Never publish the owner's internal context.** This covers customer, prospect and partner names,
  onboarding and adoption plans, roadmap order, commercial reasoning, and anything else the owner
  shared with an agent as working context and not for publication. Keep it out of files, commit
  messages, branch names, tags and pull request text. When a document needs that context, state it
  in general terms, such as "the first .NET consumer" instead of a name. If you cannot tell whether
  something is meant for publication, leave it out and ask the owner.


## Prerequisites

- **Bun** is the package manager, workspace runner and test runner.
- **A JRE** is required. OpenAPI Generator is a Java program, and every gate run uses it for the
  generation drift check. The service repository does not need Java, but this one always does. On
  first use, the scripts download the pinned generator JAR to `.tools/` and verify its SHA-256. Set
  `OPENAPI_GENERATOR_JAR` to reuse a verified copy you already have.

## Quality gate

```bash
bun run check
```

It runs these steps in order:

| Step | Proves |
| --- | --- |
| `contract:verify` | The vendored contract has not changed since it was pinned. See `contracts/README.md` for what this does not prove. |
| `format:check` | The handwritten root files are formatted. |
| `test` | The repository-level checks work. |
| `sdk:check` | The TypeScript package passes its own full gate. That covers regeneration drift, formatting, four type checks, behavioral tests, the build of both module formats, loading the built package, the tarball scan, and a pack dry run. |

The first and fourth steps are separate on purpose. The drift check only proves that the committed
client matches whatever is vendored. It cannot tell whether someone changed the vendored contract
after pinning it. Neither step proves that the pin names the revision it claims. The service
repository owns the history, so it settles that. See `contracts/README.md`.

CI runs the same gate on every push and pull request, with an explicitly pinned JRE.

## Layout

```text
contracts/          vendored, pinned OpenAPI contract
sdk/generation/     pinned generator configuration, shared across languages
sdk/typescript/     the @nevabridge/sdk package
widgets/wpf/        NevaBridge.Wpf NuGet package (.NET; dotnet CLI, not Bun)
widgets/electron/   @nevabridge/electron npm package (Bun workspace member)
widgets/webview-chat/ pinned build of the NevaBridge web chat page both host packages ship
samples/dotnet-wpf/ WPF example on NevaBridge.Wpf (.NET; dotnet CLI, not Bun)
samples/electron-desktop/ Electron example on @nevabridge/electron (Bun workspace member)
scripts/            repository-level checks
tests/              tests for those checks
docs/features/      current-state feature documentation
CHANGELOG.md        release summary across packages; each package keeps its own CHANGELOG.md
```

When a change is visible to someone integrating a package, add it to that package's
`CHANGELOG.md` under "Unreleased" and to the root `CHANGELOG.md`.

`sdk/dotnet/` is reserved for the planned .NET client. It will not be a Bun workspace member, so do
not add it to `workspaces` in the root `package.json`.

## Conventions

- TypeScript code declares explicit parameter and return types and uses no `any`, no non-null
  assertions and no unchecked casts. Comments explain why, not what.
- Tests follow Given / When / Then and test behavior through a public interface.
- Commit messages use the imperative mood, a capitalized subject, and no Conventional Commits
  prefix.
- Never add test-only branches, flags or escape hatches to shipped code.
