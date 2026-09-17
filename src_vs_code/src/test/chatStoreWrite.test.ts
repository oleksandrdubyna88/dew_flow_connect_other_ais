import assert from 'node:assert/strict';
import { test } from 'node:test';
import { SaveOutcome } from '../chatStoreFile';
import { BUSY_ELSEWHERE, CONTINUED_ELSEWHERE, INDEX_BEHIND, STILL_SAFE, WriteNext, ifStillOurs, nextAfterSave } from '../chatStoreWrite';

/**
 * What a conversation DOES about the answer its save came back with.
 *
 * <p>Pure, and its own module rather than a branch inside `chatCommand.ts`, for the reason every
 * decision in this feature has been put somewhere a test can reach: that file is 2 484 lines against
 * a limit of 800, and the one thing a reviewer cannot check there is a rule buried in wiring.</p>
 *
 * <p>The rule itself is short. A save either kept this conversation where it was, or it did not — and
 * when it did not, the conversation is not lost, it is FORKED: the transcript is in memory, it is
 * written under a new id, and the tab says so. Nothing here throws anything away.</p>
 */

test('a save that landed keeps the conversation where it is, at the revision the disk now holds', () => {
  assert.deepEqual(nextAfterSave({ kind: 'ok', rev: 7 }, 6), { kind: 'kept', rev: 7 });
});

test('a half-commit keeps the conversation too, and the caller still advances its revision', () => {
  // The record committed and its index did not. Advancing is what stops this window's NEXT save
  // refusing against its own write — and the index regenerates itself the next time anything reads
  // the record, which is why this is not a failure.
  const next = nextAfterSave({ kind: 'partial', rev: 7, reason: 'the index could not be written (ENOSPC)' }, 6);

  assert.equal(next.kind, 'kept');
  assert.equal(next.kind === 'kept' ? next.rev : 0, 7, 'a half-commit left the baseline behind the disk');
});

test('a refusal forks: the conversation is kept, under a new id, and the tab is told', () => {
  // Another window is in this conversation. Overwriting it would lose their turns and refusing to
  // save would lose ours, so neither happens: this tab becomes a copy, keeps every message it holds,
  // and writes somewhere nobody else is.
  const next = nextAfterSave({ kind: 'refused', diskRev: 9, said: ['somebody else'] }, 4, ['ours']);

  assert.equal(next.kind, 'fork');
  assert.equal(next.kind === 'fork' ? next.note : '', CONTINUED_ELSEWHERE);
});

test('a record this build cannot read forks as well, rather than being written over', () => {
  // The store refuses to replace a torn record or one a newer build wrote, which is what keeps a
  // downgrade from destroying a conversation. The tab still has somewhere to put its own words.
  const next = nextAfterSave({ kind: 'incompatible', reason: 'the conversation on disk was written by a newer version' }, 4);

  assert.equal(next.kind, 'fork', 'a conversation this build cannot read left the tab with nowhere to save');
});

test('a disk that would not answer changes nothing about the conversation, and says so once', () => {
  // Not a fork: forking would mint an id per failed write and scatter one conversation across the
  // store. The transcript is in memory and the next turn tries again.
  const next = nextAfterSave({ kind: 'failed', reason: 'the conversation could not be saved to disk (EACCES)' }, 4, []);

  assert.equal(next.kind, 'said');
  assert.ok(
    next.kind === 'said' && next.note.includes('EACCES'),
    'the sentence a person reads does not say what actually failed',
  );
});

test('the sentence a forked tab shows names what happened and what to do about it', () => {
  assert.match(CONTINUED_ELSEWHERE, /another window/u);
  assert.match(CONTINUED_ELSEWHERE, /copy/u, 'a person is not told that this tab is now a second conversation');
});

test('the sentence a half-committed tab would show is about the LIST, never about the conversation', () => {
  // It is the picker's index that did not land, not the transcript. A sentence saying the
  // conversation was not saved would be false, and it is the false half that a person would act on.
  assert.match(INDEX_BEHIND, /list/u);
  assert.doesNotMatch(INDEX_BEHIND, /lost|not saved/u);
});

// ---------------------------------------------------------------------------------------------
// Meeting its OWN record: the case that would otherwise fork every conversation a person reopens.
// ---------------------------------------------------------------------------------------------

test('a window that has never saved ADOPTS the record it can see is its own earlier self', () => {
  // The case a fork would get wrong every time. A conversation restored into a tab has an id it has
  // held since it was minted, and the record on disk under that id is usually the session it was
  // restored FROM — not a rival. Forking there would mint a second copy on every reload, for ever.
  //
  // This test asked for adoption on the REVISION alone when it was first written, which A3's plan
  // round refused: two windows can both be at baseline 0, and the second would then overwrite the
  // first. What it asserts now is the corrected rule — the disk's words must be contained in ours.
  const next = nextAfterSave({ kind: 'refused', diskRev: 5, said: ['why'] }, 0, ['why', 'because']);

  assert.deepEqual(next, { kind: 'adopt', rev: 5 });
});

test('adoption is offered ONCE, and a window that has saved forks instead', () => {
  // After this window's own write has landed, a disk that has moved on is somebody else — there is
  // no innocent explanation left. The baseline is what tells the two apart, and it is the only thing
  // that does.
  assert.equal(nextAfterSave({ kind: 'refused', diskRev: 5, said: ['why'] }, 1, ['why', 'because']).kind, 'fork');
  assert.equal(nextAfterSave({ kind: 'refused', diskRev: 9, said: [] }, 8, ['why']).kind, 'fork');
});

test('a record this build cannot read is never adopted, whatever the baseline', () => {
  // Adoption means "that is my own record, at a number I did not know". A torn one, or one a newer
  // build wrote, is not readable and so is not claimable: taking its revision would be volunteering
  // to overwrite it on the next turn, which is the downgrade the store refuses.
  assert.equal(nextAfterSave({ kind: 'incompatible', reason: 'newer version' }, 0, ['why']).kind, 'fork');
});

// ---------------------------------------------------------------------------------------------
// What A3's plan round found. Adoption cannot be decided by a number alone.
// ---------------------------------------------------------------------------------------------

test('a window adopts only a record its own transcript CONTAINS — never one that has moved on', () => {
  // gemini and local, independently, and it is the hole that would have defeated the whole swap.
  // TWO windows can both be at baseline 0 for one conversation: both restored it, neither has
  // written. A saves and the disk goes to revision 1; B, still at 0, is refused — and adoption by
  // number alone would have B take revision 1 and write over A's turns, which is exactly what the
  // revision exists to prevent.
  //
  // What tells the innocent case from that one is not the number, it is the WORDS: our own previous
  // session is a prefix of what we hold, because we restored it and added to it. A disk that has
  // turns we have never seen is somebody else, whatever its revision says.
  const ours = ['why', 'because', 'and'];

  assert.deepEqual(
    nextAfterSave({ kind: 'refused', diskRev: 5, said: ['why', 'because'] }, 0, ours),
    { kind: 'adopt', rev: 5 },
    'a window would not reclaim the record it restored from, and forks on every reopen',
  );
  assert.equal(
    nextAfterSave({ kind: 'refused', diskRev: 5, said: ['why', 'a different answer'] }, 0, ours).kind,
    'fork',
    'a window overwrote turns it had never seen, which is the lost update the swap exists to stop',
  );
  assert.equal(
    nextAfterSave({ kind: 'refused', diskRev: 5, said: ['why', 'because', 'and', 'more'] }, 0, ours).kind,
    'fork',
    'a disk AHEAD of this window was adopted, so the turns it holds beyond ours would be replaced',
  );
});

test('an identical transcript is adopted: it is our own record, unchanged', () => {
  const ours = ['why', 'because'];

  assert.deepEqual(
    nextAfterSave({ kind: 'refused', diskRev: 3, said: [...ours] }, 0, ours),
    { kind: 'adopt', rev: 3 },
  );
});

test('a store busy with THIS conversation changes nothing, and waits for the next turn', () => {
  // Forking was the first answer and A3's code round refused it: the store was mid-mutation of this
  // very conversation and never probed it, so forking would mint a copy because a lock was held for
  // a few milliseconds. Nothing changes and the next turn tries again. (local.)
  //
  // It arrives as its own outcome rather than as a refusal carrying no transcript, which is the
  // round-after correction: a genuine conflict over an ABSENT record reports no transcript either,
  // so reading that as "busy" would wait for ever and never fork. (gemini.)
  const next = nextAfterSave({ kind: 'busy' }, 0, ['why']);

  assert.equal(next.kind, 'said', 'a conversation forked because its own store was busy for an instant');
  assert.equal(next.kind === 'said' ? next.note : '', BUSY_ELSEWHERE);
});

test('a conflict the store COULD not read is a fork, not a wait — the wedge that shortcut caused', () => {
  // The case that separates the two names. A record deleted under this window, met at a baseline
  // above 0, is a real conflict reported without a transcript; treating absence of words as "the
  // store is busy" left it waiting for ever, never advancing and never forking, with every later
  // write of that conversation queued behind it.
  assert.equal(nextAfterSave({ kind: 'refused', diskRev: 0 }, 3, ['why']).kind, 'fork');
});

test('a refusal that reports NOTHING about the disk is never adopted, even at baseline 0', () => {
  // The discriminating case, and the one the tests above never reached: every fork they assert is
  // forced by a baseline above 0, so nothing here would have gone red had adoption crept back in for
  // a refusal with no transcript. This window has never saved, was refused, and was handed no words —
  // the record under the claim could not be read, so nothing is known about whose it is. The header
  // says such a refusal is never adopted; adopting it would take another window's revision on faith
  // and overwrite that window's turns on the next save. (CodeRabbit, PR #223, on the gap.)
  assert.equal(nextAfterSave({ kind: 'refused', diskRev: 5 }, 0, ['why']).kind, 'fork',
    'a record nobody could read was adopted at baseline 0 — its revision taken on faith');
});

test('an outcome this module has no arm for is a defect it names, not a disk failure it invents', () => {
  // The switch is exhaustive: `failed` has its arm by name, and what remains is `never`, so a variant
  // added to SaveOutcome without an arm here is a compile error. A value that reaches the default at
  // runtime — which the types forbid — throws with the value in the message, rather than being read
  // as a disk failure whose reason is the word undefined. (CodeRabbit, PR #223.)
  assert.throws(() => nextAfterSave({ kind: 'lost' } as unknown as SaveOutcome, 0), /lost/u);
});

test('what a failed save says names the fault AND that the conversation is still there', () => {
  // A reason on its own reads as though the words had been lost, when they are on screen, in the
  // store of record, and about to be tried again. (codex and gemini.)
  const next = nextAfterSave({ kind: 'failed', reason: 'the disk said no (EACCES)' }, 4, []);

  assert.equal(next.kind, 'said');
  assert.ok(next.kind === 'said' && next.note.includes('EACCES'), 'the fault is not named');
  assert.ok(next.kind === 'said' && next.note.includes(STILL_SAFE), 'a person is left thinking their words are gone');
});

test('an adopted record brings its own beginning with it', () => {
  // Otherwise a conversation started in January and answered in March is rewritten as having
  // started in March, because this side's best guess is when the memento was last written.
  const next = nextAfterSave(
    { kind: 'refused', diskRev: 5, said: ['why'], began: 1_700_000_000_000 },
    0,
    ['why', 'because'],
  );

  assert.deepEqual(next, { kind: 'adopt', rev: 5, began: 1_700_000_000_000 });
});

/**
 * An outcome that arrives for a conversation this tab no longer holds applies to NOTHING.
 *
 * <p><b>The asymmetry this closes.</b> `chatTurn` fences its answers twice — a generation check
 * before a turn begins and a slate check before it writes. The WRITE path had neither. `keepQueued`
 * chains `keepOnDisk` onto `thread.writes`, which reads `thread.saveId` when it RUNS rather than
 * when it was queued, and `settle` then assigns `thread.rev = next.rev` to whatever thread it is
 * holding. So a save issued before a reset and resolving after one stamped the conversation that
 * REPLACED it with the old save's revision — and on the other branches posted its note to that
 * conversation's page, or forked it.</p>
 *
 * <p>A revision is not cosmetic: it is the baseline the next save is checked against, so a thread
 * carrying a number the disk never gave it fails its next write as a conflict and forks a
 * conversation nobody split.</p>
 */

test('an outcome for a conversation the tab no longer holds applies to nothing', () => {
  const outcome: WriteNext = { kind: 'kept', rev: 7 };

  assert.equal(ifStillOurs(outcome, 'the-conversation-that-asked', 'the-one-that-replaced-it'), undefined,
    'a save issued before a reset was applied to the conversation that replaced it');
});

test('an outcome for the conversation that asked for it still applies', () => {
  // The companion the fence needs: one that refused everything would pass the case above while no
  // conversation ever advanced its revision again.
  const outcome: WriteNext = { kind: 'kept', rev: 7 };

  assert.deepEqual(ifStillOurs(outcome, 'one-and-the-same', 'one-and-the-same'), outcome,
    'a save resolving into the conversation that asked for it was dropped');
});
