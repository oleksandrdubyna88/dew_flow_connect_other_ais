import { EFFORT, EffortRow, FAST_MODE, type FastModeRow, type FastSource } from './featureAvailability.generated';
import { Runtime } from './models';

/**
 * Whether a runtime accepts an effort, judged by `shared/feature-availability.json` (PLAN_one_model_catalog.md
 * D4, E1.2) — `''` when it does, else the sentence a person can act on, naming what IS legal.
 *
 * <p>An empty effort is always legal: absent means the vendor's or module's own default (D6), and that is
 * what every row written before the catalog says. `probed` is the model's own report for a `probe` runtime
 * (`apiSettings.ts`, `report.capabilities.effortLevels`); without one there is nothing to judge by, which
 * is said rather than guessed.</p>
 */
export function effortRefusal(runtime: Runtime, effort: string, probed: readonly string[] = []): string {
  const row = EFFORT.find((one) => one.runtime === runtime);

  return effort.length === 0 || row === undefined ? '' : JUDGE[row.source](row, effort, probed);
}

type Judge = (row: EffortRow, effort: string, probed: readonly string[]) => string;

const JUDGE: Readonly<Record<EffortRow['source'], Judge>> = {
  list: (row, effort) => notIn(effort, row.levels, row.runtime),
  probe: (row, effort, probed) =>
    probed.length === 0
      ? `${row.runtime} takes the efforts its model's probe reports, and this model has not been probed — probe it first.`
      : notIn(effort, probed, row.runtime),
  unmeasured: (row, effort) =>
    `'${effort}' cannot be set: which efforts ${row.runtime} accepts has not been measured yet. ${row.note}`,
  none: (row) => `${row.runtime} takes no effort. ${row.note}`,
};

function notIn(effort: string, legal: readonly string[], runtime: string): string {
  return legal.includes(effort) ? '' : `'${effort}' is not an effort ${runtime} accepts — use one of: ${legal.join(', ')}.`;
}

/**
 * Whether a model on a runtime has a fast tier (todo/PLAN_fast_mode.md) — read from shared/feature-availability.json, the
 * list coai-mcp reads too (`FeatureAvailability.HasFastTier`): every model of an `every-model` runtime; a listed model,
 * its case and any `[1m]`-style suffix set aside; nothing else.
 */
export function hasFastTier(runtime: Runtime, model: string): boolean {
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
