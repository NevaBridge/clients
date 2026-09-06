# NevaBridge clients

Official client libraries for the [NevaBridge](https://nevabridge.com) Tenant Integration API.

| Package | Language | Status |
| --- | --- | --- |
| [`@nevabridge/sdk`](sdk/typescript) | TypeScript / Node.js 22+ | Pre-release |

The API surface is five operations for running a NevaBridge reporting conversation from your own
backend: start a conversation, list conversations, read one, append a turn, and submit the report.
See [`sdk/typescript/README.md`](sdk/typescript/README.md) for usage.

## How these clients are built

Wire models and the transport client are generated from the published OpenAPI contract with a
pinned, checksum-verified OpenAPI Generator release. Generated code is committed and replaced
wholesale on every run; it is never edited by hand. Each package wraps that generated client in a
small handwritten facade, which is the supported public API.

The contract itself is vendored under [`contracts/`](contracts) and pinned to the upstream commit
it came from, so any published client can be traced back to an exact contract revision.

## License

MIT. See [LICENSE](LICENSE).
