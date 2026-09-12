/**
 * How often the chat was REACHED FOR, which is a different question from what it answered.
 *
 * <p>Every finished turn is written down already — `chatUsage.ts` records what it cost, and the
 * spending tab adds it up. None of that can answer "how many times did I use *take the question*",
 * because a turn is written when a turn FINISHES and `add the question` normally finishes none: it
 * fills the composer and stops. The count somebody wants is of invocations, so invocations are what
 * is recorded.</p>
 *
 * <h2>Why a file of its own</h2>
 *
 * <p>The first draft of this put door lines into `chat-usage.jsonl` to save a read, and the plan
 * round refused it twice over, from two vendors independently. `parseChatUsageLine` requires a
 * non-empty `utc` and <b>nothing else</b>, coercing every other field — so a door line in that file
 * is read by every existing caller as a TURN with zero tokens. That is a phantom conversation row in
 * the rounds table today, and the same in any older build somebody rolls back to, and there is no
 * shape that avoids it while still carrying a timestamp. A second file costs one reader and one
 * stamped cache; it cannot change the meaning of a file four call sites already read.</p>
 *
 * <h2>Written by a union, read as a string</h2>
 *
 * <p>{@link Door} is what a CALLER must pass, so a new door cannot be added without the compiler
 * asking which it is. The record holds a plain string, so a line written by a LATER version — a
 * sixth door nobody here has heard of — still counts towards how often the chat was opened instead
 * of being dropped by the reader that came first. Strict where it is written, tolerant where it is
 * read.</p>
 */

/** The five ways into a chat, as the commands that open them. */
export type Door = 'key' | 'default' | 'choose' | 'take' | 'add';

/** Every door this version knows how to open, in the order the commands are registered. */
export const DOORS: readonly Door[] = ['key', 'default', 'choose', 'take', 'add'];

/**
 * The doors that take a question rather than a passage — what *Asked* counts.
 *
 * <p>A set rather than a comparison written out at the counting site: the question "is this one of
 * the two" is asked in more than one place, and two copies of a list of names is two places for a
 * sixth door to be forgotten.</p>
 */
const ASKING: ReadonlySet<string> = new Set<string>(['take', 'add']);

/** Whether this door took or added a question — the narrower of the two counts. */
export function asking(door: string): boolean {
  return ASKING.has(door);
}

/** One invocation, as it is written to disk. */
export interface ChatDoorRecord {
  readonly utc: string;
  /** Which command was used. A string, not the union: see the note about later versions above. */
  readonly door: string;
  /**
   * The vendor row that WOULD answer, resolved at the moment the door was used.
   *
   * <p>Not the vendor that did answer — a door that opens the composer may never be answered at
   * all, and a conversation can switch model afterwards. Empty when nothing was configured, which
   * is a real state and is grouped as one rather than invented into a row.</p>
   */
  readonly provider: string;
  readonly model: string;
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

/**
 * One invocation, built from an instant somebody else chose.
 *
 * <p>The clock is a PARAMETER rather than a call inside, which is the shape the UTC rule asks for -
 * "injected, so a test can control it" - and the only shape a test in this suite can reach at all,
 * since the caller lives in a module that imports `vscode`. What reaches the ledger is
 * `toISOString()`, which is UTC by definition and by the rule. (codex and gemini, the code round.)</p>
 */
export function chatDoorRecord(
  door: Door,
  provider: string,
  model: string,
  at: Date,
  vendor = '',
): ChatDoorRecord {
  return { utc: at.toISOString(), door, provider, model, ...(vendor.length > 0 ? { vendor } : {}) };
}

/** The record as one line of JSONL, with the newline. */
export function chatDoorLine(record: ChatDoorRecord): string {
  return `${JSON.stringify(record)}\n`;
}

/** A string, made safe: anything that is not one is empty rather than `undefined` downstream. */
function asText(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

/**
 * One line back, or nothing when it is torn, foreign, or names no door.
 *
 * <p>`utc` and `door` are the two fields that make a record mean anything — a line without either
 * cannot be placed in a window or counted — so those are required and the other two are coerced.
 * A line this cannot read is dropped rather than thrown: a ledger is appended to by a process that
 * can be killed mid-write, and one torn line must not take a year of history with it.</p>
 */
export function parseChatDoorLine(line: string): ChatDoorRecord | undefined {
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
  const door = asText(row['door']);
  if (utc.length === 0 || door.length === 0) {
    return undefined;
  }

  const vendor = asText(row['vendor']);

  return {
    utc,
    door,
    provider: asText(row['provider']),
    model: asText(row['model']),
    ...(vendor.length > 0 ? { vendor } : {}),
  };
}

/** Every record in the file, with unreadable lines dropped. */
export function parseChatDoors(text: string): ChatDoorRecord[] {
  return text
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0)
    .map(parseChatDoorLine)
    .filter((record): record is ChatDoorRecord => record !== undefined);
}
