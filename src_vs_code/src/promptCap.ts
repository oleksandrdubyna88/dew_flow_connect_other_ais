/**
 * A row's prompt cap — `maxPromptKib`, whole KiB, the most its reviewer is sent in a first prompt
 * (todo/PLAN_a_reviewer_is_not_sent_more_than_it_can_read.md D1).
 *
 * <p>A module of its own because it is read by `catalogFields.ts` (which `vendors.ts` imports) and by the card, and
 * `catalogRules.ts` imports `vendors.ts`: the limit lives where neither import graph gains a cycle.</p>
 */

/**
 * The most a row may say — 4 MiB, the same number `CatalogLimits.MaxPromptKib` pins in coai-mcp (`ARowCarriesAPromptCapTests`,
 * `catalogRow.test.ts` hold one half each). Not `MAX_PROMPT_BYTES`: that is the row's SYSTEM prompt.
 */
export const MAX_PROMPT_KIB = 4096;

/** A whole number of KiB the box accepts, one to {@link MAX_PROMPT_KIB}; anything else is no cap. */
export function isKib(raw: unknown): raw is number {
  return typeof raw === 'number' && Number.isInteger(raw) && raw >= 1 && raw <= MAX_PROMPT_KIB;
}
