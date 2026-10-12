/**
 * The one wording of a stated reset (story 5.3b of todo/PLAN_every_round_is_counted.md): `89 h (Wed 04:49)`.
 *
 * <p>A LEAF — it imports nothing — because two very different modules say it: `statedReset.ts` (this side's ledger,
 * which reaches `usage.ts` and the round-count cache) and `teamServerView.ts` (the Team server's catalog). Kept inside
 * `statedReset.ts`, the Team servers view pulled the ledger's whole import graph in and closed three new import cycles
 * (`importCycles.test.mjs`). Here it is two pure functions and a constant.</p>
 *
 * <p><b>Never sooner than stated.</b> Hours and minutes are rounded UP: 88 h 49 m left reads *89 h*, because a wait read
 * as shorter than the vendor's sends a person back to a refusal.</p>
 */

const MINUTE_MS = 60_000;
const HOUR_MS = 60 * MINUTE_MS;

/**
 * `89 h (Wed 04:49)` — how long until `resetsMs` and when, rounded UP — or '' when it is not ahead of `nowMs` (NaN, an
 * instant nobody stated, never is). `timeZone` is for tests; a person reads the instant in their own zone.
 */
export function untilWords(resetsMs: number, nowMs: number, timeZone?: string): string {
  return resetsMs > nowMs ? `${spanOf(resetsMs - nowMs)} (${clockOf(resetsMs, timeZone)})` : '';
}

/** A wait, rounded UP — `89 h`, or `34 min` under an hour. */
function spanOf(ms: number): string {
  return ms >= HOUR_MS ? `${Math.ceil(ms / HOUR_MS)} h` : `${Math.ceil(ms / MINUTE_MS)} min`;
}

/** `Wed 04:49` — the day and the 24-hour time, in the viewer's zone unless one is named. */
function clockOf(ms: number, timeZone: string | undefined): string {
  const zone = timeZone === undefined ? {} : { timeZone };

  return new Intl.DateTimeFormat('en-GB', { weekday: 'short', hour: '2-digit', minute: '2-digit', hourCycle: 'h23', ...zone }).format(new Date(ms));
}
