// The driver of the cold-start measurement, for a host where a test runner cannot go: a Remote-WSL window (scripts/measure-remote-wsl.ps1).
// It runs the SAME steps as src/test/host/measureScenario.ts and writes the SAME raw record (schema 1; scripts/coldstart-lib.mjs reads it):
//   1. the ConnectOtherAIs extension must not be active yet;  2. the clock starts HERE, just before the command is dispatched;
//   3. `workbench.view.extension.coai` opens the sidebar;     4. wait for a `ready` mark made at or after the clock;
//   5. read the marks through `coai.__measure` (registered because COAI_MEASURE=1 reached the extension host) and write the record.
// The record goes to `coai-measure-result.json` in the first workspace folder, which the script opened and reads back over \\wsl.localhost.
// On any failure the file holds `{ "schema": 1, "error": "..." }` so the script says why instead of timing out. The window is closed after.
// The driver is `extensionKind: workspace`, so it runs in the same extension host as ConnectOtherAIs: both clocks are one process's.
'use strict';
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const vscode = require('vscode');

const EXTENSION = 'remsoftdev.connect-other-ais';
const OPEN_SIDEBAR = 'workbench.view.extension.coai';
const MEASURE = 'coai.__measure';
// Both can be shortened from the environment, which only the driver's own test does.
const SETTLE_MS = Number(process.env.COAI_MEASURE_DRIVER_SETTLE_MS ?? '1500');
const WAIT_MS = Number(process.env.COAI_MEASURE_DRIVER_WAIT_MS ?? '30000');

const clock = () => performance.timeOrigin + performance.now();
const sleep = (ms) => new Promise((wake) => setTimeout(wake, ms));

/** The marks, or undefined while the command is not there yet or the call failed. */
async function marksNow() {
  try {
    return await vscode.commands.executeCommand(MEASURE);
  } catch {
    return undefined;
  }
}

async function measure() {
  const extension = vscode.extensions.getExtension(EXTENSION);
  if (extension === undefined) {
    throw new Error(`${EXTENSION} is not installed in this extension host (remoteName ${String(vscode.env.remoteName)})`);
  }
  const startedActive = extension.isActive;
  if (startedActive) {
    throw new Error('the extension was already active before the clock started: this is not a cold start');
  }

  const t0 = clock();
  await vscode.commands.executeCommand(OPEN_SIDEBAR);
  let report;
  for (const deadline = Date.now() + WAIT_MS; Date.now() < deadline;) {
    report = (await marksNow()) ?? report;
    if (report !== undefined && report.marks.some((one) => one.name === 'ready' && one.at >= t0)) {
      break;
    }
    await sleep(25);
  }
  if (report === undefined) {
    throw new Error(`${MEASURE} never answered: COAI_MEASURE=1 did not reach this extension host (the variable must be in the environment of the remote VS Code server when it starts; see the -StopServers switch of the script), or the extension never activated`);
  }

  return {
    schema: 1,
    t0,
    startedActive,
    marks: report.marks,
    host: report.host,
    // Where this ran, as the driver sees it: the extension host's own place, and where the extension was loaded from.
    driver: { remoteName: vscode.env.remoteName ?? null, platform: process.platform, hostname: os.hostname(), extensionPath: extension.extensionPath, uiKind: vscode.env.uiKind },
  };
}

async function activate() {
  const folder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
  if (folder === undefined) {
    return;
  }
  const out = path.join(folder, 'coai-measure-result.json');
  // `sample-<n>` is the folder's name: the script says which launch this is by where it opens the window.
  const sample = Number(/^sample-(\d+)$/.exec(path.basename(folder))?.[1] ?? '0');
  await sleep(SETTLE_MS);
  let record;
  try {
    record = { ...(await measure()), sample };
  } catch (error) {
    record = { schema: 1, sample, error: error instanceof Error ? error.message : String(error) };
  }
  // Written whole, then renamed, so the script never reads half a file.
  fs.writeFileSync(`${out}.part`, `${JSON.stringify(record, null, 2)}\n`);
  fs.renameSync(`${out}.part`, out);
  await vscode.commands.executeCommand('workbench.action.closeWindow');
}

module.exports = { activate: () => { void activate(); } };
