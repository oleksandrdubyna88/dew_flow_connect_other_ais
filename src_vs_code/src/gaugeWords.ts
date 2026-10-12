import { clockOf } from './resetWords';
import type { GaugeReading, GaugeWindow } from './teamServerApi';

/**
 * The vendor's own gauge, as a person reads it (story 6.2 of todo/PLAN_every_round_is_counted.md):
 * `codex (weekly): 37 % used · resets Thu 07:00 · seen 12 min ago` per reported window, and the window's rounds per 1 %,
 * or why there is none. Pure — every sentence is a test.
 *
 * <p><b>Reset first.</b> A window whose reset time has passed with no newer reading says <i>reset time passed;
 * availability unverified</i> and the last percentage it read — recovery is never inferred from the clock or from the
 * percentage. A reading a later capture could not refresh says <i>stale</i>; every line says how old its reading is.</p>
 *
 * <p><b>Absent is absent.</b> No reading draws no line, and a window the vendor did not report is not drawn — never a 0 %.
 * The vendor's own verdict (<c>ordinaryUsageAllowed</c>) is kept beside the windows when it refuses ordinary use.</p>
 */

/** One window's rounds per 1 %, as `/api/usage` answers it (story 6.2) — the shared library's `GaugeRatio`. */
export interface GaugeRatio {
  readonly vendor: string;
  readonly limitId: string;
  readonly windowMins: number;
  /** Null, never 0 or infinity, unless the figure is available and every consumer is counted. */
  readonly roundsPerPercent: number | null;
  readonly intervals: number;
  /** A reason code, or empty when the figure is shown. */
  readonly unavailable: string;
  /** The uncounted consumer that hides the figure, or empty. */
  readonly missingConsumer: string;
}

const MINUTE_MS = 60_000;
const HOUR_MS = 60 * MINUTE_MS;
const DAY_MS = 24 * HOUR_MS;

/** The lengths a person has a word for; any other is said in hours or minutes. */
const LENGTH_WORDS: Readonly<Record<number, string>> = { 10080: 'weekly', 43200: 'monthly' };

/** `codex (weekly)` — a window by its limit and its length. */
export function windowName(window: Pick<GaugeWindow, 'limitId' | 'windowDurationMins'>): string {
  return `${window.limitId} (${lengthWords(window.windowDurationMins)})`;
}

function lengthWords(mins: number | null): string {
  return isLength(mins) ? LENGTH_WORDS[mins] ?? hoursOrMinutes(mins) : 'a window of unstated length';
}

function isLength(mins: number | null): mins is number {
  return typeof mins === 'number' && Number.isFinite(mins) && mins > 0;
}

function hoursOrMinutes(mins: number): string {
  return mins % 60 === 0 ? `${mins / 60} h` : `${mins} min`;
}

/** One line per reported window of a reading, shortest first, then the vendor's verdict — or none when there is no reading. */
export function gaugeLines(reading: GaugeReading | undefined, nowMs: number, timeZone?: string): readonly string[] {
  if (reading === undefined) {
    return [];
  }
  const seen = seenWords(reading, nowMs);
  const windows = [...reading.windows].sort((a, b) => (a.windowDurationMins ?? Infinity) - (b.windowDurationMins ?? Infinity));

  return [...windows.map((window) => `${windowName(window)}: ${state(window, nowMs, timeZone)} · ${seen}`), ...verdict(reading)];
}

/** What a window says now: its use and its reset, or — once the reset has passed — that nobody has looked since. */
function state(window: GaugeWindow, nowMs: number, timeZone: string | undefined): string {
  const resets = Date.parse(window.resetsAtUtc);
  if (!Number.isFinite(resets)) {
    return `${percent(window.usedPercent)} used · reset time not stated`;
  }

  return resets <= nowMs
    ? `reset time passed; availability unverified · ${percent(window.usedPercent)} used when last read`
    : `${percent(window.usedPercent)} used · resets ${clockOf(resets, timeZone)}`;
}

function percent(used: number): string {
  return `${Number.isInteger(used) ? used : used.toFixed(1)} %`;
}

/** `seen 12 min ago`, and `stale` when a later capture failed. */
function seenWords(reading: GaugeReading, nowMs: number): string {
  const ago = agoWords(nowMs - Date.parse(reading.observedUtc));

  return reading.stale ? `seen ${ago} · stale — a later read failed` : `seen ${ago}`;
}

/** The units an age is said in, largest first — rounded DOWN, so a reading is never said to be fresher than it is old. */
const AGO_UNITS: readonly (readonly [number, string])[] = [[DAY_MS, 'd'], [HOUR_MS, 'h'], [MINUTE_MS, 'min']];

function agoWords(ms: number): string {
  if (!Number.isFinite(ms)) {
    return 'at an unknown time';
  }
  const unit = AGO_UNITS.find(([length]) => ms >= length);

  return unit === undefined ? 'just now' : `${Math.floor(ms / unit[0])} ${unit[1]} ago`;
}

/** The vendor's own "not allowed" — kept, never inferred from a percentage; nothing when it allows or did not say. */
function verdict(reading: GaugeReading): readonly string[] {
  return reading.ordinaryUsageAllowed === false
    ? [`${limitOf(reading)}: the vendor says ordinary use is not allowed now${reachedWords(reading)}`]
    : [];
}

function limitOf(reading: GaugeReading): string {
  return reading.windows[0]?.limitId ?? 'the vendor';
}

function reachedWords(reading: GaugeReading): string {
  return reading.rateLimitReachedType.length > 0 ? ` (${reading.rateLimitReachedType})` : '';
}

/** Why a figure is not a number, in words — a code this build does not know is said as it came. */
const REASON_WORDS: Readonly<Record<string, string>> = {
  'too-few-readings': 'fewer than two readings of this window on one account',
  'percent-did-not-rise': 'the percentage did not rise between the last readings',
  'window-reset': 'the window reset between the last readings',
  'stale-reading': 'a reading was older than the launch it followed',
  'missing-reading': 'a launch between the readings has no reading of its own',
  'run-without-round': 'a launch between the readings sent no round id',
  'no-rounds': 'no round ran between the readings',
  'local-capture-deferred': 'this machine does not read the gauge; a Team server does',
};

/** The consumers coai cannot count, in words. */
const CONSUMER_WORDS: Readonly<Record<string, string>> = {
  'direct-codex-use': 'direct codex use outside coai spends the same limit and is not counted',
};

/** What a window's rounds-per-1 % says — the figure, or why there is none; never 0 and never infinity. */
export function ratioLine(ratio: GaugeRatio): string {
  const name = windowName({ limitId: ratio.limitId, windowDurationMins: ratio.windowMins });
  if (ratio.missingConsumer.length > 0) {
    return `rounds per 1 % of ${name}: hidden — ${said(CONSUMER_WORDS, ratio.missingConsumer)}`;
  }

  return isShown(ratio.roundsPerPercent)
    ? `≈ ${ratio.roundsPerPercent.toFixed(1)} rounds per 1 % of ${name} — over the last ${ratio.intervals} intervals`
    : `rounds per 1 % of ${name}: unavailable — ${said(REASON_WORDS, ratio.unavailable)}`;
}

/** A figure that may be shown: a positive number — never a 0, a negative, an infinity or a NaN. */
export function isShown(value: number | null): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0;
}

function said(words: Readonly<Record<string, string>>, code: string): string {
  return words[code] ?? (code.length > 0 ? code : 'not said');
}
