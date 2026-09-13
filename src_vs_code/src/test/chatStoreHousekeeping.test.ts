import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { ChatStoreFile } from '../chatStoreFile';
import { HeartbeatTimers } from '../chatStoreHeartbeat';
import { Housekeeping, startHousekeeping } from '../chatStoreHousekeeping';
import { HOUSEKEEPING_DIR, heartbeatName } from '../chatStoreSweep';
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

const tick = (): Promise<void> => new Promise<void>((resolve) => { setTimeout(resolve, 20); });

test('the heartbeat beats at once; the sweep waits for what it is told to; the index is published after the sweep', async () => {
  const dir = home();
  let housekeeping: Housekeeping | undefined;
  try {
    const store = new ChatStoreFile(dir);
    const now = Date.now();
    await store.save(record({ id: 'old', updatedAt: now - KEEP_FOR_MS - 60_000 }, now), 0);
    await store.save(record({ id: 'fresh' }, now), 0);
    const migration = gate();

    housekeeping = startHousekeeping({ dir, store, held: () => [], after: migration.promise, pid: 4242, clock: () => now, timers: quietTimers });
    await tick();

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

    housekeeping = startHousekeeping({ dir, store, held: () => ['open'], after: Promise.resolve(), pid: 4242, clock: () => now, timers: quietTimers });
    const report = await quietly(() => (housekeeping as Housekeeping).ready);

    assert.equal(report?.kind, 'swept');
    assert.equal(existsSync(join(dir, recordName('open'))), true, 'a conversation this window holds open was swept by its age');
    assert.deepEqual(housekeeping.index.entries({ kind: 'everywhere' }).map((meta) => meta.id), ['open']);
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

    housekeeping = startHousekeeping({ dir: asFile, store: new ChatStoreFile(asFile), held: () => [], after: Promise.resolve(), pid: 4242, clock: Date.now, timers: quietTimers });
    const report = await quietly(() => (housekeeping as Housekeeping).ready);

    assert.deepEqual(report?.kind, 'skipped');
    assert.equal(report?.kind === 'skipped' ? report.why : '', 'unavailable');
    assert.equal(readFileSync(asFile, 'utf8'), 'a file where the store should be', 'the sweep changed what it could not read');
    assert.equal(housekeeping.index.state().kind, 'unavailable', 'the index reads as empty rather than as unavailable');
  } finally {
    housekeeping?.dispose();
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a defect in the chain is caught and said, never left as an unhandled rejection', async () => {
  const dir = home();
  let housekeeping: Housekeeping | undefined;
  try {
    const lines: string[] = [];
    const before = console.error;
    console.error = (...parts: unknown[]) => { lines.push(parts.map(String).join(' ')); };
    try {
      housekeeping = startHousekeeping({ dir, store: new ChatStoreFile(dir), held: () => [], after: Promise.reject(new Error('the migration blew up')), pid: 4242, clock: Date.now, timers: quietTimers });
      const report = await housekeeping.ready;
      assert.equal(report, undefined, 'a rejected wait was reported as a sweep');
      // The first beat is still in flight; let it land before the directory goes.
      await housekeeping.heartbeat.settled();
    } finally {
      console.error = before;
    }

    assert.ok(lines.some((line) => line.includes('the conversation sweep or index threw') && line.includes('the migration blew up')), `the defect was not said: ${lines.join(' | ')}`);
  } finally {
    housekeeping?.dispose();
    rmSync(dir, { recursive: true, force: true });
  }
});
