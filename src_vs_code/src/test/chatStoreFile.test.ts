import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { ChatStoreFile } from '../chatStoreFile';
import {
  CONVERSATION_VERSION,
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
 * record is regenerated rather than believed, that a second window holding a stale revision is
 * refused instead of overwriting the first, and that a store which cannot be READ is told apart from
 * one that is simply empty. None of that can be asserted against a fake filesystem, because the
 * things it guards against — a rename's order, a partial write, a directory that is really a file —
 * are the filesystem's own behaviour. (The plan's test rule: "against a REAL temporary directory.")</p>
 *
 * <p>Every fixture goes through the store's own writer and reader, never a hand-typed JSON cast: a
 * record is {@link ChatStoreFile.save}d and {@link ChatStoreFile.read} back, so the real validator in
 * `chatStore.ts` runs on the way in and the way out. The one exception is where a test SIMULATES an
 * interruption — a hand-written stale metadata file, a metadata deleted from under a record — which
 * is exactly the disk state a crash leaves and cannot be produced through the happy path.</p>
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
    const rev = saved.kind === 'ok' ? saved.rev : -1;
    assert.equal(rev, 1, 'a first write, with no expected rev, must land at rev 1');

    assert.deepEqual(await store.read(mine.id), { ...mine, rev }, 'the conversation did not round-trip');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a save leaves the pair and NOTHING beside it — no interrupted-write temporary', async () => {
  // The atomic write renames a `.tmp` into place; a survivor would be a crash it did not clean up,
  // and the sweep would have to tell it from a write genuinely in flight.
  const dir = home();
  try {
    await new ChatStoreFile(dir).save(record(), 0);

    assert.deepEqual(
      readdirSync(dir).sort(),
      [besideMeta('a1'), recordName('a1')].sort(),
      'a save left a temporary, or wrote the wrong two files',
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

// ---------------------------------------------------------------------------------------------
// rev — order, and the lost-update refusal.
// ---------------------------------------------------------------------------------------------

test('two saves to one id land in the order they were asked for', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);

    const first = await store.save(record({ messages: [said('you', 'first')] }), 0);
    assert.equal(first.kind, 'ok');
    const second = await store.save(
      record({ messages: [said('you', 'second')] }),
      first.kind === 'ok' ? first.rev : 0,
    );
    assert.equal(second.kind, 'ok');

    const back = await store.read('a1');
    assert.deepEqual(back?.messages, [said('you', 'second')], 'the later write did not win');
    assert.equal(back?.rev, 2, 'the second save did not advance the rev');
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

    const survived = await store.read('a1');
    assert.deepEqual(survived?.messages, [said('you', 'window A turn')], 'a refused writer clobbered the transcript anyway');
    assert.equal(survived?.rev, 2, 'the refused write advanced the rev it was not allowed to touch');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a first write needs no expected rev; a SECOND with the same stale expectation is refused', async () => {
  // `expectedRev` of 0/undefined means "I have not read this record". It is honest exactly once: the
  // second time it is a claim the record is not there, which the disk contradicts.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);

    const first = await store.save(record(), undefined);
    assert.equal(first.kind, 'ok', 'a first write with no expected rev was refused');
    assert.equal(first.kind === 'ok' ? first.rev : -1, 1);

    const again = await store.save(record({ passage: 'a second, blind write' }), 0);
    assert.equal(again.kind, 'refused', 'a blind second write over an existing conversation was allowed');
    assert.equal(again.kind === 'refused' ? again.diskRev : -1, 1);
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

    const back = await store.read('a1');
    assert.ok(back !== undefined, 'a record with no metadata was treated as no record');

    const rebuilt = metaFrom(JSON.parse(readFileSync(join(dir, besideMeta('a1')), 'utf8')));
    assert.deepEqual(rebuilt, metaOf(back!), 'the metadata was not regenerated from the record on read');
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

    const back = await store.read('a1');
    const reconciled = metaFrom(JSON.parse(readFileSync(join(dir, besideMeta('a1')), 'utf8')));
    assert.equal(reconciled?.rev, 2, 'the stale metadata was left describing the wrong save');
    assert.equal(reconciled?.turns, 3, 'the row would still show the turn count from before the crash');
    assert.deepEqual(reconciled, metaOf(back!), 'the regenerated index does not match the record it was rebuilt from');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------------------------
// Deletion — the order, and forget.
// ---------------------------------------------------------------------------------------------

test('a delete interrupted after the metadata leaves NO listable row', async () => {
  // forget deletes the metadata first, then the record. A crash between the two leaves a transcript
  // with no metadata — a file nobody can see rather than a row that opens onto nothing. It must not
  // appear in the listing, because the listing is metadata only.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await store.save(record(), 0);
    rmSync(join(dir, besideMeta('a1'))); // the metadata is gone; the record lingers

    assert.deepEqual([...(await store.listMeta())], [], 'a half-deleted conversation still shows a row');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('forget removes both files of a conversation', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await store.save(record(), 0);
    assert.equal(readdirSync(dir).length, 2, 'the setup did not write the pair');

    await store.forget('a1');

    assert.deepEqual(readdirSync(dir), [], 'a forgotten conversation left a file behind');
    assert.equal(await store.read('a1'), undefined, 'a forgotten conversation is still readable');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------------------------
// Missing vs unreadable — the load-bearing distinction.
// ---------------------------------------------------------------------------------------------

test('a directory that is not there reads as EMPTY, and its listing is [] rather than a throw', async () => {
  // Nobody has chatted yet. This is an ordinary state, not an error: the first save materialises the
  // directory, so a read of it beforehand must be quiet.
  const store = new ChatStoreFile(join(home(), 'chat-conversations-not-yet'));

  assert.deepEqual(await store.state(), { kind: 'empty' });
  assert.deepEqual([...(await store.listMeta())], []);
  assert.equal(await store.read('anything'), undefined);
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

    const state = await store.state();
    assert.equal(state.kind, 'unavailable', 'a store that could not be read was reported empty');
    assert.ok(
      state.kind === 'unavailable' && state.reason.length > 0,
      'an unavailable store gave no reason for the picker to show',
    );
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

    assert.deepEqual((await store.listMeta()).map((m) => m.id), ['good'], 'a torn temporary was read as a conversation');
    assert.equal(await store.read('torn'), undefined, 'a torn temporary surfaced as a readable conversation');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a file in the directory that is not one of ours is ignored by the listing', async () => {
  // The coai data directory is shared. A `notes.txt`, a metadata-shaped name with an UNSAFE id, and a
  // transcript-shaped name are all not our metadata files, and none of them may be opened or listed —
  // above all a transcript, whose whole point is to stay unread until a row is chosen.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await store.save(record({ id: 'mine' }), 0);
    writeFileSync(join(dir, 'notes.txt'), 'personal notes', 'utf8');
    writeFileSync(join(dir, 'x.json'), '{"unsafe":true}', 'utf8'); // a transcript-shaped stranger
    writeFileSync(join(dir, 'bad id.meta.json'), '{"has":"a space in its id"}', 'utf8'); // unsafe id

    assert.deepEqual((await store.listMeta()).map((m) => m.id), ['mine'], 'a stranger was read as a conversation');
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

    assert.deepEqual((await store.listMeta()).map((m) => m.id), ['right'], 'a metadata file filed under the wrong id was trusted');
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

    assert.deepEqual(
      (await store.listMeta()).map((m) => m.id).sort(),
      ['first', 'second'],
      'a torn index entry took the readable ones down with it',
    );
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------------------------
// A save that genuinely cannot happen.
// ---------------------------------------------------------------------------------------------

test('a save whose write cannot happen reports failed with a reason, and never throws', async () => {
  // The store turns a disk that will not answer into `failed` and keeps the conversation on screen;
  // it must not throw into the caller. Provoked portably by making the record's own path a DIRECTORY,
  // which no write can rename over. The reason is shown to a person, so it must carry no path.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    mkdirSync(join(dir, recordName('a1'))); // `a1.json` is a directory: the write cannot land

    let outcome!: Awaited<ReturnType<ChatStoreFile['save']>>;
    await assert.doesNotReject(async () => {
      outcome = await store.save(record(), 0);
    });

    assert.equal(outcome.kind, 'failed', 'a save that could not write did not report failure');
    const reason = outcome.kind === 'failed' ? outcome.reason : '';
    assert.ok(reason.length > 0, 'a failed save gave no reason for a person to read');
    assert.ok(!reason.includes(dir), 'the failure reason leaked the filesystem path');
    assert.ok(!reason.includes('\\') && !reason.includes('/'), 'the failure reason leaked a path separator');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a save whose directory cannot even be created fails cleanly, with no path in the reason', async () => {
  // The other failure return site: the probe sees nothing (ENOENT) and lets the write proceed, but
  // the atomic write cannot make the store's directory because its PARENT is a file. The whole save
  // becomes `failed` — never a throw, never a leaked path — and leaves no temporary behind.
  const dir = home();
  try {
    const asFile = join(dir, 'parent-is-a-file');
    writeFileSync(asFile, 'a file standing in for the store’s parent directory', 'utf8');
    const store = new ChatStoreFile(join(asFile, 'chat-conversations'));

    let outcome!: Awaited<ReturnType<ChatStoreFile['save']>>;
    await assert.doesNotReject(async () => {
      outcome = await store.save(record(), 0);
    });

    assert.equal(outcome.kind, 'failed', 'a save that could not create its directory did not report failure');
    const reason = outcome.kind === 'failed' ? outcome.reason : '';
    assert.ok(reason.length > 0, 'a failed save gave no reason for a person to read');
    assert.ok(!reason.includes(asFile), 'the failure reason leaked the filesystem path');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
