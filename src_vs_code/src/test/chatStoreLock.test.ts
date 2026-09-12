import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readFileSync, rmSync, utimesSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { Claim, LOCK_STALE_MS, LOCK_SUFFIX, claimRevision, lockName } from '../chatStoreLock';
import { idOfMeta, isRecordName } from '../chatStore';

/**
 * The claim that makes the store's compare-and-swap an actual swap, against a real directory.
 *
 * <p>The blocking finding of story A2's code round: "read the rev, then rename" is not a
 * compare-and-swap, because a rename does not condition itself on what was read. What does is an
 * exclusive create, and that is the one operation this module rests on — so what is tested here is the
 * behaviour of a REAL `O_EXCL` create on this machine, never a fake of it. Eight racers, one lock; a
 * fresh lock refused; a stale one broken; a release that will not delete somebody else's.</p>
 */

const NOW = Date.UTC(2026, 8, 12, 12, 0, 0);

function home(): string {
  return mkdtempSync(join(tmpdir(), 'coai-chat-lock-'));
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

/** Let a claim go when it was granted; a test that holds one must not leak it into the next. */
async function letGo(claim: Claim): Promise<void> {
  if (claim.kind === 'claimed') {
    await claim.release();
  }
}

test('a claim owns the transition, writes down who took it and when, and a release lets it go', async () => {
  const dir = home();
  try {
    const claim = await claimRevision(dir, 'a1', 6, NOW);
    assert.equal(claim.kind, 'claimed', 'a claim over an unclaimed transition was not granted');

    const path = join(dir, lockName('a1', 6));
    const note = JSON.parse(readFileSync(path, 'utf8')) as { pid: number; at: string; token: string };
    assert.equal(note.pid, process.pid, 'the lock does not say which process took it');
    assert.equal(note.at, new Date(NOW).toISOString(), 'the lock is not dated in UTC from the clock it was given');
    assert.ok(note.token.length > 0, 'the lock carries no token for the release to check');

    await letGo(claim);
    assert.equal(existsSync(path), false, 'a released lock is still on disk');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a second claim on a transition somebody holds is HELD — the exclusive create is the swap', async () => {
  const dir = home();
  try {
    const first = await claimRevision(dir, 'a1', 6, NOW);
    assert.equal(first.kind, 'claimed');

    const second = await claimRevision(dir, 'a1', 6, NOW);
    assert.equal(second.kind, 'held', 'two windows were both granted the same transition');

    await letGo(first);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('eight claims racing for one transition admit exactly one', async () => {
  // Measured, not assumed: this is the property the whole compare-and-swap rests on, and it is the
  // filesystem's, not this module's. If it ever fails here, the store's guarantee is gone with it.
  const dir = home();
  try {
    const racers = await Promise.all(Array.from({ length: 8 }, () => claimRevision(dir, 'a1', 6, NOW)));
    const won = racers.filter((one) => one.kind === 'claimed');

    assert.equal(won.length, 1, `${won.length} writers were granted one transition`);
    assert.equal(racers.filter((one) => one.kind === 'held').length, 7);
    for (const one of won) {
      await letGo(one);
    }
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('claims on DIFFERENT revisions do not contend — the lock names the transition, not the conversation', async () => {
  const dir = home();
  try {
    const six = await claimRevision(dir, 'a1', 6, NOW);
    const seven = await claimRevision(dir, 'a1', 7, NOW);

    assert.equal(six.kind, 'claimed');
    assert.equal(seven.kind, 'claimed', 'the writer of the NEXT revision was blocked by the writer of this one');
    await letGo(six);
    await letGo(seven);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a lock older than the window is broken and the claim granted — a killed writer cannot wedge a conversation', async () => {
  const dir = home();
  try {
    const path = join(dir, lockName('a1', 6));
    const abandoned = { pid: 1, at: new Date(NOW - LOCK_STALE_MS - 1_000).toISOString(), token: 'dead:1' };
    writeFileSync(path, JSON.stringify(abandoned), 'utf8');

    const { value: claim, lines } = await capturing(() => claimRevision(dir, 'a1', 6, NOW));

    assert.equal(claim.kind, 'claimed', 'a lock left by a dead writer held the transition for ever');
    assert.ok(lines.some((line) => line.includes('broken') && line.includes(path)), 'breaking a lock was not said, with its path');
    const note = JSON.parse(readFileSync(path, 'utf8')) as { token: string };
    assert.notEqual(note.token, 'dead:1', 'the stale lock was not replaced by the new claim');
    await letGo(claim);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a lock INSIDE the window is not broken, however tempting — a live writer is never written over', async () => {
  const dir = home();
  try {
    const path = join(dir, lockName('a1', 6));
    const takenAt = NOW - 1_000;
    writeFileSync(path, JSON.stringify({ pid: 1, at: new Date(takenAt).toISOString(), token: 'live:1' }), 'utf8');

    assert.equal((await claimRevision(dir, 'a1', 6, NOW)).kind, 'held', 'a one-second-old lock was broken');
    // Exactly AT the window is still inside it: the window is "older than", not "as old as".
    assert.equal((await claimRevision(dir, 'a1', 6, takenAt + LOCK_STALE_MS)).kind, 'held', 'a lock exactly at the window was broken');
    assert.equal(JSON.parse(readFileSync(path, 'utf8')).token, 'live:1', 'a live lock was replaced');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a lock whose note cannot be read is aged by its mtime — torn but fresh is held, torn and old is broken', async () => {
  // A reader can open a lock in the instant between the exclusive create and its content landing, and
  // see an empty file. It is a real, fresh lock; the file's own time says so.
  const dir = home();
  try {
    const path = join(dir, lockName('a1', 6));
    writeFileSync(path, '', 'utf8');
    assert.equal((await claimRevision(dir, 'a1', 6, Date.now())).kind, 'held', 'an empty but fresh lock was broken');

    const long = new Date(Date.now() - LOCK_STALE_MS - 60_000);
    utimesSync(path, long, long);
    const { value: claim } = await capturing(() => claimRevision(dir, 'a1', 6, Date.now()));
    assert.equal(claim.kind, 'claimed', 'an empty lock a minute past the window was honoured for ever');
    await letGo(claim);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a release does NOT remove a lock somebody else has since taken — the token fence', async () => {
  // The residual the header names: a writer that stalled past the window had its lock broken and
  // re-taken. Its `finally` must not then delete the NEW holder's lock, or a third writer walks in.
  const dir = home();
  try {
    const stalled = await claimRevision(dir, 'a1', 6, NOW);
    assert.equal(stalled.kind, 'claimed');
    const path = join(dir, lockName('a1', 6));
    writeFileSync(path, JSON.stringify({ pid: 2, at: new Date(NOW).toISOString(), token: 'breaker:1' }), 'utf8');

    const { lines } = await capturing(() => letGo(stalled));

    assert.equal(existsSync(path), true, 'a stalled writer deleted the lock of the writer that replaced it');
    assert.equal(JSON.parse(readFileSync(path, 'utf8')).token, 'breaker:1');
    assert.ok(lines.some((line) => line.includes('taken over') && line.includes(path)), 'a save that outlived its lock was not said, with the path');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a release of a lock that is already gone is nothing, not a throw', async () => {
  const dir = home();
  try {
    const claim = await claimRevision(dir, 'a1', 6, NOW);
    rmSync(join(dir, lockName('a1', 6)));

    await assert.doesNotReject(() => letGo(claim));
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a claim in a directory that is not there yet makes it — a first save is what creates a store', async () => {
  const dir = join(home(), 'chat-conversations');
  try {
    const claim = await claimRevision(dir, 'a1', 1, NOW);

    assert.equal(claim.kind, 'claimed', 'the first claim of an installation was refused for want of a directory');
    await letGo(claim);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a claim that cannot create its lock fails with a reason that carries no path', async () => {
  const dir = home();
  try {
    const asFile = join(dir, 'parent-is-a-file');
    writeFileSync(asFile, 'a file standing where the store should be', 'utf8');

    const { value: claim, lines } = await capturing(() => claimRevision(join(asFile, 'chat-conversations'), 'a1', 1, NOW));

    assert.equal(claim.kind, 'failed', 'a lock that could not be created was reported as something else');
    const reason = claim.kind === 'failed' ? claim.reason : '';
    assert.ok(reason.length > 0 && !reason.includes(asFile), 'the reason a person reads carries the path, or nothing');
    assert.ok(lines.some((line) => line.includes(asFile)), 'the console line does not name the path for whoever is debugging');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a lock file is never mistaken for a record or a metadata file', async () => {
  assert.equal(lockName('a1', 6), `a1.6${LOCK_SUFFIX}`);
  assert.equal(isRecordName(lockName('a1', 6)), false, 'a lock would be read as a transcript');
  assert.equal(idOfMeta(lockName('a1', 6)), '', 'a lock would be listed as a conversation');
  assert.throws(() => lockName('../outside', 1), /id/u, 'an escaping id built a lock path');
});
