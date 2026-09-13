import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { ChatStoreFile, QUARANTINE_DIR, SaveOutcome } from '../chatStoreFile';
import {
  DIVERGED_TWICE,
  FORK_SUFFIX,
  Fate,
  IMPORT_WIDTH,
  ImportDeps,
  ImportReport,
  allSettled,
  contentsOf,
  describe,
  fateOfSave,
  filedIds,
  forkId,
  importLegacyTabs,
  importSucceeded,
  newerOf,
  overDisk,
} from '../chatStoreImport';
import { ConversationRecord, besideMeta, fromLegacy, recordFrom, recordName, saidIn, sourceOfSession } from '../chatStore';
import { lockName } from '../chatStoreLock';
import { ChatTabMemory, SavedTab, TAB_STORE_KEY, TAB_VERSION, TabStore } from '../chatTabs';
import { ChatMessage } from '../chatPage';

/**
 * The migration of the memento into the store — the one step of this feature that can destroy a
 * person's history — against a REAL temporary directory and a memento fake.
 *
 * <p>Every rule in `chatStoreImport.ts` exists because a reviewer found a way to lose a
 * conversation silently with all tests green, and each has a test here that would have gone red:
 * the store holding an id is not proof its copy is newer (a memento of four turns against a store
 * of two); a `partial` save is not "present"; a damaged record neither vanishes nor wedges the key
 * open; the memento is sealed and re-read before it is emptied; the store is asked once more before
 * the clear; a store that cannot be read migrates nothing; a memento that throws is an outcome, not
 * an exception. The interrupted case — half the records written, the key still populated, the next
 * activation writing only what is missing and emptying the key exactly once — is the plan's own
 * test.</p>
 *
 * <p>Every store copy is written through the store's own writer and read back through its reader,
 * never typed as JSON and cast, so the real validator runs both ways. The memento fake is two
 * methods and a log of what was written to it, because what it is written is the whole question.</p>
 */

const AT = Date.UTC(2026, 8, 13, 12, 0, 0);
const WORKSPACE = 'D:\\rsd\\coai';

function home(): string {
  return mkdtempSync(join(tmpdir(), 'coai-chat-import-'));
}

const said = (role: 'you' | 'model', text: string): ChatMessage => ({ role, text });

/** Two turns, the fixture every comparison starts from. */
const TWO = [said('you', 'why'), said('model', 'because')];
const FOUR = [...TWO, said('you', 'and then'), said('model', 'this')];

function tab(over: Partial<SavedTab> = {}): SavedTab {
  return {
    id: 'a1',
    savedAt: AT,
    title: 'main',
    passage: 'the passage',
    modelId: 'gpt-5.4',
    messages: FOUR,
    fromSession: true,
    carryFrom: 0,
    ...over,
  };
}

/** The memento's value, in the shape every build before this one wrote. */
const memento = (tabs: readonly unknown[]): unknown => ({ version: TAB_VERSION, tabs });

interface FakeMemento {
  readonly store: TabStore;
  /** Every `update` that was accepted, in order. */
  readonly updates: () => readonly (readonly [string, unknown])[];
  readonly held: () => unknown;
  readonly gets: () => number;
}

/**
 * A memento as the host has one: `get` and `update` of a key. `failUpdates` makes the first N writes
 * reject, which is how a host whose storage would not take the clear is simulated; `onGet` lets a
 * test hand back a different value on each read — or do something to the world on a given read —
 * which is how another window writing between the pass and the clear, or a disk going away, is
 * simulated.
 */
function fakeMemento(initial: unknown, options: { failUpdates?: number; onGet?: (call: number, held: unknown) => unknown } = {}): FakeMemento {
  let held = initial;
  let gets = 0;
  let failures = options.failUpdates ?? 0;
  const updates: (readonly [string, unknown])[] = [];

  return {
    store: {
      get: (key: string) => {
        gets += 1;
        if (key !== TAB_STORE_KEY) {
          return undefined;
        }

        return options.onGet === undefined ? held : options.onGet(gets, held);
      },
      update: async (key: string, value: unknown) => {
        await Promise.resolve();
        if (failures > 0) {
          failures -= 1;
          throw new Error('workspaceState is not writable');
        }
        updates.push([key, value]);
        if (key === TAB_STORE_KEY) {
          held = value;
        }
      },
    },
    updates: () => updates,
    held: () => held,
    gets: () => gets,
  };
}

/** A store copy, written through the real writer so the validator runs. Returns the rev it landed at. */
async function onDisk(store: ChatStoreFile, record: ConversationRecord): Promise<number> {
  const outcome = await store.save(record, 0, AT);
  assert.equal(outcome.kind, 'ok', `the fixture could not be written: ${JSON.stringify(outcome)}`);

  return (outcome as { rev: number }).rev;
}

/** The record a read came back with — and a named failure, not a cast, otherwise. */
async function readRecord(store: ChatStoreFile, id: string): Promise<ConversationRecord> {
  const out = await store.read(id, AT);
  if (out.kind !== 'record') {
    assert.fail(`expected a record at ${id}, the read said ${out.kind}`);
  }

  return out.record;
}

/** The ids the store's own listing carries — what the picker would show. */
async function listed(store: ChatStoreFile): Promise<readonly string[]> {
  const listing = await store.listMeta();
  assert.equal(listing.kind, 'listed');

  return (listing as { metas: readonly { id: string }[] }).metas.map((meta) => meta.id).sort();
}

/** The seal and unseal hooks, counted — what the host does with them is `extension.ts`'s, and read there. */
function hooks(): { readonly seals: () => number; readonly unseals: () => number; readonly deps: Pick<ImportDeps, 'seal' | 'unseal'> } {
  let seals = 0;
  let unseals = 0;

  return {
    seals: () => seals,
    unseals: () => unseals,
    deps: {
      seal: async () => { seals += 1; await Promise.resolve(); },
      unseal: () => { unseals += 1; },
    },
  };
}

async function run(store: ChatStoreFile, fake: FakeMemento, extra: Partial<ImportDeps> = {}): Promise<ImportReport> {
  return importLegacyTabs({ memento: fake.store, store, workspace: WORKSPACE, at: AT, ...extra });
}

/** The console, kept quiet where the store is EXPECTED to speak, and its lines for assertion. */
async function capturing<T>(work: () => Promise<T>): Promise<{ readonly value: T; readonly lines: readonly string[] }> {
  const lines: string[] = [];
  const before = { error: console.error, warn: console.warn, info: console.info };
  const capture = (...parts: unknown[]): void => { lines.push(parts.map(String).join(' ')); };
  console.error = capture;
  console.warn = capture;
  console.info = capture;
  try {
    return { value: await work(), lines };
  } finally {
    console.error = before.error;
    console.warn = before.warn;
    console.info = before.info;
  }
}

/** A lock as another, live window would leave it: fresh, and not ours. */
const rivalLock = (dir: string, id: string): void => {
  writeFileSync(join(dir, lockName(id)), JSON.stringify({ pid: 1, at: new Date(AT).toISOString(), token: 'rival:1', doing: 'save 2' }), 'utf8');
};

const fatesOf = (report: ImportReport): readonly Fate[] => (report.kind === 'migrated' || report.kind === 'incomplete' ? report.fates : []);

// ---------------------------------------------------------------------------------------------
// The rules, as values.
// ---------------------------------------------------------------------------------------------

test('which copy is newer is decided by the words: a prefix is older, equal is equal, neither is diverged', () => {
  const two = saidIn(TWO);
  const four = saidIn(FOUR);

  assert.equal(newerOf(four, two), 'memento', 'a memento holding two more turns than the store was not the newer copy');
  assert.equal(newerOf(two, four), 'store');
  assert.equal(newerOf(four, four), 'same');
  assert.equal(newerOf(four, []), 'memento', 'an empty store copy is the beginning of everything');
  assert.equal(newerOf([], []), 'same');
  assert.equal(newerOf([...two, 'X'], [...two, 'Y']), 'diverged');
  assert.equal(newerOf(['a'], ['b']), 'diverged');
});

test('the memento value is sorted into what can be filed and what cannot, and an unsafe id cannot', () => {
  assert.deepEqual(contentsOf(undefined), { kind: 'empty' });
  assert.deepEqual(contentsOf(null), { kind: 'empty' }, 'a memento round-trips undefined to null');
  assert.deepEqual(contentsOf('a conversation'), { kind: 'foreign' });
  assert.deepEqual(contentsOf({ version: TAB_VERSION + 1, tabs: [tab()] }), { kind: 'foreign' },
    'a value of a shape no build wrote was read as records');
  assert.deepEqual(contentsOf({ version: TAB_VERSION, tabs: 'x' }), { kind: 'foreign' });

  const dotted = tab({ id: 'a1.meta' });
  const torn = { ...tab({ id: 'b2' }), savedAt: 'yesterday' };
  const sorted = contentsOf(memento([tab(), dotted, torn]));

  assert.equal(sorted.kind, 'records');
  assert.deepEqual((sorted as { tabs: readonly SavedTab[] }).tabs, [tab()]);
  assert.deepEqual((sorted as { damaged: readonly unknown[] }).damaged, [dotted, torn],
    'an id that cannot be a filename would fail every save with the same sentence for ever, and wedge the key open');
});

test('only a save that landed puts a copy on disk; four of the six answers say nothing about it', () => {
  const outcomes: readonly SaveOutcome[] = [
    { kind: 'ok', rev: 3 },
    { kind: 'partial', rev: 3, reason: 'the index did not land' },
    { kind: 'refused', diskRev: 4 },
    { kind: 'busy' },
    { kind: 'incompatible', reason: 'not one this build can read' },
    { kind: 'failed', reason: 'the disk would not answer' },
  ];
  const fates = outcomes.map((outcome) => fateOfSave('a1', outcome));

  assert.deepEqual(fates[0], { kind: 'written', id: 'a1', indexed: true });
  assert.deepEqual(fates[1], { kind: 'written', id: 'a1', indexed: false }, 'a half-commit was counted as indexed');
  for (const fate of fates.slice(2)) {
    assert.equal(fate.kind, 'unconfirmed', `${JSON.stringify(fate)} was counted as on disk`);
  }
  assert.match((fates[2] as { reason: string }).reason, /another window changed/u);
  assert.match((fates[3] as { reason: string }).reason, /another window is writing/u);
  assert.equal((fates[4] as { reason: string }).reason, 'not one this build can read');
  assert.equal(allSettled(fates), false);
  assert.equal(allSettled(fates.slice(0, 2)), true);
  assert.deepEqual(filedIds([...fates, { kind: 'kept', id: 'k' }, { kind: 'quarantined', at: 'q' }]), ['a1', 'a1', 'k']);
});

test('the fork id is derived from the record\'s own, never minted, so a re-run finds it', () => {
  assert.equal(forkId('a1'), `a1${FORK_SUFFIX}`);
  assert.equal(forkId('x'.repeat(199)), '', 'an id the suffix takes past the limit produced an unsafe filename');
});

test('a memento copy written over the store\'s older one is the DISK record with the memento\'s words over it', () => {
  // gemini, A4's code round: rebuilt from the legacy shape, the write dropped every field the store
  // knew and the memento never did — nothing today, and the durable source or a sweep field the
  // moment a later story adds one. The disk record is spread first, so such a field survives without
  // this function having to name it.
  const disk: ConversationRecord = {
    ...fromLegacy(tab({ messages: TWO, savedAt: AT - 9_000, fromSession: true }), 'E:\\elsewhere'),
    createdAt: AT - 100_000,
    source: sourceOfSession('9f1c-uuid'),
    closedAt: AT - 50,
  };
  // The memento's copy says the OTHER thing about origin — a hand edit; nothing writes that.
  const written = overDisk(tab({ fromSession: false }), disk, WORKSPACE);

  assert.deepEqual(written.messages, FOUR, 'the words are the memento\'s');
  assert.equal(written.createdAt, AT - 100_000, 'the beginning the store recorded was replaced by the memento\'s last write');
  assert.equal(written.updatedAt, AT, 'the last use is the memento\'s');
  assert.equal(written.workspace, 'E:\\elsewhere', 'where the dual write filed it is where it stays');
  assert.equal(written.closedAt, AT - 50, 'a field the memento never had was dropped');
  assert.deepEqual(written.source, sourceOfSession('9f1c-uuid'), 'the durable source was dropped');
  assert.equal(written.fromSession, true, 'origin stopped being a pair: a source naming a session beside a flag saying file');
  assert.notEqual(recordFrom(JSON.parse(JSON.stringify(written))), undefined, 'the written record is one the validator refuses to read back');
  assert.equal(overDisk(tab(), { ...disk, workspace: '' }, WORKSPACE).workspace, WORKSPACE,
    'a store copy filed nowhere did not take this window\'s answer');
  assert.equal(overDisk(tab({ fromSession: false }), { ...disk, source: { kind: 'none' } }, WORKSPACE).fromSession, false,
    'a disk record with no source did not take the memento\'s flag');
});

test('the report says which of the four things happened, and which ids nothing could be said about', () => {
  assert.equal(importSucceeded({ kind: 'nothing' }), true);
  assert.equal(importSucceeded({ kind: 'migrated', fates: [] }), true);
  assert.equal(importSucceeded({ kind: 'unavailable', reason: 'x' }), false);
  assert.equal(importSucceeded({ kind: 'incomplete', fates: [], reason: 'x' }), false);

  const line = describe({
    kind: 'incomplete',
    reason: '1 conversation(s) could not be confirmed on disk',
    fates: [{ kind: 'written', id: 'a1', indexed: true }, { kind: 'unconfirmed', id: 'b2', reason: 'the disk would not answer' }],
  });

  assert.match(line, /NOT fully carried/u);
  assert.match(line, /1 written/u);
  assert.match(line, /b2: the disk would not answer/u, 'the id nothing could be said about is not named');
  assert.match(line, /stays in charge/u);
  assert.match(describe({ kind: 'unavailable', reason: 'EACCES' }), /could not be read \(EACCES\)/u);
});

// ---------------------------------------------------------------------------------------------
// The migration, against a real directory.
// ---------------------------------------------------------------------------------------------

test('every memento record moves into the store, is indexed there, and the key is emptied exactly once', async () => {
  // More records than the pool is wide, so the pool is exercised and the fates still come back in
  // the memento's order.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const tabs = Array.from({ length: IMPORT_WIDTH * 2 + 1 }, (_, at) => tab({
      id: `t${at}`,
      messages: at % 3 === 0 ? [] : at % 3 === 1 ? TWO : FOUR,
      savedAt: AT - at * 1_000,
      fromSession: at % 2 === 0,
      carryFrom: at % 2,
    }));
    const fake = fakeMemento(memento(tabs));
    const sealing = hooks();

    const report = await run(store, fake, sealing.deps);

    assert.equal(report.kind, 'migrated', JSON.stringify(report));
    assert.deepEqual(fatesOf(report).map((fate) => [fate.kind, (fate as { id: string }).id]), tabs.map((one) => ['written', one.id]),
      'the fates are not in the memento\'s order, or not all written');
    for (const one of tabs) {
      const record = await readRecord(store, one.id);
      assert.deepEqual(record.messages, one.messages);
      assert.equal(record.rev, 1);
      assert.deepEqual(record.source, { kind: 'none' }, 'a migrated record invented a source');
      assert.equal(record.workspace, WORKSPACE);
      assert.equal(record.createdAt, one.savedAt, 'a migrated record ages from the migration rather than from its last use');
      assert.equal(record.updatedAt, one.savedAt);
      assert.equal(record.fromSession, one.fromSession);
      assert.equal(record.carryFrom, one.carryFrom);
      assert.equal(record.title, one.title);
      assert.equal(record.passage, one.passage);
    }
    assert.deepEqual(await listed(store), tabs.map((one) => one.id).sort(), 'a migrated conversation is not in the list that finds it');
    assert.deepEqual(fake.updates(), [[TAB_STORE_KEY, undefined]], 'the key was written more than once, or with something other than nothing');
    assert.equal(fake.held(), undefined);
    assert.equal(sealing.seals(), 1, 'the memento was not sealed before the key was emptied');
    assert.equal(sealing.unseals(), 0, 'the memento\'s writer was resumed although the key went');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a store copy that is a PREFIX of the memento\'s is replaced by it — two turns are not deleted', async () => {
  // THE FIRST FATAL FINDING of A4's plan round, as a test. The dual write was best-effort: a store
  // save could come back busy, failed or refused while the memento carried on being written. So the
  // memento can hold four turns where the store holds two under the same id, and a migration that
  // skipped on "the id exists" and then emptied the key would delete two turns with every test green.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const began = AT - 100_000;
    await onDisk(store, { ...fromLegacy(tab({ messages: TWO, savedAt: AT - 9_000 }), WORKSPACE), createdAt: began });
    const fake = fakeMemento(memento([tab({ messages: FOUR, savedAt: AT })]));

    const report = await run(store, fake);

    assert.equal(report.kind, 'migrated', JSON.stringify(report));
    assert.deepEqual(fatesOf(report), [{ kind: 'written', id: 'a1', indexed: true }]);
    const record = await readRecord(store, 'a1');
    assert.deepEqual(record.messages, FOUR, 'the store\'s two-turn copy stood and the memento\'s two extra turns were lost');
    assert.equal(record.rev, 2, 'the write was not a swap against the disk\'s revision');
    assert.equal(record.createdAt, began, 'the conversation\'s real beginning was replaced by the memento\'s last write');
    assert.equal(record.updatedAt, AT);
    assert.deepEqual(fake.updates(), [[TAB_STORE_KEY, undefined]]);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a store copy LONGER than the memento\'s stands, untouched', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await onDisk(store, fromLegacy(tab({ messages: FOUR }), WORKSPACE));
    const before = readFileSync(join(dir, recordName('a1')), 'utf8');
    const fake = fakeMemento(memento([tab({ messages: TWO, savedAt: AT + 60_000 })]));

    const report = await run(store, fake);

    assert.equal(report.kind, 'migrated', JSON.stringify(report));
    assert.deepEqual(fatesOf(report), [{ kind: 'kept', id: 'a1' }]);
    assert.equal(readFileSync(join(dir, recordName('a1')), 'utf8'), before, 'the newer store copy was rewritten');
    assert.equal((await readRecord(store, 'a1')).rev, 1);
    assert.deepEqual(fake.updates(), [[TAB_STORE_KEY, undefined]], 'the key stayed although every record is confirmed');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('equal copies write nothing, and the key is emptied', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await onDisk(store, fromLegacy(tab(), WORKSPACE));
    const before = readFileSync(join(dir, recordName('a1')), 'utf8');
    // The same words with a different mark and model: by the plan-round rule, equal transcripts
    // leave the store's copy standing. What is at stake is a setting, never a word; the residual
    // is named in the module header.
    const fake = fakeMemento(memento([tab({ modelId: 'other-model', carryFrom: 2 })]));

    const report = await run(store, fake);

    assert.equal(report.kind, 'migrated', JSON.stringify(report));
    assert.deepEqual(fatesOf(report), [{ kind: 'kept', id: 'a1' }]);
    assert.equal(readFileSync(join(dir, recordName('a1')), 'utf8'), before, 'an equal copy was rewritten');
    assert.deepEqual(fake.updates(), [[TAB_STORE_KEY, undefined]]);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('diverged copies: the store\'s stands, the memento\'s is filed under a fork of its id, and a re-run does not fork again', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const theirs = [...TWO, said('you', 'X')];
    const ours = [...TWO, said('you', 'Y')];
    await onDisk(store, fromLegacy(tab({ messages: theirs }), WORKSPACE));
    // The clear fails the first time, which is how the second run is made to see the same memento.
    const fake = fakeMemento(memento([tab({ messages: ours })]), { failUpdates: 1 });
    const sealing = hooks();

    const first = await capturing(() => run(store, fake, sealing.deps));

    assert.equal(first.value.kind, 'incomplete', 'the clear that was made to fail was reported as a success');
    assert.match((first.value as { reason: string }).reason, /could not be emptied/u);
    assert.deepEqual(fatesOf(first.value), [{ kind: 'written', id: `a1${FORK_SUFFIX}`, indexed: true, forkedFrom: 'a1' }]);
    assert.deepEqual((await readRecord(store, 'a1')).messages, theirs, 'the store\'s copy was replaced');
    assert.deepEqual((await readRecord(store, `a1${FORK_SUFFIX}`)).messages, ours, 'the memento\'s words are nowhere');
    assert.deepEqual(await listed(store), ['a1', `a1${FORK_SUFFIX}`]);
    assert.equal(sealing.seals(), 1);
    assert.equal(sealing.unseals(), 1, 'the key stayed and the memento\'s writer was not resumed — the next words go to one store only');

    const second = await run(store, fake, sealing.deps);

    assert.equal(second.kind, 'migrated', JSON.stringify(second));
    assert.deepEqual(fatesOf(second), [{ kind: 'kept', id: `a1${FORK_SUFFIX}`, forkedFrom: 'a1' }],
      'a re-run over the same memento filed a second copy');
    assert.deepEqual(await listed(store), ['a1', `a1${FORK_SUFFIX}`]);
    assert.deepEqual(fake.updates(), [[TAB_STORE_KEY, undefined]]);
    assert.equal(sealing.seals(), 2);
    assert.equal(sealing.unseals(), 1, 'the writer was resumed after a clear that succeeded');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a re-run over an existing fork whose memento copy has grown writes over the fork and keeps the fork\'s own beginning', async () => {
  // The fork goes back through the same read-then-compare as any record, so an existing fork is
  // found before anything is written, and what is written over it is the disk record first.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const forkBegan = AT - 777_000;
    await onDisk(store, fromLegacy(tab({ messages: [...TWO, said('you', 'X')] }), WORKSPACE));
    await onDisk(store, { ...fromLegacy(tab({ id: `a1${FORK_SUFFIX}`, messages: [...TWO, said('you', 'Y')], savedAt: AT - 5_000 }), WORKSPACE), createdAt: forkBegan });
    const grown = [...TWO, said('you', 'Y'), said('model', 'Z')];
    const fake = fakeMemento(memento([tab({ messages: grown })]));

    const report = await run(store, fake);

    assert.equal(report.kind, 'migrated', JSON.stringify(report));
    assert.deepEqual(fatesOf(report), [{ kind: 'written', id: `a1${FORK_SUFFIX}`, indexed: true, forkedFrom: 'a1' }]);
    const fork = await readRecord(store, `a1${FORK_SUFFIX}`);
    assert.deepEqual(fork.messages, grown, 'the grown memento copy did not reach the fork');
    assert.equal(fork.rev, 2, 'the fork was not written as a swap against its own revision');
    assert.equal(fork.createdAt, forkBegan, 'the fork\'s own beginning was clobbered by the re-run');
    assert.deepEqual((await readRecord(store, 'a1')).messages, [...TWO, said('you', 'X')], 'the store\'s own copy was touched');
    assert.deepEqual(await listed(store), ['a1', `a1${FORK_SUFFIX}`], 'a third copy appeared');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a fork that would itself diverge is not forked again; the key stays', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await onDisk(store, fromLegacy(tab({ messages: [...TWO, said('you', 'X')] }), WORKSPACE));
    await onDisk(store, fromLegacy(tab({ id: `a1${FORK_SUFFIX}`, messages: [...TWO, said('you', 'Z')] }), WORKSPACE));
    const fake = fakeMemento(memento([tab({ messages: [...TWO, said('you', 'Y')] })]));

    const report = await run(store, fake);

    assert.equal(report.kind, 'incomplete');
    assert.deepEqual(fatesOf(report), [{ kind: 'unconfirmed', id: 'a1', reason: DIVERGED_TWICE }]);
    assert.deepEqual(fake.updates(), [], 'the key was emptied over a record nothing could be said about');
    assert.deepEqual(await listed(store), ['a1', `a1${FORK_SUFFIX}`], 'a third copy appeared');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('an interrupted migration re-runs, writes only what is missing, and empties the key exactly once', async () => {
  // A crash after some files and before the clear: the store holds half the records, the key is
  // still populated. The next activation must carry the other half and clear once — the id is the
  // record's own, so a re-run can neither duplicate nor overwrite.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const tabs = [tab({ id: 'a1' }), tab({ id: 'b2', messages: TWO }), tab({ id: 'c3', messages: [] }), tab({ id: 'd4' })];
    await onDisk(store, fromLegacy(tabs[0] as SavedTab, WORKSPACE));
    await onDisk(store, fromLegacy(tabs[1] as SavedTab, WORKSPACE));
    const first = readFileSync(join(dir, recordName('a1')), 'utf8');
    const fake = fakeMemento(memento(tabs));

    const report = await run(store, fake);

    assert.equal(report.kind, 'migrated', JSON.stringify(report));
    assert.deepEqual(fatesOf(report).map((fate) => [fate.kind, (fate as { id: string }).id]),
      [['kept', 'a1'], ['kept', 'b2'], ['written', 'c3'], ['written', 'd4']]);
    assert.equal(readFileSync(join(dir, recordName('a1')), 'utf8'), first, 'a record already on disk was rewritten');
    assert.deepEqual(await listed(store), ['a1', 'b2', 'c3', 'd4']);
    assert.deepEqual(fake.updates(), [[TAB_STORE_KEY, undefined]], 'the key was not emptied exactly once');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a clear the memento would not take leaves the key, and the next run clears it without writing again', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const fake = fakeMemento(memento([tab()]), { failUpdates: 1 });

    const first = await capturing(() => run(store, fake));

    assert.equal(first.value.kind, 'incomplete');
    assert.equal(importSucceeded(first.value), false, 'a run that left the key in place was reported as a cut-over');
    assert.match(first.lines.join('\n'), /could not be emptied/u, 'the failed clear was swallowed');
    assert.deepEqual((await readRecord(store, 'a1')).messages, FOUR, 'the record was not written before the clear was tried');
    const before = readFileSync(join(dir, recordName('a1')), 'utf8');

    const second = await run(store, fake);

    assert.equal(second.kind, 'migrated', JSON.stringify(second));
    assert.deepEqual(fatesOf(second), [{ kind: 'kept', id: 'a1' }]);
    assert.equal(readFileSync(join(dir, recordName('a1')), 'utf8'), before);
    assert.deepEqual(fake.updates(), [[TAB_STORE_KEY, undefined]]);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a store that cannot be read migrates nothing and leaves the key exactly as it was', async () => {
  // Emptying the source of truth because the disk would not answer is the one unrecoverable
  // mistake available here. A FILE where the directory should be is the portable way to make a
  // store unavailable on Windows, where chmod is a no-op.
  const dir = home();
  const asFile = join(dir, 'store');
  try {
    writeFileSync(asFile, 'a file standing where the store should be', 'utf8');
    const store = new ChatStoreFile(asFile);
    const value = memento([tab(), tab({ id: 'b2' })]);
    const fake = fakeMemento(value);
    const sealing = hooks();

    const { value: report } = await capturing(() => run(store, fake, sealing.deps));

    assert.equal(report.kind, 'unavailable', JSON.stringify(report));
    // The store's own sentence for this shape of fault, handed on unchanged — never a path.
    assert.equal((report as { reason: string }).reason, 'a file is where the conversation store should be');
    assert.deepEqual(fake.updates(), [], 'the memento was written to over a store that would not answer');
    assert.deepEqual(fake.held(), value);
    assert.equal(readFileSync(asFile, 'utf8'), 'a file standing where the store should be', 'the store path was touched');
    assert.equal(sealing.seals(), 0, 'the memento was sealed over a store that would not answer');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a store that goes away between the pass and the clear leaves the key, and the writer resumes', async () => {
  // gemini, A4's code round: the store's health was checked once, before the pass. Emptying the
  // memento on the strength of records held by a store that has stopped answering in between is
  // the same unrecoverable mistake reached a moment later.
  const dir = home();
  const storeDir = join(dir, 'store');
  try {
    const store = new ChatStoreFile(storeDir);
    // Read 1 is the pass; read 2 is the check before the clear — and that is when the disk goes.
    const fake = fakeMemento(memento([tab()]), {
      onGet: (call, held) => {
        if (call === 2) {
          rmSync(storeDir, { recursive: true, force: true });
          writeFileSync(storeDir, 'the volume is gone', 'utf8');
        }

        return held;
      },
    });
    const sealing = hooks();

    const { value: report } = await capturing(() => run(store, fake, sealing.deps));

    assert.equal(report.kind, 'incomplete', JSON.stringify(report));
    assert.match((report as { reason: string }).reason, /stopped answering before the old one could be emptied/u);
    assert.deepEqual(fake.updates(), [], 'the key was emptied although the store had stopped answering');
    assert.equal(sealing.seals(), 1);
    assert.equal(sealing.unseals(), 1, 'the memento\'s writer was left sealed with the key still populated');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a damaged record is set aside as it was, its neighbours migrate, and the key is emptied', async () => {
  // Both of the first brief's bugs at once: counted as missing, a damaged record would keep the key
  // for ever; ignored, it would be deleted with no trace the moment the key went.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const torn = { ...tab({ id: 'b2' }), savedAt: 'yesterday' };
    const dotted = tab({ id: 'c3.meta' });
    const fake = fakeMemento(memento([tab(), torn, tab({ id: 'd4' }), dotted]));

    const { value: report, lines } = await capturing(() => run(store, fake));

    assert.equal(report.kind, 'migrated', JSON.stringify(report));
    const fates = fatesOf(report);
    const quarantined = fates.filter((fate): fate is Extract<Fate, { kind: 'quarantined' }> => fate.kind === 'quarantined');
    assert.equal(quarantined.length, 2);
    for (const [index, raw] of [torn, dotted].entries()) {
      const at = (quarantined[index] as { at: string }).at;
      assert.ok(at.startsWith(join(dir, QUARANTINE_DIR)), `set aside outside the store: ${at}`);
      assert.deepEqual(JSON.parse(readFileSync(at, 'utf8')), raw, 'what was set aside is not what was held');
      assert.ok(lines.some((line) => line.includes(at)), 'the quarantine was not said on the console with its path');
    }
    assert.deepEqual(fates.filter((fate) => fate.kind === 'written').map((fate) => (fate as { id: string }).id), ['a1', 'd4']);
    assert.deepEqual(await listed(store), ['a1', 'd4'], 'the quarantine leaked into the listing');
    assert.deepEqual(fake.updates(), [[TAB_STORE_KEY, undefined]]);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a memento value of a shape no build wrote is set aside whole, and the key is emptied', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const foreign = { version: TAB_VERSION + 7, tabs: [tab()], somebodyElse: true };
    const fake = fakeMemento(foreign);

    const { value: report } = await capturing(() => run(store, fake));

    assert.equal(report.kind, 'migrated', JSON.stringify(report));
    const [fate] = fatesOf(report);
    assert.equal(fate?.kind, 'quarantined');
    assert.deepEqual(JSON.parse(readFileSync((fate as { at: string }).at, 'utf8')), foreign);
    assert.deepEqual(readdirSync(dir), [QUARANTINE_DIR], 'a record was filed from a value nobody could read');
    assert.deepEqual(fake.updates(), [[TAB_STORE_KEY, undefined]]);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('an empty memento is a no-op on disk, and the memento is sealed: the store is the only store from here', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(join(dir, 'never-made'));

    for (const empty of [undefined, null]) {
      const fake = fakeMemento(empty);
      const sealing = hooks();
      const report = await run(store, fake, sealing.deps);

      assert.deepEqual(report, { kind: 'nothing' });
      assert.equal(importSucceeded(report), true, 'an empty memento kept the dual write on');
      assert.deepEqual(fake.updates(), [], 'an empty key was written to');
      assert.equal(sealing.seals(), 1, 'an empty memento was not sealed, so the dual write goes on for nothing');
      assert.equal(sealing.unseals(), 0);
    }
    assert.equal(existsSync(join(dir, 'never-made')), false, 'a store directory was made for nothing');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a record that lands in the key while it is being sealed as empty is carried, not stranded', async () => {
  // The key was empty on the first read; a queued write drained by the seal filled it. The second
  // read sees it, and the ordinary path runs.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const fake = fakeMemento(undefined, { onGet: (call) => (call === 1 ? undefined : memento([tab()])) });
    const sealing = hooks();

    const report = await run(store, fake, sealing.deps);

    assert.equal(report.kind, 'migrated', JSON.stringify(report));
    assert.deepEqual(await listed(store), ['a1'], 'the record the seal drained into the key was stranded there');
    assert.deepEqual(fake.updates(), [[TAB_STORE_KEY, undefined]]);
    assert.ok(sealing.seals() >= 1);
    assert.equal(sealing.unseals(), 0);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a memento holding an empty list is emptied, so the dual write ends', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const fake = fakeMemento(memento([]));

    const report = await run(store, fake);

    assert.equal(report.kind, 'migrated');
    assert.deepEqual(fatesOf(report), []);
    assert.deepEqual(fake.updates(), [[TAB_STORE_KEY, undefined]]);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a write queued a moment before the clear lands BEFORE it, and is carried: the seal drains the memento\'s own queue', async () => {
  // gemini, A4's code round: `ChatTabMemory` queues; without the seal, a write queued before the
  // clear would execute its update after it and fill the key again — and the migration would re-run
  // on every activation for ever.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const fake = fakeMemento(memento([tab()]));
    const memory = new ChatTabMemory(fake.store, () => AT + 1);
    // Queued, not landed: the fake's update yields a tick first, and the migration's first read is
    // synchronous, so the pass sees a memento without `b2`.
    memory.remember({ id: 'b2', title: 'late', passage: '', modelId: 'codex', messages: TWO, fromSession: true, carryFrom: 0 });
    const order: string[] = [];

    const report = await run(store, fake, {
      seal: async () => { order.push('seal'); await memory.settled(); order.push('drained'); },
    });

    assert.equal(report.kind, 'migrated', JSON.stringify(report));
    assert.deepEqual(order, ['seal', 'drained']);
    const writes = fake.updates();
    assert.equal(writes.length, 2, 'the queued write did not land, or landed after the clear and refilled the key');
    assert.deepEqual((writes[0] as readonly [string, { tabs: readonly { id: string }[] }])[1].tabs.map((one) => one.id), ['b2', 'a1'],
      'the queued write is not the one before the clear');
    assert.deepEqual(writes[1], [TAB_STORE_KEY, undefined], 'the clear did not come last');
    assert.deepEqual(await listed(store), ['a1', 'b2'], 'the record the queued write added was not carried');
    assert.deepEqual((await readRecord(store, 'b2')).messages, TWO);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the memento is re-read before it is emptied: a record another window added meanwhile is carried too', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const first = memento([tab()]);
    const second = memento([tab(), tab({ id: 'b2', messages: TWO })]);
    // Read 1 is the pass; read 2 is the check before the clear, and finds a record added meanwhile;
    // read 3 must match read 2 for the clear to go ahead.
    const fake = fakeMemento(first, { onGet: (call) => (call === 1 ? first : second) });

    const report = await run(store, fake);

    assert.equal(report.kind, 'migrated', JSON.stringify(report));
    assert.equal(fake.gets(), 3);
    assert.deepEqual(await listed(store), ['a1', 'b2'], 'the record added between the pass and the clear was erased with the key');
    assert.deepEqual((await readRecord(store, 'b2')).messages, TWO);
    assert.deepEqual(fake.updates(), [[TAB_STORE_KEY, undefined]]);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a memento that changes on every read is not emptied this time round, and the writer resumes', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const fake = fakeMemento(undefined, { onGet: (call) => memento([tab({ id: `t${call}` })]) });
    const sealing = hooks();

    const report = await run(store, fake, sealing.deps);

    assert.equal(report.kind, 'incomplete');
    assert.match((report as { reason: string }).reason, /changed twice/u);
    assert.deepEqual(fake.updates(), [], 'the key was emptied while another window was still writing it');
    // What WAS seen is on disk — nothing is lost; only the clear is deferred.
    assert.deepEqual(await listed(store), ['t1', 't2']);
    assert.equal(sealing.unseals(), 1, 'the writer stayed sealed with the key still populated');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a record another window is writing right now is not confirmed; the others are, and the key stays', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    rivalLock(dir, 'b2');
    const fake = fakeMemento(memento([tab(), tab({ id: 'b2' }), tab({ id: 'c3' })]));
    const sealing = hooks();

    const { value: report } = await capturing(() => run(store, fake, sealing.deps));

    assert.equal(report.kind, 'incomplete', JSON.stringify(report));
    assert.deepEqual(fatesOf(report).map((fate) => fate.kind), ['written', 'unconfirmed', 'written']);
    assert.match((fatesOf(report)[1] as { reason: string }).reason, /another window is writing/u);
    assert.deepEqual(await listed(store), ['a1', 'c3']);
    assert.equal(existsSync(join(dir, recordName('b2'))), false, 'a save went through a lock somebody else holds');
    assert.deepEqual(fake.updates(), [], 'the key was emptied with a record unconfirmed');
    assert.equal(sealing.seals(), 0, 'the memento was sealed before every record was confirmed');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a record on disk this build cannot read is never "present": the key stays and the file is untouched', async () => {
  // A newer build's record after a downgrade, or a damaged one — either way not proof the person's
  // conversation is safe, and the memento's copy may be the only one this build can read.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    mkdirSync(dir, { recursive: true });
    const foreign = JSON.stringify({ version: 99, id: 'a1', rev: 1 });
    writeFileSync(join(dir, recordName('a1')), foreign, 'utf8');
    const fake = fakeMemento(memento([tab()]));

    const { value: report } = await capturing(() => run(store, fake));

    assert.equal(report.kind, 'incomplete');
    assert.deepEqual(fatesOf(report).map((fate) => fate.kind), ['unconfirmed']);
    assert.match((fatesOf(report)[0] as { reason: string }).reason, /not one this build can read/u);
    assert.equal(readFileSync(join(dir, recordName('a1')), 'utf8'), foreign, 'a record this build cannot read was written over');
    assert.deepEqual(fake.updates(), []);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a record whose index could not be written is on disk but not present: the key stays', async () => {
  // THE `partial` CASE. The record committed and its index did not, so the conversation exists and
  // nothing can list it; clearing the memento on the strength of that hides it. A directory where the
  // metadata file should be makes every index write fail on both platforms this ships to.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    mkdirSync(join(dir, besideMeta('a1')), { recursive: true });
    const fake = fakeMemento(memento([tab()]));

    const { value: report } = await capturing(() => run(store, fake));

    assert.equal(report.kind, 'incomplete', JSON.stringify(report));
    assert.match((report as { reason: string }).reason, /not in the list that finds them/u);
    assert.deepEqual(fatesOf(report), [{ kind: 'written', id: 'a1', indexed: false }]);
    assert.deepEqual((await readRecord(store, 'a1')).messages, FOUR, 'the record itself did not land');
    assert.deepEqual(fake.updates(), [], 'the key was emptied over a conversation the picker cannot see');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a folderless window files under the empty string, and the record survives the validator', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const fake = fakeMemento(memento([tab()]));

    const report = await run(store, fake, { workspace: '' });

    assert.equal(report.kind, 'migrated', JSON.stringify(report));
    assert.equal((await readRecord(store, 'a1')).workspace, '');
    assert.deepEqual(await listed(store), ['a1']);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a memento that throws on read is an outcome, not an exception — the key is left and the writer resumes', async () => {
  // The one case the first version let escape to the caller's catch. A memento whose `get` throws
  // is `incomplete` with a sentence, whether it throws on the first read or on the one before the
  // clear.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const throwing: TabStore = {
      get: () => { throw new Error('storage is gone'); },
      update: () => Promise.resolve(),
    };
    const sealing = hooks();

    const first = await capturing(() => importLegacyTabs({ memento: throwing, store, workspace: WORKSPACE, at: AT, ...sealing.deps }));

    assert.deepEqual(first.value, { kind: 'incomplete', fates: [], reason: 'the old store could not be read' });
    assert.match(first.lines.join('\n'), /storage is gone/u, 'the throw was swallowed without a word on the console');
    assert.equal(existsSync(join(dir, recordName('a1'))), false);
    assert.equal(sealing.seals(), 0);

    // And on the read before the clear: the pass has written, the key cannot be re-read, the key stays.
    const value = memento([tab()]);
    let calls = 0;
    const laterThrowing: TabStore = {
      get: () => { calls += 1; if (calls > 1) { throw new Error('storage went away'); } return value; },
      update: () => Promise.resolve(),
    };
    const later = await capturing(() => importLegacyTabs({ memento: laterThrowing, store, workspace: WORKSPACE, at: AT, ...sealing.deps }));

    assert.equal(later.value.kind, 'incomplete');
    assert.equal((later.value as { reason: string }).reason, 'the old store could not be read');
    assert.deepEqual(fatesOf(later.value), [{ kind: 'written', id: 'a1', indexed: true }], 'the record was not written before the key failed');
    assert.equal(sealing.seals(), 1);
    assert.equal(sealing.unseals(), 1, 'the writer stayed sealed over a key nobody could re-read');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
