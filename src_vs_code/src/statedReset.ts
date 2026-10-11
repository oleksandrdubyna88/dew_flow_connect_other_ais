import { untilWords } from './resetWords';
import type { RoundsSnapshot } from './roundCountsCache';
import type { UsageEntry } from './usage';

/**
 * A stated reset, as a person reads it (story 5.3b of todo/PLAN_every_round_is_counted.md):
 * `quota out — resets in 89 h (Wed 04:49)` beside a vendor row on the Spending tab's Rounds block and on its model card.
 *
 * <p>Pure, and NOT a parser of vendor sentences: `coai-mcp` reads the refusal with the one parser both halves use
 * (`CooldownParser.StatedReset`) and writes the instant on the ledger line as `resetsUtc`. This decides only whether it
 * still applies, and `resetWords.ts` says it:</p>
 * <ul>
 * <li><b>The row's LAST line decides.</b> A later line of the same row means the account was used again; if it answered,
 * the quota is back whatever the vendor said before (a re-login, a plan bought). So the reset is shown only while the
 * newest line — by its own `utc`, not by the order the lines arrive in — is the refusal that stated it.</li>
 * <li><b>Only while it is ahead.</b> A reset that has passed says nothing.</li>
 * </ul>
 *
 * <p>Display only. Locally a stated reset is never waited out — the reviewer's ladder gives up on one it cannot outwait
 * (story 5.3c) — and nothing is persisted across rounds; the Team server parks its slot on the same reading.</p>
 */

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
