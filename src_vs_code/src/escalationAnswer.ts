/**
 * Answering a `call_human` verdict, which is a CHOICE rather than a sentence.
 *
 * <p>It was asked with a free-text input box — the control for a question an AI wrote in words, and
 * this is not that. Worse, a person typing into it got no error and no effect: for a `call_human`
 * notice the answer file was written and nothing on either side ever read it, because the round
 * that wrote the notice had already returned. So the card disappeared and nothing had changed,
 * which is a worse dead end than never being asked, because it looks like it worked.</p>
 *
 * <p>Pure, so the choices and the file they produce are a test rather than something confirmed by
 * clicking.</p>
 */

/** One thing a person can decide, and what it will actually cause. */
export interface DecisionChoice {
  /** What the server reads. Empty means "they typed something instead of choosing". */
  readonly decision: '' | 'continue' | 'fix' | 'discuss';
  readonly label: string;
  /** What happens next. A button whose consequence is unstated is a button nobody presses twice. */
  readonly detail: string;
}

/** The branch segment every FEATURE session lives under — the server's `SessionKey.FeatureBranch`, a string no git ref can spell. */
export const FEATURE_BRANCH = ':feature';

/**
 * The three things that can happen after the rounds run out — and notably none of them is "ship it
 * with the findings open". A human override meaning "ignore all this" would be an off switch on the
 * gate, so it is deliberately not offered; all three keep the findings alive.
 *
 * <p>For a FEATURE review the first choice is also how a person asks for the review's second round
 * (D23 of the feature-review plan): a feature review is one round unless a reviewer failed, a finding
 * was `blocking`, or the person asks — and this button, on a question filed under the feature session,
 * is the asking. The same road `humanDecision` takes, written only by this window or the phone; no
 * argument the AI passes can claim it. So the detail says what it grants THERE: the second and last
 * round, not a fresh set.</p>
 *
 * @param branch the session's branch as the question names it — `:feature` for a feature review.
 */
export function decisionChoices(branch = ''): readonly DecisionChoice[] {
  const feature = branch === FEATURE_BRANCH;
  return [
    {
      decision: 'continue',
      label: 'Keep going — more rounds',
      detail: feature
        ? 'A feature review gets its second and last round (at most two, ever) and runs again with nothing changed — or, after a call_human, a fresh set of rounds. For when you think the reviewers are wrong, or you want another pass at the same thing.'
        : 'The stage gets a fresh set of rounds and the review runs again with nothing changed. For when you think the reviewers are wrong, or you want another pass at the same thing.',
    },
    {
      decision: 'fix',
      label: 'Stop and act on the findings',
      detail:
        'The AI stops reviewing, addresses the findings, and then the stage gets a fresh set of rounds so the review runs again over the fixes.',
    },
    {
      decision: 'discuss',
      label: 'Stop and talk to me',
      detail:
        'Nothing advances. The AI is told to stop and discuss the open findings with you before doing anything else.',
    },
  ];
}

/**
 * The answer file the server reads.
 *
 * <p>The decision AND their words: a button press is what code can act on, and the sentence they
 * may have added is what the AI should read. Losing either one loses half the answer.</p>
 */
export function answerJson(id: string, answer: string, nowUtc: string, decision = ''): string {
  return JSON.stringify({ id, answer, decision, answeredUtc: nowUtc }, null, 2);
}

/** The free-text answer: a sentence of the person's own, sent back to the AI that asked. */
export const TYPE_AN_ANSWER: DecisionChoice = {
  decision: '',
  label: 'Type an answer…',
  detail: 'Your own words go back to the AI that asked — the answer to a question it put in words.',
};

/**
 * The same box on the GATE's question — asked while the gate is held, or in a feature review's second-round window: the
 * words reach the AI and decide nothing for the review, and saying so is the difference between an answer and the dead
 * end the module comment describes — the card leaves and nothing changed (the own review of 2026-10-03). Worded for both
 * cases, because the request window is not a hold (CodeRabbit on PR #656).
 */
export const TYPE_AN_ANSWER_FOR_THE_GATE: DecisionChoice = {
  decision: '',
  label: 'Type an answer…',
  detail: 'Your own words go back to the AI that asked. They decide nothing for the review — only one of the decisions below does.',
};

/**
 * What the Answer… button offers for one card (todo/PLAN_question_consultant.md, S4; research/PLAN_ask_human_is_for_the_gate.md, G4).
 *
 * <p>The card says which producer wrote it, so nothing here guesses. An AI's QUESTION — asked while the gate was held, or
 * in a feature review's second-round window, the only kinds that become a card since the server stopped carding the AI's
 * own questions — is answered in words, so the box for them comes FIRST, and the decisions after it, because only a
 * decision decides: it releases a hold, or asks for the second round. A round's call_human
 * NOTICE is a CHOICE, as the module comment says: the three decisions only, since no AI waits on a notice for words (the
 * cadence consultation b6e9df3c, 2026-10-03, found a typed answer there went nowhere).</p>
 *
 * <p>A card with no kind — an older server — or with a kind this build does not know — a newer one — is judged as cards
 * always were: no findings gating meant a question. Taking the box away from a kind nobody here knows would bring back
 * the very symptom this answers.</p>
 */
export function answerChoices(escalation: {
  readonly branch: string;
  readonly openFindings: readonly unknown[];
  readonly kind?: string;
}): readonly DecisionChoice[] {
  const decisions = decisionChoices(escalation.branch);
  switch (escalation.kind) {
    case 'question':
      return [TYPE_AN_ANSWER_FOR_THE_GATE, ...decisions];
    case 'notice':
      return decisions;
    default:
      return escalation.openFindings.length === 0 ? [TYPE_AN_ANSWER, ...decisions] : decisions;
  }
}
