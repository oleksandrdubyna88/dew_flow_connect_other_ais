import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, statSync, utimesSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { Claim, HELD_PAUSE_MS, HELD_TRIES, LOCK_STALE_MS, LOCK_SUFFIX, claimConversation, lockName } from '../chatStoreLock';
import { idOfMeta, isRecordName } from '../chatStore';

/**
 * The claim that makes the store's compare-and-swap an actual swap, against a real directory.
 *
 * <p>The blocking finding of story A2's first code round: "read the rev, then rename" is not a
 * compare-and-swap, because a rename does not condition itself on what was read. What does is an
 * exclusive create, and that is the one operation this module rests on — so what is tested here is the
 * behaviour of a REAL `O_EXCL` create on this machine, never a fake of it. Eight racers, one lock; a
 * fresh lock refused; a stale one broken; a release that will not delete anything it cannot positively
 * read as its own; and the brief wait that keeps a millisecond-long reconciliation from being mistaken
 * for a rival window.</p>
 */

const NOW = Date.UTC(2026, 8, 12, 12, 0, 0);

/** How long a claim may spend waiting on a held lock before it gives up — the bound the tests pay. */
const WAIT_MS = Array.from({ length: HELD_TRIES - 1 }, (_, at) => HELD_PAUSE_MS * (at + 1)).reduce((sum, ms) => sum + ms, 0);

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

const note = (path: string): { pid: number; at: string; token: string; doing: string } =>
  JSON.parse(readFileSync(path, 'utf8')) as { pid: number; at: string; token: string; doing: string };

test('a claim owns the conversation, writes down who took it, when and for what, and a release lets it go', async () => {
  const dir = home();
  try {
    const claim = await claimConversation(dir, 'a1', 'save 6', NOW);
    assert.equal(claim.kind, 'claimed', 'a claim over an unclaimed conversation was not granted');

    const path = join(dir, lockName('a1'));
    const taken = note(path);
    assert.equal(taken.pid, process.pid, 'the lock does not say which process took it');
    assert.equal(taken.at, new Date(NOW).toISOString(), 'the lock is not dated in UTC from the clock it was given');
    assert.ok(taken.token.length > 0, 'the lock carries no token for the release to check');
    assert.equal(taken.doing, 'save 6', 'the lock does not say what its holder is doing — the target rev lives HERE, not in the name');

    await letGo(claim);
    assert.equal(existsSync(path), false, 'a released lock is still on disk');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a second claim on a conversation somebody holds is HELD, whatever either is doing — the exclusive create is the swap', async () => {
  // One lock per CONVERSATION: a save, a forget and a reconciliation all contend, because each of them
  // changes what the other two would read. The first draft's per-revision lock let a forget race a save.
  const dir = home();
  try {
    const saving = await claimConversation(dir, 'a1', 'save 6', NOW);
    assert.equal(saving.kind, 'claimed');

    const started = Date.now();
    assert.equal((await claimConversation(dir, 'a1', 'forget', NOW)).kind, 'held', 'a forget was granted while a save held the conversation');
    assert.equal((await claimConversation(dir, 'a1', 'save 7', NOW)).kind, 'held', 'a writer of the NEXT revision was granted the conversation mid-save');
    assert.ok(Date.now() - started >= WAIT_MS, 'a held claim was reported without waiting for the holder to finish');

    await letGo(saving);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a lock that is released during the wait is then claimed — a reconciliation is not a rival', async () => {
  // The implication of one lock per conversation: a `held` lock is usually a metadata write a few
  // milliseconds from done. Reporting it as a rival on the first EEXIST would make story A3 re-mint the
  // conversation over an index write that was never another window at all.
  const dir = home();
  try {
    const first = await claimConversation(dir, 'a1', 'reconcile 3', NOW);
    setTimeout(() => { void letGo(first); }, HELD_PAUSE_MS * 2);

    const second = await claimConversation(dir, 'a1', 'save 4', NOW);

    assert.equal(second.kind, 'claimed', 'a claim gave up on a lock that was released within the wait');
    await letGo(second);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('claims on DIFFERENT conversations do not contend', async () => {
  const dir = home();
  try {
    const one = await claimConversation(dir, 'a1', 'save 6', NOW);
    const other = await claimConversation(dir, 'b2', 'save 1', NOW);

    assert.equal(one.kind, 'claimed');
    assert.equal(other.kind, 'claimed', 'a save to one conversation blocked a save to another');
    await letGo(one);
    await letGo(other);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('eight claims racing for one conversation admit exactly one', async () => {
  // Measured, not assumed: this is the property the whole compare-and-swap rests on, and it is the
  // filesystem's, not this module's. If it ever fails here, the store's guarantee is gone with it.
  const dir = home();
  try {
    const racers = await Promise.all(Array.from({ length: 8 }, (_, at) => claimConversation(dir, 'a1', `save ${at}`, NOW)));
    const won = racers.filter((one) => one.kind === 'claimed');

    assert.equal(won.length, 1, `${won.length} writers were granted one conversation`);
    assert.equal(racers.filter((one) => one.kind === 'held').length, 7);
    for (const one of won) {
      await letGo(one);
    }
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a lock older than the window is broken and the claim granted — a killed writer cannot wedge a conversation', async () => {
  const dir = home();
  try {
    const path = join(dir, lockName('a1'));
    const abandoned = { pid: 1, at: new Date(NOW - LOCK_STALE_MS - 1_000).toISOString(), token: 'dead:1', doing: 'save 9' };
    writeFileSync(path, JSON.stringify(abandoned), 'utf8');

    const { value: claim, lines } = await capturing(() => claimConversation(dir, 'a1', 'save 9', NOW));

    assert.equal(claim.kind, 'claimed', 'a lock left by a dead writer held the conversation for ever');
    assert.ok(lines.some((line) => line.includes('broken') && line.includes(path)), 'breaking a lock was not said, with its path');
    assert.notEqual(note(path).token, 'dead:1', 'the stale lock was not replaced by the new claim');
    await letGo(claim);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a lock INSIDE the window is not broken, however tempting — a live writer is never written over', async () => {
  const dir = home();
  try {
    const path = join(dir, lockName('a1'));
    writeFileSync(path, JSON.stringify({ pid: 1, at: new Date(NOW - 1_000).toISOString(), token: 'live:1', doing: 'save 2' }), 'utf8');

    assert.equal((await claimConversation(dir, 'a1', 'save 2', NOW)).kind, 'held', 'a one-second-old lock was broken');
    assert.equal(note(path).token, 'live:1', 'a live lock was replaced');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a lock that becomes stale DURING the wait is broken, not reported held — the clock runs', async () => {
  // gemini, the third round: the loop aged a rival's lock against the instant the claim began, so a
  // lock 29.95 seconds old — genuinely abandoned by the end of a 120 ms wait — was still "fresh" on
  // every attempt, and the caller re-minted a conversation as a copy for nothing. Fifty milliseconds
  // short of the window at the start; well past it before the wait is out.
  const dir = home();
  try {
    const path = join(dir, lockName('a1'));
    const almost = { pid: 1, at: new Date(NOW - LOCK_STALE_MS + 50).toISOString(), token: 'almost:1', doing: 'save 2' };
    writeFileSync(path, JSON.stringify(almost), 'utf8');

    const { value: claim } = await capturing(() => claimConversation(dir, 'a1', 'save 2', NOW));

    assert.equal(claim.kind, 'claimed', 'a lock that went stale during the wait was reported held against a frozen clock');
    assert.notEqual(note(path).token, 'almost:1');
    await letGo(claim);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a DIRECTORY left at the lock path, older than the window, is broken rather than wedging the conversation', async () => {
  // The third round: `rm` without `recursive` throws on a directory, and the throw read as "not stale"
  // — a permanent wedge, against a one-word fix. Nothing we write puts a directory there; this is the
  // defence, and it needs the ageing to work on a path that will not READ as a file at all.
  const dir = home();
  try {
    const path = join(dir, lockName('a1'));
    mkdirSync(path);
    const long = new Date(Date.now() - LOCK_STALE_MS - 60_000);
    utimesSync(path, long, long);

    const { value: claim } = await capturing(() => claimConversation(dir, 'a1', 'save 1', Date.now()));

    assert.equal(claim.kind, 'claimed', 'a stale directory at the lock path wedged the conversation for good');
    assert.equal(statSync(path).isFile(), true, 'the lock path is not our lock file after the break');
    await letGo(claim);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a lock whose note cannot be read is aged by its mtime — torn but fresh is held, torn and old is broken', async () => {
  // A reader can open a lock in the instant between the exclusive create and its content landing, and
  // see an empty file. It is a real, fresh lock; the file's own time says so.
  const dir = home();
  try {
    const path = join(dir, lockName('a1'));
    writeFileSync(path, '', 'utf8');
    assert.equal((await claimConversation(dir, 'a1', 'save 1', Date.now())).kind, 'held', 'an empty but fresh lock was broken');

    const long = new Date(Date.now() - LOCK_STALE_MS - 60_000);
    utimesSync(path, long, long);
    const { value: claim } = await capturing(() => claimConversation(dir, 'a1', 'save 1', Date.now()));
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
    const stalled = await claimConversation(dir, 'a1', 'save 6', NOW);
    assert.equal(stalled.kind, 'claimed');
    const path = join(dir, lockName('a1'));
    writeFileSync(path, JSON.stringify({ pid: 2, at: new Date(NOW).toISOString(), token: 'breaker:1', doing: 'save 6' }), 'utf8');

    const { lines } = await capturing(() => letGo(stalled));

    assert.equal(existsSync(path), true, 'a stalled writer deleted the lock of the writer that replaced it');
    assert.equal(note(path).token, 'breaker:1');
    assert.ok(lines.some((line) => line.includes('taken over') && line.includes(path)), 'a save that outlived its lock was not said, with the path');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a release leaves a lock it cannot POSITIVELY read as its own — a torn note is not a licence to delete', async () => {
  // gemini's exact fix: `note?.token !== token` returns. A replacement's lock that is still being
  // written reads back as nothing, and "not verifiably mine" must never become "so I may remove it".
  const dir = home();
  try {
    const mine = await claimConversation(dir, 'a1', 'save 6', NOW);
    assert.equal(mine.kind, 'claimed');
    const path = join(dir, lockName('a1'));
    writeFileSync(path, '', 'utf8'); // somebody's fresh claim, content not yet landed

    await letGo(mine);

    assert.equal(existsSync(path), true, 'a lock that could not be read as ours was deleted anyway');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a release of a lock that is already gone is nothing, not a throw', async () => {
  const dir = home();
  try {
    const claim = await claimConversation(dir, 'a1', 'save 6', NOW);
    rmSync(join(dir, lockName('a1')));

    await assert.doesNotReject(() => letGo(claim));
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a claim in a directory that is not there yet makes it — a first save is what creates a store', async () => {
  const dir = join(home(), 'chat-conversations');
  try {
    const claim = await claimConversation(dir, 'a1', 'save 1', NOW);

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

    const { value: claim, lines } = await capturing(() => claimConversation(join(asFile, 'chat-conversations'), 'a1', 'save 1', NOW));

    assert.equal(claim.kind, 'failed', 'a lock that could not be created was reported as something else');
    const reason = claim.kind === 'failed' ? claim.reason : '';
    assert.ok(reason.length > 0 && !reason.includes(asFile), 'the reason a person reads carries the path, or nothing');
    assert.ok(lines.some((line) => line.includes(asFile)), 'the console line does not name the path for whoever is debugging');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a lock file is never mistaken for a record or a metadata file', () => {
  assert.equal(lockName('a1'), `a1${LOCK_SUFFIX}`);
  assert.equal(isRecordName(lockName('a1')), false, 'a lock would be read as a transcript');
  assert.equal(idOfMeta(lockName('a1')), '', 'a lock would be listed as a conversation');
  assert.throws(() => lockName('../outside'), /id/u, 'an escaping id built a lock path');
});
