import { asText } from './asText';

/**
 * What one chat turn cost, written down.
 *
 * <p>A chat turn carries the whole conversation, so question five is billed for one through four.
 * That number is what a person would use to decide between asking again and starting fresh, and
 * until now it was recorded nowhere at all — the extension read `usage.jsonl` for review rounds and
 * wrote nothing of its own.</p>
 *
 * <h2>Why this is a new file rather than a new field on an old one</h2>
 *
 * <p><b>`chatLedger.ts` is not a usage ledger</b>, despite the name and despite the plan's scope line
 * saying so. Its whole record is `{ pid, image, startedMs }` and its job is crash safety: a
 * force-killed VS Code must leave no orphaned, authenticated vendor process behind. It holds no
 * model, no tokens and no money, and putting them there would conflate two unrelated concerns.</p>
 *
 * <p><b>And `usage.jsonl` belongs to the server.</b> `coai-mcp` appends a line per reviewer while
 * running; the extension only reads it. Both reviewers on this plan's round raised the same
 * objection to writing chat rows into it, independently: a file with two writers that ship
 * separately is a file whose format is a contract nobody wrote down. So chat turns go to a file of
 * their own and the log page MERGES the two in memory, which costs one read.</p>
 *
 * <p>The reviewers gave a second reason — that concurrent appenders tear each other's lines — and
 * that half was MEASURED and did not hold: see `chatUsageFile.ts`, which is where the file this
 * module describes is actually written.</p>
 */

/**
 * The RUNTIMES that report a cumulative total per thread rather than the cost of one turn.
 *
 * <p><b>Runtimes, not vendor rows.</b> Whether the numbers are cumulative is a fact about the wire
 * protocol — it is the `codex` CLI's `turn.completed` block that counts up — and a vendor row is a
 * person's own entry whose id they choose. Somebody running two Codex accounts as `codex-work` and
 * `codex-home` would have had neither of them differenced, and both conversations would have been
 * over-billed in the log, increasingly, the longer they ran: exactly the defect {@link turnTokens}
 * exists to prevent, reintroduced through the key.</p>
 */
const CUMULATIVE = ['codex'];

/**
 * One chat turn, as it is written to disk.
 *
 * <p>`tokensIn` and `tokensOut` are always the cost of THIS TURN, whatever the vendor reported —
 * see {@link turnTokens}. `costUsd` is `null` unless the vendor actually billed a number, which is
 * what lets the reader tell a bill from an estimate without a second field to keep in step.</p>
 */
export interface ChatTurnRecord {
  readonly utc: string;
  /** The vendor row that answered — not the one configured now, which may already be different. */
  readonly provider: string;
  /** The model that ACTUALLY answered. */
  readonly model: string;
  readonly tokensIn: number;
  readonly tokensOut: number;
  /** What the vendor charged, or `null` when it said nothing — never a zero standing in for silence. */
  readonly costUsd: number | null;
  readonly seconds: number;
  /** `answered`, `stopped`, or a failure. A turn that cost tokens and produced nothing still cost them. */
  readonly outcome: string;
  /** Which conversation this turn belongs to, so a running total can be summed per tab. */
  readonly conversation: string;
  /**
   * What that conversation is CALLED, because its id is a UUID nobody can recognise.
   *
   * <p>The tab's own heading — the name of the source the chat was opened against. Without it the
   * log's "What" column reads `9f3c…` for every conversation row, which is a column that costs
   * width and answers nothing. It is the name of a tab, not the text of a question: nothing anybody
   * typed is written here.</p>
   */
  readonly title: string;
}

/** What a vendor said about a turn's tokens, before anything is normalised. */
export interface ReportedUsage {
  readonly tokensIn: number;
  readonly tokensOut: number;
  readonly costUsd: number | null;
}

/**
 * The tokens THIS turn cost, from what the vendor reported and what it had reported before.
 *
 * <p><b>`codex` reports a cumulative maximum for the thread, not a per-turn figure</b> — the plan
 * says so in its own limitations section, and a reviewer caught that the plan then went on to record
 * and sum those numbers per turn anyway. Turn one reporting 1 000 and turn two reporting 1 200
 * cumulative would have been written down as 2 200, and every conversation on that vendor would have
 * been over-billed in the log, increasingly, the longer it ran.</p>
 *
 * <p>So a cumulative reporter's numbers are DIFFERENCED against what it last said. A total that goes
 * backwards — a new thread, a vendor that changed its mind, a resumed conversation whose earlier
 * turns this process never saw — is treated as a fresh start rather than as a negative cost, because
 * a negative number of tokens is not a thing that can be true.</p>
 */
export function turnTokens(
  runtime: string,
  reported: ReportedUsage,
  previous: { readonly tokensIn: number; readonly tokensOut: number } | undefined,
): { readonly tokensIn: number; readonly tokensOut: number } {
  if (!CUMULATIVE.includes(runtime) || previous === undefined) {
    return { tokensIn: Math.max(0, reported.tokensIn), tokensOut: Math.max(0, reported.tokensOut) };
  }

  return {
    tokensIn: Math.max(0, reported.tokensIn - previous.tokensIn),
    tokensOut: Math.max(0, reported.tokensOut - previous.tokensOut),
  };
}

/**
 * What THIS turn was billed, by the same rule and for the same reason as {@link turnTokens}.
 *
 * <p>Differenced for a cumulative reporter, and separately from the tokens because the two can be
 * missing independently — a vendor can report tokens and no money, which two of the three do.</p>
 *
 * <p><b>This is dead code today and is written anyway.</b> The one cumulative vendor reports
 * `costUsd: null`, so nothing reaches the second branch. Leaving money un-differenced beside tokens
 * that are would be a trap set for whoever adds the next cumulative vendor, or for the day `codex`
 * starts pricing its own turns: the tokens would be right, the money would grow with the length of
 * the conversation, and nothing would say which of the two numbers to believe.</p>
 */
export function turnCost(
  runtime: string,
  reported: ReportedUsage,
  previous: { readonly costUsd: number | null } | undefined,
): number | null {
  if (reported.costUsd === null) {
    return null;
  }
  if (!CUMULATIVE.includes(runtime) || previous === undefined || previous.costUsd === null) {
    // Passed through UNTOUCHED, deliberately. This is what a vendor said it charged — `claude` bills
    // figures like 0.107958 — and rounding somebody else's invoice to make it prettier is not this
    // module's business.
    return Math.max(0, reported.costUsd);
  }

  // The subtraction is OURS, and so is the float noise it makes: $1.50 less $1.20 is $0.30 and
  // arrives as 0.30000000000000004. Cleaned up here, at four decimals, which is the precision the
  // rest of this product already counts money in — a round is fractions of a cent and two decimals
  // would read as free.
  return Math.round(Math.max(0, reported.costUsd - previous.costUsd) * 10_000) / 10_000;
}

/** Whether a RUNTIME's numbers are cumulative, so a caller knows to keep the last ones. */
export function reportsCumulative(runtime: string): boolean {
  return CUMULATIVE.includes(runtime);
}

/**
 * The usage fields of an event or a result, or NO fields at all when the vendor said nothing.
 *
 * <p>A spread rather than `usage: maybeUndefined`. With `exactOptionalPropertyTypes`, writing the key
 * with an undefined value puts the KEY there, and every caller that compares a whole object — which
 * each adapter and session test does — then sees a shape it did not have before. A turn nobody
 * reported numbers for must look exactly as it always did.</p>
 *
 * <p>It lives here, beside the type it is about, because it was written twice: once for adapter
 * events and once for turn results, with the same body and two copies of the same paragraph. One
 * concept, one implementation.</p>
 */
export function spent(usage: ReportedUsage | undefined): { usage?: ReportedUsage } {
  return usage === undefined ? {} : { usage };
}

/**
 * How a turn ended, in the three words the ledger uses.
 *
 * <p>`stopped` is separate from `failed` because they are opposite things — one is an instruction
 * that was obeyed, the other is a defect — and because a stopped turn is the one most likely to have
 * cost real money for no answer, which is precisely what somebody reading a ledger wants to find.</p>
 */
export type ChatOutcome = 'answered' | 'stopped' | 'failed';

/** A finished turn, as everything the ledger needs to know about it and nothing else. */
export interface FinishedTurn {
  /** When the person asked — the moment the row is filed under, not when the answer landed. */
  readonly utc: string;
  /** The vendor ROW that answered — what the log page prices by. */
  readonly provider: string;
  /**
   * The CLI shape behind that row, which is what decides whether its numbers are cumulative.
   *
   * <p>Separate from `provider` because a row's id is a person's own text and its runtime is not.
   * It is not written down: by the time a record exists the differencing has already happened, and
   * the runtime of a turn from last week answers no question the log page asks.</p>
   */
  readonly runtime: string;
  readonly model: string;
  readonly conversation: string;
  readonly title: string;
  readonly seconds: number;
  readonly outcome: ChatOutcome;
  /** What the vendor said about this turn, or nothing when it said nothing. */
  readonly reported: ReportedUsage | undefined;
  /** What that same conversation's vendor last reported, for the cumulative reporters. */
  readonly previous: ReportedUsage | undefined;
}

/**
 * One finished turn, as the line to write down.
 *
 * <p>Pure, and separate from the writing, so the two rules that matter — that a cumulative vendor's
 * numbers are differenced, and that a turn which produced no answer is still recorded — are tests
 * rather than claims about a function that touches a disk.</p>
 *
 * <p><b>A turn with no numbers at all is still a record.</b> Zeroes here are not a claim that the
 * turn was free; they are the honest shape of "it ran, it ended this way, and nobody said what it
 * cost". Dropping such a turn would hide exactly the ones worth seeing — a stop, a crash, a vendor
 * that billed silently — which is the accounting hole the gate raised against the plan.</p>
 */
export function chatTurnRecord(turn: FinishedTurn): ChatTurnRecord {
  const reported = turn.reported ?? { tokensIn: 0, tokensOut: 0, costUsd: null };
  const tokens = turnTokens(turn.runtime, reported, turn.previous);

  return {
    utc: turn.utc,
    provider: turn.provider,
    model: turn.model,
    tokensIn: tokens.tokensIn,
    tokensOut: tokens.tokensOut,
    costUsd: turnCost(turn.runtime, reported, turn.previous),
    seconds: turn.seconds,
    outcome: turn.outcome,
    conversation: turn.conversation,
    title: turn.title,
  };
}

/** One record, as the line that goes on the end of the file. */
export function chatUsageLine(record: ChatTurnRecord): string {
  return `${JSON.stringify(record)}\n`;
}

/** A number a vendor may have omitted, mistyped or sent as a string. Never `NaN`. */
function number(value: unknown): number {
  const asNumber = typeof value === 'number' ? value : Number(asText(value));

  return Number.isFinite(asNumber) && asNumber > 0 ? asNumber : 0;
}

/**
 * One line, read defensively.
 *
 * <p>Undefined for anything that is not a record, so a torn last line — the file is appended to
 * while it is read — costs itself and nothing else. Exactly the rule `usage.ts` follows for the
 * server's file, for the same reason.</p>
 */
export function parseChatUsageLine(line: string): ChatTurnRecord | undefined {
  let value: unknown;
  try {
    value = JSON.parse(line);
  } catch {
    return undefined;
  }
  if (value === null || typeof value !== 'object') {
    return undefined;
  }
  const row = value as Record<string, unknown>;
  const utc = asText(row['utc']);
  if (utc.length === 0) {
    return undefined;
  }

  return {
    utc,
    provider: asText(row['provider']),
    model: asText(row['model']),
    tokensIn: number(row['tokensIn']),
    tokensOut: number(row['tokensOut']),
    // `null` is a fact — "the vendor billed nothing it told us about" — and is kept as one. A zero
    // would say the turn was free, which is a different and usually untrue claim.
    costUsd: typeof row['costUsd'] === 'number' && Number.isFinite(row['costUsd']) ? row['costUsd'] : null,
    seconds: number(row['seconds']),
    outcome: asText(row['outcome']),
    conversation: asText(row['conversation']),
    title: asText(row['title']),
  };
}

/** Every record in the file, with unreadable lines dropped. */
export function parseChatUsage(text: string): ChatTurnRecord[] {
  return text
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0)
    .map(parseChatUsageLine)
    .filter((record): record is ChatTurnRecord => record !== undefined);
}

/**
 * What one conversation has cost so far.
 *
 * <p>Summed from the RECORDS rather than accumulated in memory as turns go by, which is the answer
 * to a reviewer's point about a total surviving a crash: the file is the source of truth, so a total
 * shown in a tab and a total shown in the log are the same number derived the same way, and a
 * process killed between a turn and its record loses the turn from both rather than from one.</p>
 *
 * <p>`estimated` is true when ANY turn in the conversation went unbilled — the tilde marks the whole
 * total, because a sum that is part bill and part estimate is an estimate.</p>
 */
export function conversationTotal(
  records: readonly ChatTurnRecord[],
  conversation: string,
): { readonly tokensIn: number; readonly tokensOut: number; readonly costUsd: number | null; readonly estimated: boolean } {
  const mine = records.filter((record) => record.conversation === conversation);
  const billed = mine.filter((record) => record.costUsd !== null);

  return {
    tokensIn: mine.reduce((total, record) => total + record.tokensIn, 0),
    tokensOut: mine.reduce((total, record) => total + record.tokensOut, 0),
    costUsd: billed.length === 0 ? null : billed.reduce((total, record) => total + (record.costUsd ?? 0), 0),
    estimated: mine.length > billed.length,
  };
}
