/**
 * The words of the accounts list for a usage reading (E1.S4): the mod's three rules — `percentLeft`, `minutesLeft` and `resetText` — copied ONCE
 * into TypeScript (the mod is a `.tsx` of another runtime that this extension cannot import, so `usageWords.test.ts` holds this copy to the
 * mod's own cases), the names of the windows, and the lines built from them. Pure: it imports nothing but types, reads no clock and holds no text
 * that came from a VM.
 *
 * <p><b>Every number is said as "left".</b> The VM states percents USED; this module is the one place that turns them into "left". A number that
 * is not there is a dash with no "% left", never `0%`.</p>
 */

const FIVE_HOURS = 300;
const WEEK = 10_080;
const MODEL_DEFAULT = 'Fable';

/** The mod's rule (`limits.tsx`): 100 minus the percent used, rounded, clamped 0–100; `null` for anything that is not a finite number. */
export function percentLeft(used: unknown): number | null {
  if (typeof used !== 'number' || !Number.isFinite(used)) {
    return null;
  }

  return Math.max(0, Math.min(100, Math.round(100 - used)));
}

/**
 * The mod's rule: whole minutes from `nowMs` to the reset; `null` while either is unknown or once the reset has come.
 *
 * <p>EMBEDDED in the accounts page by its own source text (`remoteAccountsPage.ts`), so it names nothing outside itself and holds no backtick.</p>
 */
export function minutesLeft(resetsAtMs: number | null, nowMs: number | null): number | null {
  const known = (ms: number | null): ms is number => ms !== null && Number.isFinite(ms);
  if (!known(resetsAtMs) || !known(nowMs) || nowMs >= resetsAtMs) {
    return null;
  }

  return Math.floor((resetsAtMs - nowMs) / 60000);
}

/**
 * The mod's text under 24 hours — `1h 0m` from 60 minutes on, `59m`, `<1m` — and, from 24 hours on, `<d>d <h>h` (the form of the plan's sample:
 * a week's reset is `2d 15h`, never `63h 10m`). A dash when the time is not known.
 *
 * <p>EMBEDDED in the accounts page by its own source text, like `minutesLeft`: no module constant, no helper outside, no template literal.</p>
 */
export function resetText(minutes: number | null): string {
  const days = (all: number): string => Math.floor(all / 1440) + 'd ' + Math.floor((all % 1440) / 60) + 'h';
  const hours = (all: number): string => Math.floor(all / 60) + 'h ' + (all % 60) + 'm';
  const small = (all: number): string => (all < 1 ? '<1m' : all + 'm');
  if (minutes === null) {
    return '—';
  }

  return minutes >= 1440 ? days(minutes) : minutes >= 60 ? hours(minutes) : small(minutes);
}

/** A window as the two vendors name it: Claude by its name (a model by the model's), Codex by its length in minutes. */
export type NamedWindow =
  | { readonly name: '5h' | 'week' | { readonly model: string } }
  | { readonly minutes: number };

/** A length in hours with at most one decimal and no trailing `.0`: 120 is `2h`, 90 is `1.5h`. */
const hoursOf = (minutes: number): string => `${Math.round(minutes / 6) / 10}h`;

const codexLabel = (minutes: number): string => (minutes === FIVE_HOURS ? '5h' : minutes === WEEK ? 'week' : hoursOf(minutes));

/** Claude: `5h`, `week`, or the model's name. Codex: by `minutes` — 300 is `5h`, 10 080 is `week`, another length in hours. Never by a slot's name. */
export function windowLabel(window: NamedWindow): string {
  if ('minutes' in window) {
    return codexLabel(window.minutes);
  }

  return typeof window.name === 'string' ? window.name : window.name.model;
}

/** `<label> <n>% left`, or `<label> —` — with no "% left" — when the number is not there. */
export function part(label: string, used: unknown): string {
  const left = percentLeft(used);

  return left === null ? `${label} —` : `${label} ${left}% left`;
}

/** What a window's reset says: `resets in <t>`, a dash when the time is not known. */
export const resetsIn = (resetsAtMs: number | null, nowMs: number): string => `resets in ${resetText(minutesLeft(resetsAtMs, nowMs))}`;

/** A Claude window as the parser types it. */
export interface ClaudeWindowLike {
  readonly name: '5h' | 'week' | { readonly model: string };
  readonly usedPercent: number | null;
  readonly resetsAtMs: number | null;
}

/** A Codex window as the parser types it. */
export interface CodexWindowLike {
  readonly minutes: number;
  readonly usedPercent: number | null;
  readonly resetsAtMs: number | null;
}

const named = (windows: readonly ClaudeWindowLike[], name: '5h' | 'week'): ClaudeWindowLike | undefined => windows.find((one) => one.name === name);

const modelWindows = (windows: readonly ClaudeWindowLike[]): readonly (ClaudeWindowLike & { readonly name: { readonly model: string } })[] =>
  windows.flatMap((one) => (typeof one.name === 'string' ? [] : [one as ClaudeWindowLike & { readonly name: { readonly model: string } }]));

const sessionParts = (session: ClaudeWindowLike | undefined, nowMs: number): readonly string[] => [part('5h', session?.usedPercent), resetsIn(session?.resetsAtMs ?? null, nowMs)];

/**
 * The mod's line: `5h <n>% left · resets in <t> · week <n>% left · <Model> <n>% left`, one model part for each model window that `/usage` named, and
 * the mod's own `Fable —` when it named none. With no session window it says `5h — · resets in —`.
 */
export function claudeLine(windows: readonly ClaudeWindowLike[], nowMs: number): string {
  const models = modelWindows(windows);
  const modelParts = models.length === 0 ? [part(MODEL_DEFAULT, null)] : models.map((one) => part(windowLabel(one), one.usedPercent));

  return [...sessionParts(named(windows, '5h'), nowMs), part('week', named(windows, 'week')?.usedPercent), ...modelParts].join(' · ');
}

/** The week's reset, for the row's second line: `week resets in <t>`. */
export const weekResets = (windows: readonly ClaudeWindowLike[], nowMs: number): string => `week ${resetsIn(named(windows, 'week')?.resetsAtMs ?? null, nowMs)}`;

/** The sentence of a Codex window whose reset has passed: the record's number is of the window before it. */
export const RESET_PASSED = 'no reading since this window was reset';

const passed = (window: CodexWindowLike, nowMs: number): boolean => window.resetsAtMs !== null && nowMs >= window.resetsAtMs;

const codexPart = (window: CodexWindowLike, nowMs: number): string =>
  (passed(window, nowMs) ? `${codexLabel(window.minutes)} — ${RESET_PASSED}` : `${part(codexLabel(window.minutes), window.usedPercent)} · ${resetsIn(window.resetsAtMs, nowMs)}`);

/** Codex's windows, each by its length with its own reset: `5h 90% left · resets in 1h 5m · week 86% left · resets in 4d 23h`. */
export const codexLine = (windows: readonly CodexWindowLike[], nowMs: number): string => windows.map((one) => codexPart(one, nowMs)).join(' · ');

/**
 * A reset that is a day or more away, as a date in the viewer's own zone: `Nov 30, 14:00` (the mod's week reset names the day; the tab adds the time).
 *
 * <p>EMBEDDED in the accounts page by its own source text, like `resetText`: no module constant, no helper outside, no template literal.</p>
 */
export function resetDate(resetsAtMs: number): string {
  const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  const two = (n: number): string => (n < 10 ? '0' + n : String(n));
  const at = new Date(resetsAtMs);

  return months[at.getMonth()] + ' ' + at.getDate() + ', ' + two(at.getHours()) + ':' + two(at.getMinutes());
}
