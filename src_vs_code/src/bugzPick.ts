import type { ModelChoice } from './models';
import type { Vendor } from './vendors';

/**
 * A picker choice, and the runtime of the catalog row it names — what a binary that ranks by runtime matches. Here, in
 * the domain, rather than in `bugzView.ts`: the pick is decided here and only drawn there (E5.1's code round, finding 2).
 */
export type RankingChoice = ModelChoice & { readonly runtime?: string };

/**
 * The vendors that may be shown a finding's own words.
 *
 * <p><b>It is a copy, and a test is what keeps it honest.</b> The list that DECIDES lives in
 * `CoaiMcp.Core.Collecting.RankingModels` and is enforced there, before a single finding field is
 * read. TypeScript cannot import a C# constant, so this cannot literally be derived from it — the
 * plan said 'derived' and that was not achievable; what is achievable is that the two can never
 * drift silently. `theAllowlistsAgree` reads `shared/ranking-vendors.txt` and fails if this list differs, so adding
 * a vendor on one side without the other is a red test rather than a feature that half works.
 * (Code round, codex: 'the picker keeps a second independent copy'.)</p>
 *
 * <p>If they ever DO disagree at runtime the collector wins and the person sees its refusal, which
 * is the right way round — but a picker offering a model that always fails is a bug in this file.</p>
 *
 * <p>Why so narrow: a finding's `title`, `why` and `fix` are the reviewers' prose about somebody's
 * code and are <b>not</b> anonymised. The normaliser runs later and only on source, so the ranking
 * pass is the one step here that handles un-anonymised text.</p>
 */
export const RANKING_VENDORS: readonly string[] = ['local'];

/** Whether this choice may be offered, against a given list — of runtimes, or of row ids for an older binary. */
export const allowedBy = (choice: RankingChoice, vendors: readonly string[], byRuntime: boolean): boolean =>
  choice.id.length === 0 || vendors.includes(rankedAs(choice, byRuntime));

/** What the binary matches: the row's runtime when it ranks by runtime, else the row id (`local/<model>`). */
function rankedAs(choice: RankingChoice, byRuntime: boolean): string {
  return byRuntime && choice.runtime !== undefined ? choice.runtime.toLowerCase() : rowIdOf(choice.id);
}

const rowIdOf = (model: string): string => model.split('/')[0]?.toLowerCase() ?? '';

/** Whether this model may be offered at all, by the panel's own fallback list and an older binary's rule. */
export const mayRank = (model: string): boolean => allowedBy({ id: model, label: model }, RANKING_VENDORS, false);

/**
 * Which model Bugz ranks with, read from the catalog (todo/PLAN_one_model_catalog.md, E5.1 step 2): the rows ticked
 * Bugz on Models, and what `coai.bugzModel` holds. Pure — the sidebar's picker draws from it and the collect refuses
 * by it, so the two can never disagree about whether a pick holds.
 *
 * <p><b>Why a pick is kept as `bugzModel` and not read off the tick alone.</b> The Models tab's tick already WRITES the
 * setting as `row/model` (`catalogCommands.ts`, `bugzMoved`), and the collect hands that string to coai-mcp as
 * `--model`, so the collect path stays as it was; what changes is only what the picker offers.</p>
 *
 * <p><b>Three readings of a saved pick</b>, each one a decision of the plan round:</p>
 * <ul>
 *   <li>it names a row ticked Bugz and one of that row's models — the pick, shown selected;</li>
 *   <li>NO row is ticked Bugz, and it names a catalog row and one of its models — an install that never ticked Bugz,
 *       upgraded: that row's pick, so it keeps collecting with the model it had (finding 2). Unticking a row clears a
 *       pick that was that row's (`heldElsewhere`), so this cannot bring back a pick a person took away;</li>
 *   <li>anything else — a row unticked while another holds the tick, a row removed, a model the row no longer has —
 *       STRANDED: drawn as what it is, and a collect with it is refused by name (finding 5), never run with an empty or
 *       stale `--model`, nor swapped for another row's model the person did not choose. The Chat tab's rule.</li>
 * </ul>
 */
export interface BugzPick {
  /** What the picker offers: each row the pick may come from, as `row/model`. */
  readonly offered: readonly RankingChoice[];
  /** The saved pick, when it holds — one of {@link offered}; '' when there is none or it does not. */
  readonly chosen: string;
  /** The saved pick, when it does NOT hold — '' otherwise. */
  readonly stranded: string;
}

/**
 * @param rows every catalog row — a catalog-only `bugz-local` included, which the reviewer list hides
 * @param saved what `coai.bugzModel` holds: `<row id>/<model>`
 * @param modelsOf the models a row has: its own, and what its engine was last seen serving
 */
export function bugzPickOf(rows: readonly Vendor[], saved: string, modelsOf: (row: Vendor) => readonly string[]): BugzPick {
  const ticked = rows.filter((row) => (row.uses ?? []).includes('bugz'));
  const pool = ticked.length > 0 ? ticked : rows.filter((row) => names(saved, row, modelsOf));
  const offered = pool.map((row) => choiceOf(row, saved, modelsOf));
  const chosen = offered.some((one) => one.id === saved) ? saved : '';

  return { offered, chosen, stranded: chosen.length === 0 ? saved : '' };
}

/** What a local engine was last seen serving, as much of `LocalEngine` as a pick reads. */
export type ServedModels = Readonly<Record<string, { readonly models: readonly { readonly id: string }[] }>>;

/**
 * A row's models: its own, and every model its engine was last seen serving — the old picker offered each of those as
 * `row/model`, so a pick made there is one of the row's models here.
 *
 * @param engines the engines probed per row id (the panel's `localEngines`)
 */
export function modelsOfRows(engines: ServedModels): (row: Vendor) => readonly string[] {
  return (row) => [row.model, ...(engines[row.id]?.models ?? []).map((one) => one.id)];
}

/** Whether a saved pick names this row and one of its models. */
function names(saved: string, row: Vendor, modelsOf: (row: Vendor) => readonly string[]): boolean {
  const [rowId, model] = partsOf(saved);

  return rowId === row.id && model.length > 0 && modelsOf(row).includes(model);
}

/** A row as the picker offers it: the saved pick when it is this row's, else the row's own model. */
function choiceOf(row: Vendor, saved: string, modelsOf: (row: Vendor) => readonly string[]): RankingChoice {
  const model = names(saved, row, modelsOf) ? partsOf(saved)[1] : row.model;

  return { id: `${row.id}/${model}`, label: `${model} — ${row.id}`, runtime: row.runtime };
}

/** `<row id>/<model>` split at the FIRST slash — a model name may carry one of its own; the id lower-cased as ids are. */
function partsOf(saved: string): readonly [string, string] {
  const at = saved.indexOf('/');

  return at < 0 ? [saved.toLowerCase(), ''] : [saved.slice(0, at).toLowerCase(), saved.slice(at + 1)];
}

/** Where a pick is made — named in every refusal, because that is where a person fixes it. */
const WHERE = 'Settings › Models';

/**
 * Why a collect may not start with this pick — '' when it may. A stranded pick is refused BY NAME, so a person sees
 * which model the setting still holds; no pick at all is refused too, rather than collected without a ranking model.
 */
export function bugzCollectRefusal(pick: BugzPick): string {
  if (pick.stranded.length > 0) {
    return `Bugz is set to rank with ${pick.stranded}, which is no longer ticked Bugz on Models, or was removed. `
      + `Tick a model for Bugz under ${WHERE}, or pick one in the Bugz section — nothing is collected with a model nobody chose.`;
  }

  return pick.chosen.length > 0
    ? ''
    : `No model is ticked for Bugz. Tick one under ${WHERE} (the ranking pass runs on a model on this machine), then collect.`;
}
