import { askedAsText, lastAsked } from './claudeQuestion';
import { Waiting, WaitingSession, unrecognisedSaid, waitingIn } from './claudeSessions';
import type { RemoteAnswer } from './remoteRead';

/** What the doors hand back: the question as text, or the sentence that says why there is none. Exactly one of the two is empty. */
type Said = { text: string; refusal: string };

const refused = (refusal: string): Said => ({ text: '', refusal });

const SEVERAL = 'More than one Claude Code session here is waiting for an answer.'
  + ' Close the ones you do not mean, and press it again.';
const ELSEWHERE = 'A Claude Code session here is waiting for an answer, but it is not the one this tab'
  + ' is showing. Open that conversation and press it there.';
// A DIFFERENT SENTENCE from "nothing was asked". Handing a second model a question that is
// already settled is the worst outcome this command has.
const ANSWERED = 'The last question in this session has already been answered — there is nothing waiting.';
const NOTHING = 'Claude Code has asked nothing in this folder yet.';

const waitingOf = (found: readonly Waiting[]): readonly WaitingSession[] => found.flatMap((one) => (one.kind === 'one' ? [one.session] : []));

/** Two waiting sessions — in one folder or in two — are a refusal, never a pick. */
const several = (found: readonly Waiting[]): Said | undefined =>
  (waitingOf(found).length > 1 || found.some((one) => one.kind === 'several') ? refused(SEVERAL) : undefined);

const one = (found: readonly Waiting[]): Said | undefined => {
  const [only, ...more] = waitingOf(found);

  return only !== undefined && more.length === 0 ? { text: askedAsText(only.asked), refusal: '' } : undefined;
};

const elsewhere = (found: readonly Waiting[]): Said | undefined =>
  (found.some((one) => one.kind === 'elsewhere') ? refused(ELSEWHERE) : undefined);

const answered = (found: readonly Waiting[]): Said | undefined =>
  (found.some((one) => one.kind === 'answered') ? refused(ANSWERED) : undefined);

/** The first folder that failed says why; with none, nothing was asked. */
function failedOrNothing(found: readonly Waiting[]): Said {
  const failed = found.find((one) => one.kind === 'failed');

  return refused(failed?.kind === 'failed' ? failed.refusal : NOTHING);
}

/** The cases in the order they are judged: the first that answers wins. */
const CASES: readonly ((found: readonly Waiting[]) => Said | undefined)[] = [several, one, elsewhere, answered];

/**
 * What the "take the question" doors say about what was found (E2.S4, contract C5).
 *
 * <p>Pure, and the one place these sentences live: the local road (a tab, over the window's own folders) and the remote road
 * (a terminal, over a session read by ssh) both call it, so the same situation is said in the same words on both.
 * Moved out of `chatCommand.ts`, where it was the second half of `questionWaitingHere`; the words are the same, the cases are
 * small functions instead of one long one.</p>
 *
 * @param found what was found for each place that was looked in — one entry for each workspace folder on the local road, one for
 *   the one session on the remote road
 * @returns the question as text, or the sentence saying why there is none; exactly one of the two is empty
 */
export function waitingSaid(found: readonly Waiting[]): Said {
  for (const judge of CASES) {
    const said = judge(found);
    if (said !== undefined) {
      return said;
    }
  }

  return failedOrNothing(found);
}

/** The one session a terminal reads is named by what this says; the lines came over ssh, so there is no file of ours to point at. */
const THE_SESSION = 'the Claude session of this terminal';

/** What the lines of the one remote session hold, in the shape `waitingSaid` takes: a waiting question, one already answered, one this build cannot read, or nothing. */
function waitingInLines(lines: readonly string[]): Waiting {
  const asked = lastAsked(lines);
  if (asked.kind === 'asked') {
    return waitingIn([{ file: '', asked: asked.set }]);
  }

  return asked.kind === 'unreadable' ? { kind: 'failed', refusal: unrecognisedSaid(THE_SESSION) } : { kind: 'none' };
}

/**
 * What a terminal door hands the person from the road's answer: the waiting question as text, or the sentence that says why not.
 *
 * <p>The identity of the session is already exact (it was read by its id), so nothing is looked for by name. Whether the tail was
 * whole is not asked here: the road has already turned an unfinished read with no question into its own sentence, and a refusal
 * of the road arrives as it is. The remaining words are `waitingSaid`'s, the same ones the local road says.</p>
 */
export function questionSaid(answer: RemoteAnswer): Said {
  return answer.kind === 'refused' ? refused(answer.refusal) : waitingSaid([waitingInLines(answer.lines)]);
}
