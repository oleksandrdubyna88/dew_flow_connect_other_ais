import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ARCHIVED, Freshened, UNSAVED, couldNotEnd, freshened, sameSlate, turnEnded } from '../chatFresh';

/**
 * *New chat* — what a reset replaces, and what it must leave alone.
 *
 * <p>The second half is the one worth testing. A reset that clears too little carries the old
 * conversation into the new one, which is the whole defect it exists to fix; a reset that clears too
 * much throws away the model somebody chose, the prompt they pressed, or the tab's own identity. Both
 * are silent. So the fields are asserted by NAME, in both directions.</p>
 */

const FRESH = 'b6b2f0e2-0000-4000-8000-000000000001';

/** Every field of a thread a reset must NOT touch, and why it would be wrong to. */
const KEPT = [
  // The tab is still the conversation OF that tab: the title is its file's name or its Claude
  // session's, never a summary of what was said, so there is nothing in it to go stale.
  'title',
  // A reset is a new subject, not a new setup.
  'modelId', 'providerId', 'promptId', 'role', 'chosenId', 'presses', 'models', 'providers',
  // The conversation still belongs to the same tab in the same project.
  'source', 'workspace', 'fromSession', 'sessionFile',
  // Never reset anywhere: a stop names the turn it means, and a stop arriving a tick late must not
  // be able to name a turn of the NEW conversation.
  'turn',
  // The reset bumps this itself, before it stops anything — it is not part of the slate it installs.
  'generation',
  // Queues rather than contents.
  'turns', 'writes',
  // Replaced by the host, which owns them: a dead session and a released directory are not values.
  'session', 'home',
  // The host's, and decided by whichever model answers the first new question.
  'forgetful', 'ourDraft',
] as const;

test('the slate a reset installs is exactly these fields, and nothing else', () => {
  const slate = freshened(FRESH, 1_700_000_000_000);

  assert.deepEqual(slate, {
    saveId: FRESH,
    rev: 0,
    messages: [],
    // EMPTY, and this is the field the whole guarantee rests on: `carry` is how a conversation
    // reaches a process that never heard it, and a reset that cleared the transcript and left this
    // would hand the next question the very history it was pressed to be rid of.
    carry: [],
    carryFrom: 0,
    // The Team-server cap counts the turns THIS model answered. A new conversation is not three
    // turns old.
    asked: 0,
    spend: [],
    attached: '',
    attachedPath: '',
    passage: '',
    // The next question opens a process, exactly as after a reload — which is what makes `codex`
    // forget, its thread id being instance state of a session object that is now gone.
    reopen: true,
    running: false,
    // And nothing waiting: the questions went back to the composer before the slate moved.
    waiting: [],
    // A new slate has failed at nothing, so it offers no retry of anything.
    failedWith: '',
    createdAt: 1_700_000_000_000,
    usedAt: 1_700_000_000_000,
  } satisfies Freshened);
});

test('nothing a reset must keep is in the slate', () => {
  const slate: Record<string, unknown> = { ...freshened(FRESH, 1) };

  for (const field of KEPT) {
    assert.equal(field in slate, false, `a reset would overwrite “${field}”, which it must leave alone`);
  }
});

test('the clock is an argument, so a reset is not dated by whenever a test happens to run', () => {
  assert.deepEqual(
    [freshened(FRESH, 7).createdAt, freshened(FRESH, 7).usedAt],
    [7, 7],
    'a new conversation began when the reset happened, and was last used then too',
  );
  // And it is a NEW beginning rather than the old conversation's: the archived record keeps that one.
  assert.notEqual(freshened(FRESH, 7).createdAt, freshened(FRESH, 9).createdAt);
});

test('the marks that say what the disk holds are DELETED, and all three of them are named', () => {
  // The push that writes a conversation down skips itself when these three still match the thread.
  // A reset that left them would meet a new empty transcript, decide nothing had changed, and never
  // write the new record at all — the tab saying one thing and the disk another until the next
  // question. They are deleted rather than emptied because the guard reads "never written" from
  // their ABSENCE and "written as this" from their value, and an empty array is a value.
  assert.deepEqual([...UNSAVED], ['savedMessages', 'savedModelId', 'savedCarryFrom']);
  // None of them is in the slate: setting them there would be the bug this constant exists to avoid.
  const slate: Record<string, unknown> = { ...freshened(FRESH, 1) };
  for (const mark of UNSAVED) {
    assert.equal(mark in slate, false, `“${mark}” is SET by the slate, where it must be deleted`);
  }
});

test('a turn belongs to the conversation it began in, whether that is counted or named', () => {
  // Two facts, one rule. The generation says whether a RESET HAS BEGUN, and is what stops a queued
  // question ever starting; the save id says whether the slate has actually been WIPED, and is what
  // stops a late answer being written into a conversation that never asked it. Between those two
  // moments the turn in flight is still writing into the old conversation, which is where its
  // stopped line belongs — so the two questions need two facts and cannot share one.
  assert.equal(sameSlate(0, 0), true);
  assert.equal(sameSlate(0, 1), false, 'a reset that has begun is not the conversation the turn was queued in');
  assert.equal(sameSlate('a1', 'a1'), true);
  assert.equal(sameSlate('a1', 'a2'), false, 'a wiped slate is not the conversation the turn was asked in');
});

test('both sentences say what happened, and the failure says what stopped it', () => {
  // Not a dialog: the operator refused one by name, and the archived conversation is two clicks away
  // in the picker. This line is what a dialog would have said, without a keystroke to dismiss.
  assert.match(ARCHIVED, /archived/u);
  assert.match(ARCHIVED, /switch conversations/u, 'nothing says where the old conversation went');

  const failed = couldNotEnd('the process would not end');
  assert.match(failed, /the process would not end/u, 'a refusal that does not name its reason cannot be acted on');
  assert.match(failed, /nothing was archived/u, 'a half-performed reset that reports success is worse than no reset');
  assert.match(failed, /unchanged/u);
  // And it says the conversation is still usable, because it is: the next question opens it a
  // process the way a reload does.
  assert.match(failed, /next question/u, 'nothing tells the person the conversation still works');
});

/**
 * An abandoned turn hands NOTHING back to the conversation that replaced it.
 *
 * <p>`sameSlate` above says whether a turn still owns the thread, and `chatTurn` asked it — five
 * lines too late. `thread.running = false` ran BEFORE the question and `show(entry, false, '')` ran
 * inside the branch that had just answered no, so a turn belonging to a conversation the person had
 * already discarded cleared the replacement's running flag and painted it idle. A conversation
 * mid-answer could be shown as finished by a turn that was not its own.</p>
 *
 * <p>The comment on that branch was accurate about what it covered — *“Nothing is recorded anywhere,
 * ledger included”*, and the ledger write is below the return — which is exactly why it read as
 * complete. The guard was built to stop a stale answer being RECORDED and it does that; nobody asked
 * whether the two statements bracketing it also touched the new conversation.</p>
 *
 * <p>So the question and the handover are ONE unit now. A caller cannot ask and then forget to act
 * on the answer, because acting is what asking does.</p>
 */

test('a turn that no longer owns the thread hands nothing back to it', () => {
  const replacement = { running: true, saveId: 'the-conversation-that-replaced-it' };

  assert.equal(turnEnded(replacement, 'the-one-that-was-archived'), false,
    'an abandoned turn was told it still owned the thread');
  assert.equal(replacement.running, true,
    'an abandoned turn cleared the RUNNING flag of the conversation that replaced it, so a chat'
    + ' mid-answer is shown as finished');
});

test('a turn that still owns the thread hands it back, or nothing would ever finish', () => {
  // The companion the case above needs. A version that refused every turn would pass it while
  // leaving every conversation thinking for ever.
  const mine = { running: true, saveId: 'one-and-the-same' };

  assert.equal(turnEnded(mine, 'one-and-the-same'), true, 'a turn that still owns its thread was refused');
  assert.equal(mine.running, false, 'the turn finished and the conversation is still marked as running');
});
