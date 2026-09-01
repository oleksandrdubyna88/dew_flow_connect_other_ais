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

/**
 * The three things that can happen after the rounds run out — and notably none of them is "ship it
 * with the findings open". A human override meaning "ignore all this" would be an off switch on the
 * gate, so it is deliberately not offered; all three keep the findings alive.
 */
export function decisionChoices(): readonly DecisionChoice[] {
  return [
    {
      decision: 'continue',
      label: 'Keep going — more rounds',
      detail:
        'The stage gets a fresh set of rounds and the review runs again with nothing changed. For when you think the reviewers are wrong, or you want another pass at the same thing.',
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
