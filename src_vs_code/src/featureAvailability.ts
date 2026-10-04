import { EFFORT, EffortRow } from './featureAvailability.generated';
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
