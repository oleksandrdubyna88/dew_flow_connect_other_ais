import assert from 'node:assert/strict';
import { test } from 'node:test';

import { type FileRead, healthPaths, readSide, sideSignature, withBeatsSeen } from '../consultantHealthRead';

/**
 * Reading one side's `consultations/health/` through ports (E5.1).
 *
 * <p>The read never decides what the tab shows; it says what is on disk, and — the part worth a test — tells "absent"
 * from "could not be read right now". A rename landing on an open handle is the ordinary Windows case and a WSL share
 * whose distribution is stopped does not answer at all: either one blanking the block a person is reading, at random,
 * is the defect `consultationWatcher.ts` already fixed once. So a failed read answers NOTHING, and the caller keeps the
 * last snapshot.</p>
 */

const KINDS = ['claude', 'codex'];

function check(over: Record<string, unknown> = {}): string {
  return JSON.stringify({
    callerKind: 'claude', state: 'checking', startedUtc: '2026-10-03T10:00:00Z', heartbeatUtc: '2026-10-03T10:00:30Z',
    heartbeatStaleAfterSeconds: 60, ...over,
  });
}

/** A side whose files are the ones named — every other path absent — recording what was asked for. */
function disk(files: Readonly<Record<string, FileRead>>): { read: (path: string) => Promise<FileRead>; asked: string[] } {
  const asked: string[] = [];

  return {
    asked,
    read: (path) => {
      asked.push(path);
      return Promise.resolve(files[path] ?? { kind: 'absent' });
    },
  };
}

function bytes(text: string): FileRead {
  return { kind: 'bytes', bytes: new TextEncoder().encode(text) };
}

const DIR = '\\\\wsl.localhost\\Ubuntu\\home\\u\\.local\\share\\coai-mcp\\';
const PATHS = healthPaths(DIR);

test('the files are read under the directory\'s consultations/health, whatever separator it ended with', () => {
  assert.equal(PATHS.consultants, '\\\\wsl.localhost\\Ubuntu\\home\\u\\.local\\share\\coai-mcp/consultations/health/consultants.json');
  assert.equal(PATHS.check('claude'), '\\\\wsl.localhost\\Ubuntu\\home\\u\\.local\\share\\coai-mcp/consultations/health/claude.check.json');
});

test('a side nobody has written anything into reads as nothing there — not as a failure', async () => {
  const side = await readSide(DIR, KINDS, disk({}));

  assert.deepEqual(side, { report: undefined, checks: {}, outcomes: '' });
});

test('every caller kind\'s check is asked for, and the consultants file once', async () => {
  const one = disk({});
  await readSide(DIR, KINDS, one);

  assert.deepEqual([...one.asked].sort(), [PATHS.consultants, PATHS.check('claude'), PATHS.check('codex')].sort());
});

test('a read that FAILED answers nothing, so the last snapshot stays on screen', async () => {
  assert.equal(await readSide(DIR, KINDS, disk({ [PATHS.consultants]: { kind: 'failed', why: 'EBUSY' } })), undefined);
  assert.equal(await readSide(DIR, KINDS, disk({ [PATHS.check('codex')]: { kind: 'failed', why: 'EBUSY' } })), undefined);
});

test('what is there is parsed, and a file that does not parse is said to be unreadable rather than skipped', async () => {
  const side = await readSide(DIR, KINDS, disk({
    [PATHS.consultants]: bytes(JSON.stringify({ utc: '2026-10-03T10:00:00Z', side: 'wsl', consultants: [] })),
    [PATHS.check('claude')]: bytes(check()),
    [PATHS.check('codex')]: bytes('{ torn'),
  }));

  assert.equal(side?.report?.kind, 'found');
  assert.equal(side?.checks['claude']?.kind, 'found');
  assert.equal(side?.checks['codex']?.kind, 'unreadable');
});

test('a heartbeat alone does not change what a person sees — the signature ignores it', async () => {
  const before = await readSide(DIR, KINDS, disk({ [PATHS.check('claude')]: bytes(check()) }));
  const beat = await readSide(DIR, KINDS, disk({ [PATHS.check('claude')]: bytes(check({ heartbeatUtc: '2026-10-03T10:00:45Z' })) }));
  const now = Date.parse('2026-10-03T10:01:00Z');

  assert.equal(sideSignature(beat, 'this', now), sideSignature(before, 'this', now),
    'a check rewrites its heartbeat every 15 s, and a repaint per beat would rebuild the page under the person');
});

test('a state that changed does change the signature', async () => {
  const before = await readSide(DIR, KINDS, disk({ [PATHS.check('claude')]: bytes(check()) }));
  const done = await readSide(DIR, KINDS, disk({ [PATHS.check('claude')]: bytes(check({ state: 'answered', finishedUtc: '2026-10-03T10:02:00Z' })) }));
  const now = Date.parse('2026-10-03T10:01:00Z');

  assert.notEqual(sideSignature(done, 'this', now), sideSignature(before, 'this', now));
});

test('on the other side, a heartbeat this window has seen stand still past the staleness changes the signature, so the page says abandoned', async () => {
  const read = await readSide(DIR, KINDS, disk({ [PATHS.check('claude')]: bytes(check()) }));
  assert.ok(read !== undefined);
  const side = withBeatsSeen(read, undefined, Date.parse('2026-10-03T10:01:00Z'));

  assert.notEqual(
    sideSignature(side, 'other', Date.parse('2026-10-03T10:05:00Z')),
    sideSignature(side, 'other', Date.parse('2026-10-03T10:01:00Z')),
    'nothing on disk moves when a WSL check dies, so only this window\'s clock can turn its checking into abandoned',
  );
});

test('a heartbeat keeps the instant it was first seen until it moves; a moved one is first seen now (O)', async () => {
  const first = await readSide(DIR, KINDS, disk({ [PATHS.check('claude')]: bytes(check()) }));
  const moved = await readSide(DIR, KINDS, disk({ [PATHS.check('claude')]: bytes(check({ heartbeatUtc: '2026-10-03T10:00:45Z' })) }));
  assert.ok(first !== undefined && moved !== undefined);
  const seen = withBeatsSeen(first, undefined, 1000);

  assert.deepEqual(withBeatsSeen(first, seen, 5000).beatsSeenMs, { 'claude|2026-10-03T10:00:30Z': 1000 }, 'a heartbeat that stood still lost its first sighting');
  assert.deepEqual(withBeatsSeen(moved, seen, 5000).beatsSeenMs, { 'claude|2026-10-03T10:00:45Z': 5000 }, 'a moved heartbeat kept the old sighting, or the old key');
});

test('THIS side also reads its outcome files, so a consultation that answered or failed is seen — another side does not (A2)', async () => {
  const own = disk({ [PATHS.failure('claude')]: bytes('{"kind":"quota"}') });
  const side = await readSide(DIR, KINDS, own, true);
  const other = disk({});
  await readSide(DIR, KINDS, other);

  assert.ok(own.asked.includes(PATHS.answer('codex')) && own.asked.includes(PATHS.failure('claude')));
  assert.match(side?.outcomes ?? '', /quota/u);
  assert.equal(other.asked.some((path) => path.endsWith('.failure.json') || path.endsWith('.answer.json')), false,
    'reading eight more files across a WSL share every poll buys nothing — that side writes its outcomes into its survey');
});
