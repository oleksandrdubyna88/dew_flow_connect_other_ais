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
 * One chat turn, as it is written to disk.
 *
 * <p>`tokensIn` and `tokensOut` are always the cost of THIS TURN, whatever the vendor counts in — the
 * SESSION differences a cumulative reporter before the record is built, because a session's life is
 * exactly a vendor thread's life. `costUsd` is `null` unless the vendor actually billed a number,
 * which is what lets the reader tell a bill from an estimate without a second field to keep in
 * step.</p>
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
  /**
   * WHOSE row this belongs in, decided when it was written.
   *
   * <p>`provider` is the id of the model PRESET in force, and a preset is a row in a list a person
   * edits: change its runtime and every line ever recorded under it would move to another vendor,
   * in a ledger that is supposed to be append-only. So the vendor is resolved once, here, and the
   * reader prefers it. Optional because every line written before this field existed has none —
   * those are still resolved through the preset list, which is the best answer available for them.
   * (CodeRabbit, PR #209.)</p>
   */
  readonly vendor?: string;
}

/** What a vendor said about a turn's tokens, before anything is normalised. */
export interface ReportedUsage {
  readonly tokensIn: number;
  readonly tokensOut: number;
  readonly costUsd: number | null;
}

/**
 * The tokens THIS turn cost, from what the vendor reported and what the thread had reported before.
 *
 * <p><b>`codex` reports a cumulative maximum for the thread, not a per-turn figure</b> — the plan says
 * so in its own limitations section, and a reviewer caught that the plan then went on to record and
 * sum those numbers per turn anyway. Turn one reporting 1 000 and turn two reporting 1 200 cumulative
 * would have been written down as 2 200, and every conversation on that vendor would have been
 * over-billed in the log, increasingly, the longer it ran.</p>
 *
 * <p><b>`cumulative` is a BOOLEAN the adapter declares, not a name matched against a list.</b> It was
 * a list of runtime names here, and a gate reviewer named the flaw: implementing `ChatAdapter` was
 * then not enough to be billed correctly, because an unlisted runtime defaulted silently to per-turn.
 * A required field on the interface cannot be forgotten — the compiler asks. (gemini, the code
 * round.)</p>
 *
 * <p><b>A total that goes BACKWARDS is a fresh baseline, not a free turn.</b> A vendor thread that
 * restarted, compacted its context or evicted a cache reports a number below the last one; the turn
 * still cost what it says it cost, and clamping the difference to zero would silently drop it. This
 * function used to say "fresh start" in its own comment and return a zero — the code and the comment
 * disagreed, and the comment was right. (gemini, the code round.)</p>
 */
export function turnTokens(
  cumulative: boolean,
  reported: ReportedUsage,
  previous: { readonly tokensIn: number; readonly tokensOut: number } | undefined,
): { readonly tokensIn: number; readonly tokensOut: number } {
  if (!cumulative || previous === undefined || wentBackwards(reported, previous)) {
    return { tokensIn: Math.max(0, reported.tokensIn), tokensOut: Math.max(0, reported.tokensOut) };
  }

  return {
    tokensIn: reported.tokensIn - previous.tokensIn,
    tokensOut: reported.tokensOut - previous.tokensOut,
  };
}

/** Whether the running total dropped, which means the thread this reports for is not the old one. */
function wentBackwards(
  reported: ReportedUsage,
  previous: { readonly tokensIn: number; readonly tokensOut: number },
): boolean {
  return reported.tokensIn < previous.tokensIn || reported.tokensOut < previous.tokensOut;
}

/**
 * What THIS turn was billed, as its vendor billed it — or nothing, when nobody can say.
 *
 * <p><b>The money used to be DIFFERENCED here, mirroring {@link turnTokens}, and the operator ruled
 * that arithmetic out on 2026-09-10 as dead code.</b> It was: the only cumulative vendor reports
 * `costUsd: null`, so the subtraction was unreachable, and no vendor anywhere has been observed
 * billing a running total. Checked before deleting it, because two other things in this product turn
 * tokens into money and neither goes through here — the public price lists and the rate a person
 * types on a vendor row are both <i>dollars per million tokens</i>, applied when a row is DRAWN.
 * This function is only ever about a figure a vendor put on the wire itself.</p>
 *
 * <p><b>What replaces it is a refusal, not a clamp, and the difference matters the day the case
 * arrives.</b> A cumulative vendor's money is `null`: a running total is not what one turn cost, so
 * recording it as one would over-report every turn but the first, increasingly, which is precisely
 * the token defect the gate caught before this shipped. `null` says "nobody told us what this turn
 * cost" — which is true — and the log page then prices the row from the public list by the model that
 * answered and marks it with the tilde it already uses for an estimate. Today this changes nothing,
 * because the one cumulative vendor reports no money at all.</p>
 */
export function turnCost(cumulative: boolean, reported: ReportedUsage): number | null {
  if (reported.costUsd === null || cumulative) {
    return null;
  }

  // Passed through UNTOUCHED otherwise. This is what a vendor said it charged — `claude` bills figures
  // like 0.107958 — and rounding somebody else's invoice to make it prettier is not this module's
  // business. The clamp is only against nonsense: a negative bill is not a credit.
  return Math.max(0, reported.costUsd);
}

/** Four decimals, the precision this product counts money in. Cents would read a real cost as free. */
function round4(usd: number): number {
  return Math.round(usd * 10_000) / 10_000;
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
  /** WHOSE row it belongs in, resolved by the caller: the runtime behind that row. */
  readonly vendor?: string;
  readonly model: string;
  readonly conversation: string;
  readonly title: string;
  readonly seconds: number;
  readonly outcome: ChatOutcome;
  /**
   * What this ONE turn cost, or nothing when the vendor said nothing.
   *
   * <p>Already normalised: a cumulative vendor was differenced by its SESSION, which is the only
   * thing whose lifetime is exactly the vendor thread's. This function is therefore not where the
   * cumulative rule lives, and the record it writes cannot be wrong about which vendor produced it.
   * (gemini, the code round — the rule used to reach out of the adapter layer and into the command
   * that orchestrates the page.)</p>
   */
  readonly usage: ReportedUsage | undefined;
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
  const usage = turn.usage ?? { tokensIn: 0, tokensOut: 0, costUsd: null };

  return {
    utc: turn.utc,
    provider: turn.provider,
    model: turn.model,
    tokensIn: Math.max(0, usage.tokensIn),
    tokensOut: Math.max(0, usage.tokensOut),
    costUsd: usage.costUsd,
    seconds: turn.seconds,
    outcome: turn.outcome,
    conversation: turn.conversation,
    title: turn.title,
    // Resolved when it is WRITTEN, so editing a preset later cannot move a year of history to
    // another vendor. Absent when the caller could not say, which the reader then resolves the old
    // way. (CodeRabbit, PR #209.)
    ...(turn.vendor !== undefined && turn.vendor.length > 0 ? { vendor: turn.vendor } : {}),
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
    ...(asText(row['vendor']).length > 0 ? { vendor: asText(row['vendor']) } : {}),
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
    // Rounded, for the reason `turnCost` rounds its own subtraction: $0.10 + $0.20 is $0.30 and
    // arrives as 0.30000000000000004, and a running total shown to a person must not read as float
    // noise. (gemini, the code round.)
    costUsd: billed.length === 0
      ? null
      : round4(billed.reduce((total, record) => total + (record.costUsd ?? 0), 0)),
    estimated: mine.length > billed.length,
  };
}
