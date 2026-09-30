# NevaBridge clients

Official client libraries for the [NevaBridge](https://nevabridge.com) Tenant Integration API.

| Package | Language | Status |
| --- | --- | --- |
| [`@nevabridge/sdk`](sdk/typescript) | TypeScript / Node.js 22+ | Pre-release |

The API lets your backend run a NevaBridge reporting conversation. It can start a conversation,
list conversations, read one, append a turn, attach files, and submit the report. See
[`sdk/typescript/README.md`](sdk/typescript/README.md) for usage.

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
