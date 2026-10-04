import { reviewsFeatures } from './featureGate';
import { SecurityLane } from './securityLane';
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

/**
 * Whether the OLD Settings page lists a row among its reviewers. A row that reviews nothing and exists for its
 * `uses` alone came from the catalog — a migrated consultant, say — and the old page has no place for it: shown
 * as a reviewer with every box unticked, it would be a change the person can see in an epic that promises none,
 * and removing it as clutter would take their consultant with it (plan-round finding 7). Display only: every
 * write still reads, and keeps, every row.
 */
export function shownOnTheOldPage(row: Vendor): boolean {
  return reviewsAnything(row) || row.uses === undefined;
}

/**
 * The rows `COAI_VENDORS` carries: every row, except one that reviews no stage and exists only for features
 * that reach coai-mcp RESOLVED — a consultant or question row's definition, the chat, Bugz. coai-mcp 0.43.0
 * knows nothing of `uses`, so such a row on the wire is a provider in no round, and the env block of a
 * migrated setup would no longer be the one it was (plan-round finding 7).
 *
 * <p>Kept whenever anything on the wire may name it: a `security` use, a Security lane run, or a lane this
 * build could not read (its runs are unknown). A row with no `uses` at all is a row from before the catalog
 * and crosses exactly as it always did.</p>
 */
export function rowsOnTheWire(rows: readonly Vendor[], lane: SecurityLane): readonly Vendor[] {
  const named = new Set(lane.runs.map((run) => run.vendor.toLowerCase()));

  return 'invalidConfiguration' in lane ? rows : rows.filter((row) => crosses(row, named));
}

function crosses(row: Vendor, named: ReadonlySet<string>): boolean {
  return reviewsAnything(row) || row.uses === undefined || row.uses.includes('security') || named.has(row.id);
}
