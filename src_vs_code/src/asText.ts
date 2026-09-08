/**
 * Text, whatever the caller actually had.
 *
 * <p>The parameter says `string` and TypeScript erases that at run time, while every value these
 * escapers are given comes from JSON on disk — a session file, a question file — which nothing
 * fully validates. On 2026-09-08 a person opened the rounds log to answer a question a review was
 * waiting on and got <b>`e.replace is not a function`</b>: one field somewhere was not a string,
 * and the page that would have let them answer never rendered.</p>
 *
 * <p>Nullish becomes EMPTY rather than the word `undefined`. `String(undefined)` is `'undefined'`,
 * and a row reading `undefined` is a second defect wearing the costume of data. Everything else is
 * coerced and shown: `[object Object]` on screen is visible and survivable, and the person can
 * still answer their question.</p>
 *
 * <p>The in-page twin of this escaper — `esc` in `roundsLog.ts` — has always done the coercing
 * half. Half of this codebase had learned the lesson.</p>
 */
export function asText(value: unknown): string {
  return value === undefined || value === null ? '' : String(value);
}
