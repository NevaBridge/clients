import {describe, expect, it} from "bun:test";
import {chmod, mkdtemp, mkdir, rm, writeFile} from "node:fs/promises";
import {tmpdir} from "node:os";
import {join} from "node:path";

const packageRoot = new URL("../", import.meta.url);

describe("@nevabridge/sdk package contract", () => {
  it("verifies generator checksums with the Darwin sha256sum interface", async () => {
    const temporaryDirectory = await mkdtemp(
      join(tmpdir(), "nevabridge-sdk-checksum-"),
    );
    const fakeBinaryDirectory = join(temporaryDirectory, "fake-bin");
    const artifact = join(temporaryDirectory, "artifact.jar");
    const contents = "generator fixture";
    const checksum = new Bun.CryptoHasher("sha256")
      .update(contents)
      .digest("hex");
    await mkdir(fakeBinaryDirectory);
    await writeFile(artifact, contents);
    await writeFile(
      join(fakeBinaryDirectory, "sha256sum"),
      `#!/usr/bin/env bash
set -euo pipefail
if [[ "$1" == -* ]]; then
  exit 64
fi
printf '${checksum}  %s\n' "$1"
`,
    );
    await chmod(join(fakeBinaryDirectory, "sha256sum"), 0o755);

    try {
      const verification = Bun.spawn(
        [
          "bash",
          new URL("../generation/verify-sha256.sh", packageRoot).pathname,
          checksum,
          artifact,
        ],
        {
          env: {
            ...process.env,
            PATH: `${fakeBinaryDirectory}:${process.env.PATH ?? ""}`,
          },
          stdout: "ignore",
          stderr: "ignore",
        },
      );
      expect(await verification.exited).toBe(0);
    } finally {
      await rm(temporaryDirectory, {recursive: true, force: true});
    }
  });

  it("declares the permanent public identity and dual module entry points", async () => {
    const packageMetadata: unknown = await Bun.file(
      new URL("package.json", packageRoot),
    ).json();

    expect(packageMetadata).toMatchObject({
      name: "@nevabridge/sdk",
      version: "0.0.1",
      license: "MIT",
      engines: {node: ">=22"},
      exports: {
        ".": {
          import: {
            types: "./dist/types/esm/index.d.ts",
            default: "./dist/esm/index.js",
          },
          require: {
            types: "./dist/types/commonjs/index.d.cts",
            default: "./dist/cjs/index.cjs",
          },
        },
      },
    });
  });

  it("documents explicit submit and every server-owned abandonment fact", async () => {
    const readme = await Bun.file(new URL("README.md", packageRoot)).text();

    expect(readme).toContain(
      "Call `submitReport` when the user says they are finished.",
    );
    expect(readme).toContain("Do not build an inactivity timer");
    expect(readme).toContain("60 minutes without a turn");
    expect(readme).toContain("60 to 70 minutes after the last turn");
    expect(readme).toContain(
      "Every successful turn restarts the 60-minute window",
    );
    expect(readme).toContain("A manual submit cancels it");
    expect(readme).toContain("same finalization and connector delivery");
    expect(readme).toContain('submissionOrigin: "auto_abandoned"');
    expect(readme).toContain("first in-progress report");
    expect(readme).toContain(
      "Conversations authenticated as staff are never finalized automatically",
    );
  });

  it("rejects a forbidden dependency in the packed package metadata", async () => {
    const temporaryPackage = await mkdtemp(
      join(tmpdir(), "nevabridge-sdk-package-scan-"),
    );
    await mkdir(join(temporaryPackage, "dist"));
    await writeFile(join(temporaryPackage, "dist", "index.js"), "export {};\n");
    await writeFile(
      join(temporaryPackage, "package.json"),
      JSON.stringify({
        name: "package-scan-fixture",
        version: "1.0.0",
        files: ["dist"],
        dependencies: {"@nevabridge/shared": "1.0.0"},
      }),
    );

    try {
      const scan = Bun.spawn(
        [
          "bash",
          new URL("scripts/scan-package-artifacts.sh", packageRoot).pathname,
          temporaryPackage,
        ],
        {stdout: "ignore", stderr: "ignore"},
      );
      expect(await scan.exited).toBe(1);
    } finally {
      await rm(temporaryPackage, {recursive: true, force: true});
    }
  });

  it("rejects a forbidden marker in a packed hidden artifact", async () => {
    const temporaryPackage = await mkdtemp(
      join(tmpdir(), "nevabridge-sdk-hidden-package-scan-"),
    );
    await mkdir(join(temporaryPackage, "dist"));
    await writeFile(
      join(temporaryPackage, "dist", ".credentials"),
      "client_secret=fixture-value\n",
    );
    await writeFile(
      join(temporaryPackage, "package.json"),
      JSON.stringify({
        name: "hidden-package-scan-fixture",
        version: "1.0.0",
        files: ["dist"],
      }),
    );

    try {
      const scan = Bun.spawn(
        [
          "bash",
          new URL("scripts/scan-package-artifacts.sh", packageRoot).pathname,
          temporaryPackage,
        ],
        {stdout: "ignore", stderr: "ignore"},
      );
      expect(await scan.exited).toBe(1);
    } finally {
      await rm(temporaryPackage, {recursive: true, force: true});
    }
  });

  it("rejects supported credential representations in packed artifacts", async () => {
    for (const credential of [
      "sk-workos-examplecredential",
      "clientSecret=examplecredential",
      "bearer examplecredentialvalue12345",
    ]) {
      const temporaryPackage = await mkdtemp(
        join(tmpdir(), "nevabridge-sdk-credential-scan-"),
      );
      await mkdir(join(temporaryPackage, "dist"));
      await writeFile(join(temporaryPackage, "dist", "config.txt"), credential);
      await writeFile(
        join(temporaryPackage, "package.json"),
        JSON.stringify({
          name: "credential-scan-fixture",
          version: "1.0.0",
          files: ["dist"],
        }),
      );

      try {
        const scan = Bun.spawn(
          [
            "bash",
            new URL("scripts/scan-package-artifacts.sh", packageRoot).pathname,
            temporaryPackage,
          ],
          {stdout: "ignore", stderr: "ignore"},
        );
        expect(await scan.exited).toBe(1);
      } finally {
        await rm(temporaryPackage, {recursive: true, force: true});
      }
    }
  });

  it("fails loudly instead of passing when the scanner cannot run", async () => {
    const temporaryPackage = await mkdtemp(
      join(tmpdir(), "nevabridge-sdk-unrunnable-scan-"),
    );
    const shadowedTools = await mkdtemp(
      join(tmpdir(), "nevabridge-sdk-shadowed-tools-"),
    );
    await mkdir(join(temporaryPackage, "dist"));
    await writeFile(join(temporaryPackage, "dist", "index.js"), "export {};\n");
    await writeFile(
      join(temporaryPackage, "package.json"),
      JSON.stringify({
        name: "unrunnable-scan-fixture",
        version: "1.0.0",
        files: ["dist"],
      }),
    );
    const shadowedGrep = join(shadowedTools, "grep");
    await writeFile(shadowedGrep, "#!/bin/sh\nexit 2\n");
    await chmod(shadowedGrep, 0o755);

    try {
      const scan = Bun.spawn(
        [
          "bash",
          new URL("scripts/scan-package-artifacts.sh", packageRoot).pathname,
          temporaryPackage,
        ],
        {
          env: {...process.env, PATH: `${shadowedTools}:${process.env.PATH}`},
          stdout: "ignore",
          stderr: "ignore",
        },
      );
      expect(await scan.exited).toBe(2);
    } finally {
      await rm(temporaryPackage, {recursive: true, force: true});
      await rm(shadowedTools, {recursive: true, force: true});
    }
  });

  it("preserves generated source when postprocessing fails", async () => {
    const temporaryRepository = await mkdtemp(
      join(tmpdir(), "nevabridge-sdk-atomic-generation-"),
    );
    const generationDirectory = join(temporaryRepository, "sdk", "generation");
    const generatedDirectory = join(
      temporaryRepository,
      "sdk",
      "typescript",
      "src",
      "generated",
    );
    const fakeBinaryDirectory = join(temporaryRepository, "fake-bin");
    await mkdir(generationDirectory, {recursive: true});
    await mkdir(generatedDirectory, {recursive: true});
    await mkdir(join(temporaryRepository, "api", "openapi"), {recursive: true});
    await mkdir(fakeBinaryDirectory);
    await writeFile(join(generatedDirectory, "known-good.ts"), "known good\n");
    await writeFile(
      join(generationDirectory, "generate-typescript.sh"),
      await Bun.file(
        new URL("../generation/generate-typescript.sh", packageRoot),
      ).text(),
    );
    await writeFile(
      join(generationDirectory, "postprocess-typescript.ts"),
      await Bun.file(
        new URL("../generation/postprocess-typescript.ts", packageRoot),
      ).text(),
    );
    await writeFile(
      join(generationDirectory, "typescript-config.json"),
      "{}\n",
    );
    await writeFile(
      join(temporaryRepository, "api", "openapi", "nevabridge-v1.yaml"),
      "openapi: 3.1.0\n",
    );
    await writeFile(join(temporaryRepository, "generator.jar"), "fixture\n");
    await writeFile(
      join(fakeBinaryDirectory, "java"),
      `#!/usr/bin/env bash
set -euo pipefail
while [[ $# -gt 0 ]]; do
  if [[ "$1" == "--output" ]]; then
    mkdir -p "$2/src"
    exit 0
  fi
  shift
done
exit 1
`,
    );
    await writeFile(
      join(fakeBinaryDirectory, "sha256sum"),
      "#!/usr/bin/env bash\nexit 0\n",
    );
    await chmod(join(fakeBinaryDirectory, "java"), 0o755);
    await chmod(join(fakeBinaryDirectory, "sha256sum"), 0o755);

    try {
      const initialize = Bun.spawn(["git", "init", "--quiet"], {
        cwd: temporaryRepository,
        stdout: "ignore",
        stderr: "ignore",
      });
      expect(await initialize.exited).toBe(0);
      const generation = Bun.spawn(
        ["bash", join(generationDirectory, "generate-typescript.sh")],
        {
          cwd: temporaryRepository,
          env: {
            ...process.env,
            OPENAPI_GENERATOR_JAR: join(temporaryRepository, "generator.jar"),
            PATH: `${fakeBinaryDirectory}:${process.env.PATH ?? ""}`,
          },
          stdout: "ignore",
          stderr: "ignore",
        },
      );
      expect(await generation.exited).not.toBe(0);
      expect(
        await Bun.file(join(generatedDirectory, "known-good.ts")).text(),
      ).toBe("known good\n");
    } finally {
      await rm(temporaryRepository, {recursive: true, force: true});
    }
  });

  it("restores generated source when the final replacement fails", async () => {
    const temporaryRepository = await mkdtemp(
      join(tmpdir(), "nevabridge-sdk-generation-swap-"),
    );
    const generationDirectory = join(temporaryRepository, "sdk", "generation");
    const generatedDirectory = join(
      temporaryRepository,
      "sdk",
      "typescript",
      "src",
      "generated",
    );
    const fakeBinaryDirectory = join(temporaryRepository, "fake-bin");
    await mkdir(generationDirectory, {recursive: true});
    await mkdir(generatedDirectory, {recursive: true});
    await mkdir(join(temporaryRepository, "api", "openapi"), {recursive: true});
    await mkdir(fakeBinaryDirectory);
    await writeFile(join(generatedDirectory, "known-good.ts"), "known good\n");
    await writeFile(
      join(generationDirectory, "generate-typescript.sh"),
      await Bun.file(
        new URL("../generation/generate-typescript.sh", packageRoot),
      ).text(),
    );
    await writeFile(
      join(generationDirectory, "postprocess-typescript.ts"),
      "// The replacement test needs generation to reach the swap.\n",
    );
    await writeFile(
      join(generationDirectory, "typescript-config.json"),
      "{}\n",
    );
    await writeFile(
      join(temporaryRepository, "api", "openapi", "nevabridge-v1.yaml"),
      "openapi: 3.1.0\n",
    );
    await writeFile(join(temporaryRepository, "generator.jar"), "fixture\n");
    await writeFile(
      join(fakeBinaryDirectory, "java"),
      `#!/usr/bin/env bash
set -euo pipefail
while [[ $# -gt 0 ]]; do
  if [[ "$1" == "--output" ]]; then
    mkdir -p "$2/src"
    printf 'replacement\n' > "$2/src/replacement.ts"
    exit 0
  fi
  shift
done
exit 1
`,
    );
    await writeFile(
      join(fakeBinaryDirectory, "sha256sum"),
      "#!/usr/bin/env bash\nexit 0\n",
    );
    await writeFile(
      join(fakeBinaryDirectory, "mv"),
      `#!/usr/bin/env bash
set -euo pipefail
if [[ "$1" == *".replacement."* && "$2" == */generated ]]; then
  exit 1
fi
exec /usr/bin/mv "$@"
`,
    );
    for (const binary of ["java", "sha256sum", "mv"]) {
      await chmod(join(fakeBinaryDirectory, binary), 0o755);
    }

    try {
      const initialize = Bun.spawn(["git", "init", "--quiet"], {
        cwd: temporaryRepository,
        stdout: "ignore",
        stderr: "ignore",
      });
      expect(await initialize.exited).toBe(0);
      const generation = Bun.spawn(
        ["bash", join(generationDirectory, "generate-typescript.sh")],
        {
          cwd: temporaryRepository,
          env: {
            ...process.env,
            OPENAPI_GENERATOR_JAR: join(temporaryRepository, "generator.jar"),
            PATH: `${fakeBinaryDirectory}:${process.env.PATH ?? ""}`,
          },
          stdout: "ignore",
          stderr: "ignore",
        },
      );
      expect(await generation.exited).not.toBe(0);
      expect(
        await Bun.file(join(generatedDirectory, "known-good.ts")).text(),
      ).toBe("known good\n");
      expect(
        await Bun.file(join(generatedDirectory, "replacement.ts")).exists(),
      ).toBe(false);
    } finally {
      await rm(temporaryRepository, {recursive: true, force: true});
    }
  });

  it("recovers from a failed generator download without poisoning the cache", async () => {
    const temporaryDirectory = await mkdtemp(
      join(tmpdir(), "nevabridge-sdk-generator-cache-"),
    );
    const source = join(temporaryDirectory, "source.jar");
    const destination = join(temporaryDirectory, "cache", "generator.jar");
    const contents = "complete generator";
    await writeFile(source, contents);
    const checksum = new Bun.CryptoHasher("sha256")
      .update(contents)
      .digest("hex");
    const cacheScript = new URL(
      "../generation/cache-generator-jar.sh",
      packageRoot,
    ).pathname;

    try {
      const failedDownload = Bun.spawn(
        ["bash", cacheScript, `file://${source}`, "0".repeat(64), destination],
        {stdout: "ignore", stderr: "ignore"},
      );
      expect(await failedDownload.exited).not.toBe(0);
      expect(await Bun.file(destination).exists()).toBe(false);

      const successfulDownload = Bun.spawn(
        ["bash", cacheScript, `file://${source}`, checksum, destination],
        {stdout: "ignore", stderr: "ignore"},
      );
      expect(await successfulDownload.exited).toBe(0);
      expect(await Bun.file(destination).text()).toBe(contents);
    } finally {
      await rm(temporaryDirectory, {recursive: true, force: true});
    }
  });

  it("rejects an untracked generated artifact", async () => {
    const temporaryRepository = await mkdtemp(
      join(tmpdir(), "nevabridge-sdk-generated-check-"),
    );
    const generatedDirectory = join(temporaryRepository, "generated");
    await mkdir(generatedDirectory);
    await writeFile(join(generatedDirectory, "tracked.ts"), "export {};\n");

    try {
      for (const arguments_ of [
        ["git", "init", "--quiet"],
        ["git", "add", "generated/tracked.ts"],
        [
          "git",
          "-c",
          "user.name=SDK Test",
          "-c",
          "user.email=sdk-test@example.invalid",
          "commit",
          "--quiet",
          "-m",
          "Initial generated output",
        ],
      ]) {
        const command = Bun.spawn(arguments_, {
          cwd: temporaryRepository,
          stdout: "ignore",
          stderr: "ignore",
        });
        expect(await command.exited).toBe(0);
      }

      await writeFile(join(generatedDirectory, "new.ts"), "export {};\n");
      const check = Bun.spawn(
        [
          "bash",
          new URL("scripts/check-generated-clean.sh", packageRoot).pathname,
          temporaryRepository,
          "generated",
        ],
        {stdout: "ignore", stderr: "ignore"},
      );
      expect(await check.exited).toBe(1);
    } finally {
      await rm(temporaryRepository, {recursive: true, force: true});
    }
  });
});
