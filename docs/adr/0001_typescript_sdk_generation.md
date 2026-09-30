# ADR 0001: TypeScript SDK generation

## Context

NevaBridge publishes an OpenAPI 3.1 contract for tenant backend integrations.
The TypeScript client must be reproducible from that contract and must keep enum values it does not know yet.
It must work for Node.js consumers that use ESM and for those that use CommonJS, and it must not depend on internal NevaBridge types.

The SDK design required comparing Microsoft Kiota and OpenAPI Generator on the real contract before choosing a generator.
This decision covers only the TypeScript SDK. The .NET SDK must evaluate generators on its own once its target runtime is known.

## Problem

Choose a generator and a package boundary that are easy to maintain and keep the public contract intact, without making the generated code the stable API that consumers call.

## Decision

Generate the TypeScript wire client with OpenAPI Generator 7.24.0 and its `typescript-fetch` generator.
Pin the release JAR checksum and all generation options under `sdk/generation/`.
On first use, the generation script downloads the pinned JAR to a temporary file, verifies its SHA-256, and moves it into the `.tools/` cache in one atomic step.
`OPENAPI_GENERATOR_JAR` points the script at an existing copy instead. The script verifies that copy's checksum too, and fails if the file is missing rather than downloading a replacement.
Commit the generated source under `sdk/typescript/src/generated/` and replace that directory on every run.

Map the OpenAPI `Error` schema to `NevaBridgeError`. Name the inline schemas for the error and for the failed connector result in the generator arguments.
The successful connector result keeps the generator's default name, because naming both sibling inline schemas makes OpenAPI Generator collapse the union.
A deterministic step after generation widens the contract's enum aliases to accept unknown strings and leaves `const` discriminators closed.
The script then strips the trailing whitespace and extra final newlines that OpenAPI Generator emits, so generated commits pass the whitespace check.

Make `NevaBridgeClient` the supported public runtime API.
It wraps the generated client and adds a token callback that runs on every request, base URL selection, cancellation, structured errors, and one named method per operation.
The public wire types stay generated from OpenAPI. Relative imports in both generated and handwritten code use `.js` specifiers.
esbuild bundles the runtime as ESM and as CommonJS, and the build emits matching `.d.ts` and `.d.cts` declaration files that resolve under NodeNext.
Bun's bundler is not used for this, because Bun 1.3.11 and 1.3.14 produced broken bundles for the generated re-export graph.

## Rationale

On 2026-08-23 both generators ran against `contracts/nevabridge-v1.yaml`, each at the latest release listed on its GitHub page that day.
Both generated all five paths.

OpenAPI Generator 7.24.0 marks `typescript-fetch` as stable. It needs Java to run.
It generated one method per operation, with nullable request fields, optional response fields, date conversion, free-form string maps and the operation descriptions intact.
Its runtime accepted unknown enum strings without throwing.
It warned that its general OpenAPI 3.1 support is beta, but the output for every 3.1 construct this contract uses compiled and passed response tests.
Two problems needed configuration. Without a mapping it named the `Error` schema wrongly inside unions, which the `Error=NevaBridgeError` mapping fixes. Naming both members of `ConnectorDeliveryResult` collapsed the union, which is why only the failed member is named.

Kiota 1.34.1 marks TypeScript as preview and warns that future releases will break source compatibility.
It could not create error types for several `409` and `500` responses, including those on the append and submit paths.
It made every model field optional and nullable, including fields the contract requires, and warned that the connector delivery unions have no discriminator and may fail to serialize.
It represented the free-form application context through its `AdditionalDataHolder` type instead of a plain `Record<string, string>`, and the generated client needs Kiota's abstractions, HTTP and serialization runtime packages.
The missing error types and the preview status each rule Kiota out for a public client.

The runtime that OpenAPI Generator produces works, but its method names and options are not the API we want to publish.
A thin facade keeps consumers independent of the generator while the wire models still come from the contract.
`enumUnknownDefaultCase` stays disabled, because it replaces an unknown string with a sentinel value and loses the original.
Instead, the pinned step after generation widens only OpenAPI `enum` aliases with `string & {}`. Known values still autocomplete, and future values arrive unchanged.
OpenAPI `const` fields stay closed so TypeScript can discriminate unions on them.

## Consequences

- Regeneration is mechanical and pinned, the build verifies the generator's checksum, and nobody edits generated files.
- The package gate regenerates the client on every run and fails when the result differs from the committed source.
- Consumers get a small runtime with no dependencies, stable method names, and types taken from the provider's contract.
- Tests of the public facade and load checks of both module formats catch generator changes before they reach consumers.
- A type check of a NodeNext consumer validates the published declarations, and the artifact scan reads the actual npm tarball.
- Regeneration needs Java, and the first run downloads the pinned generator JAR.
- OpenAPI Generator still labels its general OpenAPI 3.1 support beta, so compile and behavior tests must cover every construct the contract uses.
- Generated source contains loose casts and unused imports and parameters. We accept them only inside the generated directory. The SDK keeps strict type checking but leaves TypeScript's unused-symbol checks off, because the generated source would fail them.

## Alternatives considered

1. Microsoft Kiota 1.34.1. Rejected for this TypeScript package because its TypeScript target is in preview and its output lost error types and required fields.
2. A handwritten client and models. Rejected because they would copy the OpenAPI wire contract by hand, and every schema change would need a manual update.
3. Custom templates or a generator fork. Deferred because configuration plus the thin facade handles the current contract, and owning templates would mean maintaining more code.

## References

- `sdk/generation/generate-typescript.sh` and `sdk/generation/typescript-config.json` hold the pinned generation.
- `sdk/typescript/src/nevabridge-client.ts` is the facade, and `sdk/typescript/tests/` tests it.
- OpenAPI Generator `typescript-fetch` options: <https://openapi-generator.tech/docs/generators/typescript-fetch/>
- Kiota TypeScript quickstart: <https://learn.microsoft.com/openapi/kiota/quickstarts/typescript>
