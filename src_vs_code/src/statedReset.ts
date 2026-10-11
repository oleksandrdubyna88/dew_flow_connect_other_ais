import type { RoundsSnapshot } from './roundCountsCache';
import type { UsageEntry } from './usage';

/**
 * A stated reset, as a person reads it (story 5.3b of todo/PLAN_every_round_is_counted.md):
 * `quota out — resets in 89 h (Wed 04:49)` beside a vendor row on the Spending tab's Rounds block and on its model card.
 *
 * <p>Pure, and NOT a parser of vendor sentences: `coai-mcp` reads the refusal with the one parser both halves use
 * (`CooldownParser.StatedReset`) and writes the instant on the ledger line as `resetsUtc`. This turns that instant into
 * words, and decides only whether it still applies:</p>
 * <ul>
 * <li><b>The row's LAST line decides.</b> A later line of the same row means the account was used again; if it answered,
 * the quota is back whatever the vendor said before (a re-login, a plan bought). So the reset is shown only while the
 * newest line — by its own `utc`, not by the order the lines arrive in — is the refusal that stated it.</li>
 * <li><b>Only while it is ahead.</b> A reset that has passed says nothing.</li>
 * <li><b>Never sooner than stated.</b> Hours and minutes are rounded UP: 88 h 49 m left reads *89 h*, because a wait read
 * as shorter than the vendor's sends a person back to a refusal.</li>
 * </ul>
 *
 * <p>Display only. Locally a stated reset is never waited out — the reviewer's ladder gives up on one it cannot outwait
 * (story 5.3c) — and nothing is persisted across rounds; the Team server parks its slot on the same reading.</p>
 */

const MINUTE_MS = 60_000;
const HOUR_MS = 60 * MINUTE_MS;

/** The words before the wait — the same on the block and on the card. */
export const QUOTA_OUT = 'quota out — resets in';

type ResetLine = Pick<UsageEntry, 'utc' | 'provider' | 'resetsUtc'>;

/**
 * `quota out — resets in 89 h (Wed 04:49)` for `provider` at `nowMs`, or '' when its newest line stated no reset still
 * ahead. `timeZone` is for tests; a person reads the instant in their own zone.
 */
export function resetLine(lines: readonly ResetLine[], provider: string, nowMs: number, timeZone?: string): string {
  const resets = statedBy(newestOf(lines.filter((line) => line.provider === provider)));
  const when = untilWords(resets, nowMs, timeZone);

  return when.length > 0 ? `${QUOTA_OUT} ${when}` : '';
}

/**
 * `89 h (Wed 04:49)` — how long until `resetsMs` and when, rounded UP — or '' when it is not ahead of `nowMs` (NaN, an
 * instant nobody stated, never is). The one wording of a reset: this side's ledger line and the Team server's catalog
 * (`slotSentence`) both say it so.
 */
export function untilWords(resetsMs: number, nowMs: number, timeZone?: string): string {
  return resetsMs > nowMs ? `${spanOf(resetsMs - nowMs)} (${clockOf(resetsMs, timeZone)})` : '';
}

/** The instant a line stated, in epoch ms — NaN when it stated none. */
function statedBy(line: ResetLine | undefined): number {
  return Date.parse(line?.resetsUtc ?? '');
}

/** The card's reset line, from the same cached snapshot as its round count — '' before the first read. */
export function cardResetLine(snapshot: RoundsSnapshot | undefined, provider: string): string {
  return snapshot === undefined ? '' : resetLine(snapshot.ledger, provider, snapshot.atMs);
}

/** The newest line by its own `utc`; of two at the same instant, the later in the list. */
function newestOf(lines: readonly ResetLine[]): ResetLine | undefined {
  return lines.reduce<ResetLine | undefined>((newest, line) => (newest === undefined || at(line) >= at(newest) ? line : newest), undefined);
}

function at(line: ResetLine): number {
  return Date.parse(line.utc);
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
