import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vscode from 'vscode';

/**
 * ONE cold-start sample, run inside a real extension host (v2 extension.md § 2 "Measurement"; epic E2, story S2).
 *
 * <p>`scripts/run-host.mjs measure` launches a separate editor for each sample, with a user-data directory, an extensions
 * directory and a data directory of that sample's own, so nothing a previous sample left (restored view state, a warm cache
 * in the profile) can have started the extension before the clock. This file is what runs in that editor:</p>
 * <ol>
 *   <li>assert the extension is NOT active (unless the launch is the v1 baseline, `COAI_MEASURE_RECORD_ONLY=1`, whose
 *       extension activates itself at start-up — then the fact is recorded and the driver says so in the result);</li>
 *   <li>start the clock — here, in the driver, just before the command is dispatched — and run
 *       `workbench.view.extension.coai`, which opens the sidebar;</li>
 *   <li>wait for a FRESH `ready` acknowledgement: one the extension marked at or after the clock started; fail if none comes;</li>
 *   <li>read the marks through `coai.__measure` (registered because the launch sets COAI_MEASURE=1) and write the raw record
 *       to the file the launcher named. What M1/M2/M3 are, and whether the sample passes, is decided by the launcher
 *       (`scripts/coldstart-lib.mjs`), from that record.</li>
 * </ol>
 *
 * <p>The clock is `performance.timeOrigin + performance.now()`, the same as the marks, in the same process: the driver
 * and the extension share one extension host.</p>
 */

const EXTENSION = 'remsoftdev.connect-other-ais';
const OPEN_SIDEBAR = 'workbench.view.extension.coai';
const MEASURE = 'coai.__measure';

interface Report {
  readonly schema: 1;
  readonly marks: readonly { readonly name: string; readonly at: number }[];
  readonly host: Record<string, unknown>;
}

const clock = (): number => performance.timeOrigin + performance.now();

/** The marks, or `undefined` while the command is not there yet (the extension has not activated) or the call failed. */
async function marksNow(): Promise<Report | undefined> {
  try {
    return await vscode.commands.executeCommand<Report>(MEASURE);
  } catch {
    return undefined;
  }
}

/** Waits for a `ready` mark at or after `t0`; polling does not affect the numbers, which come from the marks' own times. */
async function freshReady(t0: number, ms: number): Promise<Report | undefined> {
  const deadline = Date.now() + ms;
  let last: Report | undefined;
  while (Date.now() < deadline) {
    last = (await marksNow()) ?? last;
    if (last?.marks.some((one) => one.name === 'ready' && one.at >= t0) === true) {
      return last;
    }
    // Polling on purpose: each probe must finish before the next one starts.
    await new Promise((wake) => setTimeout(wake, 25)); // NOSONAR
  }

  return last;
}

export async function run(): Promise<void> {
  const out = process.env['COAI_MEASURE_OUT'];
  assert.ok(out !== undefined && out !== '', 'COAI_MEASURE_OUT names no file: the launcher did not say where this sample goes');
  assert.equal(process.env['COAI_MEASURE'], '1', 'COAI_MEASURE is not 1 in this host, so coai.__measure is not registered and nothing can be read');
  const recordOnly = process.env['COAI_MEASURE_RECORD_ONLY'] === '1';
  const extension = vscode.extensions.getExtension(EXTENSION);
  assert.ok(extension !== undefined, 'the extension under test is not installed in this host, so nothing below is about it');

  const startedActive = extension.isActive;
  if (!recordOnly) {
    assert.equal(startedActive, false, 'the extension was already active before the clock started: this is not a cold start (restored view state, or an activation event that fires at start-up)');
  }

  const t0 = clock();
  await vscode.commands.executeCommand(OPEN_SIDEBAR);
  const report = await freshReady(t0, 20_000);
  assert.ok(report !== undefined, `${MEASURE} never answered: the extension did not register it (COAI_MEASURE=1 must reach it) or never activated`);
  const fresh = report.marks.some((one) => one.name === 'ready' && one.at >= t0);

  fs.mkdirSync(path.dirname(out), { recursive: true });
  fs.writeFileSync(out, `${JSON.stringify({
    schema: 1,
    sample: Number(process.env['COAI_MEASURE_SAMPLE'] ?? '0'),
    t0,
    startedActive,
    marks: report.marks,
    host: report.host,
  }, null, 2)}\n`);

  assert.ok(fresh, 'no fresh ready acknowledgement from the sidebar within 20 s of the command: the webview never said it had painted');
}
