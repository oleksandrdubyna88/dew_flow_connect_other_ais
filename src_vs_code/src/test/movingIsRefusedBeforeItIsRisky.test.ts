import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  destinationPlaceRefusal,
  destinationRefusal,
  mayDeleteTheOldCopy,
  sourceChangedSince,
  sourceRefusal,
  sourceWarning,
  verificationFailure,
} from '../dataMove';

/**
 * Moving a data directory that already has things in it.
 *
 * <p><b>The rule here is the OPPOSITE of the install flow's, about the same folder.</b> Adopting a
 * folder means reading a history that is already there, so a non-empty folder is the good case.
 * Moving means writing where nothing is, so a non-empty destination is refused — copying a database
 * over a side's own history destroys it, and destroys it before the "check your history" step could
 * notice. Neither rule may be phrased as "the destination is checked"; each says which direction it
 * is about.</p>
 *
 * <p><b>And the source has to be quiet.</b> The journal mode is WAL, so a database being written
 * while it is read copies as a file that is missing its newest transactions — the rounds committed
 * last, which are the ones somebody would look for first. The extension cannot stop the server (the
 * MCP client owns that process), so what it can do is refuse while the evidence says something is
 * running, and say what to stop.</p>
 */

const NOTHING_RUNNING = { sidecars: [] as string[], livePids: 0 };

// ---------- the destination ----------

test('a destination holding a database is refused, and the sentence says why', () => {
  const refusal = destinationRefusal(['coai.db']);

  assert.match(refusal, /coai\.db/u);
  assert.match(refusal, /histor/u, 'the reason is a history, not a file');
});

test('a destination holding any part of a history is refused, not just the database', () => {
  // `sessions/` without `coai.db` is a half-moved directory from somebody's earlier attempt, and
  // copying into it merges two histories into one that belongs to neither.
  assert.notEqual(destinationRefusal(['sessions/']), '');
  assert.notEqual(destinationRefusal(['chat-conversations/']), '');
});

test('an empty destination is refused by nothing', () => {
  assert.equal(destinationRefusal([]), '');
});

test('a destination holding only things that are not moved is allowed', () => {
  // `logs/` and `servers/` are written by whatever already ran there. They carry no history and are
  // not copied, so their presence says nothing about whether this move is safe.
  assert.equal(destinationRefusal(['logs/', 'servers/']), '');
});

// ---------- and WHERE it is, which is the one that would have eaten the copy ----------

/** The real containment rule, as `dataCommands.ts` supplies it: canonicalised, separator-aware. */
const within = (outer: string, inner: string): boolean =>
  inner === outer || inner.startsWith(outer.endsWith('\\') ? outer : `${outer}\\`);

test('a destination inside the source is refused before anything is copied', () => {
  // codex, Blocking. C:\coai -> C:\coai\new passes every other check: empty, quiet, copies, verifies
  // — and then deleting the source takes the copy inside it, leaving the configuration pointing at a
  // directory that no longer exists.
  const refusal = destinationPlaceRefusal('C:\\coai', 'C:\\coai\\new', within);

  assert.match(refusal, /inside/u);
  assert.match(refusal, /delet/iu, 'and says what would have happened');
});

test('a destination that IS the source is refused', () => {
  assert.notEqual(destinationPlaceRefusal('C:\\coai', 'C:\\coai', within), '');
});

test('a destination that contains the source is refused too', () => {
  assert.notEqual(destinationPlaceRefusal('C:\\coai\\data', 'C:\\coai', within), '');
});

test('a sibling whose name merely starts the same is allowed', () => {
  // `C:\coai2` is not inside `C:\coai`, and a prefix test without the separator would say it was.
  assert.equal(destinationPlaceRefusal('C:\\coai', 'C:\\coai2', within), '');
});

test('an unrelated folder is refused by nothing', () => {
  assert.equal(destinationPlaceRefusal('C:\\coai', 'Z:\\coai', within), '');
});

// ---------- the source ----------

test('a source with a write-ahead log beside its database is NOT refused', () => {
  // The gate's round (codex, Major): refusing on a sidecar makes this feature unreachable for
  // exactly the installations most likely to want it — an unclean stop leaves one behind, and it
  // never goes away by itself. It is also inconsistent with the inventory, which copies BOTH
  // sidecars precisely because they carry committed rounds: with nothing running, copying the three
  // files together IS a consistent snapshot.
  assert.equal(sourceRefusal({ sidecars: ['coai.db-wal'], livePids: 0 }), '');
});

test('but it is warned about, because it means something stopped badly or is attached', () => {
  const warning = sourceWarning({ sidecars: ['coai.db-wal', 'coai.db-shm'], livePids: 0 });

  assert.match(warning, /coai\.db-wal/u);
  assert.match(warning, /copied|copy/iu, 'and says they travel with the database rather than alarming');
});

test('a quiet source warns about nothing', () => {
  assert.equal(sourceWarning(NOTHING_RUNNING), '');
});

test('a source with reviewers still running is refused', () => {
  const refusal = sourceRefusal({ sidecars: [], livePids: 2 });

  assert.notEqual(refusal, '');
  assert.match(refusal, /2/u, 'how many, so a person can tell whether it is theirs');
});

test('a quiet source is refused by nothing', () => {
  assert.equal(sourceRefusal(NOTHING_RUNNING), '');
});

// ---------- the verification ----------

const BEFORE = { rounds: 1284, sessions: 96, usageLines: 5120 };

test('a move that carried everything verifies', () => {
  assert.equal(verificationFailure(BEFORE, { ...BEFORE }), '');
});

test('a destination that reads back fewer rounds fails, naming both numbers', () => {
  const failure = verificationFailure(BEFORE, { ...BEFORE, rounds: 1200 });

  assert.match(failure, /1284/u);
  assert.match(failure, /1200/u);
});

test('a destination that reads back fewer sessions fails even though the database matched', () => {
  // The database is one file and the sessions are ninety-six, so a partial copy is far likelier to
  // lose the second. A verification that only counted rounds would call that move a success.
  assert.notEqual(verificationFailure(BEFORE, { ...BEFORE, sessions: 90 }), '');
});

test('a destination that reads back MORE than the source fails too', () => {
  // More means the destination was not what this move put there — a folder that already held a
  // history the refusal above should have caught, or two moves racing. Either way, not verified.
  assert.notEqual(verificationFailure(BEFORE, { ...BEFORE, rounds: 1300 }), '');
});

// ---------- and only then the delete ----------

test('nothing may be deleted before a move has been verified', () => {
  assert.equal(mayDeleteTheOldCopy(undefined), false, 'no move has happened in this installation');
  assert.equal(
    mayDeleteTheOldCopy({ from: 'C:\\old', to: 'Z:\\coai', verified: false }),
    false,
    'a copy that did not verify is a copy that may have lost something');
});

test('a verified move may have its old copy deleted', () => {
  assert.equal(mayDeleteTheOldCopy({ from: 'C:\\old', to: 'Z:\\coai', verified: true }), true);
});

// ---------- and the source must not have moved on since ----------

test('a source that has been written to since the move is not deleted', () => {
  // codex and gemini, independently: the extension cannot stop the MCP client's server, so a
  // quiescent-looking directory can still have one attached — the server opens the database per
  // write and closes it, so the absence of a sidecar proves nothing. What IS available is to read
  // the source again immediately before deleting it, and refuse when it has grown since the copy.
  const changed = sourceChangedSince(
    { from: 'C:\\old', to: 'Z:\\coai', verified: true, held: BEFORE },
    { ...BEFORE, rounds: BEFORE.rounds + 1 });

  assert.match(changed, /1284/u);
  assert.match(changed, /1285/u);
  assert.match(changed, /not been deleted|nothing has been deleted/iu);
});

test('a prompt added since the copy is seen, because every moved directory is counted', () => {
  // codex, code round: three counts covered the database, the sessions and the ledger, and the
  // inventory moves sixteen things — an edited prompt, a new picture or an audit record passed
  // straight through a verification that called the move safe.
  const held = { ...BEFORE, entries: { 'prompts/': 3, 'chat-conversations/': 12 } };
  const now = { ...BEFORE, entries: { 'prompts/': 4, 'chat-conversations/': 12 } };

  assert.match(
    sourceChangedSince({ from: 'C:\\old', to: 'Z:\\coai', verified: true, held }, now),
    /prompts\/: 3 before, 4 after/u);
});

test('and a comparison that cannot be made is not reported as a difference', () => {
  // A record from before this field has no counts. Reading that as "0 before, 40 after" would
  // refuse every move an older build made, for a reason that is not true.
  const held = { ...BEFORE };
  const now = { ...BEFORE, entries: { 'prompts/': 4 } };

  assert.equal(sourceChangedSince({ from: 'C:\\old', to: 'Z:\\coai', verified: true, held }, now), '');
});

test('a source that is exactly as the move left it may be deleted', () => {
  assert.equal(
    sourceChangedSince({ from: 'C:\\old', to: 'Z:\\coai', verified: true, held: BEFORE }, { ...BEFORE }),
    '');
});

test('a record from a build that kept no fingerprint refuses rather than guessing', () => {
  // An older record has no `held`. Treating "I cannot compare" as "nothing changed" is the one
  // reading that deletes a directory nobody checked.
  assert.notEqual(
    sourceChangedSince({ from: 'C:\\old', to: 'Z:\\coai', verified: true }, { ...BEFORE }),
    '');
});

test('a move whose old and new directory are the same may not delete anything', () => {
  // The one that would delete the live directory. It cannot arise from the flow, and it is exactly
  // the sort of thing a record left by an older build could say.
  assert.equal(mayDeleteTheOldCopy({ from: 'Z:\\coai', to: 'Z:\\coai', verified: true }), false);
});
