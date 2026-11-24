/**
 * The value rules of the accounts parser (E1.S2, spec § A2.7): the Claude usage lines with their reset text, the Codex record's
 * windows and times, and the small rules of a text value (version, email, organization, plan). Pure — no process, no file, no
 * `vscode` — and it takes values that have ALREADY passed `plainValue` and the credential net in `remoteAccountsReply.ts`.
 *
 * <p><b>A number is a number in its range, or `null`.</b> The VM states percents USED, and they are kept as sent; this module never
 * works out "left" (that is the list's, in one place), and no path turns a missing number into a zero. A reset time is read only with
 * the zone `UTC` and only when it falls between one hour ago and eight days ahead; anything else is a dash and the percent is still
 * shown.</p>
 */

export interface ClaudeWindow {
  readonly name: '5h' | 'week' | { readonly model: string };
  /** As the VM states it: USED. `null` is "not known", never 0. */
  readonly usedPercent: number | null;
  readonly resetsAtMs: number | null;
}

export type ClaudeUsage =
  | { readonly state: 'read'; readonly windows: readonly ClaudeWindow[]; readonly unmatched: number }
  | { readonly state: 'skipped' }
  | { readonly state: 'failed'; readonly why: 'timeout' | 'error' | 'format' | 'model-call' };

export interface CodexWindow {
  /** The length of the window: it is named by this, never by the slot (`primary`, `secondary`) it came in. */
  readonly minutes: number;
  readonly usedPercent: number | null;
  readonly resetsAtMs: number | null;
}

export type CodexUsage =
  | {
    readonly state: 'read';
    readonly windows: readonly CodexWindow[];
    readonly asOf: { readonly ms: number; readonly from: 'record' | 'file' } | null;
    /** The host has no `stat`: said, and never read as "a time that is not known". */
    readonly statMissing: boolean;
  }
  | { readonly state: 'failed'; readonly why: 'no-sessions' | 'none' | 'error' };

const HOUR_MS = 3_600_000;
const DAY_MS = 24 * HOUR_MS;

// ───────────────── the small rules ─────────────────

const PERCENT = /^\d{1,3}(?:\.\d{1,2})?$/u;

/** A percent as the VM states it: one to three digits, at most two decimals, at most 100. Anything else is not known. */
export function percentOf(word: string): number | null {
  const value = PERCENT.test(word) ? Number(word) : Number.NaN;

  return value <= 100 ? value : null;
}

const VERSION = /^\d{1,5}(?:\.\d{1,5}){1,3}$/u;
export const versionOf = (value: string): string | null => (VERSION.test(value) ? value : null);

/** One `@`, no space, at most 254 characters; else `''`: the account is not known. */
export function emailOf(value: string): string {
  const one = value.split('@').length === 2 && !value.includes(' ') && value.length <= 254;

  return one ? value : '';
}

export const orgOf = (value: string): string | null => (value.trim() === '' ? null : value);

const PLAN = /^[A-Za-z0-9][A-Za-z0-9 ._-]{0,39}$/u;
export const planOf = (value: string): string | null => (PLAN.test(value) ? value : null);

/** Digits only, 1 to `max` of them, as a number; else `null`. */
const digitsOf = (word: string, max: number): number | null => (word.length >= 1 && word.length <= max && /^\d+$/u.test(word) ? Number(word) : null);

// ───────────────── the reset text of a Claude line ─────────────────

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const RESET = /^(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec) ([1-9]|[12]\d|3[01]), (1[0-2]|[1-9])(?::([0-5]\d))?(am|pm)$/u;

/** 12am is hour 0, 12pm is hour 12, 3pm is 15. */
const hourOf = (match: RegExpExecArray): number => (Number(match[3]) % 12) + (match[5] === 'pm' ? 12 : 0);

/** The moment of the text in a given year, or `null` when the month has no such day. No minutes is `:00`. */
function momentIn(year: number, match: RegExpExecArray): number | null {
  const month = MONTHS.indexOf(match[1] as string);
  const day = Number(match[2]);
  const moment = Date.UTC(year, month, day, hourOf(match), Number(match[4] ?? '0'));
  const back = new Date(moment);

  return back.getUTCMonth() === month && back.getUTCDate() === day ? moment : null;
}

/**
 * *"resets Nov 19, 3pm (UTC)"* has no year: it is the UTC year of `nowMs`, or the next one when that moment is more than an hour
 * past. The zone must be exactly `UTC`, and the result counts only between one hour before `nowMs` and eight days after it.
 */
/** The moment, or the same moment a year on when it is more than an hour past. */
function nextYearIfPast(match: RegExpExecArray, year: number, nowMs: number): number | null {
  const first = momentIn(year, match);

  return first !== null && first < nowMs - HOUR_MS ? momentIn(year + 1, match) : first;
}

const inReach = (moment: number | null, nowMs: number): number | null => (moment !== null && moment >= nowMs - HOUR_MS && moment <= nowMs + 8 * DAY_MS ? moment : null);

export function resetMs(text: string, zone: string, nowMs: number): number | null {
  const match = zone === 'UTC' ? RESET.exec(text) : null;

  return match === null ? null : inReach(nextYearIfPast(match, new Date(nowMs).getUTCFullYear(), nowMs), nowMs);
}

// ───────────────── the Claude usage lines ─────────────────

/** The twin of the script's two patterns: session or week with its model words, the percent, the reset text and its zone. */
const CLAUDE_LINE = /^Current (session|week \(([A-Za-z0-9][A-Za-z0-9 ._-]{0,39})\)): (\d{1,3}(?:\.\d{1,2})?)% used \u00b7 resets ([A-Za-z0-9,: ]{1,40}) \(([A-Za-z0-9_/+-]{1,40})\)$/u;

function windowNameOf(match: RegExpExecArray): ClaudeWindow['name'] {
  if (match[1] === 'session') {
    return '5h';
  }

  return match[2] === 'all models' ? 'week' : { model: match[2] as string };
}

const nameKey = (name: ClaudeWindow['name']): string => (typeof name === 'string' ? name : `model:${name.model}`);

/** One `:line` as a window, or `undefined` when it fits no form. */
function claudeWindowOf(line: string, nowMs: number): ClaudeWindow | undefined {
  const match = CLAUDE_LINE.exec(line);

  return match === null ? undefined : {
    name: windowNameOf(match),
    usedPercent: percentOf(match[3] as string),
    resetsAtMs: resetMs(match[4] as string, match[5] as string, nowMs),
  };
}

interface Gathered { readonly windows: readonly ClaudeWindow[]; readonly unmatched: number }

/** One more line: a window when it fits and its name is new, else one more unmatched. */
function gathered(have: Gathered, line: string, nowMs: number): Gathered {
  const one = claudeWindowOf(line, nowMs);
  if (one === undefined || have.windows.some((there) => nameKey(there.name) === nameKey(one.name))) {
    return { windows: have.windows, unmatched: have.unmatched + 1 };
  }

  return { windows: [...have.windows, one], unmatched: have.unmatched };
}

/**
 * The usage of the lines the VM sent. A line that fits no form, and a second line for a window that is already there, are not
 * read: each adds one to `unmatched`, on top of the script's own count. `turns` that is not 0 means a model call was made, so the
 * usage is `model-call` whatever lines came with it; no window at all is `format`.
 */
export function claudeUsageOf(turns: number, lines: readonly string[], scriptUnmatched: number, nowMs: number): ClaudeUsage {
  if (turns !== 0) {
    return { state: 'failed', why: 'model-call' };
  }
  const { windows, unmatched } = lines.reduce<Gathered>((have, line) => gathered(have, line, nowMs), { windows: [], unmatched: scriptUnmatched });

  return windows.length === 0 ? { state: 'failed', why: 'format' } : { state: 'read', windows, unmatched };
}

// ───────────────── the Codex record ─────────────────

const MINUTES_MAX = 527_040;

/** The length of a window in minutes: 1 to 527 040 (366 days), else `null` and the line is no window. */
function minutesOf(word: string): number | null {
  const minutes = digitsOf(word, 6);

  return minutes !== null && minutes >= 1 && minutes <= MINUTES_MAX ? minutes : null;
}

/** Epoch seconds of 1 to 11 digits, in milliseconds; else `null`. */
const secondsToMs = (word: string): number | null => {
  const seconds = digitsOf(word, 11);

  return seconds === null ? null : seconds * 1000;
};

/** `<used> <minutes> <reset>` (the caller has checked that there are three words); a window with no usable length is no window. */
function codexWindowOf(value: string): CodexWindow | undefined {
  const words = value.split(' ');
  const minutes = minutesOf(words[1] as string);

  return minutes === null ? undefined : { minutes, usedPercent: percentOf(words[0] as string), resetsAtMs: secondsToMs(words[2] as string) };
}

/** The windows in the order sent; of two windows of one length the first is kept. */
export function codexWindowsOf(values: readonly string[]): readonly CodexWindow[] {
  const windows: CodexWindow[] = [];
  for (const value of values) {
    const one = codexWindowOf(value);
    if (one !== undefined && !windows.some((have) => have.minutes === one.minutes)) {
      windows.push(one);
    }
  }

  return windows;
}

const RECORD_TIME = /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d{1,9})?(?:Z|[+-]\d\d:\d\d)$/u;
const EARLIEST_MS = Date.UTC(2020, 0, 1);

const inRange = (ms: number, nowMs: number): boolean => Number.isFinite(ms) && ms >= EARLIEST_MS && ms <= nowMs + DAY_MS;

/** The record's own time when it fits and is in range. */
function recordTimeOf(text: string, nowMs: number): number | null {
  const ms = RECORD_TIME.test(text) ? Date.parse(text) : Number.NaN;

  return inRange(ms, nowMs) ? ms : null;
}

const fileTimeOf = (word: string, nowMs: number): number | null => {
  const ms = secondsToMs(word);

  return ms !== null && inRange(ms, nowMs) ? ms : null;
};

type AsOf = { readonly ms: number; readonly from: 'record' | 'file' } | null;

function timeOf(record: number | null, file: number | null): AsOf {
  if (record !== null) {
    return { ms: record, from: 'record' };
  }

  return file === null ? null : { ms: file, from: 'file' };
}

/**
 * `as-of` is the record's time (every word but the last, as the record has it) and the file's time (the last word: epoch seconds,
 * `-`, or `no-stat`). The record's own wins when it parses and is in range; else the file's; else there is no time. `no-stat` is no
 * time and is said by `statMissing`. The caller has already refused a last word that is none of the three.
 */
export function asOfOf(value: string, nowMs: number): { readonly asOf: AsOf; readonly statMissing: boolean } {
  const words = value.split(' ');
  const last = words[words.length - 1] as string;

  return { asOf: timeOf(recordTimeOf(words.slice(0, -1).join(' '), nowMs), fileTimeOf(last, nowMs)), statMissing: last === 'no-stat' };
}

/** The Codex usage of the `window` and `as-of` values of a `COAI-USAGE codex read` record. */
export function codexUsageOf(windowValues: readonly string[], asOfValue: string, nowMs: number): CodexUsage {
  const windows = codexWindowsOf(windowValues);
  if (windows.length === 0) {
    return { state: 'failed', why: 'none' };
  }

  return { state: 'read', windows, ...asOfOf(asOfValue, nowMs) };
}
