import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

import { STORE_REACH_DEADLINE_MS, StoreReach } from '../storeReach';
import { roundsBlock } from '../roundsBlock';
import { counted, snapshot } from './roundCountsFixtures';

/**
 * Whether the other installations' stores are THERE, asked off the render path (E6.S4, "the RENDER path" of the risk paragraph
 * of todo/PLAN_the_round_counter_tail_E6.md): `panelProvider.ts` used to ask `existsSync` while drawing, and a synchronous
 * probe against a share that is not answering (WSL stopped, a NAS unplugged) blocks the extension host for the SMB timeout —
 * which the 8 s spawn cap cannot bound. The render now reads a CACHED answer; the probe runs in the background and loses a
 * race against a deadline.
 */

const WSL = '\\\\wsl.localhost\\Ubuntu\\home\\me\\.local\\share\\coai-mcp';
const NAS = 'D:\\coai-nas';

/** A probe that never answers — a dead share — and counts how often it was asked. */
function deadShare(): { asked: string[]; probe: (path: string) => Promise<boolean> } {
  const asked: string[] = [];

  return { asked, probe: (path) => { asked.push(path); return new Promise<boolean>(() => undefined); } };
}

test('ADeadShare_NeverBlocksTheRender — a probe that never returns reads the store as not answering, and the page draws', async () => {
  const dead = deadShare();
  const reach = new StoreReach(dead.probe, { deadlineMs: 20 });

  // The render's part: a synchronous read of what is cached. It cannot wait on a probe — it is not async.
  const before = reach.current([WSL]);
  assert.deepEqual(before, [], 'a store nobody has probed yet was named before the probe answered');

  const changed = await reach.refresh([WSL]);
  const after = reach.current([WSL]);

  assert.equal(changed, true, 'a probe that lost its race changed what the panel knows, so it must repaint');
  assert.deepEqual(after, [{ directory: WSL, reach: 'not-answering' }]);
  const html = roundsBlock({
    snapshot: snapshot(counted()), chat: [], rateOf: () => undefined, ranOn: () => '',
    store: { side: '', directory: 'C:\\coai', others: after.filter((one) => one.reach === 'here').map((one) => one.directory),
      notAnswering: after.filter((one) => one.reach === 'not-answering').map((one) => one.directory) },
  });
  assert.match(html, /rounds-block/u, 'the page did not draw');
});

test('a dead share costs one bounded check — a refresh while its probe is pending starts no second one', async () => {
  const dead = deadShare();
  const reach = new StoreReach(dead.probe, { deadlineMs: 10, maxAgeMs: 0 });

  await reach.refresh([WSL]);
  await reach.refresh([WSL]);
  await reach.refresh([WSL]);

  assert.equal(dead.asked.length, 1, 'every refresh started another probe against a share that is not answering');
});

test('a probe that settles after its deadline is used by the next refresh — the share came back', async () => {
  let settle: (here: boolean) => void = () => undefined;
  let asked = 0;
  const reach = new StoreReach(() => { asked += 1; return new Promise<boolean>((resolve) => { settle = resolve; }); }, { deadlineMs: 10, maxAgeMs: 0 });

  await reach.refresh([WSL]);
  assert.deepEqual(reach.current([WSL]), [{ directory: WSL, reach: 'not-answering' }]);
  settle(true);
  await reach.refresh([WSL]);

  assert.deepEqual(reach.current([WSL]), [{ directory: WSL, reach: 'here' }]);
  assert.equal(asked, 1);
});

test('the store file is what is probed — coai.db, never read; absent and present are told apart', async () => {
  const asked: string[] = [];
  const reach = new StoreReach(async (path) => { asked.push(path); return path.includes('wsl.localhost'); }, { deadlineMs: 1_000 });

  await reach.refresh([NAS, WSL]);

  assert.deepEqual(asked.map((one) => one.endsWith('coai.db')), [true, true], 'something other than the store file\'s existence was asked');
  assert.deepEqual(reach.current([NAS, WSL]), [{ directory: NAS, reach: 'absent' }, { directory: WSL, reach: 'here' }]);
});

test('an answer is held for its age — a second refresh inside it asks nothing, and a directory the window dropped is forgotten', async () => {
  let now = 1_000;
  let asked = 0;
  const reach = new StoreReach(async () => { asked += 1; return true; }, { deadlineMs: 100, maxAgeMs: 60_000, clock: () => now });

  await reach.refresh([WSL]);
  now += 30_000;
  assert.equal(await reach.refresh([WSL]), false);
  assert.equal(asked, 1);
  now += 31_000;
  await reach.refresh([WSL]);
  assert.equal(asked, 2, 'a stale answer was kept for ever');
  assert.deepEqual(reach.current([]), [], 'a directory the setting no longer lists was still named');
});

test('the panel no longer probes existence while it draws — the Rounds block reads the cached reach (wiring)', () => {
  const text = readFileSync(join(__dirname, '..', '..', 'src', 'panelProvider.ts'), 'utf8')
    .replace(/\/\*[\s\S]*?\*\//gu, ' ')
    .replace(/(^|[^:])\/\/.*$/gmu, '$1');
  const draw = /private async roundsBlockHtml[\s\S]*?\r?\n {2}\}\r?\n/u.exec(text)?.[0] ?? '';

  assert.ok(draw.length > 0, 'roundsBlockHtml was renamed, and this guard is asserting nothing');
  assert.doesNotMatch(draw, /existsSync|statSync|accessSync/u, 'the render probes the other side\'s store synchronously: a dead share blocks the host');
  assert.match(draw, /this\.storeReach\.current\(/u, 'the render does not read the cached reach');
  assert.ok(STORE_REACH_DEADLINE_MS > 0 && STORE_REACH_DEADLINE_MS < 8_000, 'the probe\'s race must end well inside the 8 s spawn cap');
});
