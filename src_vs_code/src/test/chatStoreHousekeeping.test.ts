import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { ChatStoreFile } from '../chatStoreFile';
import { HeartbeatTimers } from '../chatStoreHeartbeat';
import { Housekeeping, startHousekeeping } from '../chatStoreHousekeeping';
import { HOUSEKEEPING_DIR, SweepReport, heartbeatName } from '../chatStoreSweep';
import { CONVERSATION_VERSION, ConversationRecord, KEEP_FOR_MS, recordName, sourceOfSession } from '../chatStore';
import { ChatMessage } from '../chatPage';

/**
 * The order housekeeping starts in, against a real directory: the heartbeat first, the sweep after
 * what it is told to wait for, the index published after the sweep — and nothing at all against a
 * store that will not answer.
 */

const said = (role: 'you' | 'model', text: string): ChatMessage => ({ role, text });

function home(): string {
  return mkdtempSync(join(tmpdir(), 'coai-chat-housekeeping-'));
}

function record(over: Partial<ConversationRecord> & { readonly id: string }, now: number): ConversationRecord {
  return {
    version: CONVERSATION_VERSION,
    rev: 1,
    title: `title ${over.id}`,
    passage: 'the passage',
    modelId: 'gpt-5.4',
    messages: [said('you', 'why'), said('model', 'because')],
    fromSession: true,
    carryFrom: 0,
    source: sourceOfSession(`session-${over.id}`),
    workspace: 'D:\\rsd\\coai',
    createdAt: now - 2_000,
    updatedAt: now - 1_000,
    ...over,
  };
}

const quietTimers: HeartbeatTimers = { every: () => ({}), stop: () => undefined };

/** A promise a test resolves by hand — the migration, standing in. */
function gate(): { readonly promise: Promise<void>; open: () => void } {
  let open: () => void = () => undefined;
  const promise = new Promise<void>((resolve) => {
    open = resolve;
  });

  return { promise, open };
}

async function quietly<T>(run: () => Promise<T>): Promise<T> {
  const before = { info: console.info, warn: console.warn, error: console.error };
  console.info = () => undefined;
  console.warn = () => undefined;
  console.error = () => undefined;
  try {
    return await run();
  } finally {
    console.info = before.info;
    console.warn = before.warn;
    console.error = before.error;
  }
}

/**
 * Wait for the first heartbeat to have LANDED — the condition, never a duration.
 *
 * <p>This used to be a twenty-millisecond sleep, and it failed about one run in seven under a loaded
 * machine: the write had not finished, and the test reported it as "the heartbeat waited for the
 * migration", which is a sentence about ordering and was not what had happened. A test that fails for
 * a reason its own message denies is worse than no test, and repeated reruns teach a reader to read
 * every red as noise. The sweep's half needs no wait at all: it is gated behind a promise this test
 * holds shut, so "it has not run yet" is true by construction rather than by being quick enough to
 * look. (codex, the plan round.)</p>
 */
const beaten = (housekeeping: Housekeeping): Promise<void> => housekeeping.heartbeat.settled();

test('the heartbeat beats at once; the sweep waits for what it is told to; the index is published after the sweep', async () => {
  const dir = home();
  let housekeeping: Housekeeping | undefined;
  try {
    const store = new ChatStoreFile(dir);
    const now = Date.now();
    await store.save(record({ id: 'old', updatedAt: now - KEEP_FOR_MS - 60_000 }, now), 0);
    await store.save(record({ id: 'fresh' }, now), 0);
    const migration = gate();

    housekeeping = startHousekeeping({ store, held: () => [], after: migration.promise, pid: 4242, clock: () => now, timers: quietTimers });
    await beaten(housekeeping);

    assert.equal(existsSync(join(dir, HOUSEKEEPING_DIR, heartbeatName(4242))), true, 'the heartbeat waited for the migration; a sweep elsewhere could not have known this window was alive');
    assert.equal(existsSync(join(dir, recordName('old'))), true, 'the sweep ran before what it was told to wait for');
    assert.deepEqual(housekeeping.index.state(), { kind: 'building' }, 'the index was published before the sweep had run');

    migration.open();
    const report = await quietly(() => (housekeeping as Housekeeping).ready);

    assert.equal(report?.kind, 'swept');
    assert.equal(existsSync(join(dir, recordName('old'))), false, 'the expired conversation survived the sweep');
    assert.deepEqual(housekeeping.index.entries({ kind: 'everywhere' }).map((meta) => meta.id), ['fresh'], 'the index holds a row for a record the sweep removed, or misses one it kept');
    assert.deepEqual(housekeeping.index.state(), { kind: 'ready', at: now });
  } finally {
    housekeeping?.dispose();
    rmSync(dir, { recursive: true, force: true });
  }
});

test('what this window holds is never swept, before its heartbeat has said so or after', async () => {
  const dir = home();
  let housekeeping: Housekeeping | undefined;
  try {
    const store = new ChatStoreFile(dir);
    const now = Date.now();
    await store.save(record({ id: 'open', updatedAt: now - KEEP_FOR_MS - 60_000 }, now), 0);

    housekeeping = startHousekeeping({ store, held: () => ['open'], after: Promise.resolve(), pid: 4242, clock: () => now, timers: quietTimers });
    const report = await quietly(() => (housekeeping as Housekeeping).ready);

    assert.equal(report?.kind, 'swept');
    assert.equal(existsSync(join(dir, recordName('open'))), true, 'a conversation this window holds open was swept by its age');
    assert.deepEqual(housekeeping.index.entries({ kind: 'everywhere' }).map((meta) => meta.id), ['open']);
  } finally {
    housekeeping?.dispose();
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a first heartbeat that could not land is tried again once the wait is over, and the sweep runs only after it has landed', async () => {
  // The sequence codex found: the first beat's boolean was awaited and discarded, so a transient write
  // failure left this window's tabs unannounced while the sweep ran anyway.
  const dir = home();
  let housekeeping: Housekeeping | undefined;
  try {
    const store = new ChatStoreFile(dir);
    const now = Date.now();
    await store.save(record({ id: 'old', updatedAt: now - KEEP_FOR_MS - 60_000 }, now), 0);
    // A directory where this window's heartbeat FILE goes: the write's rename over it fails, while
    // the housekeeping directory itself lists fine — the survey would succeed, and so would the sweep.
    const heartbeatPath = join(dir, HOUSEKEEPING_DIR, heartbeatName(4242));
    mkdirSync(heartbeatPath, { recursive: true });
    const migration = gate();

    housekeeping = startHousekeeping({ store, held: () => [], after: migration.promise, pid: 4242, clock: () => now, timers: quietTimers });
    await quietly(() => (housekeeping as Housekeeping).heartbeat.settled());
    assert.equal(statSync(heartbeatPath).isDirectory(), true, 'the test did not make the first heartbeat fail');

    // The obstacle is gone by the time the wait is over: the retry lands, and only then does the sweep run.
    rmSync(heartbeatPath, { recursive: true, force: true });
    migration.open();
    const report = await quietly(() => (housekeeping as Housekeeping).ready);

    assert.equal(existsSync(heartbeatPath) && statSync(heartbeatPath).isFile(), true, 'the heartbeat was not tried again after the wait — this window swept without announcing what it holds');
    assert.equal(report?.kind, 'swept', `with the heartbeat landed, the sweep did not run: ${JSON.stringify(report)}`);
    assert.equal(existsSync(join(dir, recordName('old'))), false);
  } finally {
    housekeeping?.dispose();
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a window that cannot announce what it holds — twice — does not sweep at all', async () => {
  const dir = home();
  let housekeeping: Housekeeping | undefined;
  try {
    const store = new ChatStoreFile(dir);
    const now = Date.now();
    await store.save(record({ id: 'old', updatedAt: now - KEEP_FOR_MS - 60_000 }, now), 0);
    mkdirSync(join(dir, HOUSEKEEPING_DIR, heartbeatName(4242)), { recursive: true });

    housekeeping = startHousekeeping({ store, held: () => [], after: Promise.resolve(), pid: 4242, clock: () => now, timers: quietTimers });
    const report = await quietly(() => (housekeeping as Housekeeping).ready);

    assert.equal(existsSync(join(dir, recordName('old'))), true, 'the sweep ran although this window could not announce what it holds');
    assert.equal(report?.kind, 'skipped', `a window that could not announce itself reported a sweep: ${JSON.stringify(report)}`);
    assert.equal(report?.kind === 'skipped' ? report.why : '', 'unannounced');
    // The index is still built: it deletes nothing, and a picker over a full store must not say "none".
    assert.equal(housekeeping.index.state().kind, 'ready', 'the index was not built because the heartbeat could not be written');
  } finally {
    housekeeping?.dispose();
    rmSync(dir, { recursive: true, force: true });
  }
});

test('against a store that will not answer, nothing is swept and the index says so instead of showing nothing', async () => {
  const dir = home();
  let housekeeping: Housekeeping | undefined;
  try {
    const asFile = join(dir, 'chat-conversations');
    writeFileSync(asFile, 'a file where the store should be', 'utf8');

    housekeeping = startHousekeeping({ store: new ChatStoreFile(asFile), held: () => [], after: Promise.resolve(), pid: 4242, clock: Date.now, timers: quietTimers });
    const report = await quietly(() => (housekeeping as Housekeeping).ready);

    assert.deepEqual(report?.kind, 'skipped');
    // The heartbeat gate comes first, and a file where the store should be refuses the heartbeat too:
    // this window could not announce itself, so it does not sweep — the heartbeat's own console line
    // has already named the path. Either answer deletes nothing, which is what is being tested.
    assert.equal(report?.kind === 'skipped' ? report.why : '', 'unannounced');
    assert.equal(readFileSync(asFile, 'utf8'), 'a file where the store should be', 'the sweep changed what it could not read');
    assert.equal(housekeeping.index.state().kind, 'unavailable', 'the index reads as empty rather than as unavailable');
  } finally {
    housekeeping?.dispose();
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a MIGRATION that failed sweeps nothing and still publishes the list, rather than neither', async () => {
  // Both halves matter and they used to fail together. The rejection ran down the chain to the
  // catch, which took the index refresh with it — so the picker said "your saved conversations are
  // still being listed" for the life of the window, over a full store, with no way to reach any of
  // it. And the sweep must still NOT run: a migration that did not finish may have left records the
  // store has not taken over, and retirement by age would delete what the memento still holds.
  // (CodeRabbit, on the pull request.)
  const dir = home();
  let housekeeping: Housekeeping | undefined;
  try {
    const store = new ChatStoreFile(dir);
    const now = Date.now();
    await store.save(record({ id: 'old', updatedAt: now - KEEP_FOR_MS - 60_000 }, now), 0);
    const lines: string[] = [];
    const before = console.error;
    console.error = (...parts: unknown[]) => { lines.push(parts.map(String).join(' ')); };
    let report: SweepReport | undefined;
    try {
      housekeeping = startHousekeeping({ store, held: () => [], after: Promise.reject(new Error('the migration blew up')), pid: 4242, clock: () => now, timers: quietTimers });
      report = await housekeeping.ready;
      // The first beat is still in flight; let it land before the directory goes.
      await housekeeping.heartbeat.settled();
    } finally {
      console.error = before;
    }

    assert.equal(report?.kind, 'skipped', 'a failed migration was followed by a sweep');
    assert.equal(report?.kind === 'skipped' ? report.why : '', 'unmigrated');
    assert.equal(existsSync(join(dir, recordName('old'))), true, 'an expired record was retired although the migration had failed');
    // AND THE LIST IS THERE. This is the half that was broken: a window whose migration failed could
    // not show a person one conversation, for ever.
    assert.deepEqual(housekeeping.index.state(), { kind: 'ready', at: now }, 'the index was never published, so the picker says “still being listed” for ever');
    assert.deepEqual(housekeeping.index.entries({ kind: 'everywhere' }).map((meta) => meta.id), ['old']);
    assert.ok(lines.some((line) => line.includes('the conversation migration failed') && line.includes('the migration blew up')), `the defect was not said: ${lines.join(' | ')}`);
  } finally {
    housekeeping?.dispose();
    rmSync(dir, { recursive: true, force: true });
  }
});
