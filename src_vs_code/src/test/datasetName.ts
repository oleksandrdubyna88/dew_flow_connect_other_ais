/**
 * How a `data-` attribute is named on `dataset`, for the page runners' fake elements.
 *
 * <p>Its own module so the two page runners share it without importing each other: `panelPageHarness.ts` used to take
 * it from `rolesPageHarness.ts`, and once the roles runner needed the panel runner's clock and busy bar
 * (research/PLAN_busy_marks_on_every_webview.md, E3) that was an import cycle (`importCycles.test.mjs`).</p>
 */

/** `data-remove-prompt` reaches the script as `dataset.removePrompt`, as a browser spells it. */
export function camel(attribute: string): string {
  return attribute.replace(/-([a-z])/g, (_, letter: string) => letter.toUpperCase());
}
