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
export function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}
