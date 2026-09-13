import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, statSync, utimesSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { ConversationIndex, mustReread } from '../chatStoreCache';
import { ChatStoreFile } from '../chatStoreFile';
import { ChatStoreKeeper } from '../chatStoreKeeper';
import { HEARTBEAT_STALE_MS } from '../chatStoreSweep';
import {
  CONVERSATION_VERSION,
  ConversationMeta,
  ConversationRecord,
  besideMeta,
  metaOf,
  recordName,
  sourceOfFile,
  sourceOfSession,
} from '../chatStore';
import { ChatMessage } from '../chatPage';

/**
 * The index the picker draws from, against a REAL directory.
 *
 * <p>Built from the metadata files alone; refreshed against the directory's filename set and the
 * stamps of the files still there; staleness `mtime >= last load` with the size beside it — not `>`;
 * published at once; the last good rows kept when the directory will not answer. The clock is the
 * wall clock here, because it is compared with the filesystem's stamps; a file that must read as
 * older or as exactly-at is placed there with `utimes`.</p>
 */

const said = (role: 'you' | 'model', text: string): ChatMessage => ({ role, text });

function home(): string {
  return mkdtempSync(join(tmpdir(), 'coai-chat-index-'));
}

function record(over: Partial<ConversationRecord> & { readonly id: string }): ConversationRecord {
  return {
    version: CONVERSATION_VERSION,
    rev: 1,
    title: `title ${over.id}`,
    passage: 'the passage',
    modelId: 'gpt-5.4',
    messages: [said('you', 'why'), said('model', 'because')],
    fromSession: false,
    carryFrom: 0,
    source: sourceOfFile(`file:///work/${over.id}.md`),
    workspace: 'D:\\rsd\\coai',
    createdAt: Date.now() - 2_000,
    updatedAt: Date.now() - 1_000,
    ...over,
  };
}

/** A store whose entry reads are counted, so a test can say what a refresh did and did not read. */
class Counting extends ChatStoreFile {
  public reads = 0;

  public override entry(id: string): Promise<ConversationMeta | undefined> {
    this.reads += 1;

    return super.entry(id);
  }
}

interface World {
  readonly dir: string;
  readonly store: Counting;
  readonly index: ConversationIndex;
}

function world(): World {
  const dir = home();
  const store = new Counting(dir);

  return { dir, store, index: new ConversationIndex(new ChatStoreKeeper(dir), store) };
}

/** Put a metadata file's stamp `ms` before `now`, so a later load reads it as untouched. */
function stampBefore(dir: string, id: string, ms: number, now = Date.now()): void {
  const then = new Date(now - ms);
  utimesSync(join(dir, besideMeta(id)), then, then);
}

const ids = (metas: readonly ConversationMeta[]): readonly string[] => metas.map((meta) => meta.id);

const EVERYWHERE = { kind: 'everywhere' } as const;

test('the index is building until its first refresh, then ready', async () => {
  const w = world();
  try {
    assert.deepEqual(w.index.state(), { kind: 'building' });
    assert.deepEqual(w.index.entries(EVERYWHERE), []);
    const now = Date.now();
    await w.index.refresh(now);
    assert.deepEqual(w.index.state(), { kind: 'ready', at: now });
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});

test('the index is built from the METADATA files alone — a transcript that cannot be read is still listed', async () => {
  const w = world();
  try {
    await w.store.save(record({ id: 'a1' }), 0);
    await w.store.save(record({ id: 'b2' }), 0);
    writeFileSync(join(w.dir, recordName('a1')), 'not json at all', 'utf8');

    await w.index.refresh();

    assert.deepEqual([...ids(w.index.entries(EVERYWHERE))].sort(),['a1', 'b2'], 'a conversation whose transcript is unreadable left the list — the index opened a transcript');
    assert.equal(w.index.size, 2);
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});

test('entries are filtered by workspace and ordered newest update first; everywhere lists them all', async () => {
  const w = world();
  try {
    const now = Date.now();
    await w.store.save(record({ id: 'a1', workspace: 'X', updatedAt: now - 3_000 }), 0);
    await w.store.save(record({ id: 'b2', workspace: 'Y', updatedAt: now - 2_000 }), 0);
    await w.store.save(record({ id: 'c3', workspace: 'X', updatedAt: now - 1_000 }), 0);
    await w.index.refresh();

    assert.deepEqual(ids(w.index.entries({ kind: 'workspace', workspace: 'X' })), ['c3', 'a1'], 'the workspace filter or the order is wrong');
    assert.deepEqual(ids(w.index.entries({ kind: 'workspace', workspace: 'Z' })), []);
    assert.deepEqual(ids(w.index.entries(EVERYWHERE)), ['c3', 'b2', 'a1']);
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});

test('the rows are sorted ONCE, when they are published: every call hands back the same list, in the same order', async () => {
  // The picker draws these on every keystroke of a filter; a copy, a filter and a sort per call is a
  // sort of thousands of rows per keystroke, and the picker then sorted them again. (The code round.)
  const w = world();
  try {
    const now = Date.now();
    await w.store.save(record({ id: 'a1', updatedAt: now - 3_000 }), 0);
    await w.store.save(record({ id: 'b2', updatedAt: now - 1_000 }), 0);
    await w.index.refresh();

    assert.equal(w.index.entries(EVERYWHERE), w.index.entries(EVERYWHERE), 'the index copies and re-sorts its rows on every call');
    assert.deepEqual(ids(w.index.entries(EVERYWHERE)), ['b2', 'a1']);
    assert.deepEqual(ids(w.index.bySource(sourceOfFile('file:///work/a1.md'))), ['a1']);
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});

test('a lookup by source finds a Claude session by its id and a document by its uri, and `none` finds nothing', async () => {
  const w = world();
  try {
    await w.store.save(record({ id: 'a1', fromSession: true, source: sourceOfSession('9f1c') }), 0);
    await w.store.save(record({ id: 'b2', source: sourceOfFile('file:///work/notes.md') }), 0);
    await w.store.save(record({ id: 'c3', source: { kind: 'none' } }), 0);
    await w.index.refresh();

    assert.deepEqual(ids(w.index.bySource(sourceOfSession('9f1c'))), ['a1']);
    assert.deepEqual(ids(w.index.bySource(sourceOfFile('file:///work/notes.md'))), ['b2']);
    assert.deepEqual(ids(w.index.bySource(sourceOfSession('other'))), []);
    assert.deepEqual(ids(w.index.bySource({ kind: 'none' })), [], 'a record with no source was matched — every migrated conversation would match every tab');
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});

test('a refresh reconciles the filename set: a conversation another window removed leaves the index, a new one joins it', async () => {
  // The finding behind "reconcile the full filename set": a trashed conversation leaves no newer file
  // to notice, and would otherwise sit in this window's picker until chosen and failed.
  const w = world();
  try {
    await w.store.save(record({ id: 'a1' }), 0);
    await w.store.save(record({ id: 'b2' }), 0);
    // Stamped before the load, so what is counted below is the reconcile and not a same-millisecond
    // write that the `>=` rule (rightly) reads again.
    const now = Date.now();
    stampBefore(w.dir, 'a1', 10_000, now);
    stampBefore(w.dir, 'b2', 10_000, now);
    await w.index.refresh(now);

    await w.store.forget('a1');
    await w.store.save(record({ id: 'c3' }), 0);
    const out = await w.index.refresh(now + 5_000);

    assert.deepEqual([...ids(w.index.entries(EVERYWHERE))].sort(),['b2', 'c3'], 'a removed conversation is still listed, or a new one is not');
    assert.deepEqual(out, { kind: 'refreshed', read: 1, kept: 1, dropped: 1 });
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});

test('an entry whose file has not changed since the last load is not read again', async () => {
  const w = world();
  try {
    await w.store.save(record({ id: 'a1' }), 0);
    await w.store.save(record({ id: 'b2' }), 0);
    const now = Date.now();
    stampBefore(w.dir, 'a1', 10_000, now);
    stampBefore(w.dir, 'b2', 10_000, now);
    await w.index.refresh(now);
    assert.equal(w.store.reads, 2, 'the first load did not read every entry');

    const again = await w.index.refresh(now + 5_000);

    assert.equal(w.store.reads, 2, 'an unchanged entry was read again');
    assert.deepEqual(again, { kind: 'refreshed', read: 0, kept: 2, dropped: 0 });
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});

test('an entry stamped exactly AT the last load is read again — the rule is >=, not >', async () => {
  // Two writes inside one filesystem timestamp granularity are real. A rule of `>` would miss a save
  // that landed in the same instant the load began.
  //
  // The instant is TAKEN FROM THE FILESYSTEM rather than demanded of it. This test used to stamp the
  // file with a chosen millisecond and assert the stamp came back unchanged, which held on NTFS and
  // failed on ext4: a nanosecond stamp converted to milliseconds comes back as `…358.999`, so the
  // boundary the test exists for was never reached and CI went red on a rule that was correct. What
  // matters is that the load instant and the file's stamp are THE SAME NUMBER, whatever number the
  // disk chose — so the file is stamped first, the stamp is read back, and the load is dated to it.
  const w = world();
  try {
    await w.store.save(record({ id: 'a1' }), 0);
    const path = join(w.dir, besideMeta('a1'));
    const chosen = new Date(Date.now() - 10_000);
    utimesSync(path, chosen, chosen);
    const taken = statSync(path).mtimeMs;

    await w.index.refresh(taken);
    assert.equal(w.store.reads, 1, 'the first refresh did not read the entry it had never seen');

    await w.index.refresh(taken + 5_000);

    assert.equal(w.store.reads, 2, 'an entry written in the very instant the load began was believed unchanged');
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});

test('an entry rewritten with an OLDER stamp but another size is read again — the size stands beside the time', async () => {
  // A clock that steps backwards is real: two machines write one directory here.
  const w = world();
  try {
    await w.store.save(record({ id: 'a1' }), 0);
    const now = Date.now();
    stampBefore(w.dir, 'a1', 10_000, now);
    await w.index.refresh(now);

    writeFileSync(join(w.dir, besideMeta('a1')), JSON.stringify(metaOf(record({ id: 'a1', title: 'a title from a machine whose clock is behind' }))), 'utf8');
    stampBefore(w.dir, 'a1', 20_000, now);
    await w.index.refresh(now + 5_000);

    assert.equal(w.index.entries(EVERYWHERE)[0]?.title, 'a title from a machine whose clock is behind', 'a rewrite with an older stamp was believed unchanged');
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});

test('the rule, as a value: unknown, at-or-after the load, or another size is re-read; older and the same size is not', () => {
  const known = { mtimeMs: 1_000, size: 300 };

  assert.equal(mustReread(undefined, known, 5_000), true, 'an entry the index has never seen was not read');
  assert.equal(mustReread(known, { mtimeMs: 5_000, size: 300 }, 5_000), true, 'exactly at the load was not re-read');
  assert.equal(mustReread(known, { mtimeMs: 5_001, size: 300 }, 5_000), true);
  assert.equal(mustReread(known, { mtimeMs: 4_999, size: 300 }, 5_000), false, 'an untouched entry was re-read');
  assert.equal(mustReread(known, { mtimeMs: 4_999, size: 301 }, 5_000), true, 'a size change under an older stamp was missed');
});

test('a refresh that cannot look keeps the last good rows and says so, with the reason and when they are from', async () => {
  const w = world();
  try {
    await w.store.save(record({ id: 'a1' }), 0);
    const now = Date.now();
    await w.index.refresh(now);

    rmSync(w.dir, { recursive: true, force: true });
    writeFileSync(w.dir, 'a file where the store should be', 'utf8');
    const before = console.error;
    console.error = () => undefined;
    const out = await w.index.refresh(now + 5_000).finally(() => {
      console.error = before;
    });

    assert.equal(out.kind, 'unavailable', 'a directory that would not answer refreshed as something');
    assert.deepEqual(ids(w.index.entries(EVERYWHERE)), ['a1'], 'the last good rows were dropped');
    const state = w.index.state();
    assert.equal(state.kind, 'unavailable');
    assert.ok(state.kind === 'unavailable' && state.reason.length > 0 && state.lastGoodAt === now, `the state does not say why or when: ${JSON.stringify(state)}`);
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});

test('a dropped row is gone at once and returns on the next refresh if its file is still there', async () => {
  const w = world();
  try {
    await w.store.save(record({ id: 'a1' }), 0);
    await w.index.refresh();

    w.index.drop('a1');
    assert.deepEqual(ids(w.index.entries(EVERYWHERE)), [], 'a dropped row is still offered');
    w.index.drop('never-there');

    await w.index.refresh();
    assert.deepEqual(ids(w.index.entries(EVERYWHERE)), ['a1']);
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});

test('two refreshes asked for at once are one refresh', async () => {
  const w = world();
  try {
    await w.store.save(record({ id: 'a1' }), 0);

    const first = w.index.refresh();
    const second = w.index.refresh();

    assert.equal(first, second, 'a second refresh started while the first was running');
    await first;
    assert.equal(w.store.reads, 1);
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});

test('the rows are published at once: a reader mid-refresh sees the old index or the new, never a half', async () => {
  const w = world();
  try {
    await w.store.save(record({ id: 'a1' }), 0);
    await w.store.save(record({ id: 'b2' }), 0);
    await w.index.refresh();
    await w.store.save(record({ id: 'c3' }), 0);

    const seen: number[] = [];
    const refreshing = w.index.refresh();
    // Sample the index while the refresh is in flight, until it has settled.
    let settled = false;
    void refreshing.then(() => { settled = true; });
    while (!settled) {
      seen.push(w.index.size);
      await new Promise<void>((resolve) => { setImmediate(resolve); });
    }
    await refreshing;

    assert.ok(seen.every((size) => size === 2 || size === 3), `a reader saw a half-built index: ${seen.join(',')}`);
    assert.equal(w.index.size, 3);
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});
test('the index knows what OTHER windows hold, out of the survey it was already doing', async () => {
  // The picker must not offer to reopen a conversation another window has open: it cannot reveal
  // that tab, so the press would make a second one, and a record with two writers is the fork the
  // store's compare-and-swap exists to catch. The announcements are already in the survey each
  // refresh performs, so knowing this costs no second listing.
  const w = world();
  try {
    const keeper = new ChatStoreKeeper(w.dir);
    const now = Date.now();
    await keeper.beat(4242, ['mine'], now);
    await keeper.beat(7, ['theirs'], now);
    const index = new ConversationIndex(keeper, w.store, 4242);

    assert.deepEqual([...index.elsewhere(now)], [], 'the index answered before it had ever looked');

    await index.refresh(now);

    assert.deepEqual([...index.elsewhere(now)].sort(), ['theirs'],
      'this window read its OWN heartbeat as another window, and would refuse to reopen its own conversations');
    // A heartbeat is written at most once a minute, so its window is believed for a while and then
    // is not — judged at the moment of asking, so a picker left open stops holding them hostage.
    assert.deepEqual([...index.elsewhere(now + HEARTBEAT_STALE_MS)], [],
      'a window that went quiet while the list was up still held its conversations');
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});

test('a survey that fails keeps the last good announcements, exactly as it keeps the last good rows', async () => {
  // "Could not look" is not "nobody holds anything". Erring towards believing another window still
  // has a conversation errs towards not giving one record two writers, which is the whole point.
  const w = world();
  try {
    const keeper = new ChatStoreKeeper(w.dir);
    const now = Date.now();
    await keeper.beat(7, ['theirs'], now);
    const index = new ConversationIndex(keeper, w.store, 4242);
    await index.refresh(now);

    rmSync(w.dir, { recursive: true, force: true });
    writeFileSync(w.dir, 'a file where the store should be', 'utf8');
    const before = console.error;
    console.error = () => undefined;
    const out = await index.refresh(now + 5_000).finally(() => {
      console.error = before;
    });

    assert.equal(out.kind, 'unavailable', 'the test did not manage to make the store unreadable');
    assert.deepEqual([...index.elsewhere(now + 5_000)], ['theirs'], 'a folder that would not answer emptied what other windows hold');
  } finally {
    rmSync(w.dir, { recursive: true, force: true });
  }
});
