import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CONTINUED_ELSEWHERE, INDEX_BEHIND, nextAfterSave } from '../chatStoreWrite';

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
  const next = nextAfterSave({ kind: 'refused', diskRev: 9 }, 4);

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
  const next = nextAfterSave({ kind: 'failed', reason: 'the conversation could not be saved to disk (EACCES)' }, 4);

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

test('a window that has never saved ADOPTS the record already under its own id', () => {
  // The case a fork would get wrong every time. A conversation restored into a tab has an id it has
  // held since it was minted, and the record on disk under that id is its own previous session — not
  // a rival. Forking there would mint a second copy of a conversation on every reload, for ever.
  // Having never written, this window has nothing to lose by taking the disk's number and saving
  // again against it.
  const next = nextAfterSave({ kind: 'refused', diskRev: 5 }, 0);

  assert.deepEqual(next, { kind: 'adopt', rev: 5 });
});

test('adoption is offered ONCE, and a window that has saved forks instead', () => {
  // After this window's own write has landed, a disk that has moved on is somebody else — there is
  // no innocent explanation left. The baseline is what tells the two apart, and it is the only thing
  // that does.
  assert.equal(nextAfterSave({ kind: 'refused', diskRev: 5 }, 1).kind, 'fork');
  assert.equal(nextAfterSave({ kind: 'refused', diskRev: 9 }, 8).kind, 'fork');
});

test('a record this build cannot read is never adopted, whatever the baseline', () => {
  // Adoption means "that is my own record, at a number I did not know". A torn one, or one a newer
  // build wrote, is not readable and so is not claimable: taking its revision would be volunteering
  // to overwrite it on the next turn, which is the downgrade the store refuses.
  assert.equal(nextAfterSave({ kind: 'incompatible', reason: 'newer version' }, 0).kind, 'fork');
});
