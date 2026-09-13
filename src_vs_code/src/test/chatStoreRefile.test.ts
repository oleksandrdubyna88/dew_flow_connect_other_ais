import assert from 'node:assert/strict';
import { mkdtempSync, readdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { ChatStoreFile } from '../chatStoreFile';
import {
  CONVERSATION_VERSION,
  ConversationRecord,
  sourceOfFile,
  sourceOfSession,
} from '../chatStore';

/**
 * Following a moved file, against a REAL temporary directory.
 *
 * <p>The operation exists because three reviewers refused the first design, which read the record and
 * then saved it and treated a refusal as "somebody else did it". That is the check-then-act this
 * store has been corrected for twice, and a refusal is not evidence that anybody wrote YOUR change:
 * the other window may have been writing a turn and may never learn about the rename at all. So the
 * read and the write happen inside one claim and there is no retry to get wrong.</p>
 */

const AT = Date.UTC(2026, 8, 13, 12, 0, 0);
const WAS = 'file:///D:/rsd/one/main.ts';
const NOW = 'file:///D:/rsd/two/main.ts';

function home(): string {
  return mkdtempSync(join(tmpdir(), 'coai-chat-refile-'));
}

function record(over: Partial<ConversationRecord> = {}): ConversationRecord {
  return {
    version: CONVERSATION_VERSION,
    rev: 1,
    id: 'a1',
    title: 'Why the lock is fenced',
    passage: 'the passage',
    modelId: 'gpt-5.4',
    messages: [{ role: 'you', text: 'why' }, { role: 'model', text: 'because' }],
    fromSession: false,
    carryFrom: 0,
    source: sourceOfFile(WAS),
    workspace: 'D:\\rsd\\one',
    createdAt: AT - 10_000,
    updatedAt: AT - 1_000,
    ...over,
  };
}

const locksIn = (dir: string): readonly string[] => readdirSync(dir).filter((name) => name.endsWith('.lock'));

test('a moved file takes its conversation with it — the source AND the root, in one revision', async () => {
  // Both facts change together. A file dragged from one root into another changes where it is and
  // which project it belongs to, and writing the uri alone would leave the conversation filed under
  // the root it left — invisible in exactly the folder the person is now looking at, which is the
  // misfiling this whole story exists to remove. (Two vendors, the plan round.)
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    assert.equal((await store.save(record(), 0)).kind, 'ok');

    const done = await store.refile('a1', sourceOfFile(WAS), sourceOfFile(NOW), 'D:\\rsd\\two', AT);

    assert.equal(done.kind, 'followed', `a renamed file did not take its conversation: ${JSON.stringify(done)}`);
    const seen = await store.read('a1');
    assert.equal(seen.kind, 'record');
    const now = seen.kind === 'record' ? seen.record : undefined;
    assert.deepEqual(now?.source, sourceOfFile(NOW), 'the conversation still names the path the file left');
    assert.equal(now?.workspace, 'D:\\rsd\\two', 'the conversation is still filed under the root it left');
    assert.equal(now?.rev, 2, 'the follow did not count as a revision, so another window cannot tell it happened');
    // And nothing else moved: a follow is not an edit of the conversation.
    assert.deepEqual(now?.messages, record().messages, 'following a rename rewrote what was said');
    assert.equal(now?.title, record().title);
    assert.equal(now?.createdAt, record().createdAt, 'the conversation was re-dated by a file move');
    assert.deepEqual(locksIn(dir), [], 'the follow left its lock behind');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a record whose source has MOVED ON is left alone, and that is not a failure', async () => {
  // Another window followed the same rename first, or the person closed the conversation and opened
  // another from the new file. Overwriting on the strength of what this window read a moment ago
  // would undo their work — and there is nothing wrong here to report.
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);
    assert.equal((await store.save(record({ source: sourceOfFile(NOW) }), 0)).kind, 'ok');

    const done = await store.refile('a1', sourceOfFile(WAS), sourceOfFile('file:///D:/rsd/three/main.ts'), 'D:\\rsd\\three', AT);

    assert.equal(done.kind, 'kept');
    assert.equal(done.kind === 'kept' ? done.why : '', 'moved on');
    const seen = await store.read('a1');
    assert.deepEqual(seen.kind === 'record' ? seen.record.source : undefined, sourceOfFile(NOW), 'somebody else’s follow was overwritten');
    assert.equal(seen.kind === 'record' ? seen.record.rev : 0, 1, 'a record that was left alone was still written');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a conversation that is not there, and one of another kind, are both kept rather than invented', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);

    const missing = await store.refile('a1', sourceOfFile(WAS), sourceOfFile(NOW), 'D:\\rsd\\two', AT);
    assert.equal(missing.kind, 'kept');
    assert.equal(missing.kind === 'kept' ? missing.why : '', 'absent', 'a conversation that does not exist was written');
    assert.deepEqual(readdirSync(dir), [], 'following a rename created a conversation nobody had');

    // A CLAUDE conversation is named by a session id, which no file move can change. `fromSession`
    // goes with it: a record states its origin twice and `agreeOnOrigin` refuses a pair that
    // disagrees — which is why `pinSession` writes a session source only on a tab that has one.
    assert.equal((await store.save(record({ source: sourceOfSession('9f1c'), fromSession: true }), 0)).kind, 'ok');
    const claude = await store.refile('a1', sourceOfFile(WAS), sourceOfFile(NOW), 'D:\\rsd\\two', AT);
    assert.equal(claude.kind, 'kept', 'a Claude conversation was re-filed by a file rename');
    assert.equal(claude.kind === 'kept' ? claude.why : '', 'moved on');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('an id that cannot be a filename is refused before anything is opened', async () => {
  const dir = home();
  try {
    const store = new ChatStoreFile(dir);

    const out = await store.refile('../escape', sourceOfFile(WAS), sourceOfFile(NOW), 'D:\\rsd\\two', AT);

    assert.equal(out.kind, 'failed');
    assert.deepEqual(readdirSync(dir), [], 'something was written for an id that cannot be a filename');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
