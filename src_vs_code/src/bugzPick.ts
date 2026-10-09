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
 * Which model Bugz ranks with, read from the catalog (research/PLAN_one_model_catalog.md, E5.1 step 2): the rows ticked
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
  /** Why it does not hold — read only when {@link stranded} is set. */
  readonly why: StrandedWhy;
  /** Who the ranking pass runs on — the rule's list, named when a ticked row is refused by it. */
  readonly rankers: readonly string[];
}

/**
 * Why a saved pick does not hold (CodeRabbit on #709): its row is not ticked Bugz, or is gone, or no longer has that
 * model — `unticked`; or its row IS ticked and the ranking allowlist refuses it — `refused`. The two are told apart
 * because the second is not fixed by Models' tick, and calling a ticked row "no longer ticked" is false.
 */
export type StrandedWhy = 'unticked' | 'refused';

/**
 * What a stranded pick is, in the one sentence both the sidebar and the collect's refusal say — so the two cannot word
 * the same pick differently.
 */
export function strandedHead(stranded: string, why: StrandedWhy, rankers: readonly string[]): string {
  return why === 'refused'
    ? `${stranded} is ticked Bugz, but the ranking pass does not run on it — it runs only on ${rankers.join(', ')}, because`
      + ' the findings it reads are not anonymised.'
    : `${stranded} is no longer ticked Bugz on Models, or was removed.`;
}

/**
 * Who may rank: the vendors the installed server says, or this build's own list from a server too old to say — matched
 * by the row's runtime for a binary that ranks by it, else by the row id. Part of the PICK, not a filter of the view
 * (E5.1's code round, findings 0 and 7): a ticked row this refuses is neither offered nor chosen, so its pick is
 * stranded and the collect refuses it by the same reading — one rule, where the view used to apply a second.
 */
export interface RankingRule {
  readonly vendors: readonly string[];
  readonly byRuntime: boolean;
}

/** The rule from what the server said: its own list, or {@link RANKING_VENDORS} when it is too old to send one. */
export function rankingRuleOf(serverVendors: readonly string[], byRuntime: boolean): RankingRule {
  return { vendors: serverVendors.length > 0 ? serverVendors : RANKING_VENDORS, byRuntime };
}

/**
 * @param rows every catalog row — a catalog-only `bugz-local` included, which the reviewer list hides
 * @param saved what `coai.bugzModel` holds: `<row id>/<model>`
 * @param modelsOf the models a row has: its own, and what its engine was last seen serving
 * @param rule who may rank — a row it refuses is never offered
 */
export function bugzPickOf(rows: readonly Vendor[], saved: string, modelsOf: (row: Vendor) => readonly string[], rule: RankingRule): BugzPick {
  const ticked = rows.filter((row) => (row.uses ?? []).includes('bugz'));
  const pool = ticked.length > 0 ? ticked : rows.filter((row) => names(saved, row, modelsOf));
  const named = pool.map((row) => choiceOf(row, saved, modelsOf));
  const offered = named.filter((one) => allowedBy(one, rule.vendors, rule.byRuntime));
  const chosen = offered.some((one) => one.id === saved) ? saved : '';

  return { offered, chosen, stranded: chosen.length === 0 ? saved : '', why: whyStranded(named, saved), rankers: rule.vendors };
}

/** `refused` when the pick names one of the rows it may come from — so only the allowlist kept it out — else `unticked`. */
function whyStranded(named: readonly RankingChoice[], saved: string): StrandedWhy {
  return named.some((one) => one.id === saved) ? 'refused' : 'unticked';
}

/** What a local engine was last seen serving, as much of `LocalEngine` as a pick reads. */
export type ServedModels = Readonly<Record<string, { readonly models: readonly { readonly id: string }[] }>>;

/**
 * Everything a Bugz pick is read from — the sidebar draws its picker from these and the collect refuses by them, so
 * the two read ONE set of facts (E5.1's code round, findings 1 and 6).
 */
export interface BugzInputs {
  /** Every catalog row — a catalog-only `bugz-local` included, which the reviewer list hides. */
  readonly rows: readonly Vendor[];
  /** What `coai.bugzModel` holds: `<row id>/<model>`. */
  readonly saved: string;
  /** The engines last seen serving, per row id. */
  readonly engines: ServedModels;
  /** The vendors the installed server says may rank — empty from a server too old to say. */
  readonly serverVendors: readonly string[];
  /** Whether the installed coai-mcp ranks by the row's runtime (`--features` lists `bugzRuntime`). */
  readonly byRuntime: boolean;
}

/** As much of the panel's state as a Bugz pick reads — the panel's `PanelState` is one. */
export interface BugzReads {
  readonly catalogRows?: readonly Vendor[] | undefined;
  readonly vendors: readonly Vendor[];
  readonly settings: { readonly bugzModel: string };
  readonly localEngines: ServedModels;
  readonly bugz?: { readonly rankingVendors: readonly string[] } | undefined;
  readonly rankByRuntime?: boolean | undefined;
}

/**
 * The ONE reader of a Bugz pick's facts (E5.1's code round, finding 1): the sidebar draws its picker from it and the
 * provider's collect refuses or starts by it, each from a state built the same way — so the two can never disagree about
 * a row the reviewer list hides. Every catalog row (`catalogRows`, the reviewers only where a state has no catalog), the
 * saved pick, the engines, the server's ranking list (empty until the corpus is read) and whether it ranks by runtime.
 */
export function bugzInputsOf(reads: BugzReads): BugzInputs {
  return {
    rows: reads.catalogRows ?? reads.vendors,
    saved: reads.settings.bugzModel,
    engines: reads.localEngines,
    serverVendors: reads.bugz?.rankingVendors ?? [],
    byRuntime: reads.rankByRuntime === true,
  };
}

/** The pick these inputs make. */
export function bugzPickFrom(inputs: BugzInputs): BugzPick {
  return bugzPickOf(inputs.rows, inputs.saved, modelsOfRows(inputs.engines), rankingRuleOf(inputs.serverVendors, inputs.byRuntime));
}

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
    return strandedRefusal(pick);
  }

  return pick.chosen.length > 0 ? '' : noPick(pick);
}

/** A stranded pick, refused by name — and sent to the picker only when it offers another model (finding 5). */
function strandedRefusal(pick: BugzPick): string {
  const orHere = pick.offered.length > 0 ? ', or pick one in the Bugz section' : '';

  return `Bugz is set to rank with ${strandedHead(pick.stranded, pick.why, pick.rankers)} `
    + `Tick a model for Bugz under ${WHERE}${orHere} — nothing is collected with a model nobody chose.`;
}

/**
 * Why no pick refuses, in the words of where it is fixed (E5.1's code round, finding 3): with a model ticked the picker
 * offers it and a pick is all that is missing, so the sentence sends the person to the Bugz section, not to Models.
 */
function noPick(pick: BugzPick): string {
  return pick.offered.length > 0
    ? 'Pick a ranking model in the Bugz section before collecting.'
    : `No model is ticked for Bugz. Tick one under ${WHERE} (the ranking pass runs on a model on this machine), then collect.`;
}
