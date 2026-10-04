import { reviewsFeatures } from './featureGate';
import { reviewsDocuments, Vendor } from './vendors';

/**
 * What a catalog may hold (PLAN_one_model_catalog.md D1, E1.1) — a pure judgement, so the migration
 * (E1.3) can refuse to write a catalog this would refuse, and the Models page (E3) can say why.
 */

/** Rows in one catalog. Past it the settings file and every picker stop being readable. */
export const MAX_ROWS = 64;

/**
 * A prompt's limit in BYTES, because bytes are what crosses: E2.2 carries prompts through the settings
 * file, and a limit counted in characters would let a Cyrillic prompt be twice the size of a Latin one.
 */
export const MAX_PROMPT_BYTES = 8192;

/** Why this catalog cannot be stored, or `''` when it can. The first reason found, said in full. */
export function catalogRefusal(rows: readonly Vendor[]): string {
  if (rows.length > MAX_ROWS) {
    return `${rows.length} models is more than a catalog holds — at most ${MAX_ROWS}; remove ${rows.length - MAX_ROWS} first.`;
  }

  return rows.map(promptRefusal).find((reason) => reason.length > 0) ?? '';
}

/** A row whose system prompt or chat starting prompt is past {@link MAX_PROMPT_BYTES}. */
function promptRefusal(row: Vendor): string {
  const prompts: readonly (readonly [string, string | undefined])[] = [
    ['system prompt', row.systemPrompt],
    ['chat starting prompt', row.chatStartingPrompt],
  ];
  const long = prompts.find(([, text]) => bytesOf(text) > MAX_PROMPT_BYTES);

  return long === undefined
    ? ''
    : `${row.id}'s ${long[0]} is ${bytesOf(long[1])} bytes — at most ${MAX_PROMPT_BYTES} bytes; shorten it.`;
}

function bytesOf(text: string | undefined): number {
  return new TextEncoder().encode(text ?? '').length;
}

/**
 * Whether a row reviews any stage — plan, code, documents or features — read exactly as a round reads
 * it. A row that reviews nothing exists for its `uses` alone and is left out of `COAI_VENDORS` (E1.4).
 */
export function reviewsAnything(row: Vendor): boolean {
  return row.plan || row.code || reviewsDocuments(row) || reviewsFeatures(row);
}
