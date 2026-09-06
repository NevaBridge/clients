# NevaBridge clients — Agent instructions

This repository holds the official client libraries for the NevaBridge Tenant Integration API.
It is public. Everything committed here is published, including commit messages and author
identities.

## Boundaries

- **The contract is not owned here.** `contracts/nevabridge-v1.yaml` is vendored from the
  NevaBridge service repository and pinned in `contracts/pin.json`. Never edit it by hand; see
  [`contracts/README.md`](contracts/README.md) for the refresh procedure.
- **Generated code is never edited by hand.** `sdk/typescript/src/generated/` is replaced
  wholesale on every generation run. Fix generation configuration or the post-generation step
  instead.
- **No dependency on NevaBridge-internal packages.** The published packages have no runtime
  dependencies and must never import internal service types. The artifact scan enforces this.
- **Nothing internal, private, or credential-bearing may be committed.** No internal hostnames,
  no service repository paths in published files, no tenant data, no keys.

## Identity

Commits use the author and committer identity `Code Monkey <codemonkey@nevabridge.com>`, set as
repo-local `user.name` and `user.email`. Personal identities must not appear in this history.

## Prerequisites

- **Bun** is the package manager, workspace runner and test runner.
- **A JRE** is required. OpenAPI Generator is a Java program, and the generation drift check runs
  it on every gate. This is a hard prerequisite of this repository, unlike the service repository.
  On first use the pinned generator JAR is downloaded to `.tools/` and its SHA-256 verified. Set
  `OPENAPI_GENERATOR_JAR` to reuse an existing verified copy.

## Quality gate

```bash
bun run check
```

That runs, in order:

| Step | Proves |
| --- | --- |
| `contract:verify` | The vendored contract still matches the upstream commit recorded in `pin.json`. |
| `format:check` | Handwritten root files are formatted. |
| `test` | The repository-level checks behave. |
| `sdk:check` | The TypeScript package's own full gate: regeneration drift, formatting, four typechecks, behavioral tests, dual-format build, built-package load, tarball scan, and pack dry-run. |

The first and fourth steps are deliberately separate. Generation drift only proves the committed
client matches whatever is vendored; it says nothing about whether what is vendored is authentic.

CI repeats the same gate on every push and pull request, with an explicitly pinned JRE.

## Layout

```text
contracts/          vendored, pinned OpenAPI contract
sdk/generation/     pinned generator configuration, shared across languages
sdk/typescript/     the @nevabridge/sdk package
scripts/            repository-level checks
tests/              tests for those checks
```

`sdk/dotnet/` is reserved for the planned .NET client. It will not be a Bun workspace member; do
not add it to `workspaces` in the root `package.json`.

## Conventions

- TypeScript: explicit parameter and return types, no `any`, no non-null assertions, no unchecked
  casts. Comments explain why, not what.
- Tests are structured Given / When / Then and test behavior through a public interface.
- Commit messages: imperative mood, capitalized subject, no Conventional Commits prefix.
- Never add test-only branches, flags or escape hatches to shipped code.
