/**
 * A consultation as it sits in `<dataDir>/consultations/<id>.json`, and the card the sidebar draws.
 *
 * <p>Pure and `vscode`-free, like `rounds.ts` and `escalations.ts` beside it: the parser meets files
 * written by a server that may be a version ahead or behind, so every fallback here is a unit test
 * rather than a claim.</p>
 *
 * <p><b>Running ones only.</b> The sidebar is present tense — the 2026-09-05 ruling that took the
 * finished rounds out of it — so a consultation that is closed or failed is read in the log page,
 * not here. What a person wants from the sidebar is "is something being asked right now, and of
 * whom".</p>
 */

import { escapeHtml } from './escapeHtml';
import { shortDuration, shortNumber } from './usage';

/** One answered turn, as the record keeps it. */
export interface ConsultationTurn {
  readonly utc: string;
  readonly problem: string;
  readonly advice: string;
  readonly seconds: number;
  readonly tokensIn: number;
  readonly tokensOut: number;
  readonly costUsd: number | null;
}

/** The states the server writes. `closed` and `failed` are over; the other three are not. */
export const RUNNING_STATUSES: readonly string[] = ['asking', 'open', 'interrupted'];

/**
 * How a consultation ENDED, as against why it stopped — the two questions `Reason` used to answer
 * with one string.
 *
 * <p>Three of the four are somebody's verdict: a caller that verified the advice, or a person
 * closing one by hand. The fourth is the server's own, and it is deliberately not a verdict —
 * `lapsed` says the budget ran out or the thing sat idle, which is a fact about the clock rather
 * than about whether the advice was any good. Flattening it into `abandoned` would put words in
 * somebody's mouth. (issue #309, the plan round.)</p>
 */
export const OUTCOMES: Readonly<Record<string, string>> = {
  solved: 'solved',
  not_solved: 'not solved',
  abandoned: 'abandoned',
  lapsed: 'ran out',
};

/**
 * What a PERSON may record when they close one by hand, and what each word means.
 *
 * <p>Three, not four: `lapsed` is the server's own statement that the clock ran out, and offering it
 * here would be asking somebody to state a fact that is not theirs — the server refuses it from this
 * door anyway. The detail beside each is the catalogue's own sentence, for the reason the consultant
 * picker carries one: a list of bare words leaves a person choosing between them with nothing to
 * choose ON. (issue #309.)</p>
 */
export const CLOSE_CHOICES: readonly { readonly outcome: string; readonly label: string; readonly detail: string }[] = [
  { outcome: 'solved', label: 'Solved', detail: 'the advice was verified and it worked' },
  { outcome: 'not_solved', label: 'Not solved', detail: 'it was verified and it did not' },
  { outcome: 'abandoned', label: 'Abandoned', detail: 'nobody is going to act on it' },
];

/**
 * The sentence out of a one-shot mode's answer, or the raw text when it is not ours.
 *
 * <p>The server writes `{"error": "…"}` for a refusal and a `CloseAnswer` for a success, both on
 * stdout. A caller that showed the raw JSON would be showing a person braces; one that showed
 * nothing when the JSON did not parse would hide a crash. (issue #309.)</p>
 */
export function refusalIn(output: string): string {
  try {
    const parsed: unknown = JSON.parse(output);
    const said = (parsed as { error?: unknown; said?: unknown } | null);
    const sentence = typeof said?.error === 'string' ? said.error
      : typeof said?.said === 'string' ? said.said
        : '';

    return sentence.length > 0 ? sentence : output.trim();
  } catch {
    return output.trim().length > 0 ? output.trim() : 'the server said nothing at all';
  }
}

/** What no outcome at all looks like. A dash, and never a word that could be read as a verdict. */
export const NO_OUTCOME = '\u2014';

/**
 * What the log says about how a consultation ended.
 *
 * <p><b>Absent is not a verdict, and that is the whole of this function.</b> Every record written
 * before this field existed carries nothing, and so does every one an older server still writes —
 * and reading that as `not_solved`, or inferring `solved` from a `closed` status, would turn "the
 * budget ran out" into "it worked". A value this build does not recognise is treated the same way:
 * a newer server may write a fifth word, and guessing at it is the same mistake one release later.</p>
 */
export function outcomeSaid(outcome: string | undefined): string {
  return OUTCOMES[outcome ?? ''] ?? NO_OUTCOME;
}

export interface Consultation {
  readonly id: string;
  readonly callerKind: string;
  readonly repoPath: string;
  readonly branch: string;
  readonly vendor: string;
  readonly model: string;
  readonly status: string;
  readonly startedUtc: string;
  readonly updatedUtc: string;
  readonly maxTurns: number;
  readonly turns: readonly ConsultationTurn[];
  readonly alert: string;
  readonly reason: string;
}

/**
 * One record, or nothing.
 *
 * <p>An id and a status are what make a file a consultation; everything else is defaulted, because a
 * server one version behind wrote fewer fields and a card missing a number is better than a sidebar
 * that refuses to draw. The same decision `parseSession` made, for the same reason.</p>
 */
export function parseConsultation(text: string): Consultation | undefined {
  try {
    const parsed = JSON.parse(text) as Partial<Consultation>;
    if (typeof parsed.id !== 'string' || parsed.id.length === 0 || typeof parsed.status !== 'string') {
      return undefined;
    }

    return {
      id: parsed.id,
      callerKind: text_(parsed.callerKind),
      repoPath: text_(parsed.repoPath),
      branch: text_(parsed.branch),
      vendor: text_(parsed.vendor),
      model: text_(parsed.model),
      status: parsed.status,
      startedUtc: text_(parsed.startedUtc),
      updatedUtc: text_(parsed.updatedUtc),
      maxTurns: typeof parsed.maxTurns === 'number' && parsed.maxTurns > 0 ? parsed.maxTurns : 0,
      turns: Array.isArray(parsed.turns) ? parsed.turns.filter(isTurn) : [],
      alert: text_(parsed.alert),
      reason: text_(parsed.reason),
    };
  } catch {
    return undefined;
  }
}

/** Whether this one is still going, which is the only kind the sidebar shows. */
export function isLive(consultation: Consultation): boolean {
  return RUNNING_STATUSES.includes(consultation.status);
}

/**
 * The live region's body: every running consultation, newest first.
 *
 * <p>Empty is a SENTENCE rather than nothing, and it says where the finished ones are — the same
 * shape the running-rounds region uses, and for the same reason: a blank area cannot tell "nothing
 * is happening" from "this is broken".</p>
 */
export function consultationsBody(
  consultations: readonly Consultation[],
  nowMs: number = Date.now(),
): string {
  const live = consultations
    .filter(isLive)
    .sort((a, b) => (b.startedUtc ?? '').localeCompare(a.startedUtc ?? ''));
  if (live.length === 0) {
    return '<div class="empty">Nobody is consulting anybody. Finished consultations are in <b>Show review rounds</b>.</div>';
  }

  return live.map((one) => card(one, nowMs)).join('\n');
}

/** What one running consultation says on screen. */
function card(consultation: Consultation, nowMs: number): string {
  // The turn IN FLIGHT counts. A record written before `maxTurns` existed falls to the second
  // branch, which counted only FINISHED turns — so the first `asking` state of such a consultation
  // said "0 turn(s)" while a vendor was being asked. (CodeRabbit, on the pull request.)
  const active = consultation.turns.length + (consultation.status === 'asking' ? 1 : 0);
  const turn = consultation.maxTurns > 0 ? Math.min(active, consultation.maxTurns) : active;
  const budget = consultation.maxTurns > 0 ? `turn ${turn} of ${consultation.maxTurns}` : `${active} turn(s)`;
  const spent = consultation.turns.reduce(
    (total, one) => ({ tokens: total.tokens + one.tokensIn + one.tokensOut, seconds: total.seconds + one.seconds }),
    { tokens: 0, seconds: 0 },
  );

  return `<div class="round" data-consultation="${escapeHtml(consultation.id)}">
  <div class="subject">${escapeHtml(who(consultation))}</div>
  <div class="line branch">${escapeHtml(consultation.branch)}</div>
  <div class="line"><span class="badge ${badgeClass(consultation.status)}">${escapeHtml(label(consultation.status))}</span> · ${escapeHtml(budget)}</div>
  <div class="usage">${escapeHtml(age(consultation, nowMs))}${spent.tokens > 0 ? ` · ${escapeHtml(shortNumber(spent.tokens))} tokens` : ''}</div>
${alertLine(consultation)}${note(consultation.status)}</div>`;
}

/** Who is asking whom, which is the whole point of the card. */
function who(consultation: Consultation): string {
  const asking = consultation.callerKind.length > 0 ? callerLabel(consultation.callerKind) : 'An AI';
  const model = consultation.model.length > 0 ? ` · ${consultation.model}` : '';

  return `${asking} is asking ${consultation.vendor.length > 0 ? consultation.vendor : 'another vendor'}${model}`;
}

function callerLabel(kind: string): string {
  return kind === 'claude' ? 'Claude Code'
    : kind === 'codex' ? 'Codex'
      : kind === 'gemini' ? 'Gemini'
        : 'Another client';
}

/**
 * `interrupted` is not a failure and must not read as one.
 *
 * <p>The vendor accepted the turn and the process died before the answer was read; the conversation
 * is resumable and that turn was not counted. A person seeing red would go looking for a fault that
 * is not there.</p>
 *
 * <p><b>And the card says who resumes it</b>, because "resumable" on its own promises an action a
 * person cannot take: the AI picks the conversation up by passing its consultation id back, which is
 * what `status` hands it. Two reviewers read the word as a button that was missing. A button that
 * resumed somebody's consultation for them would spend a vendor turn nobody asked for.</p>
 */
function label(status: string): string {
  return status === 'asking' ? 'asking' : status === 'interrupted' ? 'resumable' : 'open';
}

/** The sentence under a card whose state a person might otherwise expect to act on. */
function note(status: string): string {
  return status === 'interrupted'
    ? '  <div class="hint">The turn was not counted. The AI picks this up with its consultation id — <code>status</code> hands it back.</div>\n'
    : '';
}

function badgeClass(status: string): string {
  return status === 'asking' ? 'running' : 'idle';
}

/** Since the last thing happened, or since it started when nothing has. */
function age(consultation: Consultation, nowMs: number): string {
  const stamp = consultation.updatedUtc.length > 0 ? consultation.updatedUtc : consultation.startedUtc;
  const since = Date.parse(stamp);
  if (!Number.isFinite(since)) {
    return 'just now';
  }

  return `${shortDuration(Math.max(0, (nowMs - since) / 1000))} ago`;
}

/**
 * The filesystem invariant's sentence, when it fired.
 *
 * <p>The one field a person MUST read: it says the working tree changed while the consultant was
 * reading it, so the advice was formed against a tree that no longer holds. It never claims the
 * consultant did it — two snapshots cannot name a culprit, and a person's own editor is the likelier
 * one.</p>
 */
function alertLine(consultation: Consultation): string {
  return consultation.alert.length === 0
    ? ''
    : `  <div class="hint stale">${escapeHtml(consultation.alert)}</div>\n`;
}

/**
 * A turn is an object whose numbers are numbers.
 *
 * <p>Loose enough to take a turn from a server that added a field, strict enough that the card's
 * token sum cannot become `NaN` — which is a number nobody can read, and the only way this file can
 * put one on screen. (gemini, code round.)</p>
 */
function isTurn(value: unknown): value is ConsultationTurn {
  if (typeof value !== 'object' || value === null) {
    return false;
  }
  const turn = value as Partial<ConsultationTurn>;

  return Number.isFinite(turn.seconds) && Number.isFinite(turn.tokensIn) && Number.isFinite(turn.tokensOut);
}

function text_(value: unknown): string {
  return typeof value === 'string' ? value : '';
}
