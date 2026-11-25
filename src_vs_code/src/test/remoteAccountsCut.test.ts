import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { ROUND_GAP_MS, runRound, timerFor } from '../remoteAccountsRound';
import type { AccountsView } from '../remoteAccountsRows';
import { accountsRows } from '../remoteAccountsRows';
import { callsOf, world } from './roundFakes';

/**
 * The cut of the accounts plan's stop: in a build from main the accounts round reads no host. The round is called with its DEFAULTS here, the
 * way the window calls it: a test that passes `'on'` is `remoteAccountsRound.test.ts`'s and tests the modules E2 builds on.
 */

const NOW = Date.UTC(2026, 10, 25, 12, 0, 0);

test('CUT-T1: a timer tick and a Refresh press call no runner, ask no door and write no store row', async () => {
  for (const trigger of ['timer', 'refresh', 'open'] as const) {
    const w = world({ hosts: ['hA', 'hB'] });

    const result = await runRound(w.seams, new Map(), 0, trigger);

    assert.deepEqual(callsOf(w), [], `a ${trigger} round called a runner`);
    assert.deepEqual(w.admits, [], `a ${trigger} round asked the door, which may write an automatic "yes" row`);
    assert.deepEqual(w.dropped, []);
    assert.equal(result.abandoned, true, `a ${trigger} round was not left as abandoned, so its notices could run`);
    assert.deepEqual(result.allowedNow, []);
    assert.deepEqual([...result.memory], [], `a ${trigger} round remembered a host`);
  }
});

test('CUT-T2: no timer is armed, by a terminal that opens or by the end of a round', () => {
  assert.deepEqual(timerFor('arm-first', false), { kind: 'none' }, 'a terminal that opened armed the timer');
  assert.deepEqual(timerFor({ kind: 'at', atMs: 1_000_000 + ROUND_GAP_MS }, false, 1_000_000), { kind: 'none' }, 'the end of a round armed the timer');
  assert.deepEqual(timerFor('keep', false), { kind: 'none' });
});

test('CUT-T3: the list is one fixed row that says the reading is off, whatever the window holds', () => {
  const view = (over: Partial<AccountsView>): AccountsView =>
    ({ live: true, stopped: false, busy: false, memory: new Map(), standings: [], hosts: ['hA'], ignored: undefined, allowNew: true, ...over });
  const said = 'The accounts reading is off in this build: it comes back when the next build asks for one permission.';

  for (const over of [{}, { stopped: true }, { live: false }, { hosts: [], busy: true }, { ignored: 'workspace' as const }]) {
    const rows = accountsRows(view(over), NOW);

    assert.deepEqual(rows, [{ kind: 'info', label: said }], `the list drew something else than the one fixed row for ${JSON.stringify(over)}`);
  }
});
