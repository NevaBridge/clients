# Agent instructions for NevaBridge clients

This repository holds the official client libraries for the NevaBridge Tenant Integration API.
It is public. Everything committed here is published, including commit messages and author
identities.

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
scripts/            repository-level checks
tests/              tests for those checks
```

`sdk/dotnet/` is reserved for the planned .NET client. It will not be a Bun workspace member, so do
not add it to `workspaces` in the root `package.json`.

## Conventions

- TypeScript code declares explicit parameter and return types and uses no `any`, no non-null
  assertions and no unchecked casts. Comments explain why, not what.
- Tests follow Given / When / Then and test behavior through a public interface.
- Commit messages use the imperative mood, a capitalized subject, and no Conventional Commits
  prefix.
- Never add test-only branches, flags or escape hatches to shipped code.
