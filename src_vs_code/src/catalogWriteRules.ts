import { MAX_PROMPT_BYTES, MAX_ROWS } from './catalogRules';
import { effortRefusal } from './featureAvailability';
import type { Vendor } from './vendors';

/**
 * What a write of one catalog row may not do — decided BEFORE it is saved, for both Settings pages
 * (todo/PLAN_one_model_catalog.md E3.2). `''` when the write may go; otherwise the sentence the person is shown while
 * the control snaps back.
 *
 * <p>The limits were written in epic 1 (`catalogRules.ts`, `featureAvailability.ts`) and called by the migration
 * alone, so a page could store a 9 KB prompt or an effort its runtime refuses. Epic 3's plan round: the 64-row cap is
 * checked where a row is ADDED ({@link addRefusal}), never on an edit — a catalog already past it, from a hand-edited
 * file, must stay editable back under it.</p>
 */
/**
 * @param probed the efforts the row's own probe reported — what a `probe` runtime (a local engine) is judged by
 */
export function rowWriteRefusal(rows: readonly Vendor[], id: string, key: string, value: unknown, probed: readonly string[] = []): string {
  const row = rows.find((one) => one.id === id);

  return row === undefined ? '' : firstRefusal([promptRefusal(key, value), effortRefusalOf(row, key, value, probed), lastStageRefusal(rows, row, key, value)]);
}

function firstRefusal(reasons: readonly string[]): string {
  return reasons.find((reason) => reason.length > 0) ?? '';
}

/** Whether one more row may be added (add, duplicate) — the catalog's own cap. */
export function addRefusal(rows: readonly Vendor[]): string {
  return rows.length >= MAX_ROWS ? `The catalog holds at most ${MAX_ROWS} models — remove one before adding another.` : '';
}

const ENCODER = new TextEncoder();

function promptRefusal(key: string, value: unknown): string {
  const bytes = key === 'systemPrompt' && typeof value === 'string' ? ENCODER.encode(value).length : 0;

  return bytes > MAX_PROMPT_BYTES ? `The system prompt is ${bytes} bytes — at most ${MAX_PROMPT_BYTES}; shorten it.` : '';
}

/**
 * A CLI row's effort, judged by its runtime (D4). An `api` row is judged against its own probe report on its own path
 * (`withApiSetting`); a Team server row by its server, which says what it dropped (contract 2).
 */
function effortRefusalOf(row: Vendor, key: string, value: unknown, probed: readonly string[]): string {
  return key === 'effort' && typeof value === 'string' && JUDGED_HERE.has(row.runtime) ? effortRefusal(row.runtime, value, probed) : '';
}

/** The runtimes whose effort this side judges: every one but `api` (its probe report) and `remote` (its server). */
const JUDGED_HERE: ReadonlySet<Vendor['runtime']> = new Set(['codex', 'claude', 'antigravity', 'gemini', 'local']);

/** The two review stages a catalog may never be left without a model for. */
const STAGES = ['plan', 'code'] as const;

/** Switching off — or unticking a stage of — the last model switched on for that stage. */
function lastStageRefusal(rows: readonly Vendor[], row: Vendor, key: string, value: unknown): string {
  const lost = value === false ? STAGES.filter((stage) => key === 'enabled' || key === stage) : [];
  const alone = lost.find((stage) => servesAlone(rows, row, stage));

  return alone === undefined ? '' : lastStageMessage(row.id, [alone]);
}

/** The one sentence a locked row is refused with — by the write path and the Models tab's remove alike. */
export function lastStageMessage(id: string, stages: readonly string[]): string {
  return `${id} is the only model switched on for ${stages.join(' and ')} review — switch another on first.`;
}

/** The review stages this row is the ONLY switched-on model for — what the card locks, and the write refuses. */
export function lastStagesOf(rows: readonly Vendor[], row: Vendor): readonly ('plan' | 'code')[] {
  return STAGES.filter((stage) => servesAlone(rows, row, stage));
}

function servesAlone(rows: readonly Vendor[], row: Vendor, stage: 'plan' | 'code'): boolean {
  const serving = rows.filter((one) => one.enabled && one[stage]);

  return serving.length === 1 && serving[0]!.id === row.id;
}
