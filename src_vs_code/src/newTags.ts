/**
 * When each new control was first seen, so the page can mark it "new" for a week (todo/PLAN_one_model_catalog.md, E3.1
 * and *Growth surfaces*): one small record in `globalState`, bounded by what is listed here and pruned after 60 days.
 *
 * <p><b>By control, never by a version number.</b> A control is listed here in the change that brings it; its clock
 * starts the first time a window of this profile runs with it. Nothing guesses which release that is (the plan's epic 2
 * revision forbids a constant that guesses a release before release-please cuts it).</p>
 */

/** The `globalState` key the record lives under. */
export const FIRST_SEEN_KEY = 'coai.firstSeenControls';

/** After this, an entry is forgotten — its tag is long gone, and the record must not grow with every release. */
export const FORGET_AFTER_MS = 60 * 24 * 60 * 60 * 1000;

/** The controls the new Settings page marks as new, by id. Remove an id once its tag no longer matters. */
export const NEW_CONTROLS: readonly string[] = [
  'settings.preview',
  'model.thinking',
  'model.systemPrompt',
  'model.effort',
  'model.timeout',
  'model.check',
];

/**
 * The record after this window looked: every listed control keeps its first-seen time or gets `now`; an entry that is
 * no longer listed, is older than {@link FORGET_AFTER_MS}, or is not a time at all is dropped. Whatever was stored —
 * nothing, an older shape, a hand edit — reads as a record or as nothing, never as an error.
 */
export function seenNow(stored: unknown, controls: readonly string[], now: number): Record<string, number> {
  const kept = timesIn(stored);

  return Object.fromEntries(controls
    .map((id) => [id, kept[id] ?? now] as const)
    .filter(([, at]) => now - at < FORGET_AFTER_MS));
}

/** The stored entries that are times; anything else in the record is ignored. */
function timesIn(stored: unknown): Readonly<Record<string, number>> {
  if (typeof stored !== 'object' || stored === null) {
    return {};
  }

  return Object.fromEntries(Object.entries(stored).filter(([, at]) => typeof at === 'number' && Number.isFinite(at)));
}
