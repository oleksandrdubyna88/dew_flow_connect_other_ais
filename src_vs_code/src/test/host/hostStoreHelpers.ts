import * as assert from 'node:assert/strict';
import * as vscode from 'vscode';

import type { CoaiExports } from '../../extension';
import type { HostChange, HostStanding } from '../../remoteHosts';
import { until } from './hostHelpers';

/**
 * What the host scenarios that write the hosts store share, kept out of `hostHelpers.ts` (which no unit test may load with `vscode` in it).
 *
 * <p>All of them are about ONE fact of the editor: it keeps one `globalState` value for each extension, sends the whole value on every `update`, and
 * replaces the window's copy when its own copy changes. A write made a moment after another can be taken away by the older write's late news
 * (`research/PLAN_auto_never_asks_a_person_E2_S2_spec.md`, V1 to V3; `storeSettle.ts`). So a scenario never trusts the read that comes right after a
 * write: it reads again a moment later, and when the write is gone it makes it again, a bounded number of times.</p>
 */

type Door = NonNullable<CoaiExports['remoteSessions']>;

const pause = (ms: number): Promise<void> => new Promise((resolve) => { setTimeout(resolve, ms); });

const TRIES = 6;
const STEP_MS = 300;

/**
 * Close the terminals this scenario opened and wait until the window no longer lists them, so the events of their closing (the context key, the
 * round's re-read) are raised before the profile is put back, not after the next scenario began. It never fails: a terminal that stays is reported by what
 * the scenario asserts.
 */
export async function closedAll(terminals: readonly vscode.Terminal[]): Promise<void> {
  for (const one of terminals) {
    one.dispose();
  }
  await until(() => terminals.every((one) => !vscode.window.terminals.includes(one)), 'a terminal of the scenario did not close', 5000).catch(() => undefined);
}

/**
 * A change that stays: it is committed, and read again after a step and after one more; a read that does not show it means the editor took the write
 * away, so the change is committed again. The change of the hosts store is idempotent (a person's "no" for a host, a Forget of a host).
 */
export async function committedAndHeld(door: Door, change: HostChange, held: (standings: readonly HostStanding[]) => boolean, whatDidNotHold: string): Promise<void> {
  for (let attempt = 1; attempt <= TRIES; attempt += 1) {
    await door.commit(change);
    // Polling on purpose — each read must finish before the next one starts (S9382).
    await pause(STEP_MS); // NOSONAR
    if (held(await door.standings())) { // NOSONAR
      await pause(STEP_MS); // NOSONAR
      if (held(await door.standings())) { // NOSONAR
        return;
      }
    }
  }
  assert.fail(`${whatDidNotHold} (committed ${TRIES} times)`);
}

/** The row the list holds for a host, as it is read from the real `globalState`; a person's "no" is committed again when the editor took the list away. */
export async function listRowOf(door: Door, host: string): Promise<{ readonly host: string; readonly how?: string }> {
  for (let attempt = 1; attempt <= TRIES; attempt += 1) {
    const list = ((await door.stored()).list ?? []) as readonly { readonly host: string; readonly how?: string }[]; // NOSONAR
    const row = list.find((one) => one.host === host);
    if (row !== undefined) {
      return row;
    }
    await door.commit({ kind: 'person', host, answer: 'no' }); // NOSONAR
    await pause(STEP_MS); // NOSONAR
  }

  return assert.fail(`the list holds no row for the host ${host} (committed ${TRIES} times)`);
}

/** Wait until an asynchronous check is true; `nudge` is called before each check after the first (a read of the door that repairs a copy). */
export async function untilAsync(ready: () => Promise<boolean>, whatDidNotHappen: string, nudge: () => Promise<unknown> = () => Promise.resolve(), ms = 15_000): Promise<void> {
  const deadline = Date.now() + ms;
  let first = true;
  while (Date.now() < deadline) {
    if (!first) {
      await nudge(); // NOSONAR
    }
    first = false;
    if (await ready()) { // NOSONAR
      return;
    }
    await pause(200); // NOSONAR
  }
  assert.fail(`${whatDidNotHappen} (waited ${Math.round(ms / 1000)}s)`);
}
