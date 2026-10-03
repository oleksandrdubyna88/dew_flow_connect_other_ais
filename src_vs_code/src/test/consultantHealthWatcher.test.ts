import assert from 'node:assert/strict';
import { test } from 'node:test';

import type { FileRead } from '../consultantHealthRead';
import { type ConsultantHealthState, rowHealth } from '../consultantHealthState';
import { CONSULTANT_TAB, ConsultantHealthWatcher, HEALTH_POLL_MS, type HealthWatchPorts, consultantTabShowing } from '../consultantHealthWatcher';
import type { HealthSide } from '../consultantSides';

/**
 * The health files of every side, kept fresh — this side by a file watcher AND a poll, every other side by the poll
 * (E5.4). A watcher on a path outside the workspace is not guaranteed to fire, and on a `\\wsl.localhost` share it
 * cannot be had at all, so the five-second poll is what makes "you will see it" true — the reason
 * `consultationWatcher.ts` and `escalationWatcher.ts` both poll.
 */

const OWN = 'C:/coai';
const WSL = '\\\\wsl.localhost\\Ubuntu\\home\\u\\.local\\share\\coai-mcp';
const SIDES: readonly HealthSide[] = [{ kind: 'this', label: 'Windows', dir: OWN }, { kind: 'other', label: 'WSL: Ubuntu', dir: WSL }];

function bytes(text: string): FileRead {
  return { kind: 'bytes', bytes: new TextEncoder().encode(text) };
}

function check(state: string, startedUtc = '2026-10-03T10:00:00Z'): FileRead {
  return bytes(JSON.stringify({ callerKind: 'claude', state, startedUtc, heartbeatUtc: startedUtc, heartbeatStaleAfterSeconds: 60 }));
}

/** What the fake ports record, and the knobs a test turns. */
interface Box {
  readonly files: Map<string, FileRead>;
  readonly watched: string[];
  readonly polls: number[];
  changed: number;
  ownChanged: number;
  stopped: number;
  now: number;
}

interface Kit extends Box {
  readonly watcher: ConsultantHealthWatcher;
  readonly files: Map<string, FileRead>;
  readonly watched: string[];
  readonly polls: number[];
  changed: number;
  ownChanged: number;
  stopped: number;
  now: number;
}

function kit(): Kit {
  const box: Box = { files: new Map<string, FileRead>(), watched: [], polls: [], changed: 0, ownChanged: 0, stopped: 0, now: Date.parse('2026-10-03T10:00:30Z') };
  const ports: HealthWatchPorts = {
    sides: () => SIDES,
    read: (path) => Promise.resolve(box.files.get(path) ?? { kind: 'absent' }),
    now: () => box.now,
    watch: (dir) => { box.watched.push(dir); return () => { box.stopped += 1; }; },
    every: (ms) => { box.polls.push(ms); return () => { box.stopped += 1; }; },
  };
  const watcher = new ConsultantHealthWatcher(ports);
  watcher.onChanged = () => { box.changed += 1; };
  watcher.onOwnChecksChanged = () => { box.ownChanged += 1; };

  return Object.assign(box, { watcher });
}

const OWN_CHECK = `${OWN}/consultations/health/claude.check.json`;
const WSL_CHECK = `${WSL}/consultations/health/claude.check.json`;

test('this side is watched as files change, and every side is polled every five seconds', () => {
  const one = kit();
  one.watcher.start();

  assert.deepEqual(one.watched, [OWN]);
  assert.deepEqual(one.polls, [HEALTH_POLL_MS]);
  assert.equal(HEALTH_POLL_MS, 5000);
});

test('what each side holds is kept by directory, and the view is told when it changed', async () => {
  const one = kit();
  one.files.set(WSL_CHECK, check('answered'));
  await one.watcher.refresh();

  assert.equal(one.watcher.side(WSL)?.checks['claude']?.kind, 'found');
  assert.equal(one.changed, 1);
});

test('a refresh that found nothing new tells nobody — a repaint per poll would rebuild the page under the person', async () => {
  const one = kit();
  one.files.set(WSL_CHECK, check('answered'));
  await one.watcher.refresh();
  await one.watcher.refresh();

  assert.equal(one.changed, 1);
});

test('a side that could not be read keeps its last snapshot on screen', async () => {
  const one = kit();
  one.files.set(WSL_CHECK, check('answered'));
  await one.watcher.refresh();
  one.files.set(WSL_CHECK, { kind: 'failed', why: 'the distribution is stopped' });
  await one.watcher.refresh();

  assert.equal(one.watcher.side(WSL)?.checks['claude']?.kind, 'found', 'a stopped distribution blanked what a person was reading');
});

test('a check state that moved on THIS side makes the server be asked again — only it can settle its lock', async () => {
  const one = kit();
  one.files.set(OWN_CHECK, check('answered'));
  await one.watcher.refresh();
  const before = one.ownChanged;
  one.files.set(OWN_CHECK, check('checking', '2026-10-03T10:05:00Z'));
  await one.watcher.refresh();

  assert.equal(one.ownChanged, before + 1);
});

test('a check moving on the OTHER side does not re-ask this side\'s server — it knows nothing of that side', async () => {
  const one = kit();
  await one.watcher.refresh();
  const before = one.ownChanged;
  one.files.set(WSL_CHECK, check('checking', '2026-10-03T10:05:00Z'));
  await one.watcher.refresh();

  assert.equal(one.ownChanged, before);
});

test('one refresh at a time — a slow share is not read twice over itself', async () => {
  let release: () => void = () => undefined;
  const gate = new Promise<void>((resolve) => { release = resolve; });
  let reads = 0;
  const watcher = new ConsultantHealthWatcher({
    sides: () => SIDES,
    read: async () => { reads += 1; await gate; return { kind: 'absent' }; },
    now: () => 0,
    watch: () => () => undefined,
    every: () => () => undefined,
  });
  const first = watcher.refresh();
  await watcher.refresh();
  const during = reads;
  release();
  await first;

  assert.equal(during, 18, 'the second refresh read again while the first was still waiting on the share');
});

test('dispose stops the file watcher and the poll', () => {
  const one = kit();
  one.watcher.start();
  one.watcher.dispose();

  assert.equal(one.stopped, 2);
});

test('a new consultants.json on THIS side does not re-ask the server — every --consultants rewrites it, so that would loop', async () => {
  const one = kit();
  const report = (utc: string): FileRead => bytes(JSON.stringify({ utc, side: 'windows', consultants: [] }));
  one.files.set(`${OWN}/consultations/health/consultants.json`, report('2026-10-03T10:00:00Z'));
  await one.watcher.refresh();
  const before = one.ownChanged;
  one.files.set(`${OWN}/consultations/health/consultants.json`, report('2026-10-03T10:01:00Z'));
  await one.watcher.refresh();

  assert.equal(one.ownChanged, before, 'the answer the probe just wrote asked the probe again — an endless loop of --consultants');
});

test('starting a running watcher starts nothing twice — every render that asks may start it', () => {
  const one = kit();
  one.watcher.start();
  one.watcher.start();

  assert.deepEqual(one.watched, [OWN]);
  assert.deepEqual(one.polls, [HEALTH_POLL_MS]);
});

test('paused when the Settings tab closes, it stops polling every side — a WSL share is not woken for a tab nobody sees — and resumes on the next ask', async () => {
  const one = kit();
  one.files.set(WSL_CHECK, check('answered'));
  await one.watcher.refresh();
  one.watcher.start();
  one.watcher.pause();

  assert.equal(one.stopped, 2, 'the poll and the file watcher outlived the tab');
  assert.equal(one.watcher.side(WSL)?.checks['claude']?.kind, 'found', 'pausing threw away what the tab will show when it reopens');
  one.watcher.start();
  assert.deepEqual(one.polls, [HEALTH_POLL_MS, HEALTH_POLL_MS], 'the next ask did not resume the poll');
});

// ---------- the whole-branch review of PLAN_the_consultant_works_on_every_vendor.md (2026-10-03) ----------

test('a new failure on THIS side re-asks the server — the probe never writes the outcome files, so this cannot loop (A2)', async () => {
  const one = kit();
  await one.watcher.refresh();
  const before = one.ownChanged;
  one.files.set(`${OWN}/consultations/health/claude.failure.json`, bytes(JSON.stringify({ utc: '2026-10-03T10:00:40Z', kind: 'quota', vendor: 'codex' })));
  await one.watcher.refresh();

  assert.equal(one.ownChanged, before + 1, 'a consultation failed on this side and the tab went on showing the consultant as healthy');
});

test('a new answer on THIS side re-asks the server too — it may clear the failure shown (A2)', async () => {
  const one = kit();
  await one.watcher.refresh();
  const before = one.ownChanged;
  one.files.set(`${OWN}/consultations/health/claude.answer.json`, bytes(JSON.stringify({ utc: '2026-10-03T10:00:40Z', vendor: 'codex' })));
  await one.watcher.refresh();

  assert.equal(one.ownChanged, before + 1);
});

test('the first read only takes note of this side — opening the tab must not run --consultants a second time (N3)', async () => {
  const one = kit();
  one.files.set(OWN_CHECK, check('answered'));
  await one.watcher.refresh();

  assert.equal(one.ownChanged, 0, 'the render that opened the tab already asked the server; the first read asked it again');
  assert.equal(one.changed, 1, 'the view is still told that the first read landed');
});

/** A WSL check whose heartbeat the WSL side stamps with ITS clock. */
function wslCheck(heartbeatUtc: string): FileRead {
  return bytes(JSON.stringify({ callerKind: 'claude', state: 'checking', startedUtc: '2026-10-03T09:00:00Z', heartbeatUtc, heartbeatStaleAfterSeconds: 60, side: 'wsl' }));
}

/** How the WSL block's check line is judged NOW, from what the watcher holds. */
function shownAcross(one: Kit): string {
  const state: ConsultantHealthState = {
    thisSide: { label: 'Windows', probe: { kind: 'not-installed' }, files: undefined, checking: [], runs: {} },
    otherSides: [{ label: 'WSL: Ubuntu', dir: WSL, files: one.watcher.side(WSL) }],
    nowMs: one.now,
  };

  return rowHealth('claude', { vendor: '', model: '' }, state).otherSides[0]?.check.state ?? '';
}

test('a WSL clock five minutes BEHIND this one: a heartbeat that keeps moving is a live check (O)', async () => {
  const one = kit();
  const behind = (ms: number): string => new Date(ms - 5 * 60_000).toISOString();
  one.files.set(WSL_CHECK, wslCheck(behind(one.now)));
  await one.watcher.refresh();
  one.now += 15_000;
  one.files.set(WSL_CHECK, wslCheck(behind(one.now)));
  await one.watcher.refresh();

  assert.equal(shownAcross(one), 'checking', 'a skewed WSL stamp subtracted from this clock read a live check as abandoned');
});

test('a WSL clock five minutes AHEAD: a heartbeat that stopped moving is abandoned once THIS window has seen it still for the staleness (O)', async () => {
  const one = kit();
  one.files.set(WSL_CHECK, wslCheck(new Date(one.now + 5 * 60_000).toISOString()));
  await one.watcher.refresh();
  one.now += 30_000;
  await one.watcher.refresh();
  assert.equal(shownAcross(one), 'checking', 'thirty seconds of silence is inside the published sixty');
  one.now += 31_000;
  await one.watcher.refresh();

  assert.equal(shownAcross(one), 'abandoned', 'a stamp from the future never aged, so a dead check read Checking… for ever');
});

test('the Consultant tab is watched only while it is SHOWING — the Settings tab visible and holding that tab (N2)', () => {
  assert.equal(consultantTabShowing(true, CONSULTANT_TAB), true);
  assert.equal(consultantTabShowing(false, CONSULTANT_TAB), false, 'a Settings tab behind another editor still spawned --consultants');
  assert.equal(consultantTabShowing(true, 'reviewers'), false, 'the Reviewers tab polled every side for a block nobody can see');
  assert.equal(CONSULTANT_TAB, 'consultant', 'the id must be the section the panel draws the Consultant tab under');
});
