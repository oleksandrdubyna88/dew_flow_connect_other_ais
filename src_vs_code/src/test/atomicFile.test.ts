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

// ---------------------------------------------------------------------------------------------
// What the code round of story A1 found. Each of these is a test before it was a fix.
// ---------------------------------------------------------------------------------------------

test('two writes to one destination from ONE process do not share a temporary', async () => {
  // codex and gemini, independently: the beside-name carried the pid and nothing else, so two
  // overlapping saves of the same conversation in one window wrote and renamed the same temporary.
  // One call can truncate what the other is still writing, and the loser reports success over
  // content it did not write.
  const dir = tempDir();
  const file = path.join(dir, 'record.json');

  const first = writeFileAtomically(file, 'a'.repeat(120_000));
  const second = writeFileAtomically(file, 'b'.repeat(120_000));
  await Promise.all([first, second]);

  const landed = fs.readFileSync(file, 'utf8');
  assert.ok(
    landed === 'a'.repeat(120_000) || landed === 'b'.repeat(120_000),
    'the file is a mixture of two writes, which is the tear this module exists to prevent',
  );
  assert.deepEqual(fs.readdirSync(dir), ['record.json'], 'a temporary survived a concurrent write');
});

test('two beside-names from one process differ, so overlapping writes cannot collide', () => {
  const path1 = besideName('/data/abc.json');
  const path2 = besideName('/data/abc.json');

  assert.notEqual(path1, path2, 'one process reuses a temporary name for two writes in flight');
});

test('a write that fails leaves no temporary behind, and still says it failed', async () => {
  // Otherwise every failed save leaves a `.tmp` for the sweep to find, and a directory accumulates
  // one per disk error. The throw is what the store turns into a refusal, so it must survive.
  const dir = tempDir();
  const file = path.join(dir, 'sub', 'record.json');
  fs.mkdirSync(path.join(dir, 'sub'));
  fs.mkdirSync(file); // the destination is a directory: the rename cannot happen

  await assert.rejects(() => writeFileAtomically(file, 'x'));

  const left = fs.readdirSync(path.join(dir, 'sub')).filter((name) => name.endsWith('.tmp'));
  assert.deepEqual(left, [], `a failed write left ${left.join(', ')} behind`);
});

test('the synchronous form cleans up after itself too', () => {
  const dir = tempDir();
  const file = path.join(dir, 'record.json');
  fs.mkdirSync(file);

  assert.throws(() => writeFileAtomicallySync(file, 'x'));
  assert.deepEqual(fs.readdirSync(dir).filter((name) => name.endsWith('.tmp')), []);
});

test('a rename over an existing file replaces it — measured here, not assumed', () => {
  // gemini's round said a POSIX rename over an existing destination is not atomic on Windows and
  // throws EPERM/EEXIST. Node's rename goes through MoveFileEx with MOVEFILE_REPLACE_EXISTING, so
  // it replaces; this test is the measurement, and it runs on whatever this suite runs on. The
  // orphan ledger has shipped on exactly this call for months.
  const dir = tempDir();
  const file = path.join(dir, 'record.json');

  writeFileAtomicallySync(file, 'first');
  writeFileAtomicallySync(file, 'second');

  assert.equal(fs.readFileSync(file, 'utf8'), 'second', 'a rename over an existing file did not replace it');
});

test('many writes racing for one destination all succeed — Windows EPERM is retried, not reported', async () => {
  // MEASURED, and it is why the retry exists. Two renames aiming at one destination at the same
  // moment fail on Windows with `EPERM: operation not permitted, rename` — the destination is held
  // for an instant by the other rename. It surfaced in this suite's own parallel run, on the test
  // above, after that test had passed on its own a dozen times: the classic "passes alone, fails in
  // the suite", which is never a flake.
  //
  // It matters because the store saves a record and its metadata on every turn, and a turn landing
  // while the previous save is in flight is ordinary. Without the retry a person would see a save
  // refused for a reason that has nothing to do with their conversation.
  const dir = tempDir();
  const file = path.join(dir, 'record.json');

  const racers = Array.from({ length: 24 }, (_, at) => writeFileAtomically(file, `write-${at}`));
  await Promise.all(racers);

  assert.ok(fs.readFileSync(file, 'utf8').startsWith('write-'), 'the destination holds no whole write');
  assert.deepEqual(
    fs.readdirSync(dir),
    ['record.json'],
    'a temporary survived: a retry that gave up must still clean up after itself',
  );
});

/**
 * BYTES, and the guarantee a pasted picture needed from this.
 *
 * <p><b>What it was written for.</b> `attachPicture` deleted the picture already attached and THEN
 * wrote the replacement straight to its final name. A full or unwritable disk left the conversation
 * with no image and nothing to retry from — the failure destroyed the state it was meant to replace.
 * The fix is this helper, which writes beside the destination and renames over it, so the old file
 * survives anything that goes wrong on the way; it took `string` only, and a picture is a
 * `Buffer`.</p>
 *
 * <p><b>And a claim about it that was measured and dropped.</b> The first draft branched on the type
 * so that `'utf8'` was passed only for a string, on the reasoning that handing an encoding to a
 * buffer write mangles it. It does not — Node ignores the encoding for a buffer, and the case below
 * passes with that branch removed. The branch went; the case stayed, because a type widened to accept
 * bytes is exactly where a silent re-encoding would otherwise hide.</p>
 */

test('bytes survive the round trip, and are not read as text on the way', async (t) => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-atomic-bytes-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const file = path.join(dir, 'picture.png');
  // The first eight bytes of a real PNG, including the 0x89 that no UTF-8 encoding survives.
  const png = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

  await writeFileAtomically(file, png);

  assert.deepEqual([...fs.readFileSync(file)], [...png],
    'the bytes came back changed on the way through the atomic write');
});

test('a write that fails leaves the file that was already there', async (t) => {
  // THE GUARANTEE THE PICTURE PATH NEEDED. Replacing an attachment used to delete first and write
  // second; this is the shape that makes deleting unnecessary, because the old bytes are still the
  // ones at that path until the new ones have fully landed.
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-atomic-bytes-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const file = path.join(dir, 'picture.png');
  const before = Buffer.from([1, 2, 3, 4]);
  await writeFileAtomically(file, before);

  // A directory where the temporary would go cannot be written: the write throws, the rename never
  // happens, and the destination is untouched.
  await assert.rejects(writeFileAtomically(path.join(dir, 'picture.png', 'nested'), Buffer.from([9])));

  assert.deepEqual([...fs.readFileSync(file)], [...before],
    'a failed replacement destroyed the file that was already attached');
});
