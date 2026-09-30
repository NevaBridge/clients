import electronPath from "electron";

// Terminals inside Electron-based editors (VS Code and others) export ELECTRON_RUN_AS_NODE, which
// would start Electron as plain Node.js without its APIs. The example always wants the real app.
const env = {...process.env};
delete env["ELECTRON_RUN_AS_NODE"];

// Extra arguments go to Electron, for example `bun run start --no-sandbox` (see the guide).
const app = Bun.spawn(
  [electronPath as unknown as string, ".", ...process.argv.slice(2)],
  {
    env,
    stdio: ["inherit", "inherit", "inherit"],
  },
);
process.exit(await app.exited);
