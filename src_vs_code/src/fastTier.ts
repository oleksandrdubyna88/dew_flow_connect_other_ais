import { FAST_MODE, type FastModeRow, type FastSource } from './fastMode.generated';

/**
 * Which rows have a fast tier (research/PLAN_fast_mode.md) — in a module of its own that imports only the generated rows,
 * because the stored field (`catalogFields`) asks it and `vendors` imports `catalogFields`: reached through
 * `featureAvailability`, which imports `models`, the rule closed a new import cycle (the fast-mode code round).
 */

/**
 * Whether a model on a runtime has a fast tier — read from shared/feature-availability.json, the list coai-mcp reads too
 * (`FeatureAvailability.HasFastTier`): every model of an `every-model` runtime; a listed model, its case and any
 * `[1m]`-style suffix set aside; nothing else.
 */
export function hasFastTier(runtime: string, model: string): boolean {
  const row = FAST_MODE.find((one) => one.runtime === runtime);

  return row !== undefined && HAS_TIER[row.source](row, model);
}

/** What each source means for one model. */
const HAS_TIER: Readonly<Record<FastSource, (row: FastModeRow, model: string) => boolean>> = {
  'every-model': () => true,
  'models': (row, model) => row.models.includes(baseModel(model)),
  'none': () => false,
};

/** A model id with its case and any `[1m]`-style suffix set aside. */
function baseModel(model: string): string {
  return (model.split('[')[0] ?? '').trim().toLowerCase();
}

/**
 * Whether this ROW has a fast tier: its runtime and model by the shared file — and a codex row only on codex's own
 * service, since a row on somebody else's endpoint (a base URL) has no codex tier. The one rule the stored field, the
 * card and the wire all ask, as coai-mcp's `RowHasFastTier` does; the seam's `fastTierSeam` asks both the same rows.
 */
export function rowHasFastTier(row: { readonly runtime: string; readonly model: string; readonly baseUrl: string }): boolean {
  return hasFastTier(row.runtime, row.model) && !(row.runtime === 'codex' && row.baseUrl.length > 0);
}
