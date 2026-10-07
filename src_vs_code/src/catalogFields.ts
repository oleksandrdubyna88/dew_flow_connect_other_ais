import { isMinutes } from './apiSettings';
import { saidText } from './saidText';
import { rowHasFastTier } from './fastTier';

/**
 * The fields a reviewer row gains as it becomes a catalog row (PLAN_one_model_catalog.md D1, E1.1).
 *
 * <p>Parsed here rather than in `vendorsFrom` because `vendors.ts` was at the 800-line limit, and kept
 * free of `vendors.ts` so the import graph gains no cycle. Every field is ABSENT unless it was said, as
 * every optional field of a row already is: `coai.vendors` is JSON a person reads, and a row written
 * before the catalog must parse to exactly what it parsed to before.</p>
 */

/** A non-review feature an instance may serve. The review stages stay the row's own flags. */
export type CatalogUse = 'security' | 'consultant' | 'qconsult' | 'chat' | 'bugz';

/** Every use, in the order the catalog stores and draws them. */
export const CATALOG_USES: readonly CatalogUse[] = ['security', 'consultant', 'qconsult', 'chat', 'bugz'];

export interface CatalogFields {
  name?: string;
  uses?: readonly CatalogUse[];
  systemPrompt?: string;
  timeoutMinutes?: number;
  chatStartingPrompt?: string;
  stream?: boolean;
  fast?: FastSetting;
}

/** A fast mode that is not the default (todo/PLAN_fast_mode.md): On, or As the CLI is set. Off is stored as nothing. */
export type FastSetting = 'on' | 'cli';

/** The catalog fields off a stored row, each only when it holds a value somebody could have meant. */
export function catalogFields(v: Record<string, unknown>): CatalogFields {
  const name = saidText(v['name']);
  const uses = usesFrom(v['uses']);

  return {
    ...(name === undefined ? {} : { name }),
    ...(uses.length === 0 ? {} : { uses }),
    ...promptField('systemPrompt', v['systemPrompt']),
    ...timeoutField(v['runtime'], v['timeoutMinutes']),
    ...promptField('chatStartingPrompt', v['chatStartingPrompt']),
    ...streamField(v['runtime'], v['stream']),
    ...fastField(v),
  };
}

/** Known uses only, once each, in {@link CATALOG_USES} order — so two spellings of one list are one list. */
export function usesFrom(raw: unknown): readonly CatalogUse[] {
  const said: readonly unknown[] = Array.isArray(raw) ? raw : [];

  return CATALOG_USES.filter((use) => said.includes(use));
}

/**
 * A prompt as written — NOT trimmed, because its whitespace is the person's text — and absent when it
 * is not a string or holds nothing but spaces. Its length is judged by `catalogRefusal`, never cut here.
 */
function promptField(field: 'systemPrompt' | 'chatStartingPrompt', raw: unknown): CatalogFields {
  return typeof raw === 'string' && raw.trim().length > 0 ? { [field]: raw } : {};
}

/**
 * A row's fast mode (todo/PLAN_fast_mode.md) — `on` or `cli` kept only on a row that has a tier; Off, the default, is
 * kept as nothing, so a row that never set it and one set to Off read alike.
 */
function fastField(v: Record<string, unknown>): CatalogFields {
  const said = FAST_SETTINGS.find((one) => one === v['fast']);

  return said !== undefined && rowHasFastTier(launchOf(v)) ? { fast: said } : {};
}

const FAST_SETTINGS: readonly FastSetting[] = ['on', 'cli'];

/** What decides a row's tier, off the stored row: its runtime, its model, and whether it is on somebody else's endpoint. */
function launchOf(v: Record<string, unknown>): { readonly runtime: string; readonly model: string; readonly baseUrl: string } {
  return { runtime: saidText(v['runtime']) ?? '', model: saidText(v['model']) ?? '', baseUrl: saidText(v['baseUrl']) ?? '' };
}

/** An api row's stream switch (todo/PLAN_api_streaming.md) — kept only when ON, so a switched-off row reads as one that never had it. */
function streamField(runtime: unknown, raw: unknown): CatalogFields {
  return runtime === 'api' && raw === true ? { stream: true } : {};
}

/** A CLI row's own limit; an api row has `reviewMinutes`, its module's calibrated value, instead. */
function timeoutField(runtime: unknown, raw: unknown): CatalogFields {
  return runtime !== 'api' && isMinutes(raw) ? { timeoutMinutes: raw } : {};
}
