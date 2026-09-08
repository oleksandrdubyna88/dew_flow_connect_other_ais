/**
 * The ONE HTML escaper.
 *
 * <p>Its own module because two files that both build markup would otherwise have to import it from
 * each other. It used to live in `panelView.ts`, and `teamServerView.ts` — which `panelView` imports
 * — grew a private copy rather than create a cycle. That copy had already drifted: it did not escape
 * an apostrophe.</p>
 *
 * <p>This is the anti-pattern `.claude/rules/shared/common/security.md` names by example — "the HTML
 * escaper | three byte-identical private copies | hardening one left the other two behind". Caught
 * on the code round of epic 3, at the moment a second copy appeared.</p>
 *
 * <p>Byte-identical to the one it replaces, deliberately. Escaping an apostrophe as well would be a
 * defensible improvement and is NOT made here: it changes what every existing caller emits, and
 * smuggling a behaviour change in under a de-duplication is how a refactor stops being one. It
 * belongs in its own change, with the callers checked.</p>
 */
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

export function escapeHtml(text: string): string {
  return asText(text)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}
