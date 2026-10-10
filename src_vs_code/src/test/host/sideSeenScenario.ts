import * as assert from 'node:assert/strict';
import * as vscode from 'vscode';

import { thisSideLabel } from '../../consultantSides';
import { familyOf } from '../../pathFamily';
import { STAMP_EVERY_MS, type SeenSide } from '../../sidesSeen';

/**
 * Activation stamps this window's side, in a REAL editor (todo/PLAN_paths_per_side.md E2.4 — the release requirement of
 * E0.2: the "both sides known" signal is proven by a `test:host` scenario).
 *
 * <p><b>What it drives for real.</b> The shipped extension, activated by the editor, writing into ITS OWN `globalState` —
 * the store every window of a profile shares — and the registry read back from that same store through what activation
 * returns (`CoaiExports.sidesSeen`: a host has no other way into another extension's `globalState`). The record must name
 * this window's side: the family `process.platform` makes it, the label the This side page names it by, a key built on
 * the extension's own storage path, and a stamp no older than the once-a-day rule allows. The stamp is not awaited by
 * activation, so it is waited for, with a deadline.</p>
 *
 * <p><b>What it does NOT drive.</b> A second window on the other side — the cross-window half was measured by hand
 * (`research/RESULTS_side_registry_reaches_the_other_window.md`) and cannot run in CI; the 90-day window, the pruning
 * and Forget are `sidesSeen.test.ts` / `sidesSeenStore.test.ts`; what the page draws is `sidesSeenOnThisSide.test.ts`.
 * The editor's user-data directory outlives a run, so a stamp from an earlier run the same day satisfies this too — what
 * is proved is that a window of this profile has a fresh record for this side, which is the guarantee.</p>
 */
export async function activationStampsThisSideInARealEditor(): Promise<void> {
  const extension = vscode.extensions.getExtension<{ readonly sidesSeen?: () => readonly SeenSide[] }>('remsoftdev.connect-other-ais');
  assert.ok(extension !== undefined, 'the extension under test is not installed in this host');
  const exports = await extension.activate();
  const sidesSeen = exports?.sidesSeen;
  assert.ok(typeof sidesSeen === 'function', 'activation returned no sidesSeen, so this host cannot read the side registry');

  const label = thisSideLabel(vscode.env.remoteName, process.env['WSL_DISTRO_NAME'] ?? '', process.platform);
  const family = familyOf(process.platform);
  const mine = (): SeenSide | undefined => sidesSeen().find((one) => one.side.includes('remsoftdev.connect-other-ais') && one.label === label);
  const until = Date.now() + 10_000;
  while (mine() === undefined && Date.now() < until) {
    await new Promise((settle) => { setTimeout(settle, 50); });
  }

  const found = mine();
  assert.ok(found !== undefined,
    `activation left no coai.sideSeen@ record for this window's side (${label}); the registry holds ${JSON.stringify(sidesSeen())}`);
  assert.equal(found.family, family, 'this side was stamped with the other family');
  assert.ok(Date.now() - found.lastSeen <= STAMP_EVERY_MS, `this side's record is older than a day: ${new Date(found.lastSeen).toISOString()}`);
}
