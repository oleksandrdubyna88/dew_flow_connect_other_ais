import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { ChatStoreFile, SaveOutcome } from '../chatStoreFile';
import { LOCK_STALE_MS, lockName } from '../chatStoreLock';
import {
  CONVERSATION_VERSION,
  ConversationMeta,
  ConversationRecord,
  besideMeta,
  metaFrom,
  metaOf,
  recordName,
  sourceOfSession,
} from '../chatStore';
import { ChatMessage } from '../chatPage';

/**
 * The conversation store's file half, against a REAL temporary directory — never a mocked disk.
 *
 * <p>`chatStore.ts` decides what a record MEANS and is tested without a disk. This is the other half:
 * that the two files land in the order one commit needs, that a metadata file torn away from its
 * record is regenerated rather than believed — under the conversation's lock, against the disk as it
 * is then — that a second window holding a stale revision is refused instead of overwriting the first,
 * that a delete cannot race a save, that a read says WHICH of three reasons there is no record, and
 * that a store which cannot be READ is told apart from one that is simply empty — at every boundary.
 * None of that can be asserted against a fake filesystem, because the things it guards against — a
 * rename's order, a partial write, an `O_EXCL` race, a directory that is really a file — are the
 * filesystem's own behaviour. (The plan's test rule: "against a REAL temporary directory.")</p>
 *
 * <p>Every fixture goes through the store's own writer and reader, never a hand-typed JSON cast: a
 * record is {@link ChatStoreFile.save}d and {@link ChatStoreFile.read} back, so the real validator in
 * `chatStore.ts` runs on the way in and the way out. The exceptions are where a test SIMULATES a
 * crash or a rival — a hand-written stale metadata file, a metadata deleted from under a record, a
 * lock held by another window — which is exactly the disk state those leave and cannot be produced
 * through the happy path.</p>
 */

const AT = Date.UTC(2026, 8, 12, 12, 0, 0);

function home(): string {
  return mkdtempSync(join(tmpdir(), 'coai-chat-store-'));
}

const said = (role: 'you' | 'model', text: string): ChatMessage => ({ role, text });

function record(over: Partial<ConversationRecord> = {}): ConversationRecord {
  return {
    version: CONVERSATION_VERSION,
    rev: 1,
    id: 'a1',
    title: 'main',
    passage: 'the passage',
    modelId: 'gpt-5.4',
    messages: [said('you', 'why'), said('model', 'because')],
    fromSession: true,
    carryFrom: 0,
    source: sourceOfSession('9f1c-uuid'),
    workspace: 'D:\\rsd\\coai',
    createdAt: AT - 1_000,
    updatedAt: AT,
    ...over,
  };
}

/** The rev an `ok` or `partial` outcome committed, or `-1` — so a test reads it in one line. */
const revOf = (outcome: SaveOutcome): number => (outcome.kind === 'ok' || outcome.kind === 'partial' ? outcome.rev : -1);

/** The record a read came back with — and a named failure, not a cast, when it came back with anything else. */
async function readRecord(store: ChatStoreFile, id: string, now?: number): Promise<ConversationRecord> {
  const out = await store.read(id, now);
  if (out.kind !== 'record') {
    assert.fail(`expected a record at ${id}, the read said ${out.kind}`);
  }

  return out.record;
}

/** The rows a listing came back with — and a named failure when the store said it could not look. */
async function rows(store: ChatStoreFile): Promise<readonly ConversationMeta[]> {
  const out = await store.listMeta();
  if (out.kind !== 'listed') {
    assert.fail(`expected a listing, the store said ${out.kind}: ${out.reason}`);
  }

  return out.metas;
}

/** Run something with `console.error` captured, and hand back both what it returned and what it said. */
async function capturing<T>(run: () => Promise<T>): Promise<{ readonly value: T; readonly lines: readonly string[] }> {
  const lines: string[] = [];
  const before = console.error;
  console.error = (...parts: unknown[]) => { lines.push(parts.map(String).join(' ')); };
  try {
    return { value: await run(), lines };
  } finally {
    console.error = before;
  }
}

/** Every name in the directory that is a lock — a mutation must never leave one, whatever it came to. */
const locksIn = (dir: string): readonly string[] => readdirSync(dir).filter((name) => name.endsWith('.lock'));

/** A lock as another, live window would leave it: fresh, and not ours. */
const rivalLock = (dir: string, id: string, doing: string): void => {
  writeFileSync(join(dir, lockName(id)), JSON.stringify({ pid: 1, at: new Date(AT).toISOString(), token: 'rival:1', doing }), 'utf8');
};

// ---------------------------------------------------------------------------------------------
// The round trip, and the atomic write it rests on.
// ---------------------------------------------------------------------------------------------

test('a record saved comes back through read exactly as it went in, at its new rev', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const mine = record();

    const saved = await store.save(mine, 0);
    assert.equal(saved.kind, 'ok', 'a first save was not accepted');
    assert.equal(revOf(saved), 1, 'a first write, at baseline 0, must land at rev 1');

    assert.deepEqual(await readRecord(store, mine.id), { ...mine, rev: 1 }, 'the conversation did not round-trip');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a save leaves the pair and NOTHING beside it — no temporary, and no lock', async () => {
  // The atomic write renames a `.tmp` into place and the claim removes its `.lock` in a `finally`; a
  // survivor of either would be a crash nobody cleaned up, and a lock left behind wedges the
  // conversation for the length of the stale window.
  const dir = home();
  try {
    await new ChatStoreFile(dir).save(record(), 0);

    assert.deepEqual(
      readdirSync(dir).sort(),
      [besideMeta('a1'), recordName('a1')].sort(),
      'a save left a temporary or a lock, or wrote the wrong two files',
    );
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a save writes the record AND its metadata, both carrying the one new rev', async () => {
  // The two-file commit: the metadata is derived from the record and stamped with the same number,
  // so a reader can tell a whole pair from an interrupted one by the rev alone.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await store.save(record(), 0);

    const onDisk = metaFrom(JSON.parse(readFileSync(join(dir, besideMeta('a1')), 'utf8')));
    assert.ok(onDisk !== undefined, 'the metadata file was not written, or is not readable');
    assert.equal(onDisk!.rev, 1, 'the metadata carries a different rev from its record');
    assert.equal(onDisk!.turns, 2, 'the metadata was not derived from the record it was saved with');
    assert.equal(onDisk!.lastLine, 'because');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a first save into a directory that does not exist yet succeeds, end to end', async () => {
  // Owed to the gate: the layer below (`atomicFile.ts`) makes the directory and has a test saying so,
  // but the guarantee belongs at THIS layer too — the store is what the first chat on a fresh machine
  // actually calls, and it is empty before and ready after.
  const dir = join(home(), 'chat-conversations');
  try {
    const store = new ChatStoreFile(dir);
    assert.deepEqual(await store.state(), { kind: 'empty' }, 'a store nobody has written to is not empty');

    const saved = await store.save(record(), 0);

    assert.equal(saved.kind, 'ok', 'the first save of an installation was refused for want of a directory');
    assert.deepEqual(await store.state(), { kind: 'ready' }, 'the first save did not materialise the store');
    assert.deepEqual(await readRecord(store, 'a1'), { ...record(), rev: 1 });
    assert.deepEqual(locksIn(dir), [], 'the first save left its lock behind');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------------------------
// rev — order, and the lost-update refusal.
// ---------------------------------------------------------------------------------------------

test('two saves to one id land in the order they were asked for', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);

    const first = await store.save(record({ messages: [said('you', 'first')] }), 0);
    assert.equal(first.kind, 'ok');
    const second = await store.save(record({ messages: [said('you', 'second')] }), revOf(first));
    assert.equal(second.kind, 'ok');

    const back = await readRecord(store, 'a1');
    assert.deepEqual(back.messages, [said('you', 'second')], 'the later write did not win');
    assert.equal(back.rev, 2, 'the second save did not advance the rev');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a second writer holding a stale rev is REFUSED, and the first writer\u2019s transcript survives whole', async () => {
  // Two windows both reopened one conversation. The window that read rev 1 and did not notice the
  // other's rev-2 write must not silently replace it. It is refused, told the disk rev, and re-mints
  // elsewhere (story A3) — nothing is lost on either side.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);

    const born = await store.save(record({ messages: [said('you', 'shared start')] }), 0);
    assert.equal(born.kind, 'ok'); // rev 1 — both windows have now read this

    const advanced = await store.save(record({ messages: [said('you', 'window A turn')] }), 1);
    assert.equal(advanced.kind, 'ok', 'the first window could not take its turn');

    const stale = await store.save(record({ messages: [said('you', 'window B turn')] }), 1);
    assert.equal(stale.kind, 'refused', 'a writer whose baseline is behind the disk was allowed to overwrite');
    assert.equal(stale.kind === 'refused' ? stale.diskRev : -1, 2, 'the refusal did not report the current disk rev');

    const survived = await readRecord(store, 'a1');
    assert.deepEqual(survived.messages, [said('you', 'window A turn')], 'a refused writer clobbered the transcript anyway');
    assert.equal(survived.rev, 2, 'the refused write advanced the rev it was not allowed to touch');
    assert.deepEqual(locksIn(dir), [], 'a refused save left a lock behind');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a first write is baseline 0; a SECOND at baseline 0 over what it created is refused', async () => {
  // Baseline 0 means "I have not read this record". It is honest exactly once: the second time it is a
  // claim the record is not there, which the disk contradicts.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);

    const first = await store.save(record(), 0);
    assert.equal(first.kind, 'ok', 'a first write at baseline 0 was refused');
    assert.equal(revOf(first), 1);

    const again = await store.save(record({ passage: 'a second, blind write' }), 0);
    assert.equal(again.kind, 'refused', 'a blind second write over an existing conversation was allowed');
    assert.equal(again.kind === 'refused' ? again.diskRev : -1, 1);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------------------------
// What a read SAYS — three reasons for no record, and none of them is the others.
// ---------------------------------------------------------------------------------------------

test('a conversation that was never written reads as ABSENT — the one answer a caller may act on as "none"', async () => {
  const dir = home();
  try {
    assert.deepEqual(await new ChatStoreFile(dir).read('never'), { kind: 'absent' });
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a record the disk will not hand over reads as UNAVAILABLE with a reason — not as absent', async () => {
  // codex, the third round: the reload serializer disposes a panel over "no record". A permissions
  // error folded into `undefined` would throw a person's tab away over a fault that has nothing to do
  // with their conversation. Provoked portably: the record's path is a directory, which will not read
  // as a file (EISDIR).
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    mkdirSync(join(dir, recordName('a1')));

    const { value: out, lines } = await capturing(() => store.read('a1'));

    assert.equal(out.kind, 'unavailable', 'an unreadable record was reported as something else');
    const reason = out.kind === 'unavailable' ? out.reason : '';
    assert.ok(reason.length > 0 && !reason.includes(dir), 'the reason is empty, or carries the path a person must not see');
    assert.ok(lines.some((line) => line.includes(join(dir, recordName('a1')))), 'the console did not name the file that would not read');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a record this build cannot read — torn, or of a newer version — reads as INCOMPATIBLE, not as absent', async () => {
  // The other half of the same finding: a record a NEWER build wrote must be preserved, not disposed
  // of by an older build that mistakes "I cannot read this" for "there is nothing here".
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    writeFileSync(join(dir, recordName('a1')), '{"version":1,"rev":', 'utf8');
    const { value: torn } = await capturing(() => store.read('a1'));
    assert.equal(torn.kind, 'incompatible', 'a torn record was reported as absent');
    assert.ok(torn.kind === 'incompatible' && torn.reason.length > 0, 'an incompatible record gave no reason');

    writeFileSync(join(dir, recordName('a1')), JSON.stringify({ ...record(), version: CONVERSATION_VERSION + 1 }), 'utf8');
    const { value: newer } = await capturing(() => store.read('a1'));
    assert.equal(newer.kind, 'incompatible', 'a newer build\u2019s record was reported as absent, which is what disposes it');
    assert.equal(existsSync(join(dir, besideMeta('a1'))), false, 'a read wrote metadata for a record it could not read');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------------------------
// Reconciliation on read — the record is the truth, the metadata is a derived index.
// ---------------------------------------------------------------------------------------------

test('a record whose metadata is MISSING is still readable, and the metadata is regenerated', async () => {
  // The delete-interrupted state from the record's side: the metadata is gone, the transcript is not.
  // A read must return the conversation and rebuild the index it draws rows from.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await store.save(record(), 0);
    rmSync(join(dir, besideMeta('a1'))); // the metadata vanishes

    const back = await readRecord(store, 'a1');

    const rebuilt = metaFrom(JSON.parse(readFileSync(join(dir, besideMeta('a1')), 'utf8')));
    assert.deepEqual(rebuilt, metaOf(back), 'the metadata was not regenerated from the record on read');
    assert.deepEqual(locksIn(dir), [], 'the reconciliation left its lock behind');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a metadata file at an OLDER rev than its record is reconciled on read, not believed', async () => {
  // The two-file-commit hole three vendors found: the record was renamed, the host died, the metadata
  // still describes the turn before. A picker row would show a last line that is no longer last, so
  // the reader regenerates from the record rather than trusting the stale index.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await store.save(record({ messages: [said('you', 'q1')] }), 0);
    const advanced = await store.save(record({ messages: [said('you', 'q1'), said('model', 'a1'), said('you', 'q2')] }), 1);
    assert.equal(advanced.kind, 'ok'); // record is now rev 2, 3 turns

    // Hand-write a metadata file describing the PREVIOUS save (rev 1, one turn) beside the rev-2 record.
    const stale = { ...metaOf(record({ messages: [said('you', 'q1')] })), rev: 1 };
    writeFileSync(join(dir, besideMeta('a1')), JSON.stringify(stale), 'utf8');

    const back = await readRecord(store, 'a1');
    const reconciled = metaFrom(JSON.parse(readFileSync(join(dir, besideMeta('a1')), 'utf8')));
    assert.equal(reconciled?.rev, 2, 'the stale metadata was left describing the wrong save');
    assert.equal(reconciled?.turns, 3, 'the row would still show the turn count from before the crash');
    assert.deepEqual(reconciled, metaOf(back), 'the regenerated index does not match the record it was rebuilt from');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a TORN metadata file is said out loud with its path before it is written over', async () => {
  // Accepted on the second round: turning a torn index silently into "absent" and regenerating it
  // hides the fact from whoever is debugging why rows kept disappearing. It is still regenerated.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await store.save(record(), 0);
    const path = join(dir, besideMeta('a1'));
    writeFileSync(path, '{ not json', 'utf8');

    const { value: back, lines } = await capturing(() => readRecord(store, 'a1'));

    assert.ok(lines.some((line) => line.includes('torn') && line.includes(path)), `a torn index was not said with its path: ${lines.join(' | ')}`);
    assert.deepEqual(metaFrom(JSON.parse(readFileSync(path, 'utf8'))), metaOf(back), 'the torn index was not regenerated');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------------------------
// Deletion — the order, and forget.
// ---------------------------------------------------------------------------------------------

test('a delete interrupted after the metadata leaves NO listable row — and an id that cannot be reused', async () => {
  // forget deletes the metadata first, then the record. A crash between the two leaves a transcript
  // with no metadata — a file nobody can see rather than a row that opens onto nothing. It must not
  // appear in the listing, because the listing is metadata only; and a baseline-0 save meets it and is
  // refused, so the id stays taken until story B1's sweep collects the file. That is the shape the
  // header documents, and it is deliberately not repaired here.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await store.save(record(), 0);
    rmSync(join(dir, besideMeta('a1'))); // the metadata is gone; the record lingers

    assert.deepEqual([...(await rows(store))], [], 'a half-deleted conversation still shows a row');
    const reuse = await store.save(record({ passage: 'a new conversation under the old id' }), 0);
    assert.equal(reuse.kind, 'refused', 'an orphaned transcript was written over by a new conversation');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('forget removes both files of a conversation and says so', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await store.save(record(), 0);
    assert.equal(readdirSync(dir).length, 2, 'the setup did not write the pair');

    assert.deepEqual(await store.forget('a1'), { kind: 'ok' }, 'a completed deletion did not report itself completed');

    assert.deepEqual(readdirSync(dir), [], 'a forgotten conversation left a file — or its lock — behind');
    assert.deepEqual(await store.read('a1'), { kind: 'absent' }, 'a forgotten conversation is still readable');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------------------------
// Missing vs unreadable — the load-bearing distinction, at every boundary.
// ---------------------------------------------------------------------------------------------

test('a directory that is not there reads as EMPTY, lists as nothing, and its records are ABSENT', async () => {
  // Nobody has chatted yet. This is an ordinary state, not an error: the first save materialises the
  // directory, so a read of it beforehand must be quiet — and a cache told "nothing" here is right to
  // hold nothing.
  const store = new ChatStoreFile(join(home(), 'chat-conversations-not-yet'));

  assert.deepEqual(await store.state(), { kind: 'empty' });
  assert.deepEqual(await store.listMeta(), { kind: 'listed', metas: [] }, 'a missing directory did not list as an ordinary nothing');
  assert.deepEqual(await store.read('anything'), { kind: 'absent' });
});

test('a store that cannot be READ is UNAVAILABLE with a reason, not empty', async () => {
  // The distinction the whole module turns on: reporting "no conversations" for a store that could
  // not be read would be this half lying about the world, and a later sweep must delete nothing while
  // it cannot read. Simulated portably on Windows — where chmod is a no-op — by putting a FILE where
  // the directory should be (ENOTDIR / a non-directory), which is a real half-done install too.
  const dir = home();
  try {
    const asFile = join(dir, 'chat-conversations');
    writeFileSync(asFile, 'a file where the store\u2019s directory should be');
    const store = new ChatStoreFile(asFile);

    const { value: state } = await capturing(() => store.state());
    assert.equal(state.kind, 'unavailable', 'a store that could not be read was reported empty');
    assert.ok(
      state.kind === 'unavailable' && state.reason.length > 0,
      'an unavailable store gave no reason for the picker to show',
    );
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a directory that will not answer LISTS as unavailable with a reason — never as an empty list', async () => {
  // codex, the third round: the cache of story B1 must keep its last good rows when a refresh fails,
  // and it cannot if "could not look" and "nothing here" are both `[]`. `state()` does not cover it —
  // a permission change between the state call and the listing is the race the cache rule is about.
  const dir = home();
  try {
    const asFile = join(dir, 'chat-conversations');
    writeFileSync(asFile, 'a file where the store\u2019s directory should be');
    const store = new ChatStoreFile(asFile);

    const { value: listing, lines } = await capturing(() => store.listMeta());

    assert.equal(listing.kind, 'unavailable', 'a directory that could not be read listed as an empty store');
    const reason = listing.kind === 'unavailable' ? listing.reason : '';
    assert.ok(reason.length > 0 && !reason.includes(asFile), 'the reason is empty, or carries the path');
    assert.ok(lines.some((line) => line.includes(asFile)), 'the console did not name the store that would not list');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a usable directory reports ready', async () => {
  const dir = home();
  try {
    assert.deepEqual(await new ChatStoreFile(dir).state(), { kind: 'ready' });
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------------------------
// What the listing must and must not open.
// ---------------------------------------------------------------------------------------------

test('an interrupted .tmp write is invisible to both listMeta and read', async () => {
  // A `.tmp` is a rename that never happened. The listing skips it before reading a byte (it is not a
  // `.meta.json`), and a read of the base id finds no `<id>.json`, so the torn write cannot surface
  // as a conversation either.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await store.save(record({ id: 'good' }), 0);
    writeFileSync(join(dir, `${recordName('torn')}.4321.1.tmp`), '{"half":', 'utf8');
    writeFileSync(join(dir, `${besideMeta('torn')}.4321.2.tmp`), '{"half":', 'utf8');

    assert.deepEqual((await rows(store)).map((m) => m.id), ['good'], 'a torn temporary was read as a conversation');
    assert.deepEqual(await store.read('torn'), { kind: 'absent' }, 'a torn temporary surfaced as a conversation');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a file in the directory that is not one of ours — a lock included — is ignored by the listing', async () => {
  // The coai data directory is shared. A `notes.txt`, a metadata-shaped name with an UNSAFE id, a
  // transcript-shaped name and a `.lock` are all not our metadata files, and none of them may be
  // opened or listed — above all a transcript, whose whole point is to stay unread until a row is chosen.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await store.save(record({ id: 'mine' }), 0);
    writeFileSync(join(dir, 'notes.txt'), 'personal notes', 'utf8');
    writeFileSync(join(dir, 'x.json'), '{"unsafe":true}', 'utf8'); // a transcript-shaped stranger
    writeFileSync(join(dir, 'bad id.meta.json'), '{"has":"a space in its id"}', 'utf8'); // unsafe id
    rivalLock(dir, 'mine', 'save 2'); // a claim in flight

    assert.deepEqual((await rows(store)).map((m) => m.id), ['mine'], 'a stranger was read as a conversation');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a metadata file whose inner id disagrees with its filename is dropped', async () => {
  // A hand-edited or mis-renamed entry: trusting either half would key an index by one id and open
  // the record of another. It is dropped rather than believed.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await store.save(record({ id: 'right' }), 0);
    writeFileSync(join(dir, besideMeta('wrong')), JSON.stringify(metaOf(record({ id: 'right' }))), 'utf8');

    const { value: listed } = await capturing(async () => (await rows(store)).map((m) => m.id));
    assert.deepEqual(listed, ['right'], 'a metadata file filed under the wrong id was trusted');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('one torn metadata file does not empty the whole listing', async () => {
  // "Drop unreadable entries rather than failing the whole listing." A single corrupt index file must
  // cost only its own row, never a person's entire picker.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await store.save(record({ id: 'first' }), 0);
    await store.save(record({ id: 'second' }), 0);
    writeFileSync(join(dir, besideMeta('third')), '{ this is not json', 'utf8');

    const { value: listed } = await capturing(async () => (await rows(store)).map((m) => m.id).sort());
    assert.deepEqual(listed, ['first', 'second'], 'a torn index entry took the readable ones down with it');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a listing wider than the read window returns every row, in directory order', async () => {
  // The metadata files are read eight at a time, not serially and not all at once; what must not
  // change is the answer. Twenty conversations is two and a half windows.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const ids = Array.from({ length: 20 }, (_, at) => `c${String(at).padStart(2, '0')}`);
    for (const id of ids) {
      assert.equal((await store.save(record({ id }), 0)).kind, 'ok');
    }

    const listed = (await rows(store)).map((m) => m.id);

    assert.deepEqual(listed, ids, 'a bounded-concurrency listing lost or reordered rows');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------------------------
// A save that genuinely cannot happen.
// ---------------------------------------------------------------------------------------------

test('a save whose record cannot be read reports failed with a reason, never throws, and leaves no lock', async () => {
  // The store turns a disk that will not answer into `failed` and keeps the conversation on screen;
  // it must not throw into the caller. Provoked portably by making the record's own path a DIRECTORY,
  // which cannot be read as a file (EISDIR) — and a swap that cannot see the disk must not guess.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    mkdirSync(join(dir, recordName('a1'))); // `a1.json` is a directory

    const { value: outcome } = await capturing(() => store.save(record(), 0)); // resolves: a rejection would fail the test here

    assert.equal(outcome.kind, 'failed', 'a save that could not read the disk did not report failure');
    const reason = outcome.kind === 'failed' ? outcome.reason : '';
    assert.ok(reason.length > 0, 'a failed save gave no reason for a person to read');
    assert.ok(!reason.includes(dir), 'the failure reason leaked the filesystem path');
    assert.ok(!reason.includes('\\') && !reason.includes('/'), 'the failure reason leaked a path separator');
    assert.deepEqual(locksIn(dir), [], 'a failed save left its lock behind — a lock must never outlive a failed save');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a save whose directory cannot even be created fails cleanly, with no path in the reason', async () => {
  // The claim cannot make the store's directory because its PARENT is a file. The whole save becomes
  // `failed` — never a throw, never a leaked path in the reason a person reads.
  const dir = home();
  try {
    const asFile = join(dir, 'parent-is-a-file');
    writeFileSync(asFile, 'a file standing in for the store\u2019s parent directory', 'utf8');
    const store = new ChatStoreFile(join(asFile, 'chat-conversations'));

    const { value: outcome } = await capturing(() => store.save(record(), 0)); // resolves: a rejection would fail the test here

    assert.equal(outcome.kind, 'failed', 'a save that could not create its directory did not report failure');
    const reason = outcome.kind === 'failed' ? outcome.reason : '';
    assert.ok(reason.length > 0, 'a failed save gave no reason for a person to read');
    assert.ok(!reason.includes(asFile), 'the failure reason leaked the filesystem path');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a baseline that is not a whole number of zero or more is a typed failure, never quietly floored', async () => {
  // Accepted on the second round: an update whose baseline the caller forgot — or garbled — must not
  // silently become a creation attempt, or a swap against a number the caller never read.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);

    for (const bad of [-1, 1.5, Number.NaN, Number.POSITIVE_INFINITY]) {
      const outcome = await store.save(record(), bad);
      assert.equal(outcome.kind, 'failed', `a baseline of ${bad} was acted on`);
      assert.ok(outcome.kind === 'failed' && outcome.reason.length > 0, 'an invalid baseline gave no reason');
    }
    assert.deepEqual(readdirSync(dir), [], 'an invalid baseline wrote something, or took a lock');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------------------------
// What the code rounds of story A2 found. Each of these is a test before it was a fix.
// ---------------------------------------------------------------------------------------------

test('two writers with ONE baseline racing for one id: exactly one lands, whole, and the other is refused', async () => {
  // codex, Blocking, round one: "read the rev, then rename" is not a compare-and-swap. Two windows that
  // both read 5 both see 5, both choose 6, both rename, and the second silently replaces the first.
  // The fix is an exclusive create of the conversation (`chatStoreLock.ts`); this is the race, run
  // for real. The loser either finds the lock held or, having waited it out, finds rev 1 on disk.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const mine = record({ messages: [said('you', 'window A')] });
    const theirs = record({ messages: [said('you', 'window B')] });

    const [a, b] = await Promise.all([store.save(mine, 0), store.save(theirs, 0)]);

    const kinds = [a.kind, b.kind].sort();
    assert.deepEqual(kinds, ['ok', 'refused'], `both racers were granted the same transition: ${kinds.join(', ')}`);
    const landed = await readRecord(store, 'a1');
    const winner = a.kind === 'ok' ? mine : theirs;
    assert.deepEqual(landed.messages, winner.messages, 'the transcript on disk is not the one whose save was reported ok');
    assert.equal(landed.rev, 1);
    assert.deepEqual(locksIn(dir), [], 'the race left a lock behind');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a conversation another window holds right now answers BUSY, and is not read behind its back', async () => {
  // It answered `refused` with no transcript until A3's second round, and that overloading wedges:
  // a genuine conflict over an ABSENT record reports no transcript either, so a caller telling the
  // two apart that way waits for ever on a real conflict and never forks. Two facts, two names.
  //
  // And nothing is PROBED here. Reading the record while another process is mid-mutation of it is
  // the unfenced read this module's lock exists to forbid, and on Windows it can meet the sharing
  // error that mutation is holding — which would reach a person as a failed save. (gemini.)
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    rivalLock(dir, 'a1', 'save 1');

    const outcome = await store.save(record(), 0, AT);

    assert.equal(outcome.kind, 'busy', 'a save proceeded over a conversation somebody else holds');
    assert.equal(existsSync(join(dir, recordName('a1'))), false, 'a refused save wrote its record anyway');
    assert.equal(JSON.parse(readFileSync(join(dir, lockName('a1')), 'utf8')).token, 'rival:1', 'the rival\u2019s lock was removed');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a lock left by a dead writer is broken after the window and the save goes through', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const dead = { pid: 1, at: new Date(AT - LOCK_STALE_MS - 1_000).toISOString(), token: 'dead:1', doing: 'save 1' };
    writeFileSync(join(dir, lockName('a1')), JSON.stringify(dead), 'utf8');

    const { value: outcome } = await capturing(() => store.save(record(), 0, AT));

    assert.equal(outcome.kind, 'ok', 'a killed writer wedged the conversation for ever');
    assert.deepEqual(locksIn(dir), [], 'the broken lock, or the new one, was left behind');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a metadata failure after the record landed is PARTIAL — the baseline advances, and the caller is told', async () => {
  // codex, gemini and local, independently: the first draft returned `ok` here. The reasoning it gave
  // was right — the record IS the commit, and a caller that treated this as failed would hold a stale
  // baseline and see its own next write refused — but a half-done save is not a whole one. The answer
  // is a third outcome: the rev to advance to, AND the reason the index did not land.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    mkdirSync(join(dir, besideMeta('a1'))); // the metadata's path is a directory: its rename cannot land

    const { value: outcome } = await capturing(() => store.save(record(), 0));

    assert.equal(outcome.kind, 'partial', 'a save whose index did not land called itself whole, or failed');
    assert.equal(revOf(outcome), 1, 'a partial save does not tell the caller the rev it committed');
    const reason = outcome.kind === 'partial' ? outcome.reason : '';
    assert.ok(reason.length > 0 && !reason.includes(dir), 'the partial reason is empty or carries the path');

    // The record committed: it reads back, and the NEXT save from the advanced baseline is accepted,
    // which is the whole point of telling the caller the rev.
    const { value: back } = await capturing(() => readRecord(store, 'a1'));
    rmSync(join(dir, besideMeta('a1')), { recursive: true }); // the obstruction clears
    const next = await store.save(record({ passage: 'after' }), revOf(outcome));
    assert.deepEqual(back.messages, record().messages, 'the record of a partial save did not commit');
    assert.equal(next.kind, 'ok', 'a caller that advanced its baseline on partial was refused its own next write');
    assert.equal(revOf(next), 2);
    assert.deepEqual(locksIn(dir), [], 'a partial save left a lock behind');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a file this build cannot read at an id is INCOMPATIBLE — refused untouched, never treated as absent', async () => {
  // All three vendors: the first draft read an unparseable record as rev 0 and let a save proceed
  // over it. That lets an older build replace a newer build's conversation after a downgrade, and a
  // conversation minted under a fresh id — saved with baseline 0 — meets whatever is at that id. So
  // it writes NOTHING; the file is quarantined until `forget` or the sweep clears it.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const torn = '{"version":1,"rev":';
    writeFileSync(join(dir, recordName('a1')), torn, 'utf8');

    const { value: outcome } = await capturing(() => store.save(record(), 0));

    assert.equal(outcome.kind, 'incompatible', 'a torn record was written over as if absent');
    assert.equal(readFileSync(join(dir, recordName('a1')), 'utf8'), torn, 'the quarantined file was changed');
    assert.equal(existsSync(join(dir, besideMeta('a1'))), false, 'metadata was written for a record that was refused');
    assert.deepEqual(locksIn(dir), [], 'an incompatible save left a lock behind');

    // A record of a version this build does not know is the same answer — the downgrade case.
    const newer = JSON.stringify({ ...record(), version: CONVERSATION_VERSION + 1 });
    writeFileSync(join(dir, recordName('a1')), newer, 'utf8');
    const { value: downgrade } = await capturing(() => store.save(record(), 0));
    assert.equal(downgrade.kind, 'incompatible', 'an older build replaced a newer build\u2019s conversation');
    assert.equal(readFileSync(join(dir, recordName('a1')), 'utf8'), newer);

    // And `forget` is the built-in way out.
    assert.deepEqual(await store.forget('a1'), { kind: 'ok' });
    assert.equal((await store.save(record(), 0)).kind, 'ok', 'the id could not be reused after the quarantined file was cleared');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a save with a baseline above zero over an ABSENT record is refused — it was deleted under you', async () => {
  // codex: window A reads rev 4, window B deletes the conversation, A saves with baseline 4. Absence
  // read as rev 0 let the save proceed and the deleted conversation came back. Only baseline zero may
  // create; anything else meeting nothing is a conflict, reported with `diskRev` 0.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    const born = await store.save(record(), 0);
    assert.equal(revOf(born), 1);
    assert.deepEqual(await store.forget('a1'), { kind: 'ok' }); // the other window trashes it

    const resurrect = await store.save(record({ passage: 'from the grave' }), 1);

    assert.equal(resurrect.kind, 'refused', 'a conversation deleted in another window was resurrected');
    assert.equal(resurrect.kind === 'refused' ? resurrect.diskRev : -1, 0, 'the refusal does not say the record is gone');
    assert.deepEqual(readdirSync(dir), [], 'a refused resurrection wrote something');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a record REPLACED under a writer — present, but at a lower rev than it read — is refused too', async () => {
  // The same fact from the other side: the disk must hold EXACTLY the revision the writer read. A
  // conversation deleted and re-created under the same id is a different lineage, not an older state
  // of this one, and writing rev 3 over its rev 1 would splice two conversations into one file.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await store.save(record(), 0);
    await store.save(record(), 1); // window A has read rev 2
    await store.forget('a1');
    await store.save(record({ passage: 'a new lineage' }), 0); // re-created at rev 1 by somebody else

    const splice = await store.save(record({ passage: 'window A, still at 2' }), 2);

    assert.equal(splice.kind, 'refused', 'a writer wrote over a record that was not the one it read');
    assert.equal(splice.kind === 'refused' ? splice.diskRev : -1, 1);
    assert.equal((await readRecord(store, 'a1')).passage, 'a new lineage', 'the re-created conversation was overwritten');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('forget takes the conversation\u2019s lock: a conversation another window is changing is not deleted, and says so', async () => {
  // codex, round two: with a per-revision lock, `forget` raced `save` into either resurrecting a
  // deleted conversation or leaving a row that opens onto nothing. Under one lock per conversation the
  // delete waits, and if the holder is still there it reports `failed` for the sweep to retry.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await store.save(record(), 0);
    rivalLock(dir, 'a1', 'save 2');

    const outcome = await store.forget('a1', AT);

    assert.equal(outcome.kind, 'failed', 'a conversation another window holds was deleted under it');
    assert.ok(outcome.kind === 'failed' && outcome.reason.length > 0, 'a refused delete gave no reason');
    assert.equal(existsSync(join(dir, recordName('a1'))), true, 'the transcript was deleted under a live writer');
    assert.equal(existsSync(join(dir, besideMeta('a1'))), true, 'the index was deleted under a live writer');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a read\u2019s reconciliation takes the conversation\u2019s lock, and writes nothing while another window holds it', async () => {
  // Two races the second round found: a `forget` that has just unlinked the metadata has its deletion
  // undone by a reader regenerating the row; and a read that captured rev 1 while another window
  // committed rev 2 writes a rev-1 index beside a rev-2 transcript. Both are closed by the same rule —
  // the index is written only under the lock — and this is the observable half of it: with the lock
  // held by somebody else, the read still returns the record and leaves the index alone.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await store.save(record(), 0);
    rmSync(join(dir, besideMeta('a1'))); // the index is gone — a forget mid-flight, say
    rivalLock(dir, 'a1', 'forget');

    await readRecord(store, 'a1', AT);

    assert.equal(existsSync(join(dir, besideMeta('a1'))), false, 'a reader regenerated a row while another window was deleting the conversation');

    rmSync(join(dir, lockName('a1'))); // the other window finishes
    await readRecord(store, 'a1', AT);
    assert.equal(existsSync(join(dir, besideMeta('a1'))), true, 'with the lock free, the index was not reconciled after all');
    assert.deepEqual(locksIn(dir), [], 'the reconciliation left its lock behind');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the console names the directory or file that failed; the reason a person reads never does', async () => {
  // gemini, Minor: a person running several profiles cannot tell which store refused from a sentence
  // with no path in it — and a person reading the page must not be shown one. Two audiences, two texts.
  const dir = home();
  try {
    const asFile = join(dir, 'chat-conversations');
    writeFileSync(asFile, 'a file where the store should be', 'utf8');
    const store = new ChatStoreFile(asFile);

    const { value: state, lines } = await capturing(() => store.state());

    assert.equal(state.kind, 'unavailable');
    assert.ok(state.kind === 'unavailable' && !state.reason.includes(asFile), 'the reason a person reads carries the path');
    assert.ok(lines.some((line) => line.includes(asFile)), `no console line named the store that refused: ${lines.join(' | ')}`);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
