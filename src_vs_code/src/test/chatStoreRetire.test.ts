import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { ChatStoreFile, RetireOutcome } from '../chatStoreFile';
import { lockName } from '../chatStoreLock';
import {
  CONVERSATION_VERSION,
  ConversationRecord,
  KEEP_FOR_MS,
  besideMeta,
  metaFrom,
  metaOf,
  recordName,
  sourceOfSession,
} from '../chatStore';
import { ChatMessage } from '../chatPage';

/**
 * The ONE way the sweep deletes a conversation: `retireIfExpired`, against a real directory.
 *
 * <p>The rule all three vendors insisted on in B1's gate round. The sweep's listing says a record is
 * expired; that is a fact about a moment that has passed, and deleting on it is a check-then-act — a
 * save can land in between and would be destroyed. So the store takes the conversation's lock, reads
 * the record again, and deletes only if it is still at the revision the listing showed and still past
 * the window. Every other answer is `kept`, named, and deletes nothing.</p>
 */

const AT = Date.UTC(2026, 8, 13, 12, 0, 0);
const LONG_AGO = AT - KEEP_FOR_MS - 60_000;

function home(): string {
  return mkdtempSync(join(tmpdir(), 'coai-chat-retire-'));
}

const said = (role: 'you' | 'model', text: string): ChatMessage => ({ role, text });

/** A conversation last written well past the window, unless the test says otherwise. */
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
    createdAt: LONG_AGO - 1_000,
    updatedAt: LONG_AGO,
    ...over,
  };
}

const why = (out: RetireOutcome): string => (out.kind === 'kept' ? out.why : out.kind);

const locksIn = (dir: string): readonly string[] => readdirSync(dir).filter((name) => name.endsWith('.lock'));

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

test('an expired conversation still at the revision the listing showed is retired — both files, and no lock left', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    assert.equal((await store.save(record(), 0)).kind, 'ok');

    const out = await store.retireIfExpired('a1', 1, AT);

    assert.equal(out.kind, 'retired', `an expired, unchanged conversation was not retired: ${why(out)}`);
    assert.equal(existsSync(join(dir, recordName('a1'))), false, 'the transcript is still on disk');
    assert.equal(existsSync(join(dir, besideMeta('a1'))), false, 'the index entry is still on disk');
    assert.deepEqual(locksIn(dir), [], 'the retire left its lock behind');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a save that landed between the listing and the delete is NOT destroyed — the record changed under the lock and is kept', async () => {
  // THE check-then-act. The sweep listed rev 1 as expired; a person spoke in that conversation before
  // the sweep reached it; the delete must see rev 2 and stop.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await store.save(record(), 0);
    assert.equal((await store.save(record({ updatedAt: AT - 1_000 }), 1)).kind, 'ok', 'the turn that landed was not saved');
    // And its index entry is torn away, as a crash between the two renames would leave it — the retire
    // holds the lock, so it is the one that may put it back.
    rmSync(join(dir, besideMeta('a1')));

    const out = await store.retireIfExpired('a1', 1, AT);

    assert.equal(why(out), 'changed', `a conversation written since the listing was ${out.kind}`);
    assert.equal(existsSync(join(dir, recordName('a1'))), true, 'the turn that landed was deleted');
    const meta = metaFrom(JSON.parse(readFileSync(join(dir, besideMeta('a1')), 'utf8')));
    assert.equal(meta?.rev, 2, 'the index entry was not regenerated from the record under the lock');
    assert.deepEqual(locksIn(dir), []);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a conversation that is not expired is kept, whatever the listing claimed', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await store.save(record({ updatedAt: AT - 1_000 }), 0);

    const out = await store.retireIfExpired('a1', 1, AT);

    assert.equal(why(out), 'not expired', `a live conversation was ${out.kind}`);
    assert.equal(existsSync(join(dir, recordName('a1'))), true, 'a live conversation was deleted');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('exactly at the window a conversation is kept; one millisecond past it goes', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await store.save(record({ id: 'edge', updatedAt: AT - KEEP_FOR_MS }), 0);
    await store.save(record({ id: 'past', updatedAt: AT - KEEP_FOR_MS - 1 }), 0);

    assert.equal(why(await store.retireIfExpired('edge', 1, AT)), 'not expired', 'a conversation exactly at the window was retired');
    assert.equal(why(await store.retireIfExpired('past', 1, AT)), 'retired', 'a conversation past the window was kept');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a conversation another window is changing is kept — held — and nothing is deleted', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await store.save(record(), 0);
    writeFileSync(join(dir, lockName('a1')), JSON.stringify({ pid: 1, at: new Date(AT).toISOString(), token: 'rival:1', doing: 'save 2' }), 'utf8');

    const out = await store.retireIfExpired('a1', 1, AT);

    assert.equal(why(out), 'held', `a conversation under another window's lock was ${out.kind}`);
    assert.equal(existsSync(join(dir, recordName('a1'))), true, 'a conversation being written was deleted');
    assert.equal(existsSync(join(dir, besideMeta('a1'))), true);
    assert.deepEqual(locksIn(dir), [lockName('a1')], 'the rival lock was broken or a second one left');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a record that is already gone is kept as absent, and its orphaned index entry is left for the sweep’s other rule', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await store.save(record(), 0);
    rmSync(join(dir, recordName('a1')));

    const out = await store.retireIfExpired('a1', 1, AT);

    assert.equal(why(out), 'absent');
    assert.equal(existsSync(join(dir, besideMeta('a1'))), true, 'the retire removed something that was not the conversation it was asked about');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a record this build cannot read is kept, untouched — age never deletes what cannot be read', async () => {
  // A newer build's record after a downgrade looks exactly like this. Deleting it by its neighbour's
  // timestamp would destroy a conversation the next upgrade could read.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await store.save(record(), 0);
    writeFileSync(join(dir, recordName('a1')), '{"version": 99, "rev": 1, "id": "a1"}', 'utf8');

    const { value: out } = await capturing(() => store.retireIfExpired('a1', 1, AT));

    assert.equal(why(out), 'incompatible', `an unreadable record was ${out.kind}`);
    assert.equal(readFileSync(join(dir, recordName('a1')), 'utf8'), '{"version": 99, "rev": 1, "id": "a1"}', 'the unreadable record was changed or removed');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a stale index entry under an expired record is repaired first, and the retire waits for a listing that agrees', async () => {
  // The crash between a save's two renames: the record is at rev 2, the index still says rev 1. The
  // sweep listed rev 1. Deleting would be deleting on a description of the wrong save; instead the
  // index is regenerated under the lock, and the NEXT sweep — which lists rev 2 — retires it.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    await store.save(record(), 0);
    await store.save(record(), 1);
    writeFileSync(join(dir, besideMeta('a1')), JSON.stringify(metaOf(record({ rev: 1 }))), 'utf8');

    const first = await store.retireIfExpired('a1', 1, AT);
    assert.equal(why(first), 'changed', 'a listing at the wrong revision was acted on');
    assert.equal(metaFrom(JSON.parse(readFileSync(join(dir, besideMeta('a1')), 'utf8')))?.rev, 2, 'the stale index entry was not repaired');

    const second = await store.retireIfExpired('a1', 2, AT);
    assert.equal(second.kind, 'retired', 'with the listing agreeing, the expired conversation was still kept');
    assert.equal(existsSync(join(dir, recordName('a1'))), false);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('an id that cannot be a filename is refused, naming the rule, before any lock is taken', async () => {
  const dir = home();
  try {
    const out = await new ChatStoreFile(dir).retireIfExpired('../elsewhere', 1, AT);

    assert.equal(out.kind, 'failed');
    assert.match(out.kind === 'failed' ? out.reason : '', /cannot be a filename/u);
    assert.deepEqual(readdirSync(dir), [], 'something was written for an id that cannot be a filename');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
