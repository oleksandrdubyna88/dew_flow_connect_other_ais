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
 * What the Answer… button offers for one card (todo/PLAN_question_consultant.md, S4).
 *
 * <p>An AI's QUESTION — no findings gating, which is every question that went through the question consultant —
 * is answered in words, so the box for them comes FIRST, and the decisions after it. It used to offer only the three
 * decisions, so a person could not type an answer to a question at all. A gate verdict with findings still gating
 * is a CHOICE, as the module comment says, and keeps exactly the three.</p>
 */
export function answerChoices(escalation: { readonly branch: string; readonly openFindings: readonly unknown[] }): readonly DecisionChoice[] {
  return escalation.openFindings.length === 0
    ? [TYPE_AN_ANSWER, ...decisionChoices(escalation.branch)]
    : decisionChoices(escalation.branch);
}
