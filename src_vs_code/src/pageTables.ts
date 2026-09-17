/**
 * The mechanics every paginated, sortable page in this extension shares.
 *
 * <p>Extracted from `roundsLog.ts` when the notifications page needed the same three things, and
 * extracted rather than copied because `reuse-first.md` is explicit that a second implementation
 * of a capability is a defect from the moment it compiles: two comparators would drift about what
 * a blank sorts as, and two page sizes would drift about what the operator ruled.</p>
 *
 * <h2>Two of these run INSIDE a webview page, embedded by their own source text</h2>
 *
 * <p><b>`compareRows` and `asInstant` must reference nothing outside their parameters</b> — no
 * import, no module constant, no helper — and must use no template literal, because their source is
 * read with `.toString()` and pasted into a script that itself lives inside one. That constraint
 * came with them from `roundsLog.ts` and it survives the move unchanged; it is the reason they are
 * written the way they are, and the reason they are in a module of their own rather than beside
 * anything that might tempt a helper out of them.</p>
 *
 * <p>The embedding is bound BY ASSIGNMENT (`var compareRows = <source>`) rather than by declaring
 * the function again, because a minifier renames a declaration and the page then calls a name that
 * is not there. `roundsLog.test.ts` asserts the exact string, so a change here that broke the page
 * is a red test rather than a blank table — this repository has shipped that hazard broken twice.
 * A generic type parameter is safe: types are erased, so the emitted source is identical.</p>
 */

/**
 * How many rows one page holds.
 *
 * <p>Two hundred, from the operator on 2026-09-09: «у нас есть пагинаций. 200 на стр достаточно.»
 * The same number the server pages its own list at, and it is a constant rather than a setting on
 * purpose — a configurable page size is the first step towards a page-number strip, which is the
 * growth this deliberately does not have.</p>
 *
 * <p>It is interpolated into page scripts as a VALUE (`var PAGE_SIZE = ${PAGE_SIZE};`), which is
 * why it may live in a shared module while the two functions below are constrained: a number
 * survives interpolation, a reference does not.</p>
 */
export const PAGE_SIZE = 200;

/**
 * Orders two rows by one column. A blank sorts after every real value in BOTH directions: a
 * missing number is not a small one, and "oldest first" must not begin with rounds that have no
 * date at all.
 *
 * <p>Returns 0 for two equal keys, which is what keeps `Array.prototype.sort` stable for them —
 * a page that reorders equal rows on every re-sort is a page that looks like it lost data.</p>
 */
export function compareRows<Row extends object>(
  a: Row,
  b: Row,
  key: keyof Row & string,
  dir: 'asc' | 'desc',
): number {
  const x = (a as unknown as Record<string, unknown>)[key];
  const y = (b as unknown as Record<string, unknown>)[key];
  const xBlank = x === null || x === undefined || x === '';
  const yBlank = y === null || y === undefined || y === '';
  if (xBlank && yBlank) {
    return 0;
  }
  if (xBlank) {
    return 1;
  }
  if (yBlank) {
    return -1;
  }
  const sign = dir === 'asc' ? 1 : -1;
  if (typeof x === 'number' && typeof y === 'number') {
    return (x - y) * sign;
  }
  return String(x).localeCompare(String(y)) * sign;
}

/**
 * A wall-clock value from a `datetime-local` input, as the instant a filter compares against.
 *
 * <p>What the input holds is WALL CLOCK and what a record carries is UTC, so comparing the two as
 * strings is wrong by the reader's offset. `endOfMinute` includes the minute the bound names, which
 * is what a minute-granularity picker means by it — without it "to 23:59" ends at 23:59:00.000 and
 * the last minute of today falls outside the range the page opens on.</p>
 *
 * <p><b>One home, because two pages must agree.</b> `utc-timestamps.md` asks for a single place per
 * comparison, and two pages each deciding for themselves whether an instant is in range is how they
 * come to disagree about the same row.</p>
 */
export function asInstant(localValue: string, endOfMinute: boolean): string {
  if (!localValue) {
    return '';
  }
  var at = new Date(localValue).getTime();

  return isNaN(at) ? '' : new Date(at + (endOfMinute ? 59999 : 0)).toISOString();
}
