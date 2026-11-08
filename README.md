# NevaBridge clients

Official client libraries for the [NevaBridge](https://nevabridge.com) Tenant Integration API.

| Package                                    | Language                    | Status      |
| ------------------------------------------ | --------------------------- | ----------- |
| [`@nevabridge/sdk`](sdk/typescript)        | TypeScript / Node.js 22+    | Pre-release |
| [`NevaBridge.Wpf`](widgets/wpf)            | C# / .NET 8+ WPF on Windows | Pre-release |
| [`@nevabridge/electron`](widgets/electron) | TypeScript / Electron 32+   | Pre-release |

The API lets your application run NevaBridge conversations. In **Generic Intake**, the
recommended conversation, an assistant works out what the user needs, answers questions and files
one or more reports; each reply runs in the background and the client polls it. Files can be
attached, and users can dictate or talk with the assistant hands-free. The older **classic chat**,
where the user first picks a report type and files one report, stays supported. See
[`sdk/typescript/README.md`](sdk/typescript/README.md) for the TypeScript SDK and
[`widgets/wpf/README.md`](widgets/wpf/README.md) for WPF.

What changed is in the [changelog](CHANGELOG.md). If you point a coding agent at this repository
to upgrade your own integration, it finds its instructions in [`AGENTS.md`](AGENTS.md).

## Get an API key

Every request needs a NevaBridge API key. Create one in your NevaBridge account under
[Setup > API](https://app.nevabridge.com/setup?section=api).

The key grants access to your whole tenant. Keep it on your server, in a secret store or an
environment variable, and never commit it or send it to browser code.

Pass the key to the SDK through its token provider, as the
[SDK README](sdk/typescript/README.md#create-a-client) shows. If you call the API with your own
HTTP client, send the key as `Authorization: Bearer <key>` on every request. The production API is
at `https://tenant.api.nevabridge.com`, and the sandbox is at
`https://tenant.api.sandbox.nevabridge.com`.

## Samples

For a WPF application, start with the [integration guide](widgets/wpf/README.md). It explains the
Generic Intake chat, attachments, voice and feature switches, then covers native controls, the
NevaBridge-hosted web chat, and a bundled or self-hosted page, with settings in your database.
Build the components from this repository; no published NevaBridge NuGet package is required. The
[WPF example](samples/dotnet-wpf) runs every chat side by side and also works without a database.

| Sample                                                 | Shows                                                                                                                                                |
| ------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| [`samples/dotnet-wpf`](samples/dotnet-wpf)             | A WPF application with the native Generic Intake chat (attachments, dictation, hands-free voice), the classic native chat and the NevaBridge web chat in WebView2, each behind a feature switch, built on [`NevaBridge.Wpf`](widgets/wpf). |
| [`samples/electron-desktop`](samples/electron-desktop) | An Electron application with the NevaBridge web chat, built on [`@nevabridge/electron`](widgets/electron).                                           |

## How these clients are built

A pinned OpenAPI Generator release generates the wire models and the transport client from the
published OpenAPI contract. The build verifies the generator's checksum before running it. The
generated code is committed, and every run replaces it wholesale. Nobody edits it by hand. Each
package wraps the generated client in a small handwritten facade, and that facade is the supported
public API.

The contract is vendored under [`contracts/`](contracts) and pinned to the upstream commit it came
from. Every published client therefore traces back to an exact contract revision.

## License

MIT. See [LICENSE](LICENSE).
