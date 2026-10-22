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
  readonly decision: '' | 'continue' | 'fix' | 'discuss' | 'proceed';
  readonly label: string;
  /** What happens next. A button whose consequence is unstated is a button nobody presses twice. */
  readonly detail: string;
}

/** The branch segment every FEATURE session lives under — the server's `SessionKey.FeatureBranch`, a string no git ref can spell. */
export const FEATURE_BRANCH = ':feature';

/**
 * The three things that keep the findings alive after the rounds run out: another set of rounds, a pass
 * after fixing, a conversation. They are on every card that offers decisions. The fourth — "proceed
 * despite the findings", which ships WITH them open — is deliberately not among them; see
 * {@link proceedChoice} for where it lives and why.
 *
 * <p>For a FEATURE review the first choice is also how a person asks for the review's second round
 * (D23 of the feature-review plan): a feature review is one round unless a reviewer failed, a finding
 * was `blocking`, or the person asks — and this button, on a question filed under the feature session,
 * is the asking. The same road a held gate's decisions take, written only by this window (anything
 * that writes `<id>.answer.json` is a writer); no argument the AI passes can claim it. So the detail
 * says what it grants THERE: the second and last round, not a fresh set.</p>
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
 * The fourth decision — "ship it with the findings open" — and the only one that does (D1 of
 * todo/PLAN_only_a_person_releases_the_gate.md).
 *
 * <p>This reverses an earlier rationale ("a human override meaning 'ignore all this' would be an off switch on the
 * gate, so it is deliberately not offered") knowingly: the product already promised the override and shipped it
 * through the calling AI's own tool argument, which made the GATED party the one that could release the gate. The
 * objection was to WHO holds the switch. A button only a person can press, on the card that lists the findings
 * being overridden, with its consequence written under it, is the gate asking the person — which is what a
 * `call_human` means. So it is offered on the gate's NOTICE only (an AI-worded question card never carries it), and only when the
 * card itself lists it: a card an older server wrote has no list, and a button that server would read as prose is
 * the dead end this module has paid for twice.</p>
 *
 * <p>Nothing here makes the answer verified: it is written to a file in the data directory, and anything running
 * as the same user can write that file too. What is closed is the protocol road — no argument of the AI's releases
 * the gate.</p>
 *
 * @param branch the session's branch as the card names it — `:feature` for a feature review, which no git ref can spell.
 */
export function proceedChoice(branch = ''): DecisionChoice {
  const where = branch === FEATURE_BRANCH || branch === '' ? 'this feature review' : `branch ${branch}`;
  return {
    decision: 'proceed',
    label: 'Proceed despite the findings',
    detail:
      `The stage advances on ${where} with the findings open — nothing is fixed first. ` +
      'Every finding the AI did not decide is recorded as proceeded over by you. This covers this one stage change only: ' +
      'the same finding, raised again in a later round, counts in full. Use it when the reviewers are plainly wrong, or no one is left to ask.',
  };
}

/** The sentence a question card adds under Continue and Fix: the press releases the hold, whatever the question asked (D5). */
const FOR_THE_HELD_GATE =
  " It is a decision for the held gate (or a feature review's second round), not a reply to the question above.";

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
 * decision decides: it releases a hold, or asks for the second round — so Continue and Fix there say, in so many words, that
 * they are a decision for the held gate and not a reply to the question (D5). A round's call_human
 * NOTICE is a CHOICE, as the module comment says: the decisions only, since no AI waits on a notice for words (the
 * cadence consultation b6e9df3c, 2026-10-03, found a typed answer there went nowhere).</p>
 *
 * <p><b>Proceed is on the NOTICE only, and only when the card lists it</b> (G4, G5): the card that shows the findings being
 * overridden is the card that can override them; an AI-worded question card never carries it, and the server reads
 * a Proceed from anywhere else as prose. <b>The notice's list decides every button</b>, not only Proceed: when it is present
 * the card offers exactly the decisions it names. A list that is absent (an older server), is not a list, or names nothing
 * this build knows offers the three it always did.</p>
 *
 * <p>A card with no kind — an older server — or with a kind this build does not know — a newer one — is judged as cards
 * always were: no findings gating meant a question. Taking the box away from a kind nobody here knows would bring back
 * the very symptom this answers. Neither of those kinds ever offers Proceed.</p>
 */
export function answerChoices(escalation: {
  readonly branch: string;
  readonly openFindings: readonly unknown[];
  readonly kind?: string;
  /** Which decisions the server says this card offers; absent in a card an older server wrote. */
  readonly decisions?: unknown;
}): readonly DecisionChoice[] {
  const decisions = decisionChoices(escalation.branch);
  switch (escalation.kind) {
    case 'question':
      return [TYPE_AN_ANSWER_FOR_THE_GATE, ...decisions.map(forTheHeldGate)];
    case 'notice':
      return offeredByTheNotice(escalation);
    default:
      return escalation.openFindings.length === 0 ? [TYPE_AN_ANSWER, ...decisions] : decisions;
  }
}

/** Every decision a card can offer, in the one order a card shows them — the single place that order is written down. */
const CANONICAL_DECISIONS: readonly Exclude<DecisionChoice['decision'], ''>[] = ['continue', 'fix', 'discuss', 'proceed'];

/**
 * The notice's buttons: exactly the decisions the card lists, in the canonical order ({@link CANONICAL_DECISIONS}).
 *
 * <p>The list is the single source of truth when it is PRESENT — the server wrote it, so it is the server that says which
 * decisions it reads. A list that is absent (an older server), is not an array, or names nothing this build knows is
 * judged as absent and offers the three it always did: a card with no button is the dead end this module exists to avoid.</p>
 */
function offeredByTheNotice(escalation: { readonly branch: string; readonly decisions?: unknown }): readonly DecisionChoice[] {
  const always = decisionChoices(escalation.branch);
  const known = new Map([...always, proceedChoice(escalation.branch)].map((choice) => [choice.decision, choice] as const));
  const named = CANONICAL_DECISIONS.filter((decision) => offers(escalation.decisions, decision)).flatMap((decision) => known.get(decision) ?? []);

  return named.length === 0 ? always : named;
}

/** Whether the card's own list names a decision — a list that is not an array of words names nothing. */
function offers(listed: unknown, decision: string): boolean {
  return Array.isArray(listed) && listed.includes(decision);
}

/** Continue and Fix on a question card release the hold whatever the question asked; Discuss advances nothing and needs no warning. */
function forTheHeldGate(choice: DecisionChoice): DecisionChoice {
  return choice.decision === 'continue' || choice.decision === 'fix' ? { ...choice, detail: choice.detail + FOR_THE_HELD_GATE } : choice;
}
