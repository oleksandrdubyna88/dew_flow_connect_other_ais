import assert from 'node:assert/strict';
import { test } from 'node:test';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { besideName, writeFileAtomically, writeFileAtomicallySync } from '../atomicFile';

/**
 * The write that a crash cannot leave half of.
 *
 * <p>Extracted from `chatOrphans.ts`, where it was private, because the conversation store needs the
 * same bargain and copying it would be the second implementation the reuse rule forbids. It is
 * tested here rather than there because here is where it now lives.</p>
 *
 * <p><b>Two forms, and that is not an accident.</b> The orphan ledger writes SYNCHRONOUSLY on
 * purpose — a child must be written down before it can be orphaned, and a queued write is exactly
 * what a force-kill does not wait for (`chatOrphans.ts:42-47`). The store has no such constraint and
 * must not block the extension host on a disk. One rule, two shapes, which is why the name is shared
 * and the body is not.</p>
 */

function tempDir(): string {
  return fs.mkdtempSync(path.join(os.tmpdir(), 'coai-atomic-'));
}

test('a file written atomically is there, whole, with nothing left beside it', async () => {
  const dir = tempDir();
  const file = path.join(dir, 'record.json');

  await writeFileAtomically(file, '{"a":1}');

  assert.equal(fs.readFileSync(file, 'utf8'), '{"a":1}');
  assert.deepEqual(fs.readdirSync(dir), ['record.json'], 'a temporary file was left behind');
});

test('the synchronous form is the same bargain, for the caller that cannot wait', () => {
  const dir = tempDir();
  const file = path.join(dir, 'ledger.json');

  writeFileAtomicallySync(file, 'first');
  writeFileAtomicallySync(file, 'second');

  assert.equal(fs.readFileSync(file, 'utf8'), 'second');
  assert.deepEqual(fs.readdirSync(dir), ['ledger.json']);
});

test('a write that replaces a file never leaves the old one truncated', async () => {
  // The defect the whole module exists for: a plain write truncates FIRST, so a crash in that window
  // leaves a file that parses to nothing. Here the old content must survive right up to the rename.
  const dir = tempDir();
  const file = path.join(dir, 'record.json');
  fs.writeFileSync(file, '{"old":true}', 'utf8');

  const big = `{"new":"${'x'.repeat(200_000)}"}`;
  const writing = writeFileAtomically(file, big);

  // While the write is in flight the destination still reads as the OLD record, whole.
  assert.equal(JSON.parse(fs.readFileSync(file, 'utf8')).old, true, 'the destination was truncated mid-write');
  await writing;
  assert.equal(JSON.parse(fs.readFileSync(file, 'utf8')).new.length, 200_000);
});

test('the directory is made when it is not there — the first write of an installation', async () => {
  const dir = path.join(tempDir(), 'not', 'yet');

  await writeFileAtomically(path.join(dir, 'record.json'), 'x');

  assert.equal(fs.readFileSync(path.join(dir, 'record.json'), 'utf8'), 'x');
});

test('the temporary name carries this process, so two windows never write one another’s', () => {
  // Two extension hosts write the same directory. If the beside-file were named for the DESTINATION
  // alone, the second window's write would clobber the first window's half-written temporary and one
  // of the two renames would move a file the other process was still filling.
  const mine = besideName('/data/chat-conversations/abc.json', 111);
  const theirs = besideName('/data/chat-conversations/abc.json', 222);

  assert.notEqual(mine, theirs, 'two processes share one temporary name');
  assert.ok(mine.endsWith('.tmp'), 'the sweep finds an interrupted write by its extension');
  assert.ok(mine.includes('abc.json'), 'a stranded temporary does not say which record it belonged to');
});

test('a write that cannot happen throws rather than reporting success', async () => {
  // The store turns this into `failed` with a reason and keeps the conversation on screen; the
  // ledger logs it. Neither can do that if the failure is swallowed here.
  const dir = tempDir();
  const asDirectory = path.join(dir, 'record.json');
  fs.mkdirSync(asDirectory);

  await assert.rejects(() => writeFileAtomically(asDirectory, 'x'));
  assert.throws(() => writeFileAtomicallySync(asDirectory, 'x'));
});
