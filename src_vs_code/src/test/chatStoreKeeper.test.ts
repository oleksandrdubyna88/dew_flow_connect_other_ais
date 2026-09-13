import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, utimesSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { ChatStoreFile, Listing, QUARANTINE_DIR, RetireOutcome } from '../chatStoreFile';
import { ChatStoreKeeper, DebrisOutcome, SweepDeps, runSweep } from '../chatStoreKeeper';
import { LOCK_STALE_MS, lockName } from '../chatStoreLock';
import {
  DEBRIS_AGE_MS,
  HEARTBEAT_STALE_MS,
  HOUSEKEEPING_DIR,
  ORPHAN_GRACE_MS,
  SWEEP_CLAIM_MS,
  SWEEP_MARKER,
  Survey,
  SweepReport,
  heartbeatName,
} from '../chatStoreSweep';
import { CONVERSATION_VERSION, ConversationRecord, KEEP_FOR_MS, besideMeta, recordName, sourceOfSession } from '../chatStore';
import { ChatMessage } from '../chatPage';

/**
 * The store's housekeeping half against a REAL directory: the survey, the heartbeat file, the marker,
 * every removal of debris under the conversation's lock, and the sweep end to end.
 *
 * <p>The rules are values in `chatStoreSweep.test.ts`; what is tested here is that the I/O carries them
 * out and no further — that a removal re-checks under the lock, that a lock is collected only by
 * claiming through the store's own module, that an orphaned transcript is MOVED with its bytes intact,
 * that a store which cannot be read is swept by nothing, and that a second window finds the marker and
 * stands down. Ages are measured from a real `now`, with the files that must read as old aged by
 * `utimes`, because a filesystem stamps what it writes with its own clock.</p>
 */

const said = (role: 'you' | 'model', text: string): ChatMessage => ({ role, text });

function home(): string {
  return mkdtempSync(join(tmpdir(), 'coai-chat-keeper-'));
}

function record(over: Partial<ConversationRecord> & { readonly id: string }): ConversationRecord {
  return {
    version: CONVERSATION_VERSION,
    rev: 1,
    title: 'main',
    passage: 'the passage',
    modelId: 'gpt-5.4',
    messages: [said('you', 'why'), said('model', 'because')],
    fromSession: true,
    carryFrom: 0,
    source: sourceOfSession(`session-${over.id}`),
    workspace: 'D:\\rsd\\coai',
    createdAt: Date.now() - 2_000,
    updatedAt: Date.now() - 1_000,
    ...over,
  };
}

/** Set a file's times to `ms` before now. */
function age(path: string, ms: number, now = Date.now()): void {
  const then = new Date(now - ms);
  utimesSync(path, then, then);
}

/** A lock as another window would leave it, dated `agoMs` before `now`. Aged on disk too, for the survey. */
function rivalLock(dir: string, id: string, agoMs: number, now = Date.now()): string {
  const path = join(dir, lockName(id));
  writeFileSync(path, JSON.stringify({ pid: 1, at: new Date(now - agoMs).toISOString(), token: 'rival:1', doing: 'save 2' }), 'utf8');
  age(path, agoMs, now);

  return path;
}

async function survey(keeper: ChatStoreKeeper): Promise<Survey> {
  const out = await keeper.survey();
  if (out.kind !== 'surveyed') {
    assert.fail(`expected a survey, the keeper said ${out.kind}: ${out.reason}`);
  }

  return out.survey;
}

const said2 = (out: DebrisOutcome): string => (out.kind === 'kept' ? `kept: ${out.why}` : out.kind === 'failed' ? `failed: ${out.reason}` : out.kind);

async function capturing<T>(run: () => Promise<T>, channel: 'error' | 'info' = 'error'): Promise<{ readonly value: T; readonly lines: readonly string[] }> {
  const lines: string[] = [];
  const before = console[channel];
  console[channel] = (...parts: unknown[]) => { lines.push(parts.map(String).join(' ')); };
  try {
    return { value: await run(), lines };
  } finally {
    console[channel] = before;
  }
}

const swept = (report: SweepReport): Extract<SweepReport, { kind: 'swept' }> => {
  if (report.kind !== 'swept') {
    assert.fail(`expected a sweep, it was skipped: ${report.why} ${report.reason}`);
  }

  return report;
};

// ---------------------------------------------------------------------------------------------
// The survey.
// ---------------------------------------------------------------------------------------------

test('the survey sorts every file in the store into what the layout says it is, and opens no transcript', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const keeper = new ChatStoreKeeper(dir);
    const now = Date.now();
    await store.save(record({ id: 'a1' }), 0);
    writeFileSync(join(dir, 'a1.json.100.1.tmp'), '{', 'utf8');
    rivalLock(dir, 'b2', 0, now);
    writeFileSync(join(dir, 'notes.txt'), 'not ours', 'utf8');
    mkdirSync(join(dir, 'stray'));
    await store.quarantine({ x: 1 }, 'coai.chatTabs-entry-0', now);
    await keeper.beat(7, ['a1'], now);
    // The transcript is made unreadable AFTER it was written: a survey that opened it would say so.
    writeFileSync(join(dir, recordName('a1')), 'not json', 'utf8');

    const { value: found, lines } = await capturing(() => survey(keeper));

    assert.deepEqual([...found.records.keys()], ['a1'], 'the record was not found by its id');
    assert.ok((found.records.get('a1')?.size ?? 0) > 0, 'a record carries no size');
    assert.deepEqual([...found.metas.keys()], ['a1']);
    assert.deepEqual([...found.locks.keys()], ['b2']);
    assert.deepEqual(found.temps.map((file) => file.name), ['a1.json.100.1.tmp']);
    assert.deepEqual(found.quarantined.map((file) => file.name), [`coai.chatTabs-entry-0-${now}.json`]);
    assert.deepEqual(found.heartbeats.map((file) => [file.name, file.beat?.pid, file.beat?.ids]), [[heartbeatName(7), 7, ['a1']]]);
    assert.deepEqual([...found.foreign].sort(), ['notes.txt', 'stray'], 'what is not ours was not reported, or our own subdirectories were');
    assert.deepEqual(lines, [], 'the survey said something — it opened a transcript and found it torn');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a store that is not there surveys as empty; a store that will not answer is unavailable, with the path on the console only', async () => {
  const dir = home();
  try {
    assert.deepEqual((await new ChatStoreKeeper(join(dir, 'nowhere')).survey()).kind, 'surveyed');

    const asFile = join(dir, 'chat-conversations');
    writeFileSync(asFile, 'a file where the store should be', 'utf8');
    const { value: out, lines } = await capturing(() => new ChatStoreKeeper(asFile).survey());

    assert.equal(out.kind, 'unavailable', 'a store that could not be read surveyed as something');
    const reason = out.kind === 'unavailable' ? out.reason : '';
    assert.ok(reason.length > 0 && !reason.includes(asFile), 'the reason is empty or carries the path');
    assert.ok(lines.some((line) => line.includes(asFile)), 'the console did not name the store that refused');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a survey that cannot read the heartbeats is unavailable as a whole — a sweep must not believe nothing is protected', async () => {
  const dir = home();
  try {
    await new ChatStoreFile(dir).save(record({ id: 'a1' }), 0);
    writeFileSync(join(dir, HOUSEKEEPING_DIR), 'a file where the heartbeats should be', 'utf8');

    const { value: out } = await capturing(() => new ChatStoreKeeper(dir).survey());

    assert.equal(out.kind, 'unavailable', 'the survey answered with the records and no heartbeats');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a torn heartbeat is surveyed with its name and stamp and no beat, and said with its reason', async () => {
  const dir = home();
  try {
    mkdirSync(join(dir, HOUSEKEEPING_DIR), { recursive: true });
    writeFileSync(join(dir, HOUSEKEEPING_DIR, heartbeatName(9)), '{"version":1,"pid":9,"at":"noon","ids":[]}', 'utf8');

    const { value: found, lines } = await capturing(() => survey(new ChatStoreKeeper(dir)));

    assert.deepEqual(found.heartbeats.map((file) => [file.name, file.beat]), [[heartbeatName(9), undefined]]);
    assert.ok(lines.some((line) => line.includes('at noon is not a UTC instant') && line.includes(heartbeatName(9))), `the torn heartbeat was not said, or not with its reason: ${lines.join(' | ')}`);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------------------------
// The heartbeat file and the marker.
// ---------------------------------------------------------------------------------------------

test('a beat writes the window’s heartbeat, a second beat replaces it, and an id that cannot be a filename is left out and said', async () => {
  const dir = home();
  try {
    const keeper = new ChatStoreKeeper(dir);
    const now = Date.now();

    assert.equal(await keeper.beat(7, ['a1'], now), true);
    assert.deepEqual((await survey(keeper)).heartbeats.map((file) => file.beat?.ids), [['a1']]);

    const { value: landed, lines } = await capturing(() => keeper.beat(7, ['a1', 'b2', '../x'], now + 1));
    assert.equal(landed, true);
    assert.deepEqual((await survey(keeper)).heartbeats.map((file) => file.beat?.ids), [['a1', 'b2']], 'the second beat did not replace the first, or kept an unsafe id');
    assert.ok(lines.some((line) => line.includes('1 open conversation id(s) could not be announced')), 'the dropped id was not said');
    assert.deepEqual(readdirSync(join(dir, HOUSEKEEPING_DIR)), [heartbeatName(7)], 'a temporary or a second file was left');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('no marker reads as none; a claim written reads back as a claim, and a finished marker over it reads back as finished', async () => {
  const dir = home();
  try {
    const keeper = new ChatStoreKeeper(dir);
    const now = Date.now();

    assert.equal(await keeper.marker(), undefined);
    assert.equal(await keeper.writeMarker({ kind: 'claimed', pid: 4242, began: now }), true);
    assert.deepEqual(await keeper.marker(), { kind: 'claimed', pid: 4242, began: now });
    assert.equal(await keeper.writeMarker({ kind: 'finished', pid: 4242, began: now, finished: now + 5_000 }), true);
    assert.deepEqual(await keeper.marker(), { kind: 'finished', pid: 4242, began: now, finished: now + 5_000 });
    assert.deepEqual(readdirSync(join(dir, HOUSEKEEPING_DIR)), [SWEEP_MARKER], 'a temporary or a second file was left');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------------------------
// Debris, each under the lock and re-checked there.
// ---------------------------------------------------------------------------------------------

test('an orphaned index entry is removed only if, with the conversation claimed, its record is still absent', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const keeper = new ChatStoreKeeper(dir);
    const now = Date.now();
    await store.save(record({ id: 'a1' }), 0);
    await store.save(record({ id: 'b2' }), 0);
    rmSync(join(dir, recordName('a1')));

    const gone = await keeper.removeOrphanMeta('a1', now);
    assert.equal(gone.kind, 'removed', `an orphaned index entry was ${said2(gone)}`);
    assert.equal(existsSync(join(dir, besideMeta('a1'))), false, 'the orphaned index entry is still there');

    const kept = await keeper.removeOrphanMeta('b2', now);
    assert.equal(said2(kept), 'kept: a record has appeared beside it', 'an index entry whose record is present was removed');
    assert.equal(existsSync(join(dir, besideMeta('b2'))), true);

    rmSync(join(dir, recordName('b2')));
    rivalLock(dir, 'b2', 0, now);
    const held = await keeper.removeOrphanMeta('b2', now);
    assert.equal(said2(held), 'kept: another window is changing this conversation', 'a removal went ahead under another window\'s lock');
    assert.equal(existsSync(join(dir, besideMeta('b2'))), true);
    assert.deepEqual(readdirSync(dir).filter((name) => name.endsWith('.lock')), [lockName('b2')], 'the rival lock was broken, or ours was left');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('an orphaned transcript is MOVED into quarantine with its bytes untouched — never deleted — and only if no index entry has appeared', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const keeper = new ChatStoreKeeper(dir);
    const now = Date.now();
    await store.save(record({ id: 'a1' }), 0);
    await store.save(record({ id: 'b2' }), 0);
    rmSync(join(dir, besideMeta('a1')));
    const bytes = readFileSync(join(dir, recordName('a1')));

    const { value: moved, lines } = await capturing(() => keeper.quarantineOrphanRecord('a1', now), 'info');

    assert.equal(moved.kind, 'removed', `an orphaned transcript was ${said2(moved)}`);
    assert.equal(existsSync(join(dir, recordName('a1'))), false, 'the transcript is still in the root, holding its id against reuse');
    const at = join(dir, QUARANTINE_DIR, `a1-orphan-${now}.json`);
    assert.equal(existsSync(at), true, 'the transcript was not set aside under its id and the instant');
    assert.deepEqual(readFileSync(at), bytes, 'the set-aside transcript is not byte-for-byte what was there');
    assert.ok(lines.some((line) => line.includes(at)), 'the console did not say where the transcript went');

    const kept = await keeper.quarantineOrphanRecord('b2', now);
    assert.equal(said2(kept), 'kept: an index entry has appeared beside it', 'a listed transcript was set aside');
    assert.equal(existsSync(join(dir, recordName('b2'))), true);

    rmSync(join(dir, besideMeta('b2')));
    rivalLock(dir, 'b2', 0, now);
    assert.equal(said2(await keeper.quarantineOrphanRecord('b2', now)), 'kept: another window is changing this conversation');
    assert.equal(existsSync(join(dir, recordName('b2'))), true, 'a transcript was moved from under another window\'s lock');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a temporary is removed by its name, and a name that is not a temporary of this store is refused, naming the rule', async () => {
  const dir = home();
  try {
    const keeper = new ChatStoreKeeper(dir);
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, 'a1.json.100.1.tmp'), '{', 'utf8');
    writeFileSync(join(dir, 'notes.txt'), 'x', 'utf8');

    assert.equal((await keeper.removeTemp('a1.json.100.1.tmp')).kind, 'removed');
    assert.equal(existsSync(join(dir, 'a1.json.100.1.tmp')), false);

    for (const name of ['notes.txt', '../a1.json.1.2.tmp', 'sub/x.tmp']) {
      const out = await keeper.removeTemp(name);
      assert.equal(out.kind, 'failed', `${name} was accepted as a temporary`);
      assert.match(out.kind === 'failed' ? out.reason : '', /a bare file name ending in \.tmp/u, 'the refusal does not name the rule');
    }
    assert.equal(existsSync(join(dir, 'notes.txt')), true, 'a file that is not ours was removed');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('an abandoned lock is collected by CLAIMING through the store’s own lock module; a live one is kept', async () => {
  // The sweep does not break locks. What it does is claim: the claim breaks a lock older than the lock
  // module's window through that module's verified path, and the release removes what the claim took.
  const dir = home();
  try {
    const keeper = new ChatStoreKeeper(dir);
    const now = Date.now();
    const old = rivalLock(dir, 'a1', DEBRIS_AGE_MS + 60_000, now);
    const fresh = rivalLock(dir, 'b2', 0, now);

    const { value: collected, lines } = await capturing(() => keeper.collectLock('a1', now));
    assert.equal(collected.kind, 'removed', `an hour-old lock was ${said2(collected)}`);
    assert.equal(existsSync(old), false, 'the abandoned lock is still there');
    assert.ok(lines.some((line) => line.includes(`older than ${LOCK_STALE_MS / 1000}s was broken`)), 'the lock was removed by some path other than the lock module\'s own');

    const kept = await keeper.collectLock('b2', now);
    assert.equal(said2(kept), 'kept: the lock is held by a live writer');
    assert.equal(readFileSync(fresh, 'utf8').includes('rival:1'), true, 'a live writer\'s lock was replaced or removed');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('an old quarantined value is removed by its dated name; an undated one is refused', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const keeper = new ChatStoreKeeper(dir);
    await store.quarantine({ x: 1 }, 'old', 1_700_000_000_000);
    writeFileSync(join(dir, QUARANTINE_DIR, 'undated.json'), '{}', 'utf8');

    assert.equal((await keeper.removeQuarantined('old-1700000000000.json')).kind, 'removed');
    const refused = await keeper.removeQuarantined('undated.json');
    assert.equal(refused.kind, 'failed');
    assert.match(refused.kind === 'failed' ? refused.reason : '', /-<instant>\.json/u);
    assert.deepEqual(readdirSync(join(dir, QUARANTINE_DIR)), ['undated.json']);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a heartbeat is removed by its name; the marker is not a heartbeat and is refused', async () => {
  const dir = home();
  try {
    const keeper = new ChatStoreKeeper(dir);
    const now = Date.now();
    await keeper.beat(7, [], now);
    await keeper.writeMarker({ kind: 'claimed', pid: 1, began: now });

    assert.equal((await keeper.removeHeartbeat(heartbeatName(7))).kind, 'removed');
    const refused = await keeper.removeHeartbeat(SWEEP_MARKER);
    assert.equal(refused.kind, 'failed');
    assert.match(refused.kind === 'failed' ? refused.reason : '', /window-<pid>\.json/u);
    assert.deepEqual(readdirSync(join(dir, HOUSEKEEPING_DIR)), [SWEEP_MARKER]);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------------------------
// The sweep, end to end.
// ---------------------------------------------------------------------------------------------

interface World {
  readonly dir: string;
  readonly store: ChatStoreFile;
  readonly keeper: ChatStoreKeeper;
  readonly now: number;
}

/** A store with one of everything the sweep retires, and one of everything it must leave alone. */
async function world(): Promise<World> {
  const dir = home();
  const store = new ChatStoreFile(dir);
  const keeper = new ChatStoreKeeper(dir);
  const now = Date.now();
  const expired = now - KEEP_FOR_MS - 60_000;
  await store.save(record({ id: 'a1', updatedAt: expired }), 0); // expired, nobody holds it
  await store.save(record({ id: 'b2', updatedAt: expired }), 0); // expired, THIS window holds it
  await store.save(record({ id: 'c3', updatedAt: expired }), 0); // expired, ANOTHER window holds it
  await store.save(record({ id: 'd4' }), 0); // fresh
  await store.save(record({ id: 'e5' }), 0); // will be an orphaned index entry
  rmSync(join(dir, recordName('e5')));
  await store.save(record({ id: 'f6' }), 0); // will be an orphaned transcript, past the grace period
  rmSync(join(dir, besideMeta('f6')));
  age(join(dir, recordName('f6')), ORPHAN_GRACE_MS + 60_000, now);
  await store.save(record({ id: 'g7' }), 0); // an orphaned transcript inside the grace period
  rmSync(join(dir, besideMeta('g7')));
  writeFileSync(join(dir, 'x.json.1.1.tmp'), '{', 'utf8');
  age(join(dir, 'x.json.1.1.tmp'), DEBRIS_AGE_MS + 60_000, now);
  writeFileSync(join(dir, 'y.json.1.2.tmp'), '{', 'utf8'); // young
  rivalLock(dir, 'h8', DEBRIS_AGE_MS + 60_000, now); // abandoned
  rivalLock(dir, 'i9', 0, now); // live
  await store.quarantine({ old: true }, 'old', now - KEEP_FOR_MS - 1);
  await store.quarantine({ young: true }, 'young', now - 1);
  await keeper.beat(7, ['c3'], now - 60_000); // another window, alive
  await keeper.beat(8, ['a1'], now - HEARTBEAT_STALE_MS - 60_000); // a window that died holding a1

  return { dir, store, keeper, now };
}

const deps = (w: World, over: Partial<SweepDeps> = {}): SweepDeps => ({
  store: w.store,
  keeper: w.keeper,
  held: () => new Set(['b2']),
  pid: 4242,
  now: w.now,
  ...over,
});

test('the sweep retires what is expired and unheld, sets aside what is orphaned past the grace, collects the debris, and leaves everything else', async () => {
  const w = await world();
  try {
    const { value: report } = await capturing(() => runSweep(deps(w)), 'info');
    const done = swept(report);

    assert.equal(existsSync(join(w.dir, recordName('a1'))), false, 'the expired conversation nobody holds was kept');
    assert.equal(existsSync(join(w.dir, besideMeta('a1'))), false);
    assert.equal(existsSync(join(w.dir, recordName('b2'))), true, 'the expired conversation THIS window holds was retired');
    assert.equal(existsSync(join(w.dir, recordName('c3'))), true, 'the expired conversation ANOTHER window holds was retired');
    assert.equal(existsSync(join(w.dir, recordName('d4'))), true, 'a fresh conversation was retired');
    assert.equal(existsSync(join(w.dir, besideMeta('e5'))), false, 'the orphaned index entry was kept');
    assert.equal(existsSync(join(w.dir, recordName('f6'))), false, 'the orphaned transcript past the grace was kept in the root');
    assert.ok(readdirSync(join(w.dir, QUARANTINE_DIR)).some((name) => name.startsWith('f6-orphan-')), 'the orphaned transcript was not set aside');
    assert.equal(existsSync(join(w.dir, recordName('g7'))), true, 'an orphaned transcript inside the grace period was taken');
    assert.equal(existsSync(join(w.dir, 'x.json.1.1.tmp')), false, 'the stale temporary was kept');
    assert.equal(existsSync(join(w.dir, 'y.json.1.2.tmp')), true, 'a young temporary was removed');
    assert.equal(existsSync(join(w.dir, lockName('h8'))), false, 'the abandoned lock was kept');
    assert.equal(existsSync(join(w.dir, lockName('i9'))), true, 'a live lock was collected');
    assert.equal(readdirSync(join(w.dir, QUARANTINE_DIR)).some((name) => name.startsWith('old-')), false, 'the old quarantined value was kept');
    assert.equal(readdirSync(join(w.dir, QUARANTINE_DIR)).some((name) => name.startsWith('young-')), true, 'a young quarantined value was removed');
    assert.equal(existsSync(join(w.dir, HOUSEKEEPING_DIR, heartbeatName(8))), false, 'the stale heartbeat was kept');
    assert.equal(existsSync(join(w.dir, HOUSEKEEPING_DIR, heartbeatName(7))), true, 'a live heartbeat was removed');
    const marker = await w.keeper.marker();
    assert.equal(marker?.kind, 'finished', `a sweep that walked its whole plan left no finished marker for the other windows: ${JSON.stringify(marker)}`);
    assert.equal(marker?.pid, 4242);
    assert.equal(marker?.began, w.now);
    assert.deepEqual(readdirSync(w.dir).filter((name) => name.endsWith('.lock')), [lockName('i9')], 'a lock of the sweep\'s own was left');

    assert.deepEqual(done, {
      kind: 'swept',
      retired: 1,
      keptUnderLock: 0,
      orphanMetasRemoved: 1,
      recordsQuarantined: 1,
      tempsRemoved: 1,
      locksCollected: 1,
      quarantineRemoved: 1,
      heartbeatsRemoved: 1,
      failed: 0,
      unfinished: false,
    });
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});

test('a second window finds the marker and stands down; forced, a second sweep finds nothing left to do', async () => {
  const w = await world();
  try {
    await capturing(() => runSweep(deps(w)), 'info');

    assert.deepEqual(await runSweep(deps(w, { pid: 1 })), { kind: 'skipped', why: 'recent', reason: '' }, 'a second window swept behind a recent marker');
    const again = swept(await runSweep(deps(w, { pid: 1, force: true })));
    assert.equal(again.retired + again.orphanMetasRemoved + again.recordsQuarantined + again.tempsRemoved + again.locksCollected + again.quarantineRemoved + again.heartbeatsRemoved, 0,
      `a forced second sweep found work the first had left: ${JSON.stringify(again)}`);
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});

test('a store that cannot be read is swept by nothing, and no marker is written', async () => {
  const dir = home();
  try {
    const asFile = join(dir, 'chat-conversations');
    writeFileSync(asFile, 'a file where the store should be', 'utf8');

    const { value: report } = await capturing(() => runSweep({
      store: new ChatStoreFile(asFile),
      keeper: new ChatStoreKeeper(asFile),
      held: () => new Set(),
      pid: 1,
      now: Date.now(),
    }));

    assert.equal(report.kind, 'skipped');
    assert.equal(report.kind === 'skipped' ? report.why : '', 'unavailable', 'a store that would not answer was read as a directory of expired things');
    assert.equal(readFileSync(asFile, 'utf8'), 'a file where the store should be', 'the sweep changed what was there');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a store that is not there yet is not swept and not created', async () => {
  const dir = home();
  try {
    const nowhere = join(dir, 'chat-conversations');
    const report = await runSweep({ store: new ChatStoreFile(nowhere), keeper: new ChatStoreKeeper(nowhere), held: () => new Set(), pid: 1, now: Date.now() });

    assert.deepEqual(report, { kind: 'skipped', why: 'empty', reason: '' });
    assert.equal(existsSync(nowhere), false, 'the sweep created the store it was asked to sweep');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('what this window opens AFTER the plan was made is still spared — `held` is read at the moment of each decision', async () => {
  // A tab restored a moment ago: the plan nominated the id, and by the time the sweep reaches it the
  // window holds it. The heartbeat is a minute behind; the registry is not.
  const w = await world();
  try {
    let asked = 0;
    const { value: report } = await capturing(() => runSweep(deps(w, {
      held: () => {
        asked += 1;

        // The first ask is the plan's; every later one — the decisions — finds a1 open too.
        return asked === 1 ? new Set(['b2']) : new Set(['a1', 'b2']);
      },
    })), 'info');

    assert.equal(existsSync(join(w.dir, recordName('a1'))), true, 'a conversation opened after the plan was made was retired');
    assert.equal(swept(report).retired, 0);
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});

/** A store in which a person speaks in `a1` in the instant before the sweep reaches for it — after the listing, before the lock. */
class Spoken extends ChatStoreFile {
  public constructor(dir: string, private readonly at: number) {
    super(dir);
  }

  public override async retireIfExpired(id: string, seenRev: number, now?: number): Promise<RetireOutcome> {
    if (id === 'a1') {
      assert.equal((await this.save(record({ id: 'a1', updatedAt: this.at - 1_000 }), 1)).kind, 'ok', 'the turn that landed was not saved');
    }

    return super.retireIfExpired(id, seenRev, now);
  }
}

test('an expiry is re-checked under the lock: a conversation written since the listing is kept and counted, not retired', async () => {
  const w = await world();
  try {
    // The plan nominated a1 at rev 1 from the listing; a turn then lands (rev 2) before the sweep takes
    // the lock. Written and COMPLETED before the claim, deliberately: a save that arrives after the
    // delete is a different story, and A3's swap refuses it and forks rather than loses it.
    const { value: report } = await capturing(() => runSweep(deps(w, { store: new Spoken(w.dir, w.now) })), 'info');

    const done = swept(report);
    assert.equal(existsSync(join(w.dir, recordName('a1'))), true, 'a conversation spoken in after the listing was destroyed');
    assert.equal(done.retired, 0, 'the sweep reported retiring what it must have kept');
    assert.equal(done.keptUnderLock, 1, 'the conversation kept under the lock was not counted');
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});

test('the budget bounds the sweep: what is not reached waits, and the report says so', async () => {
  const w = await world();
  try {
    // A clock that has already run past the budget when the first decision is checked.
    let ticks = 0;
    const clock = (): number => {
      ticks += 1;

      return ticks === 1 ? w.now : w.now + 10 * 60_000;
    };
    const { value: report } = await capturing(() => runSweep(deps(w, { clock })), 'info');

    const done = swept(report);
    assert.equal(done.unfinished, true, 'a sweep that ran out of budget reported itself finished');
    assert.equal(existsSync(join(w.dir, recordName('a1'))), true, 'work was done after the budget ran out');
    assert.equal(done.retired, 0);
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------------------------
// The marker: a CLAIM while the sweep runs, and a day's marker only once it has done its work.
// ---------------------------------------------------------------------------------------------

test('a sweep that failed before doing any work does not stand every window down for a day', async () => {
  // The marker used to be written before the survey. One transient failure then left it standing,
  // and every window opening for the next day read it and did nothing. (codex and the local round.)
  const w = await world();
  try {
    // The survey needs the quarantine subdirectory; a file where it should be makes the survey fail
    // AFTER the marker check and the claim, while the store itself reads fine.
    rmSync(join(w.dir, QUARANTINE_DIR), { recursive: true, force: true });
    writeFileSync(join(w.dir, QUARANTINE_DIR), 'a file where the quarantine should be', 'utf8');
    const { value: first } = await capturing(() => runSweep(deps(w)));
    assert.equal(first.kind, 'skipped');
    assert.equal(first.kind === 'skipped' ? first.why : '', 'unavailable', 'the sweep ran against a survey that failed');
    assert.equal(existsSync(join(w.dir, recordName('a1'))), true, 'the failed sweep deleted something');
    assert.equal((await w.keeper.marker())?.kind, 'claimed', 'a sweep that did no work left a FINISHED marker');

    rmSync(join(w.dir, QUARANTINE_DIR));
    // Once the claim has aged out — and a day short of the marker a finished sweep leaves.
    const { value: second } = await capturing(() => runSweep(deps(w, { pid: 1, now: w.now + SWEEP_CLAIM_MS })), 'info');

    assert.equal(second.kind, 'swept', `a sweep that failed before doing any work left a marker that stood every window down for a day: ${JSON.stringify(second)}`);
    assert.equal(existsSync(join(w.dir, recordName('a1'))), false, 'the second sweep did not do the work the first could not');
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});

test('an unfinished sweep leaves itself due — the backlog it did not reach is not put off for a day', async () => {
  // The first run on a store nobody has ever swept: the budget runs out, the report says
  // `unfinished`, and the marker used to say "swept today" regardless. (codex.)
  const w = await world();
  try {
    let ticks = 0;
    const spent = (): number => {
      ticks += 1;

      return ticks === 1 ? w.now : w.now + 10 * 60_000;
    };
    const { value: first } = await capturing(() => runSweep(deps(w, { clock: spent })), 'info');
    assert.equal(swept(first).unfinished, true, 'the test did not produce an unfinished sweep');
    assert.equal(existsSync(join(w.dir, recordName('a1'))), true);
    assert.equal((await w.keeper.marker())?.kind, 'claimed', 'an unfinished sweep wrote the marker that holds for a day');

    const { value: second } = await capturing(() => runSweep(deps(w, { pid: 1, now: w.now + SWEEP_CLAIM_MS })), 'info');

    assert.equal(second.kind, 'swept', `an unfinished sweep blocked its own continuation for a day: ${JSON.stringify(second)}`);
    assert.equal(existsSync(join(w.dir, recordName('a1'))), false, 'the continuation did not reach the work the first sweep left');
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});

test('a sweep whose claim cannot be written does not run — six windows that cannot see each other must not all sweep', async () => {
  const w = await world();
  try {
    // A directory where the marker file goes: the marker reads as none, and the claim's rename over it fails.
    mkdirSync(join(w.dir, HOUSEKEEPING_DIR, SWEEP_MARKER));
    const { value: report } = await capturing(() => runSweep(deps(w)), 'info');

    assert.equal(report.kind, 'skipped', `the sweep ran without a claim: ${JSON.stringify(report)}`);
    assert.equal(report.kind === 'skipped' ? report.why : '', 'unclaimed');
    assert.equal(existsSync(join(w.dir, recordName('a1'))), true, 'the sweep deleted without having claimed the store');
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});

/** A store whose listing waits for the test to say go — a sweep held mid-way, its claim taken and its work not yet done. */
class Gated extends ChatStoreFile {
  public open: () => void = () => undefined;

  private readonly gate = new Promise<void>((resolve) => {
    this.open = resolve;
  });

  public override async listMeta(): Promise<Listing> {
    await this.gate;

    return super.listMeta();
  }
}

test('a second window arriving while the first is mid-sweep stands down, and after the first has finished stands down for a day', async () => {
  const w = await world();
  try {
    const gated = new Gated(w.dir);
    const first = capturing(() => runSweep(deps(w, { store: gated })), 'info');
    // Let the first sweep reach its listing: state, marker, claim and survey are behind it.
    await new Promise<void>((resolve) => { setTimeout(resolve, 50); });

    const during = await runSweep(deps(w, { pid: 1, now: w.now + 1_000 }));
    assert.equal(during.kind, 'skipped', `a second window swept beside one that was mid-sweep: ${JSON.stringify(during)}`);
    assert.equal(during.kind === 'skipped' ? during.why : '', 'sweeping', 'the second window was not told a sweep is running');

    gated.open();
    swept((await first).value);
    const after = await runSweep(deps(w, { pid: 1, now: w.now + 3_600_000 }));
    assert.equal(after.kind, 'skipped', 'a second window swept an hour after the first had finished');
    assert.equal(after.kind === 'skipped' ? after.why : '', 'recent');
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});

test('a marker of another shape, or a torn one, reads as none AND is said with its path and its reason', async () => {
  // A value somebody could hand-edit must not vanish silently into "no marker". (The code round.)
  const dir = home();
  try {
    const keeper = new ChatStoreKeeper(dir);
    mkdirSync(join(dir, HOUSEKEEPING_DIR), { recursive: true });
    const path = join(dir, HOUSEKEEPING_DIR, SWEEP_MARKER);

    writeFileSync(path, '{"version":1,"pid":7,"began":"yesterday"}', 'utf8');
    const { value: shaped, lines: shapedLines } = await capturing(() => keeper.marker());
    assert.equal(shaped, undefined, 'a marker of another shape was believed');
    assert.ok(shapedLines.some((line) => line.includes(path) && /began yesterday is not a UTC instant/u.test(line)), `the malformed marker was not said with its path and reason: ${shapedLines.join(' | ')}`);

    writeFileSync(path, '{{', 'utf8');
    const { value: torn, lines: tornLines } = await capturing(() => keeper.marker());
    assert.equal(torn, undefined, 'a torn marker was believed');
    assert.ok(tornLines.some((line) => line.includes(path)), `the torn marker was not said with its path: ${tornLines.join(' | ')}`);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------------------------
// A record that changed under the lock: the retire writes nothing; the SWEEP repairs the index.
// ---------------------------------------------------------------------------------------------

test('a stale index entry under an expired record is repaired by the sweep through the store’s read, and retired only by a later sweep that lists the agreeing revision', async () => {
  // The crash between a save's two renames: the record is at rev 2, the index still says rev 1, and
  // both are expired. The listing nominates rev 1; the retire finds rev 2 and keeps it. What repairs
  // the index is the sweep asking the store to read the record — the read's own reconciliation, under
  // its own lock — not a write hidden inside the retire. The NEXT sweep lists rev 2 and retires it.
  const w = await world();
  try {
    const expired = w.now - KEEP_FOR_MS - 60_000;
    await w.store.save(record({ id: 'j1', updatedAt: expired }), 0);
    await w.store.save(record({ id: 'j1', updatedAt: expired }), 1);
    const staleText = readFileSync(join(w.dir, besideMeta('j1')), 'utf8').replace('"rev":2', '"rev":1');
    assert.notEqual(staleText.indexOf('"rev":1'), -1, 'the test could not make the index entry stale');
    writeFileSync(join(w.dir, besideMeta('j1')), staleText, 'utf8');

    const { value: first } = await capturing(() => runSweep(deps(w)), 'info');
    assert.equal(existsSync(join(w.dir, recordName('j1'))), true, 'a record whose revision differed from the listing was retired');
    assert.equal(swept(first).keptUnderLock, 1, 'the changed record was not counted as kept under the lock');
    assert.match(readFileSync(join(w.dir, besideMeta('j1')), 'utf8'), /"rev":2/u, 'the stale index entry was not repaired by the sweep');

    const { value: second } = await capturing(() => runSweep(deps(w, { force: true })), 'info');
    assert.equal(swept(second).retired, 1, 'with the listing agreeing, the expired conversation was still kept');
    assert.equal(existsSync(join(w.dir, recordName('j1'))), false);
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});
